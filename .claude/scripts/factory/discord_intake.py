#!/usr/bin/env python3
"""discord_intake.py -- #blue-mao-pow -> docs/loop/INBOX.md, commit, push (charter D12).

    python discord_intake.py [--repo PATH] [--env-file PATH] [--dry-run] [--no-git] [--verbose]

Runs on the hive from a systemd timer every five minutes (systemd/robogame-
inbox-intake.*), and would run unchanged on the desktop. Ported from the
Cosmonaut's botlet.discord_intake (bootleg_botlet, 2026-08-31) with the
same cursor semantics, stdlib only:

  * Channel ids are not secrets (CHANNELS below). The bot token is
    DISCORD_BOT_TOKEN from the environment, else the single variable read by
    NAME from --env-file (the botlet's .env on the hive: the same bot, no
    copy of the secret). No token -> silent no-op, exit 0: intake must never
    stall the loop.
  * Pages are walked newest-first with `before=` from the channel's newest
    message until the cursor (last-seen id) is reached, capped at
    MAX_PER_POLL. A capped (incomplete) fetch is treated as a failed fetch
    for that channel this round: nothing written, cursor unchanged,
    retried next poll -- advancing past an unfetched gap would skip it
    forever. Messages from bots and webhooks (our own pings) are ignored.
  * The cursor (.utmp/factory/intake/discord_seen.json) moves only after
    the INBOX append has landed on disk.
  * Attachments and embeds are recorded on the line as notes with their
    URL (Discord attachment URLs expire in about a day; a screenshot the
    loop needs is fetched at the next wake, not here).
  * Git: pull --rebase --autostash on the current branch, add ONLY
    docs/loop/INBOX.md, commit, push. A git failure leaves the append in
    the working tree and exits 1; the cursor has already moved, which is
    correct: the messages are on disk and the next run's commit carries them.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

API = "https://discord.com/api/v10"
CHANNELS = {"blue-mao-pow": "1549614284684796044"}   # ids are not secrets; the webhook object reported this one on 2026-09-16
PAGE_SIZE = 100
MAX_PER_POLL = 500
TIMEOUT = 15
INBOX_REL = Path("docs/loop/INBOX.md")
STATE_REL = Path(".utmp/factory/intake/discord_seen.json")


def repo_root(start: Path | None = None) -> Path:
    p = (start or Path(__file__).resolve().parent)
    for cand in [p, *p.parents]:
        if (cand / ".git").exists():
            return cand
    return p


def token_from(env_file: Path | None) -> str | None:
    tok = os.environ.get("DISCORD_BOT_TOKEN", "").strip()
    if tok:
        return tok
    if env_file and env_file.exists():
        for line in env_file.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("DISCORD_BOT_TOKEN="):
                return line.split("=", 1)[1].strip().strip("'\"") or None
    return None


def _get(url: str, tok: str) -> list[dict]:
    req = urllib.request.Request(url, headers={"Authorization": f"Bot {tok}", "User-Agent": "robogame-factory-intake/1"})
    with urllib.request.urlopen(req, timeout=TIMEOUT) as r:
        return json.loads(r.read().decode("utf-8"))


def fetch_page(cid: str, tok: str, before: str | None) -> list[dict]:
    q = {"limit": PAGE_SIZE}
    if before:
        q["before"] = before
    return _get(f"{API}/channels/{cid}/messages?{urllib.parse.urlencode(q)}", tok)


def walk(fetch, after_id: str | None) -> tuple[list[dict], bool]:
    """Every message newer than after_id, oldest-first, capped. fetch(before) -> page (newest-first).
    Returns (messages, complete); complete is False only when the cap was hit before the cursor."""
    collected: list[dict] = []
    before: str | None = None
    while True:
        page = fetch(before)
        if not page:
            break
        reached = capped = False
        for m in page:
            if after_id is not None and int(m["id"]) <= int(after_id):
                reached = True
                break
            collected.append(m)
            if len(collected) >= MAX_PER_POLL:
                capped = True
                break
        if reached:
            break
        if capped:
            collected.sort(key=lambda m: int(m["id"]))
            return collected, False
        if len(page) < PAGE_SIZE:
            break
        before = page[-1]["id"]
    collected.sort(key=lambda m: int(m["id"]))
    return collected, True


def format_line(name: str, m: dict) -> str | None:
    a = m.get("author") or {}
    if a.get("bot") or m.get("webhook_id"):
        return None
    body = (m.get("content") or "").strip()
    notes = [f"[attachment: {att.get('filename', 'file')} {att.get('url', '')}".rstrip() + "]" for att in (m.get("attachments") or [])]
    for e in (m.get("embeds") or []):
        t = " ".join(x for x in ((e.get("title") or "").strip(), (e.get("url") or "").strip()) if x)
        if t:
            notes.append(f"[embed: {t}]")
    if not body and not notes:
        return None
    stamp = (m.get("timestamp") or "")[:16]
    line = f"- [discord #{name} {stamp}] {a.get('username', '?')}: {body}".rstrip()
    if notes:
        line += " " + " ".join(notes)
    return re.sub(r"\s*\n\s*", " / ", line)


def git(repo: Path, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)


def commit_and_push(repo: Path, n: int, verbose: bool) -> bool:
    branch = git(repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()
    r = git(repo, "pull", "--rebase", "--autostash", "--quiet", "origin", branch)
    if r.returncode != 0 and verbose:
        print(f"intake: pull --rebase failed ({r.stderr.strip()[:200]}); committing anyway")
    if git(repo, "add", "--", str(INBOX_REL)).returncode != 0:
        return False
    if git(repo, "commit", "-q", "-m", f"factory: INBOX +{n} from discord", "--", str(INBOX_REL)).returncode != 0:
        return False
    r = git(repo, "push", "-q", "origin", f"HEAD:{branch}")
    if r.returncode != 0:
        if verbose:
            print(f"intake: push failed ({r.stderr.strip()[:200]}); committed locally, will retry next run")
        return False
    return True


def poll(repo: Path, tok: str | None, fetch=None, dry_run: bool = False, no_git: bool = False, verbose: bool = False) -> int:
    if tok is None:
        if verbose:
            print("intake: no DISCORD_BOT_TOKEN; no-op")
        return 0
    state = repo / STATE_REL
    seen = json.loads(state.read_text(encoding="utf-8")) if state.exists() else {}
    added: list[str] = []
    cursor: dict[str, str] = {}
    for name, cid in CHANNELS.items():
        f = fetch or (lambda before, cid=cid: fetch_page(cid, tok, before))
        try:
            msgs, complete = walk(f, seen.get(cid))
        except (urllib.error.URLError, urllib.error.HTTPError, OSError, ValueError) as e:
            if verbose:
                print(f"intake {name}: {e}")
            continue
        if not msgs:
            continue
        if not complete:
            if verbose:
                print(f"intake {name}: backlog exceeds {MAX_PER_POLL}/poll; deferring whole channel, cursor unchanged")
            continue
        cursor[cid] = msgs[-1]["id"]
        for m in msgs:
            line = format_line(name, m)
            if line:
                added.append(line)
    if dry_run:
        print(f"DRY: +{len(added)} line(s)" + ("\n" + "\n".join(added) if added else ""))
        return len(added)
    if added:
        inbox = repo / INBOX_REL
        if not inbox.exists():
            if verbose:
                print(f"intake: {inbox} missing (branch not merged yet?); nothing written, cursor unchanged")
            return 0
        with inbox.open("a", encoding="utf-8") as fh:
            fh.write("\n" + "\n".join(added) + "\n")
    if cursor:
        state.parent.mkdir(parents=True, exist_ok=True)
        state.write_text(json.dumps(seen | cursor), encoding="utf-8")
    if added and not no_git:
        ok = commit_and_push(repo, len(added), verbose)
        if verbose:
            print(f"intake: +{len(added)} ({'pushed' if ok else 'NOT pushed'})")
        return len(added) if ok else -len(added)
    if verbose:
        print(f"intake: +{len(added)}")
    return len(added)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo", help="repo root (default: this script's checkout)")
    ap.add_argument("--env-file", help="read DISCORD_BOT_TOKEN by name from this file when not in the environment")
    ap.add_argument("--dry-run", action="store_true", help="fetch and print; write nothing, move no cursor")
    ap.add_argument("--no-git", action="store_true", help="append and move the cursor, but do not commit or push")
    ap.add_argument("--verbose", action="store_true")
    a = ap.parse_args()
    repo = Path(a.repo).resolve() if a.repo else repo_root()
    n = poll(repo, token_from(Path(a.env_file) if a.env_file else None), dry_run=a.dry_run, no_git=a.no_git, verbose=a.verbose)
    return 1 if n < 0 else 0


if __name__ == "__main__":
    sys.exit(main())
