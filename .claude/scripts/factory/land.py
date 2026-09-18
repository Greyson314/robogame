#!/usr/bin/env python3
"""land.py -- THE LANDING CHAIN AS ONE COMMAND (charter D4 (4)-(5), D13).

    python land.py gate --change CHG-007 --branch chg/007-slug --suite PASS --perf N/A --red-team PASS|N/A [--notes "..."]
    python land.py land --change CHG-007 --branch chg/007-slug --title "Pogo tune reaches the game" \\
        --slug pogo-tune --entry entry.md --bullet "short state bullet" \\
        [--fyi "one line for NEEDS-GREY § FYI" | --play play.md] [--ping ping.txt] [--tick "..."] \\
        [--dry-run] [--no-push] [--repo PATH]

`gate` records the three gate verdicts against the branch's FROZEN sha in
.utmp/factory/gate/<CHG>.json. `land` refuses to merge without that file,
without PASS on the suite, PASS or N/A on the red team (N/A only with a
reason in --notes: charter D16b, narrowed 2026-09-17, runs the red team only
for shipped runtime code, NOD-list actions or invariants), and PASS or N/A on
perf (N/A when nothing hot was
touched), or when the branch has moved since the gate ran.

`land`, in order (every step printed; --dry-run computes 1-4 and prints
the rest as the commands it would run, writing nothing):
  1. the working tree must be clean apart from docs/loop/INBOX.md (the
     intake may have appended) -- anything else is a fieldhand's unlanded
     work or an orphan (D13) and the chain refuses;
  2. the gate record: present, PASS, sha == `git rev-parse <branch>`;
  3. main: checked out (the chain checks it out), no merge in progress;
  4. the record: docs/changes/NNN-<slug>.md with NNN = highest + 1, and
     the LOOP-STATE bullet (<= 600 chars, refused longer), and the
     NEEDS-GREY line (FYI) or entry (PLAY; refused when 4 are open);
  5. `git merge --no-ff <branch>`; a conflict aborts the merge and stops;
  6. the record written: the docs/changes entry (the charter's evidence
     block prepended to --entry), the bullet under LOOP-STATE § SHIFT LOG,
     the NEEDS-GREY line/entry;
  6b. docs/changes/README.md's "## Sessions (newest first)" table gains
      `| NNN | [<title>](NNN-<slug>.md) |` as its first row, right under
      the header separator (refuses -- before the merge -- if the table
      is missing: the index is never silently left behind);
  7. `git add` of the NAMED paths only, each checked against
     `git check-ignore`; the commit "factory: record CHG-007";
  8. the ping through ping.send (six-bullet cap, repetition guard) when
     --ping is given;
  9. the tick line appended to .utmp/factory/loop-tick.txt;
 10. `git push origin HEAD:main` unless --no-push.

Nothing here decides anything: the verdicts come from qa-verifier,
perf-checker and red-team; the loop writes the entry text. The chain only
refuses to land what the charter says may not land, and does the twelve
hand-typed steps in one command so they cannot drift.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

BULLET_MAX = 600
PLAY_CAP = 4
CHANGES_DIR = Path("docs/changes")
CHANGES_README = CHANGES_DIR / "README.md"
SESSIONS_HEADING = "## Sessions (newest first)"
STATE = Path("docs/loop/LOOP-STATE.md")
BOARD = Path("docs/loop/NEEDS-GREY.md")
INBOX = Path("docs/loop/INBOX.md")
GATE_DIR = Path(".utmp/factory/gate")
TICK = Path(".utmp/factory/loop-tick.txt")
CHG_RE = re.compile(r"^CHG-\d{3,}$")
NUM_RE = re.compile(r"^(\d+)-.*\.md$")
SEP_RE = re.compile(r"^\|[\s:-]+\|[\s:-]+\|\s*$")


class Refuse(SystemExit):
    def __init__(self, msg: str):
        print(f"REFUSED: {msg}")
        super().__init__(2)


def now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def repo_root(arg: str | None) -> Path:
    if arg:
        return Path(arg).resolve()
    p = Path(__file__).resolve().parent
    for cand in [p, *p.parents]:
        if (cand / ".git").exists():
            return cand
    raise Refuse("not inside a git checkout; pass --repo")


def git(repo: Path, *args: str, check: bool = True) -> str:
    r = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)
    if check and r.returncode != 0:
        raise Refuse(f"git {' '.join(args)} failed: {r.stderr.strip()[:300]}")
    return r.stdout


def sha_of(repo: Path, ref: str) -> str:
    return git(repo, "rev-parse", "--verify", ref).strip()


# ------------------------------------------------------------------ gate
def cmd_gate(a: argparse.Namespace) -> int:
    repo = repo_root(a.repo)
    if not CHG_RE.match(a.change):
        raise Refuse(f"change id must look like CHG-007, got {a.change!r}")
    if a.suite != "PASS":
        raise Refuse(f"suite verdict is {a.suite!r}; only PASS may be recorded (a FAIL is not a gate record, it is work)")
    if a.red_team not in ("PASS", "N/A"):
        raise Refuse(f"red-team verdict is {a.red_team!r}; only PASS or N/A may be recorded (a KILL is not a gate record, it is work)")
    if a.red_team == "N/A" and not (a.notes or "").strip():
        raise Refuse("red-team N/A needs its reason in --notes (charter D16b: tests, fixtures, dev-facing content, docs, generated files, tooling, instruments or coverage)")
    if a.perf not in ("PASS", "N/A"):
        raise Refuse(f"perf verdict must be PASS or N/A, got {a.perf!r}")
    sha = sha_of(repo, a.branch)
    rec = {"change": a.change, "branch": a.branch, "sha": sha, "suite": a.suite, "perf": a.perf,
           "red_team": a.red_team, "notes": a.notes or "", "when": now()}
    p = repo / GATE_DIR / f"{a.change}.json"
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(rec, indent=2), encoding="utf-8")
    print(f"gate recorded: {p.relative_to(repo)} @ {sha[:10]} (suite {a.suite}, perf {a.perf}, red team {a.red_team})")
    return 0


# ------------------------------------------------------------------ land
def next_change_number(repo: Path) -> int:
    hi = 0
    for f in (repo / CHANGES_DIR).glob("*.md"):
        m = NUM_RE.match(f.name)
        if m:
            hi = max(hi, int(m.group(1)))
    return hi + 1


def open_play_count(board_text: str) -> int:
    sec = section(board_text, "## PLAY")
    return len(re.findall(r"^\s*### PT-", sec, re.M))


def section(text: str, heading_prefix: str) -> str:
    """The text of the section whose heading line starts with heading_prefix, up to the next '## '."""
    lines = text.splitlines(keepends=True)
    start = None
    for i, l in enumerate(lines):
        if l.startswith(heading_prefix):
            start = i
            break
    if start is None:
        return ""
    end = len(lines)
    for j in range(start + 1, len(lines)):
        if lines[j].startswith("## "):
            end = j
            break
    return "".join(lines[start:end])


def append_to_section(text: str, heading_prefix: str, addition: str) -> str:
    """Insert addition at the end of the section (before the next '## '), replacing a lone '(empty)' placeholder."""
    lines = text.splitlines(keepends=True)
    start = None
    for i, l in enumerate(lines):
        if l.startswith(heading_prefix):
            start = i
            break
    if start is None:
        raise Refuse(f"heading {heading_prefix!r} not found")
    end = len(lines)
    for j in range(start + 1, len(lines)):
        if lines[j].startswith("## "):
            end = j
            break
    body = lines[start + 1:end]
    # drop the placeholder and trailing blank lines
    body = [l for l in body if l.strip() != "(empty)"]
    while body and not body[-1].strip():
        body.pop()
    add = addition.rstrip("\n") + "\n"
    if not body:
        new_body = ["\n", add, "\n"]
    elif body[-1].lstrip().startswith(("-", "#")):
        new_body = body + [add, "\n"]          # continue a list / follow an entry directly
    else:
        new_body = body + ["\n", add, "\n"]   # a paragraph or a format block: blank line first
    return "".join(lines[:start + 1]) + "".join(new_body) + "".join(lines[end:])


def insert_session_row(text: str, row: str) -> str:
    """Insert `row` as the new first data row of the "## Sessions (newest first)" table
    (directly under the '|---|---|' header separator). Refuses if the table is absent."""
    lines = text.splitlines(keepends=True)
    start = None
    for i, l in enumerate(lines):
        if l.startswith(SESSIONS_HEADING):
            start = i
            break
    if start is None:
        raise Refuse(f"{CHANGES_README.as_posix()} has no {SESSIONS_HEADING!r} table; the index cannot be updated")
    sep = None
    for j in range(start + 1, len(lines)):
        if lines[j].startswith("## "):
            break
        if SEP_RE.match(lines[j].strip()):
            sep = j
            break
    if sep is None:
        raise Refuse(f"{CHANGES_README.as_posix()}'s {SESSIONS_HEADING!r} section has no header separator row")
    add = row.rstrip("\n") + "\n"
    return "".join(lines[:sep + 1]) + add + "".join(lines[sep + 1:])


def cmd_land(a: argparse.Namespace) -> int:
    repo = repo_root(a.repo)
    dry = a.dry_run
    if not CHG_RE.match(a.change):
        raise Refuse(f"change id must look like CHG-007, got {a.change!r}")
    if not re.match(r"^[a-z0-9][a-z0-9-]{1,60}$", a.slug):
        raise Refuse("slug must be lowercase kebab-case")
    if len(a.bullet) > BULLET_MAX:
        raise Refuse(f"LOOP-STATE bullet is {len(a.bullet)} chars > {BULLET_MAX} (D1); the record is the docs/changes entry, the bullet points at it")
    if a.fyi and a.play:
        raise Refuse("give --fyi or --play, not both")
    entry_path = Path(a.entry)
    if not entry_path.exists():
        raise Refuse(f"--entry file not found: {entry_path}")
    play_text = Path(a.play).read_text(encoding="utf-8") if a.play else ""
    if a.play and not play_text.lstrip().startswith("### PT-"):
        raise Refuse("--play file must start with '### PT-NNN — ...' (NEEDS-GREY § PLAY format)")

    # 1. clean tree apart from INBOX
    dirty = [l for l in git(repo, "status", "--porcelain").splitlines() if l.strip()]
    stray = [l for l in dirty if not l[3:].strip().strip('"').endswith(str(INBOX).replace("\\", "/"))]
    if stray:
        raise Refuse("working tree has uncommitted paths other than INBOX (a fieldhand's unlanded work, or an orphan — absorb or archive first):\n  " + "\n  ".join(stray[:20]))

    # 2. gate record
    gp = repo / GATE_DIR / f"{a.change}.json"
    if not gp.exists():
        raise Refuse(f"no gate record at {gp.relative_to(repo)}; run `land.py gate` after the suite, perf and red team")
    gate = json.loads(gp.read_text(encoding="utf-8"))
    if gate.get("branch") != a.branch:
        raise Refuse(f"gate record is for branch {gate.get('branch')!r}, not {a.branch!r}")
    if gate.get("suite") != "PASS" or gate.get("red_team") not in ("PASS", "N/A") or gate.get("perf") not in ("PASS", "N/A"):
        raise Refuse(f"gate verdicts not passing: {gate}")
    head = sha_of(repo, a.branch)
    if gate.get("sha") != head:
        raise Refuse(f"branch {a.branch} moved since the gate ran (gate {gate.get('sha', '')[:10]}, now {head[:10]}): re-run the gate on the frozen artifact")

    # 3. main
    gitdir = Path(git(repo, "rev-parse", "--git-dir").strip())
    if not gitdir.is_absolute():
        gitdir = repo / gitdir
    if (gitdir / "MERGE_HEAD").exists():
        raise Refuse("a merge is already in progress")
    cur = git(repo, "rev-parse", "--abbrev-ref", "HEAD").strip()
    if cur != "main" and not dry:
        git(repo, "checkout", "-q", "main")      # the record's number and anchors come from main, not the branch
        cur = "main"

    # 4. the record pieces
    n = next_change_number(repo)
    entry_rel = CHANGES_DIR / f"{n:03d}-{a.slug}.md"
    if (repo / entry_rel).exists():
        raise Refuse(f"{entry_rel.as_posix()} already exists")
    state_text = (repo / STATE).read_text(encoding="utf-8")
    if "## SHIFT LOG" not in state_text:
        raise Refuse("LOOP-STATE has no '## SHIFT LOG' section")
    board_text = (repo / BOARD).read_text(encoding="utf-8")
    if a.play and open_play_count(board_text) >= PLAY_CAP:
        raise Refuse(f"NEEDS-GREY § PLAY already holds {PLAY_CAP} open questions (D12 backpressure); land as FYI-less and queue the question later, or wait for a verdict")
    readme_path = repo / CHANGES_README
    if not readme_path.exists():
        raise Refuse(f"{CHANGES_README.as_posix()} not found")
    readme_text = readme_path.read_text(encoding="utf-8")
    session_row = f"| {n:03d} | [{a.title.replace('|', '/')}]({entry_rel.name}) |"
    readme_new = insert_session_row(readme_text, session_row)   # refuses here, before the merge, if the table is missing
    stamp = now()
    evidence = (
        f"# {n:03d} — {a.title} (LOG-{n:03d})\n\n"
        f"Landed by the factory {stamp}. Change {a.change}, branch `{a.branch}` @ {head[:10]}. "
        f"Gate: suite {gate['suite']}, perf {gate['perf']}, red team {gate['red_team']}"
        + (f" — {gate['notes']}" if gate.get("notes") else "") + ".\n\n"
    )
    entry_text = evidence + entry_path.read_text(encoding="utf-8").rstrip("\n") + "\n"
    bullet = f"- {stamp} — {a.change} {a.title} — {entry_rel.as_posix()} — {a.bullet}"
    fyi_line = f"- {stamp[:10]} {a.change} {a.title} — {entry_rel.as_posix()}" + (f" — {a.fyi}" if a.fyi else "")
    merge_msg = f"factory: land {a.change} — {a.title}"
    record_msg = f"factory: record {a.change} — {entry_rel.name}"
    tick_line = f"{stamp} land {a.change}: {a.tick or a.title}"

    print(f"[1] tree clean (INBOX aside: {len(dirty) - len(stray)} line(s))")
    print(f"[2] gate {gp.relative_to(repo).as_posix()} PASS @ {head[:10]}")
    print(f"[3] on {cur}; will land on main")
    print(f"[4] record: {entry_rel.as_posix()} · bullet {len(bullet)} chars · " + ("PLAY entry" if a.play else "FYI line") + f" · README row {session_row}")
    if dry:
        print("DRY RUN — would run:")
        print(f"    git checkout main && git merge --no-ff --no-edit -m {merge_msg!r} {a.branch}")
        print(f"    write {entry_rel.as_posix()}; append bullet to {STATE.as_posix()} § SHIFT LOG; append to {BOARD.as_posix()} § {'PLAY' if a.play else 'FYI'}")
        print(f"    insert into {CHANGES_README.as_posix()}: {session_row}")
        print(f"    git add -- {entry_rel.as_posix()} {STATE.as_posix()} {BOARD.as_posix()} {CHANGES_README.as_posix()} && git commit -m {record_msg!r}")
        if a.ping:
            print(f"    ping.send(<{a.ping}>, lead='landed {a.change}')")
        print(f"    append {tick_line!r} to {TICK.as_posix()}")
        print("    " + ("(no push)" if a.no_push else "git push origin HEAD:main"))
        return 0

    # 5. merge
    r = subprocess.run(["git", "-C", str(repo), "merge", "--no-ff", "--no-edit", "-m", merge_msg, a.branch], capture_output=True, text=True)
    if r.returncode != 0:
        subprocess.run(["git", "-C", str(repo), "merge", "--abort"], capture_output=True)
        raise Refuse(f"merge of {a.branch} into main failed and was aborted: {r.stderr.strip()[:300]}")
    print(f"[5] merged {a.branch} into main")

    # 6. write the record. F-061: re-read the three shared files AFTER the
    # merge; the pre-merge texts above are for the refusals only, and writing
    # them back would drop whatever the branch itself changed in these files
    # (CHG-025's 19 index rows were lost this way).
    state_text = (repo / STATE).read_text(encoding="utf-8")
    board_text = (repo / BOARD).read_text(encoding="utf-8")
    readme_new = insert_session_row(readme_path.read_text(encoding="utf-8"), session_row)
    (repo / entry_rel).write_text(entry_text, encoding="utf-8")
    (repo / STATE).write_text(append_to_section(state_text, "## SHIFT LOG", bullet), encoding="utf-8")
    board_new = append_to_section(board_text, "## PLAY" if a.play else "## FYI", play_text.rstrip("\n") if a.play else fyi_line)
    (repo / BOARD).write_text(board_new, encoding="utf-8")
    print("[6] record written")

    # 6b. the changes index keeps up with the ledger
    readme_path.write_text(readme_new, encoding="utf-8")
    print(f"[6b] {CHANGES_README.as_posix()} row inserted: {session_row}")

    # 7. add named paths, refuse ignored
    named = [str(entry_rel.as_posix()), str(STATE.as_posix()), str(BOARD.as_posix()), str(CHANGES_README.as_posix())]
    for p in named:
        if subprocess.run(["git", "-C", str(repo), "check-ignore", "-q", p]).returncode == 0:
            raise Refuse(f"{p} is git-ignored; the chain never commits ignored paths")
    git(repo, "add", "--", *named)
    git(repo, "commit", "-q", "-m", record_msg, "--", *named)
    print(f"[7] committed: {record_msg}")

    # 8. ping
    if a.ping:
        import ping as pingmod  # noqa: E402
        res = pingmod.send(Path(a.ping).read_text(encoding="utf-8"), lead=f"landed {a.change}", repo=repo)
        print(f"[8] ping: {res.get('status')}")
    # 9. tick
    tp = repo / TICK
    tp.parent.mkdir(parents=True, exist_ok=True)
    with tp.open("a", encoding="utf-8") as fh:
        fh.write(tick_line + "\n")
    print("[9] tick stamped")
    # 10. push
    if a.no_push:
        print("[10] no push (--no-push)")
    else:
        git(repo, "push", "-q", "origin", "HEAD:main")
        print("[10] pushed origin main")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("gate", help="record the three gate verdicts against the branch's frozen sha")
    g.add_argument("--change", required=True)
    g.add_argument("--branch", required=True)
    g.add_argument("--suite", required=True)
    g.add_argument("--perf", required=True)
    g.add_argument("--red-team", dest="red_team", required=True)
    g.add_argument("--notes", default="")
    g.add_argument("--repo")
    g.set_defaults(fn=cmd_gate)
    l = sub.add_parser("land", help="merge, record, ping, tick, push — one command")
    l.add_argument("--change", required=True)
    l.add_argument("--branch", required=True)
    l.add_argument("--title", required=True)
    l.add_argument("--slug", required=True)
    l.add_argument("--entry", required=True, help="file: the docs/changes entry body (the evidence header is prepended)")
    l.add_argument("--bullet", required=True, help=f"LOOP-STATE § SHIFT LOG bullet, <= {BULLET_MAX} chars")
    l.add_argument("--fyi", default="", help="one line for NEEDS-GREY § FYI (AUTO class)")
    l.add_argument("--play", help="file: a NEEDS-GREY § PLAY entry (feel change)")
    l.add_argument("--ping", help="file: text for ping.py (optional)")
    l.add_argument("--tick", default="")
    l.add_argument("--dry-run", action="store_true")
    l.add_argument("--no-push", action="store_true")
    l.add_argument("--repo")
    l.set_defaults(fn=cmd_land)
    a = ap.parse_args()
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())
