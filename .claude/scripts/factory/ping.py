#!/usr/bin/env python3
"""ping.py -- the one road a ping takes to Grey (charter D12).

    python ping.py --file msg.txt [--lead "shift 2026-09-17"] [--level info|error]
                   [--dry-run] [--allow-repeat] [--repo PATH]

THE FORM, refused rather than asked of the prose:
  1. to_bullets(): hard-wrapped paragraphs are unwrapped, every ``**`` is
     stripped, each sentence becomes one ``- `` bullet; existing bullet
     lines are kept one per line. A --lead becomes the ``[factory] <lead>``
     header line (not a bullet).
  2. THE SIX-BULLET CAP: more than six bullets is REFUSED (exit 2, nothing
     sent). Cut the ping down; do not split it in two.
  3. THE REPETITION GUARD: .utmp/factory/pings/sent_<UTC date>.jsonl is the
     day's ledger, one row per bullet sent (sha1 of the normalised text).
     A bullet already sent today is DROPPED and named on stdout
     (--allow-repeat keeps it).
  4. THE POST: Discord webhook from DISCORD_WEBHOOK_FACTORY (environment,
     else <repo>/.env), split into <= 1,900-character parts at line
     boundaries. Without a webhook the ping is printed and exit is 0 with
     "DRY" on stdout -- a missing secret never stalls the loop, and the
     ledger is written only after a real post.

Exit codes: 0 sent (or nothing new); 1 not delivered (ledger not written,
so the retry is not a repeat); 2 refused (cap, empty text, missing file).
Importable: send(text, lead=..., level=..., repo=..., dry_run=...).
Only the standard library: this runs on the desktop under whatever Python
winget installed, and on the hive.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys

# A cp1252 console cannot print the report's own characters (an "≈" killed the shift-5
# report before it was posted, 2026-09-17): print UTF-8 wherever stdout allows it.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except (AttributeError, ValueError):
    pass
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

MAX_BULLETS = 6
PART_LIMIT = 1900
PREFIX = "[factory]"
_SENT = re.compile(r"(?<=[.!?])\s+(?=[A-Z\"'(\[$+\-0-9])")
_ABBR = re.compile(r"\b(e\.g|i\.e|vs|approx|cf|No|St|Mr|Ms|Dr|Inc|Ltd|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\.$")


def repo_root(start: Path | None = None) -> Path:
    p = (start or Path(__file__).resolve().parent)
    for cand in [p, *p.parents]:
        if (cand / ".git").exists():
            return cand
    return p


def webhook_url(repo: Path) -> str | None:
    url = os.environ.get("DISCORD_WEBHOOK_FACTORY", "").strip()
    if url:
        return url
    env = repo / ".env"
    if env.exists():
        for line in env.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("DISCORD_WEBHOOK_FACTORY="):
                return line.split("=", 1)[1].strip().strip("'\"") or None
    return None


def _sentences(text: str) -> list[str]:
    out: list[str] = []
    for piece in _SENT.split(text):
        piece = piece.strip()
        if not piece:
            continue
        if out and (_ABBR.search(out[-1]) or len(out[-1]) < 12):
            out[-1] = out[-1] + " " + piece
        else:
            out.append(piece)
    return out


def to_bullets(text: str) -> list[str]:
    """Plain dash-bullets from prose or an existing list; no bolding."""
    text = text.replace("**", "")
    bullets: list[str] = []
    para: list[str] = []

    def flush() -> None:
        if para:
            joined = re.sub(r"\s*\n\s*", " ", "\n".join(para)).strip()
            bullets.extend(_sentences(joined))
            para.clear()

    for raw in text.splitlines():
        line = raw.rstrip()
        if not line.strip():
            flush()
            continue
        m = re.match(r"^\s*(?:[-*•]|\d+[.)])\s+(.*)$", line)
        if m:
            flush()
            bullets.append(m.group(1).strip())
        else:
            para.append(line)
    flush()
    return [b for b in bullets if b]


def normalise(b: str) -> str:
    return re.sub(r"\s+", " ", b.lower().replace("**", "").strip().rstrip(".").lstrip("- ").strip())


def ledger_path(repo: Path, day: str | None = None) -> Path:
    day = day or datetime.now(timezone.utc).strftime("%Y-%m-%d")
    return repo / ".utmp" / "factory" / "pings" / f"sent_{day}.jsonl"


def already_sent(repo: Path) -> set[str]:
    p = ledger_path(repo)
    if not p.exists():
        return set()
    seen = set()
    for line in p.read_text(encoding="utf-8").splitlines():
        try:
            seen.add(json.loads(line)["sha"])
        except (json.JSONDecodeError, KeyError):
            continue
    return seen


def split_parts(text: str, limit: int = PART_LIMIT) -> list[str]:
    parts: list[str] = []
    cur = ""
    for line in text.splitlines():
        while len(line) > limit:
            parts.append((cur + "\n" if cur else "") + line[:limit])
            cur, line = "", line[limit:]
        if len(cur) + len(line) + 1 > limit and cur:
            parts.append(cur)
            cur = line
        else:
            cur = (cur + "\n" + line) if cur else line
    if cur:
        parts.append(cur)
    if len(parts) > 1:
        parts = [f"{p}\n({i + 1}/{len(parts)})" for i, p in enumerate(parts)]
    return parts


def post(url: str, content: str) -> bool:
    data = json.dumps({"content": content}).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json", "User-Agent": "robogame-factory-ping/1"})
    try:
        with urllib.request.urlopen(req, timeout=15) as r:
            return r.status < 300
    except (urllib.error.URLError, urllib.error.HTTPError, OSError):
        return False


def send(text: str, lead: str = "", level: str = "info", repo: Path | None = None,
         dry_run: bool = False, allow_repeat: bool = False, out=sys.stdout) -> dict:
    repo = repo or repo_root()
    bullets = to_bullets(text)
    if not bullets:
        print("REFUSED: empty ping", file=out)
        return {"status": "refused", "reason": "empty", "exit": 2}
    if len(bullets) > MAX_BULLETS:
        print(f"REFUSED: {len(bullets)} bullets > cap {MAX_BULLETS}; cut it down, do not split it", file=out)
        for b in bullets:
            print(f"   - {b[:100]}", file=out)
        return {"status": "refused", "reason": "cap", "exit": 2, "bullets": len(bullets)}
    seen = already_sent(repo)
    kept, dropped = [], []
    for b in bullets:
        sha = hashlib.sha1(normalise(b).encode("utf-8")).hexdigest()
        (kept if (allow_repeat or sha not in seen) else dropped).append((sha, b))
    for _, b in dropped:
        print(f"dropped (already sent today): {b[:100]}", file=out)
    if not kept:
        print("nothing new to send", file=out)
        return {"status": "nothing-new", "exit": 0, "dropped": len(dropped)}
    header = f"{PREFIX} {lead}".strip() if lead else PREFIX
    body = header + "\n" + "\n".join(f"- {b}" for _, b in kept)
    parts = split_parts(body)
    url = webhook_url(repo)
    if dry_run or not url:
        tag = "DRY" if dry_run else "DRY (no DISCORD_WEBHOOK_FACTORY)"
        print(f"{tag} level={level} parts={len(parts)}\n" + "\n---\n".join(parts), file=out)
        return {"status": "dry", "exit": 0, "sent": len(kept), "dropped": len(dropped)}
    for p in parts:
        if not post(url, p):
            print("NOT DELIVERED: webhook post failed; ledger not written", file=out)
            return {"status": "failed", "exit": 1}
    lp = ledger_path(repo)
    lp.parent.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).isoformat(timespec="seconds")
    with lp.open("a", encoding="utf-8") as fh:
        for sha, b in kept:
            fh.write(json.dumps({"ts": stamp, "sha": sha, "text": b, "level": level}) + "\n")
    print(f"sent {len(kept)} bullet(s) in {len(parts)} part(s); dropped {len(dropped)}", file=out)
    return {"status": "sent", "exit": 0, "sent": len(kept), "dropped": len(dropped)}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--file", required=True, help="text file with the ping (prose or bullets)")
    ap.add_argument("--lead", default="", help="header after [factory], e.g. 'shift 2026-09-17'")
    ap.add_argument("--level", default="info", choices=["info", "error"])
    ap.add_argument("--repo", help="repo root (default: this script's checkout)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--allow-repeat", action="store_true")
    a = ap.parse_args()
    p = Path(a.file)
    if not p.exists():
        print(f"REFUSED: no such file {p}")
        return 2
    r = send(p.read_text(encoding="utf-8"), lead=a.lead, level=a.level,
             repo=Path(a.repo) if a.repo else None, dry_run=a.dry_run, allow_repeat=a.allow_repeat)
    return int(r.get("exit", 1))


if __name__ == "__main__":
    sys.exit(main())
