#!/usr/bin/env python3
"""backfill_changes_index.py -- one-off catch-up for the docs/changes/README.md
"## Sessions (newest first)" table (CHG-017, F-036).

Run from the clone root:

    python .claude/scripts/factory/backfill_changes_index.py [--dry-run]
        [--readme docs/changes/README.md] [--changes-dir docs/changes]

Idempotent: catches the table's row-count UP to the ledger, forward only --
every docs/changes/NNN-slug.md with NNN >= 100 AND NNN greater than the
table's current top row gets a new row (skipped again if somehow already
present), inserted newest-first directly under the table's '|---|---|'
header separator. A run with nothing new past the table's top writes
nothing. (The table also has older, pre-existing gaps below its top row --
e.g. 108-122 have files but no row, from before this tool existed -- this
backfill does not touch those; it closes the drift F-036 found, not every
historical omission.)

Row text: a file's first heading line ("# NNN — Title" or "# NNN - Title")
with that "# NNN <dash> " prefix stripped, and a trailing " (LOG-NNN)"
suffix (added to headings from session 170 on) stripped too. Nothing
outside the table is touched.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# A cp1252 console cannot print every docs/changes heading (e.g. "shōga" in
# 147's title killed a dry-run print here the same way it killed ping.py's
# report, 2026-09-17): print UTF-8 wherever stdout allows it.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except (AttributeError, ValueError):
    pass

SESSIONS_HEADING = "## Sessions (newest first)"
SEP_RE = re.compile(r"^\|[\s:-]+\|[\s:-]+\|\s*$")
FILE_RE = re.compile(r"^(\d+)-.*\.md$")
ROW_NUM_RE = re.compile(r"^\|\s*(\d+)\s*\|")


class Refuse(SystemExit):
    def __init__(self, msg: str):
        print(f"REFUSED: {msg}")
        super().__init__(2)


def find_table(lines: list[str]) -> tuple[int, int]:
    """Return (heading_index, separator_index) of the Sessions table, or refuse."""
    start = None
    for i, l in enumerate(lines):
        if l.startswith(SESSIONS_HEADING):
            start = i
            break
    if start is None:
        raise Refuse(f"no {SESSIONS_HEADING!r} table found")
    sep = None
    for j in range(start + 1, len(lines)):
        if lines[j].startswith("## "):
            break
        if SEP_RE.match(lines[j].strip()):
            sep = j
            break
    if sep is None:
        raise Refuse(f"{SESSIONS_HEADING!r} section has no header separator row")
    return start, sep


def existing_numbers(lines: list[str], sep: int) -> set[int]:
    nums = set()
    for l in lines[sep + 1:]:
        if l.startswith("## "):
            break
        m = ROW_NUM_RE.match(l)
        if m:
            nums.add(int(m.group(1)))
    return nums


def title_from_heading(first_line: str, n: int) -> str:
    first_line = first_line.strip()
    for dash in ("—", "-"):
        prefix = f"# {n:03d} {dash} "
        if first_line.startswith(prefix):
            title = first_line[len(prefix):]
            break
    else:
        title = first_line.lstrip("#").strip()   # fallback: an unexpected heading shape
    title = re.sub(rf"\s*\(LOG-{n:03d}\)\s*$", "", title).strip()
    return title.replace("|", "/")


def build_rows(changes_dir: Path, table_max: int, skip: set[int]) -> list[tuple[int, str]]:
    """[(NNN, row_text), ...] for every NNN >= 100 past the table's current top row
    (table_max) under changes_dir, not already in `skip`, newest first."""
    found: dict[int, Path] = {}
    for f in changes_dir.glob("*.md"):
        m = FILE_RE.match(f.name)
        if not m:
            continue
        n = int(m.group(1))
        if n >= 100 and n > table_max and n not in skip:
            found[n] = f

    rows = []
    for n in sorted(found, reverse=True):
        f = found[n]
        first_line = f.read_text(encoding="utf-8").splitlines()[0] if f.stat().st_size else ""
        title = title_from_heading(first_line, n)
        rows.append((n, f"| {n:03d} | [{title}]({f.name}) |"))
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--changes-dir", default="docs/changes")
    ap.add_argument("--readme", default=None, help="default: <changes-dir>/README.md")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    changes_dir = Path(a.changes_dir)
    readme_path = Path(a.readme) if a.readme else changes_dir / "README.md"
    if not changes_dir.is_dir():
        raise Refuse(f"{changes_dir} is not a directory")
    if not readme_path.exists():
        raise Refuse(f"{readme_path} not found")

    text = readme_path.read_text(encoding="utf-8")
    lines = text.splitlines(keepends=True)
    _heading, sep = find_table(lines)
    skip = existing_numbers(lines, sep)
    table_max = max(skip, default=0)
    rows = build_rows(changes_dir, table_max, skip)

    if not rows:
        print(f"{readme_path}: nothing past the table's top row ({table_max}) to add")
        return 0

    for n, row in rows:
        print(row)

    if a.dry_run:
        print(f"DRY RUN — would insert {len(rows)} row(s) into {readme_path}")
        return 0

    addition = "".join(row + "\n" for _n, row in rows)
    new_text = "".join(lines[:sep + 1]) + addition + "".join(lines[sep + 1:])
    readme_path.write_text(new_text, encoding="utf-8", newline="\n")   # keep the file's own LF endings; don't let Windows text-mode widen them to CRLF
    print(f"inserted {len(rows)} row(s) into {readme_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
