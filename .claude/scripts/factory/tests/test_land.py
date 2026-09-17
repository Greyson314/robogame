import os, subprocess, sys, tempfile, unittest
from pathlib import Path
HERE = Path(__file__).resolve().parents[1]
LAND = HERE / "land.py"

STATE = "# LOOP-STATE\n\n## THE BUILD\n\nmain @ x.\n\n## SHIFT LOG (bullets)\n\n(empty)\n\n## NEXT ITEM\n\nHANDOFF (1).\n"
BOARD = ("# NEEDS-GREY\n\n## APPROVE (0/4)\n\n(empty)\n\n## PLAY (0/4) — one question per build.\n\n    ### PT-NNN — format\n    Build: ...\n\n(empty)\n\n"
         "## BUY (0)\n\n(empty)\n\n## DECIDE\n\n- D-001 x\n\n## FYI — AUTO landings.\n\n(empty)\n\n## ANSWERED\n\n(empty)\n")
CHANGES_README = "# changes index\n\n## Sessions (newest first)\n\n| # | Title |\n|---|---|\n| 001 | [First](001-first.md) |\n"


def sh(repo, *args, check=True):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True, check=check)


def land(repo, *args):
    return subprocess.run([sys.executable, str(LAND), *args, "--repo", str(repo)], capture_output=True, text=True)


class Chain(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.repo = Path(self.tmp.name)
        sh(self.repo, "init", "-q", "-b", "main")
        sh(self.repo, "config", "user.email", "t@t"); sh(self.repo, "config", "user.name", "t")
        (self.repo / "docs/changes").mkdir(parents=True); (self.repo / "docs/loop").mkdir()
        (self.repo / "docs/changes/001-first.md").write_text("# 001\n", encoding="utf-8"); (self.repo / "docs/changes/README.md").write_text(CHANGES_README, encoding="utf-8")
        (self.repo / "docs/loop/LOOP-STATE.md").write_text(STATE, encoding="utf-8"); (self.repo / "docs/loop/NEEDS-GREY.md").write_text(BOARD, encoding="utf-8")
        (self.repo / "docs/loop/INBOX.md").write_text("# INBOX\n", encoding="utf-8"); (self.repo / ".gitignore").write_text(".utmp/\n", encoding="utf-8")
        sh(self.repo, "add", "-A"); sh(self.repo, "commit", "-q", "-m", "seed")
        sh(self.repo, "checkout", "-q", "-b", "chg/007-pogo")
        (self.repo / "Pogo.cs").write_text("class Pogo {}\n", encoding="utf-8"); sh(self.repo, "add", "-A"); sh(self.repo, "commit", "-q", "-m", "feat: pogo")
        sh(self.repo, "checkout", "-q", "main")
        self.scratch = tempfile.TemporaryDirectory()                # the loop's entry/play drafts live OUTSIDE the repo
        self.entry = Path(self.scratch.name) / "entry.md"; self.entry.write_text("## What\n\nPogo tune now reaches the game.\n", encoding="utf-8")

    def tearDown(self):
        self.scratch.cleanup(); self.tmp.cleanup()

    def gate(self, **kw):
        args = {"--suite": "PASS", "--perf": "N/A", "--red-team": "PASS"} | kw
        return land(self.repo, "gate", "--change", "CHG-007", "--branch", "chg/007-pogo", *sum(([k, v] for k, v in args.items()), []))

    def land_args(self, *extra):
        return ["land", "--change", "CHG-007", "--branch", "chg/007-pogo", "--title", "Pogo tune", "--slug", "pogo-tune",
                "--entry", str(self.entry), "--bullet", "pogo tune lands; test PogoTuneTests.ApplyReachesBlueprint green", "--no-push", *extra]

    def test_gate_refuses_fail_and_records_pass(self):
        r = self.gate(**{"--suite": "FAIL"}); self.assertEqual(r.returncode, 2); self.assertIn("REFUSED", r.stdout)
        r = self.gate(); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertTrue((self.repo / ".utmp/factory/gate/CHG-007.json").exists())

    def test_gate_red_team_na_needs_a_reason_then_lands(self):
        # charter D16b (narrowed 2026-09-17): the red team runs only for shipped runtime code, NOD-list
        # actions or invariants; elsewhere the gate records N/A with the reason in the notes.
        r = self.gate(**{"--red-team": "KILL"}); self.assertEqual(r.returncode, 2); self.assertIn("only PASS or N/A", r.stdout)
        r = self.gate(**{"--red-team": "N/A"}); self.assertEqual(r.returncode, 2); self.assertIn("needs its reason", r.stdout)
        r = self.gate(**{"--red-team": "N/A", "--notes": "tooling only: charter D16b"}); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        r = land(self.repo, *self.land_args()); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        entry = (self.repo / "docs/changes/002-pogo-tune.md").read_text(encoding="utf-8"); self.assertIn("red team N/A", entry)

    def test_land_refuses_without_gate(self):
        r = land(self.repo, *self.land_args()); self.assertEqual(r.returncode, 2); self.assertIn("no gate record", r.stdout)

    def test_dry_run_changes_nothing(self):
        self.gate(); before = sh(self.repo, "rev-parse", "HEAD").stdout
        r = land(self.repo, *self.land_args("--dry-run")); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("DRY RUN", r.stdout); self.assertIn("docs/changes/002-pogo-tune.md", r.stdout)
        self.assertEqual(before, sh(self.repo, "rev-parse", "HEAD").stdout)
        self.assertFalse((self.repo / "docs/changes/002-pogo-tune.md").exists())

    def test_land_merges_records_and_ticks(self):
        self.gate()
        r = land(self.repo, *self.land_args("--fyi", "auto: coverage", "--tick", "t"))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        log = sh(self.repo, "log", "--oneline", "-3").stdout
        self.assertIn("factory: record CHG-007", log); self.assertIn("factory: land CHG-007", log)
        self.assertTrue((self.repo / "Pogo.cs").exists())
        entry = (self.repo / "docs/changes/002-pogo-tune.md").read_text(encoding="utf-8")
        self.assertTrue(entry.startswith("# 002 — Pogo tune (LOG-002)")); self.assertIn("red team PASS", entry); self.assertIn("Pogo tune now reaches", entry)
        state = (self.repo / "docs/loop/LOOP-STATE.md").read_text(encoding="utf-8")
        self.assertIn("— CHG-007 Pogo tune — docs/changes/002-pogo-tune.md —", state)
        self.assertNotIn("## SHIFT LOG (bullets)\n\n(empty)", state); self.assertIn("## NEXT ITEM", state)
        board = (self.repo / "docs/loop/NEEDS-GREY.md").read_text(encoding="utf-8")
        self.assertIn("CHG-007 Pogo tune — docs/changes/002-pogo-tune.md — auto: coverage", board.split("## FYI")[1].split("## ANSWERED")[0])
        self.assertIn("land CHG-007: t", (self.repo / ".utmp/factory/loop-tick.txt").read_text(encoding="utf-8"))
        self.assertEqual(sh(self.repo, "status", "--porcelain").stdout.strip(), "")

    def test_land_refuses_moved_branch_long_bullet_dirty_tree_and_play_cap(self):
        self.gate()
        sh(self.repo, "checkout", "-q", "chg/007-pogo"); (self.repo / "Pogo.cs").write_text("class Pogo { int x; }\n", encoding="utf-8")
        sh(self.repo, "commit", "-q", "-am", "more"); sh(self.repo, "checkout", "-q", "main")
        r = land(self.repo, *self.land_args()); self.assertEqual(r.returncode, 2); self.assertIn("moved since the gate", r.stdout)
        self.gate()
        r = land(self.repo, *self.land_args("--bullet", "x" * 601)); self.assertEqual(r.returncode, 2); self.assertIn("> 600", r.stdout)
        (self.repo / "stray.txt").write_text("orphan\n", encoding="utf-8")
        r = land(self.repo, *self.land_args()); self.assertEqual(r.returncode, 2); self.assertIn("uncommitted paths", r.stdout)
        (self.repo / "stray.txt").unlink()
        board = (self.repo / "docs/loop/NEEDS-GREY.md").read_text(encoding="utf-8").replace("(empty)\n\n## BUY", "".join(f"### PT-00{i} — q\n" for i in range(4)) + "\n## BUY")
        (self.repo / "docs/loop/NEEDS-GREY.md").write_text(board, encoding="utf-8"); sh(self.repo, "commit", "-q", "-am", "fill play")
        play = Path(self.scratch.name) / "play.md"; play.write_text("### PT-005 — CHG-007 Pogo tune — queued\nBuild: main\nQuestion: heavier?\n", encoding="utf-8")
        r = land(self.repo, *self.land_args("--play", str(play))); self.assertEqual(r.returncode, 2); self.assertIn("backpressure", r.stdout)

    def test_land_from_branch_checks_out_main_first(self):
        self.gate(); sh(self.repo, "checkout", "-q", "chg/007-pogo")
        r = land(self.repo, *self.land_args("--fyi", "x")); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(sh(self.repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip(), "main")

    def test_land_inserts_session_row_at_top_of_sessions_table(self):
        self.gate()
        r = land(self.repo, *self.land_args("--fyi", "x")); self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        readme = (self.repo / "docs/changes/README.md").read_text(encoding="utf-8").splitlines()
        sep = readme.index("|---|---|")
        self.assertEqual(readme[sep + 1], "| 002 | [Pogo tune](002-pogo-tune.md) |")
        self.assertEqual(readme[sep + 2], "| 001 | [First](001-first.md) |")

    def test_land_refuses_without_sessions_table_before_merging(self):
        self.gate()
        (self.repo / "docs/changes/README.md").write_text("# changes index\n\nno table here.\n", encoding="utf-8")
        sh(self.repo, "commit", "-q", "-am", "strip the sessions table")
        before = sh(self.repo, "rev-parse", "HEAD").stdout
        r = land(self.repo, *self.land_args("--fyi", "x"))
        self.assertEqual(r.returncode, 2); self.assertIn("REFUSED", r.stdout); self.assertIn("Sessions (newest first)", r.stdout)
        self.assertEqual(before, sh(self.repo, "rev-parse", "HEAD").stdout)   # no merge happened
        self.assertFalse((self.repo / "docs/changes/002-pogo-tune.md").exists())


if __name__ == "__main__":
    unittest.main()
