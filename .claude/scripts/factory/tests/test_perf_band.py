"""Tests for perf_band.py's row parser and band math (CHG-026).

The parser must accept the harness row as it was before CHG-026 (no focus
field) and as it is after (` focus=True|False|n/a` appended after gcPerFrame),
and the band must be min/max/spread per metric over the measured rows only.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import perf_band  # noqa: E402

OLD = ('2026-09-17 17:48:19  [PERF-BASELINE] scene=Arena state=idle frames=600 avg=1.982ms median=1.925ms '
       'min=1.700ms p99=2.962ms p99.9=6.631ms fps=504.7 gcTotal=0B gcPerFrame=0.0B')
NEW = ('2026-09-18 00:01:17  [PERF-BASELINE] scene=Garage state=idle frames=600 avg=2.350ms median=2.186ms '
       'min=1.540ms p99=4.564ms p99.9=6.529ms fps=425.5 gcTotal=0B gcPerFrame=0.0B focus=False')
NEW2 = ('2026-09-18 00:01:40  [PERF-BASELINE] scene=Garage state=idle frames=600 avg=2.090ms median=1.970ms '
        'min=1.500ms p99=3.500ms p99.9=4.410ms fps=478.0 gcTotal=0B gcPerFrame=0.0B focus=True')


class ParseRowsTests(unittest.TestCase):
    def test_parses_a_row_without_the_focus_field(self):
        rows = perf_band.parse_rows([OLD])
        self.assertEqual(len(rows), 1)
        r = rows[0]
        self.assertEqual(r['scene'], 'Arena')
        self.assertAlmostEqual(r['avg'], 1.982)
        self.assertAlmostEqual(r['median'], 1.925)
        self.assertAlmostEqual(r['p99'], 2.962)
        self.assertAlmostEqual(r['p999'], 6.631)
        self.assertEqual(r['gc'], 0)
        self.assertIsNone(r['focus'])

    def test_parses_the_focus_field_when_present(self):
        r = perf_band.parse_rows([NEW])[0]
        self.assertEqual(r['scene'], 'Garage')
        self.assertEqual(r['focus'], 'False')
        self.assertAlmostEqual(r['p99'], 4.564)

    def test_skips_lines_that_are_not_rows(self):
        rows = perf_band.parse_rows(['[SURFACENETS-BENCH] dim=34 medians=0.4/0.4/0.4 ms', '', NEW])
        self.assertEqual(len(rows), 1)


class BandTests(unittest.TestCase):
    def test_band_is_min_max_spread_per_metric(self):
        b = perf_band.band(perf_band.parse_rows([NEW, NEW2]))
        lo, hi, spread = b['avg']
        self.assertAlmostEqual(lo, 2.090)
        self.assertAlmostEqual(hi, 2.350)
        self.assertAlmostEqual(spread, 0.260)
        lo, hi, spread = b['p99']
        self.assertAlmostEqual(lo, 3.500)
        self.assertAlmostEqual(hi, 4.564)
        self.assertAlmostEqual(spread, 1.064)
        self.assertEqual(b['gc'], [0])
        self.assertEqual(b['focus'], ['False', 'True'])

    def test_band_line_names_scene_n_warmups_and_the_metrics_in_order(self):
        line = perf_band.band_line('Garage', perf_band.parse_rows([NEW, NEW2]), 2, '2026-09-18')
        self.assertTrue(line.startswith('BAND Garage idle, live factory Editor, n=2 after 2 warm-ups, 2026-09-18: '))
        self.assertIn('avg 2.09-2.35 ms (spread 0.26)', line)
        self.assertIn('p99.9 4.41-6.53 ms (spread 2.12)', line)
        self.assertIn('gcTotal 0 B', line)
        self.assertIn('focus=False/True', line)

    def test_empty_band_says_so(self):
        self.assertEqual(perf_band.band({}), {})
        self.assertTrue(perf_band.band_line('Arena', [], 2, '2026-09-18').startswith('BAND: no measured rows'))


if __name__ == '__main__':
    unittest.main()
