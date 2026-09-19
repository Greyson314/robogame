#!/usr/bin/env python3
"""inbox_mark.py -- mark ONE INBOX entry handled, without ever losing a line (LESSONS 18).

    python inbox_mark.py --match "<text unique to the entry>" --note "<what the loop did>" [--push] [--dry-run] [--repo PATH]

The scribe's /inbox commits onto main with plumbing, so local main moves UNDER the checkout and the
working copy of docs/loop/INBOX.md goes stale; committing an edit of that stale file deletes Grey's
newer lines (it did, 1feebf82). So this tool never reads the working copy: it takes the file from
`git show HEAD:`, appends " -> note" (with the real arrow) to the LAST line of the one entry that
matches, proves the result differs from HEAD by exactly that suffix, writes it over the working
copy, and commits only that path. An entry is a line starting "- [" plus the lines under it.
Refuses: no match, more than one match, an entry that already carries an arrow, a staged diff that
is not exactly one changed line. The working copy is REPLACED by HEAD's content plus the mark.
"""
import argparse, subprocess, sys
from pathlib import Path

REL = "docs/loop/INBOX.md"
ARROW = " → "


def git(repo, *args, text=True):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=text, encoding="utf-8" if text else None)


def die(msg):
    sys.stderr.write("inbox_mark: REFUSED: " + msg + "\n"); sys.exit(1)


def marked(head_text, match, note):
    lines = head_text.splitlines(keepends=True)
    starts = [i for i, l in enumerate(lines) if l.startswith("- [")]
    hits = []
    for n, i in enumerate(starts):
        end = starts[n + 1] if n + 1 < len(starts) else len(lines)
        if match in "".join(lines[i:end]):
            hits.append((i, end))
    if not hits: die(f"no entry contains {match!r}")
    if len(hits) > 1: die(f"{len(hits)} entries contain {match!r}; give a longer --match")
    i, end = hits[0]
    if ARROW.strip() in "".join(lines[i:end]): die("that entry already carries a handled arrow")
    last = max(k for k in range(i, end) if lines[k].strip())
    body = lines[last].rstrip("\r\n"); eol = lines[last][len(body):] or "\n"
    lines[last] = body.rstrip() + ARROW + note.strip() + eol
    new_text = "".join(lines)
    # the proof: undoing the one suffix gives HEAD back, byte for byte (modulo trailing spaces on that line)
    undone = lines[:]; undone[last] = body.rstrip() + eol
    if "".join(undone) != head_text.replace(body + eol, body.rstrip() + eol, 1): die("internal: the edit is not a pure suffix")
    return new_text


def main():
    for stream in (sys.stdout, sys.stderr):                     # Windows consoles default to cp1252 (F-043)
        if hasattr(stream, "reconfigure"): stream.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--match", required=True); ap.add_argument("--note", required=True)
    ap.add_argument("--push", action="store_true"); ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--repo", default=".")
    a = ap.parse_args()
    repo = Path(a.repo).resolve()
    if "\n" in a.note: die("--note must be one line")
    show = git(repo, "show", f"HEAD:{REL}", text=False)
    if show.returncode != 0: die(f"cannot read HEAD:{REL}")
    head_text = show.stdout.decode("utf-8")
    new_text = marked(head_text, a.match, a.note)
    if a.dry_run:
        print("DRY: would mark ->", [l for l in new_text.splitlines() if l.endswith(ARROW + a.note.strip())][0]); return
    (repo / REL).write_bytes(new_text.encode("utf-8"))
    if git(repo, "add", "--", REL).returncode != 0: die("git add failed")
    stat = git(repo, "diff", "--cached", "--numstat", "--", REL).stdout.split()
    if stat[:2] != ["1", "1"]:
        git(repo, "restore", "--staged", "--worktree", "--", REL); die(f"the staged diff is {stat[:2]}, not one line changed; restored")
    c = git(repo, "commit", "-q", "--only", "-m", f"factory: INBOX mark: {a.note.strip()[:120]}", "--", REL)
    if c.returncode != 0: die("commit failed: " + c.stderr)
    print("marked:", a.match, "->", a.note.strip())
    if a.push:
        p = git(repo, "push", "-q", "origin", "HEAD")
        if p.returncode != 0: die("push failed: " + p.stderr)


if __name__ == "__main__":
    main()
