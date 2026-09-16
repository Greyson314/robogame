import json, sys, tempfile, unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import discord_intake as di  # noqa: E402


def msg(i, content="hi", bot=False, webhook=None, attachments=(), embeds=()):
    return {"id": str(i), "content": content, "timestamp": f"2026-09-16T03:{i % 60:02d}:00.000000+00:00",
            "author": {"username": "grey", "bot": bot}, "webhook_id": webhook,
            "attachments": list(attachments), "embeds": list(embeds)}


def pager(ids_newest_first, page_size=di.PAGE_SIZE):
    """A fake Discord: fetch(before) returns the next page newest-first."""
    def fetch(before):
        ids = [i for i in ids_newest_first if before is None or i < int(before)]
        return [msg(i) for i in ids[:page_size]]
    return fetch


class Walk(unittest.TestCase):
    def test_stops_at_cursor_and_returns_oldest_first(self):
        msgs, complete = di.walk(pager([10, 9, 8, 7, 6]), after_id="7")
        self.assertTrue(complete); self.assertEqual([m["id"] for m in msgs], ["8", "9", "10"])

    def test_no_cursor_walks_whole_history_across_pages(self):
        ids = list(range(250, 0, -1))
        msgs, complete = di.walk(pager(ids), after_id=None)
        self.assertTrue(complete); self.assertEqual(len(msgs), 250); self.assertEqual(msgs[0]["id"], "1")

    def test_cap_hit_before_cursor_is_incomplete(self):
        old = di.MAX_PER_POLL; di.MAX_PER_POLL = 5
        try:
            msgs, complete = di.walk(pager([20, 19, 18, 17, 16, 15, 14]), after_id="10")
            self.assertFalse(complete); self.assertEqual(len(msgs), 5)
        finally:
            di.MAX_PER_POLL = old


class Format(unittest.TestCase):
    def test_bots_and_webhooks_skipped(self):
        self.assertIsNone(di.format_line("c", msg(1, bot=True)))
        self.assertIsNone(di.format_line("c", msg(1, webhook="w")))

    def test_line_shape_and_notes(self):
        line = di.format_line("blue-mao-pow", msg(5, content="ship it\nplease", attachments=[{"filename": "a.png", "url": "https://x/a.png"}], embeds=[{"title": "T", "url": "https://y"}]))
        self.assertTrue(line.startswith("- [discord #blue-mao-pow 2026-09-16T03:05] grey: ship it / please"))
        self.assertIn("[attachment: a.png https://x/a.png]", line); self.assertIn("[embed: T https://y]", line)


class Poll(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.repo = Path(self.tmp.name)
        (self.repo / ".git").mkdir(); (self.repo / "docs/loop").mkdir(parents=True)
        (self.repo / di.INBOX_REL).write_text("# INBOX\n\n(empty)\n")

    def tearDown(self):
        self.tmp.cleanup()

    def test_no_token_is_noop(self):
        self.assertEqual(di.poll(self.repo, None, fetch=pager([3, 2, 1])), 0)

    def test_appends_and_moves_cursor_only_after_write(self):
        n = di.poll(self.repo, "tok", fetch=pager([3, 2, 1]), no_git=True)
        self.assertEqual(n, 3)
        text = (self.repo / di.INBOX_REL).read_text()
        self.assertEqual(text.count("- [discord #blue-mao-pow"), 3)
        cid = di.CHANNELS["blue-mao-pow"]
        self.assertEqual(json.loads((self.repo / di.STATE_REL).read_text())[cid], "3")
        # second poll with nothing new appends nothing
        self.assertEqual(di.poll(self.repo, "tok", fetch=pager([3, 2, 1]), no_git=True), 0)

    def test_dry_run_writes_nothing(self):
        n = di.poll(self.repo, "tok", fetch=pager([2, 1]), dry_run=True)
        self.assertEqual(n, 2); self.assertNotIn("discord", (self.repo / di.INBOX_REL).read_text())
        self.assertFalse((self.repo / di.STATE_REL).exists())

    def test_incomplete_fetch_defers_channel(self):
        old = di.MAX_PER_POLL; di.MAX_PER_POLL = 2
        try:
            n = di.poll(self.repo, "tok", fetch=pager([5, 4, 3, 2, 1]), no_git=True)
            self.assertEqual(n, 0); self.assertFalse((self.repo / di.STATE_REL).exists())
        finally:
            di.MAX_PER_POLL = old


if __name__ == "__main__":
    unittest.main()
