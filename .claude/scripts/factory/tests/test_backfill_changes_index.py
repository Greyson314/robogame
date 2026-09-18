import subprocess, sys, tempfile, unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
SCRIPT = HERE / "backfill_changes_index.py"


def run(*args):
    return subprocess.run([sys.executable, str(SCRIPT), *args], capture_output=True, text=True)


def write_change(changes_dir: Path, n: int, title: str) -> None:
    (changes_dir / f"{n:03d}-x.md").write_text(f"# {n:03d} — {title}\n\nbody\n", encoding="utf-8")


class AllGapsBackfill(unittest.TestCase):
    """CHG-025 / F-044: the table had two gaps below its top row (101-104,
    108-122) that the CHG-017 forward-only mode never reached. --all closes
    a gap anywhere in the table, not just above the top row."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.changes_dir = self.root / "changes"
        self.changes_dir.mkdir()
        # A fixture README with a middle gap: 105 and 100 are in the table,
        # 101-104 have files on disk but no row -- exactly the shift-8
        # finding's shape, at a size a test can read at a glance.
        for n, title in [(105, "Fifth"), (104, "Fourth"), (103, "Third"), (102, "Second"), (101, "First"), (100, "Zeroth")]:
            write_change(self.changes_dir, n, title)
        self.readme = self.root / "README.md"
        self.readme.write_text(
            "# changes index\n\n"
            "## Sessions (newest first)\n\n"
            "| # | Title |\n"
            "|---|---|\n"
            "| 105 | [Fifth](105-x.md) |\n"
            "| 100 | [Zeroth](100-x.md) |\n",
            encoding="utf-8",
        )

    def tearDown(self):
        self.tmp.cleanup()

    def read_table_numbers(self):
        nums = []
        for l in self.readme.read_text(encoding="utf-8").splitlines():
            if l.startswith("| ") and " | [" in l:
                nums.append(int(l.split("|")[1].strip()))
        return nums

    def test_default_mode_does_not_touch_a_gap_below_the_top_row(self):
        # RED-before-the-flag-exists proof, and the CHG-017 behaviour's own
        # regression guard: without --all, 101-104 sit below the table's
        # top row (105), so the forward-only scan must leave them alone.
        r = run("--readme", str(self.readme), "--changes-dir", str(self.changes_dir))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(self.read_table_numbers(), [105, 100])

    def test_all_backfills_the_middle_gap_in_descending_order(self):
        r = run("--all", "--readme", str(self.readme), "--changes-dir", str(self.changes_dir))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(self.read_table_numbers(), [105, 104, 103, 102, 101, 100])

    def test_all_is_idempotent(self):
        run("--all", "--readme", str(self.readme), "--changes-dir", str(self.changes_dir))
        r = run("--all", "--readme", str(self.readme), "--changes-dir", str(self.changes_dir))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("nothing past any gap", r.stdout)
        self.assertEqual(self.read_table_numbers(), [105, 104, 103, 102, 101, 100])

    def test_all_dry_run_writes_nothing(self):
        before = self.readme.read_text(encoding="utf-8")
        r = run("--all", "--dry-run", "--readme", str(self.readme), "--changes-dir", str(self.changes_dir))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("DRY RUN", r.stdout)
        self.assertEqual(self.readme.read_text(encoding="utf-8"), before)


if __name__ == "__main__":
    unittest.main()
