import io, json, sys, tempfile, unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import ping  # noqa: E402


class Form(unittest.TestCase):
    def test_prose_becomes_sentence_bullets_without_bolding(self):
        text = "The **suite** is green on main. Perf held at 5.1 ms\navg (p99 6.1) on Arena idle.\n\n- an existing bullet\n"
        self.assertEqual(ping.to_bullets(text),
                         ["The suite is green on main.", "Perf held at 5.1 ms avg (p99 6.1) on Arena idle.", "an existing bullet"])

    def test_numbered_and_star_bullets_kept_one_per_line(self):
        self.assertEqual(ping.to_bullets("1. one\n* two\n• three"), ["one", "two", "three"])

    def test_abbreviation_does_not_split(self):
        self.assertEqual(ping.to_bullets("Use e.g. the harness. Then stop."), ["Use e.g. the harness.", "Then stop."])

    def test_split_parts_respects_limit_and_numbers(self):
        parts = ping.split_parts("a\n" * 1500, limit=1900)
        self.assertGreater(len(parts), 1)
        self.assertTrue(all(len(p) <= 1900 + 12 for p in parts))
        self.assertTrue(parts[0].endswith(f"(1/{len(parts)})"))


class Send(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.repo = Path(self.tmp.name)
        (self.repo / ".git").mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def test_cap_refuses_seven(self):
        out = io.StringIO()
        r = ping.send("\n".join(f"- b{i}" for i in range(7)), repo=self.repo, dry_run=True, out=out)
        self.assertEqual(r["exit"], 2); self.assertEqual(r["reason"], "cap"); self.assertIn("REFUSED", out.getvalue())

    def test_empty_refused(self):
        r = ping.send("   \n", repo=self.repo, dry_run=True, out=io.StringIO())
        self.assertEqual(r["exit"], 2)

    def test_dry_run_writes_no_ledger_and_prefixes(self):
        out = io.StringIO()
        r = ping.send("- landed CHG-007", lead="shift 1", repo=self.repo, dry_run=True, out=out)
        self.assertEqual(r["status"], "dry"); self.assertIn("[factory] shift 1", out.getvalue())
        self.assertFalse(ping.ledger_path(self.repo).exists())

    def test_repetition_guard_drops_bullets_sent_today(self):
        lp = ping.ledger_path(self.repo); lp.parent.mkdir(parents=True)
        import hashlib
        sha = hashlib.sha1(ping.normalise("Landed CHG-007.").encode()).hexdigest()
        lp.write_text(json.dumps({"sha": sha, "text": "x"}) + "\n")
        out = io.StringIO()
        r = ping.send("- landed CHG-007\n- something new", repo=self.repo, dry_run=True, out=out)
        self.assertEqual(r["dropped"], 1); self.assertEqual(r["sent"], 1); self.assertIn("dropped (already sent today)", out.getvalue())
        r2 = ping.send("- landed CHG-007", repo=self.repo, dry_run=True, out=io.StringIO())
        self.assertEqual(r2["status"], "nothing-new")

    def test_no_webhook_is_dry_not_failure(self):
        import os
        os.environ.pop("DISCORD_WEBHOOK_FACTORY", None)
        r = ping.send("- hello", repo=self.repo, out=io.StringIO())
        self.assertEqual(r["status"], "dry"); self.assertEqual(r["exit"], 0)


if __name__ == "__main__":
    unittest.main()
