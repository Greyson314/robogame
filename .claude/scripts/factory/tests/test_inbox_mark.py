import subprocess, sys, tempfile, unittest
from pathlib import Path
HERE = Path(__file__).resolve().parents[1]
TOOL = HERE / "inbox_mark.py"

HEAD_TEXT = ("# INBOX\n\n"
             "- [2026-09-17T01:00:00Z via /inbox] decide D-001: it can.\n"
             "  (scribe: relayed by hand.)\n"
             "- [2026-09-19T05:34:30Z via /inbox] decide D-009: delete\n"
             "  (scribe: chosen from the offered options.) → recorded verbatim; D-009 closed.\n"
             "- [2026-09-19T06:00:00Z via /inbox] the hook feels floaty\n")


def sh(repo, *args, check=True):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, check=check)


def mark(repo, *args):
    return subprocess.run([sys.executable, str(TOOL), *args, "--repo", str(repo)], capture_output=True, text=True, encoding="utf-8")


class InboxMark(unittest.TestCase):
    """LESSONS 18: the loop once committed a STALE working copy of INBOX.md and deleted six of Grey's
    lines from main. The tool exists so that can not happen: it edits from HEAD, never from the checkout,
    and refuses any result that loses a line."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.repo = Path(self.tmp.name)
        sh(self.repo, "init", "-q", "-b", "main")
        sh(self.repo, "config", "user.email", "t@t"); sh(self.repo, "config", "user.name", "t")
        (self.repo / "docs/loop").mkdir(parents=True)
        self.inbox = self.repo / "docs/loop/INBOX.md"
        self.inbox.write_bytes(HEAD_TEXT.encode("utf-8"))
        sh(self.repo, "add", "-A"); sh(self.repo, "commit", "-q", "-m", "seed")

    def tearDown(self):
        self.tmp.cleanup()

    def head(self):
        return sh(self.repo, "show", "HEAD:docs/loop/INBOX.md").stdout if False else \
            subprocess.run(["git", "-C", str(self.repo), "show", "HEAD:docs/loop/INBOX.md"], capture_output=True).stdout.decode("utf-8")

    def test_marks_a_single_line_entry_and_commits_only_an_insertion(self):
        r = mark(self.repo, "--match", "hook feels floaty", "--note", "PT-004 verdict recorded")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("the hook feels floaty → PT-004 verdict recorded\n", self.head())
        self.assertEqual(self.head().replace(" → PT-004 verdict recorded", ""), HEAD_TEXT)   # nothing else moved
        self.assertEqual(sh(self.repo, "status", "--porcelain").stdout, "")

    def test_marks_a_multi_line_entry_on_its_last_line(self):
        r = mark(self.repo, "--match", "decide D-001", "--note", "D-001 closed")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("  (scribe: relayed by hand.) → D-001 closed\n- [2026-09-19T05:34:30Z", self.head())

    def test_a_stale_working_copy_cannot_delete_lines(self):
        # the scribe moved main under the checkout: the working file is OLD (two lines short)
        stale = "".join(HEAD_TEXT.splitlines(keepends=True)[:4])
        self.inbox.write_bytes(stale.encode("utf-8"))
        r = mark(self.repo, "--match", "decide D-001", "--note", "D-001 closed")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        for line in HEAD_TEXT.splitlines():
            if "D-001" not in line and "relayed by hand" not in line:
                self.assertIn(line + "\n", self.head(), "a line from HEAD was lost")
        self.assertIn("the hook feels floaty\n", self.inbox.read_bytes().decode("utf-8"))      # the checkout is current again

    def test_refuses_an_entry_already_marked(self):
        r = mark(self.repo, "--match", "decide D-009", "--note", "again")
        self.assertNotEqual(r.returncode, 0)
        self.assertEqual(self.head(), HEAD_TEXT)

    def test_refuses_an_ambiguous_or_missing_match(self):
        self.assertNotEqual(mark(self.repo, "--match", "via /inbox", "--note", "x").returncode, 0)
        self.assertNotEqual(mark(self.repo, "--match", "no such line", "--note", "x").returncode, 0)
        self.assertEqual(self.head(), HEAD_TEXT)

    def test_dry_run_writes_nothing(self):
        r = mark(self.repo, "--match", "hook feels floaty", "--note", "noted", "--dry-run")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(self.head(), HEAD_TEXT)
        self.assertEqual(sh(self.repo, "status", "--porcelain").stdout, "")


if __name__ == "__main__":
    unittest.main()
