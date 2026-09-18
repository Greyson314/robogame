#!/usr/bin/env python3
"""backfill_changes_index.py -- catch-up for the docs/changes/README.md
"## Sessions (newest first)" table (CHG-017, F-036; --all added by CHG-025, F-044).

Run from the clone root:

    python .claude/scripts/factory/backfill_changes_index.py [--dry-run] [--all]
        [--readme docs/changes/README.md] [--changes-dir docs/changes]

Idempotent: a number already in the table is never re-added.

Default mode catches the table's row-count up to the ledger, forward only --
every docs/changes/NNN-slug.md with NNN >= 100 AND NNN greater than the
table's current top row gets a new row, inserted newest-first directly
under the table's '|---|---|' header separator. A run with nothing new
past the table's top writes nothing.

--all closes every gap, not only the top: every docs/changes/NNN-slug.md
with NNN >= 100 that has no row ANYWHERE in the table gets one, threaded in
at its sorted position so the table stays descending throughout (CHG-025,
F-044: 101-104 and 108-122 had files but no row, below the table's top and
so untouched by the default mode's forward-only reach).

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


def build_rows(changes_dir: Path, table_max: int, skip: set[int], all_gaps: bool = False) -> list[tuple[int, str]]:
    """[(NNN, row_text), ...] for every NNN >= 100 under changes_dir, not
    already in `skip`, newest first. Default mode restricts this to NNN >
    table_max (the table's current top row); with all_gaps=True every
    missing NNN >= 100 qualifies, wherever in the table the gap sits."""
    found: dict[int, Path] = {}
    for f in changes_dir.glob("*.md"):
        m = FILE_RE.match(f.name)
        if not m:
            continue
        n = int(m.group(1))
        if n < 100 or n in skip:
            continue
        if not all_gaps and n <= table_max:
            continue
        found[n] = f

    rows = []
    for n in sorted(found, reverse=True):
        f = found[n]
        # F-056: an entry may open with an HTML comment or blank lines; the
        # title is its first `#` heading, else its first line.
        lines = f.read_text(encoding="utf-8").splitlines()
        first_line = next((ln for ln in lines if ln.lstrip().startswith("#")), lines[0] if lines else "")
        title = title_from_heading(first_line, n)
        rows.append((n, f"| {n:03d} | [{title}]({f.name}) |"))
    return rows


def insert_rows(lines: list[str], sep: int, rows: list[tuple[int, str]], all_gaps: bool) -> str:
    """Return the README text with `rows` (already sorted descending by
    build_rows) inserted into the Sessions table.

    Default mode: every row's NNN exceeds every existing row's (build_rows
    guaranteed it), so appending them, in order, directly below the header
    separator keeps the whole table sorted -- the CHG-017 behaviour.

    --all mode: a row can belong anywhere in the table, so each is threaded
    in at its sorted position among the existing rows instead of only at
    the top.
    """
    if not all_gaps:
        addition = "".join(row + "\n" for _n, row in rows)
        return "".join(lines[:sep + 1]) + addition + "".join(lines[sep + 1:])

    end = len(lines)
    for j in range(sep + 1, len(lines)):
        if lines[j].startswith("## "):
            end = j
            break

    merged: list[str] = []
    pending = list(rows)
    for line in lines[sep + 1:end]:
        m = ROW_NUM_RE.match(line)
        cur = int(m.group(1)) if m else None
        while pending and cur is not None and pending[0][0] > cur:
            merged.append(pending.pop(0)[1] + "\n")
        merged.append(line)
    for _n, row in pending:
        merged.append(row + "\n")

    return "".join(lines[:sep + 1]) + "".join(merged) + "".join(lines[end:])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--changes-dir", default="docs/changes")
    ap.add_argument("--readme", default=None, help="default: <changes-dir>/README.md")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--all", action="store_true", help="close every gap, not only the top (CHG-025)")
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
    rows = build_rows(changes_dir, table_max, skip, all_gaps=a.all)

    if not rows:
        scope = "any gap" if a.all else f"the table's top row ({table_max})"
        print(f"{readme_path}: nothing past {scope} to add")
        return 0

    for n, row in rows:
        print(row)

    if a.dry_run:
        print(f"DRY RUN — would insert {len(rows)} row(s) into {readme_path}")
        return 0

    new_text = insert_rows(lines, sep, rows, a.all)
    readme_path.write_text(new_text, encoding="utf-8", newline="\n")   # keep the file's own LF endings; don't let Windows text-mode widen them to CRLF
    print(f"inserted {len(rows)} row(s) into {readme_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
