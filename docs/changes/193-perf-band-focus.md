# 193 — perf_band.py under .claude/scripts/factory and a focus= field in the perf harness row (LOG-193)

Landed by the factory 2026-09-18T05:13:08Z. Change CHG-026, branch `chg/026-perf-band` @ e3b9d5f7b6. Gate: suite PASS, perf N/A, red team N/A — Suite at e3b9d5f7 on the rig (direct run, MCP package stripped): EditMode 543/543, PlayMode 152/153 (1 documented skip), 0 failures (.utmp/factory/gate/CHG-026/suite.txt; rig logs TestRun-CHG026-*.log). Python: 35/35 existing + 6/6 new (test_perf_band.py) OK. Perf N/A: an instrument and a test-only row field, nothing hot. Red team N/A under D16b: tooling and test code, no shipped runtime code, no NOD action, no invariant; the green suite is the gate..

## Intent

A perf proxy without its band is a number nobody may compare (charter D6). Shift 7 took the first SETTLED Arena band with a throwaway script in the runner session's scratchpad; shift 5's and shift 7's series both moved with the Editor's focus, and the harness row could not say whether the Editor had been focused. This change makes the band-taking script a factory instrument and puts the focus into the row itself, then uses both to take the Garage settled band (LOOP-STATE BACKLOG 21).

## What shipped

- `.claude/scripts/factory/perf_band.py` — `--scene Arena|Garage --warmups N --runs M`: runs `PerfBaselineHarness.<Scene>_Idle_Baseline` over the MCP bridge (HTTP, `mcp_http.py`), discards the warm-ups, parses the new `[PERF-BASELINE]` rows and prints one `BAND` line (min–max and spread per metric, GC totals, the focus values seen) to `.utmp/factory/perf-band-<scene>-<date>.txt`. It waits for the rig before every run on the right predicate (a Unity.exe whose command line carries `-runTests`; not `-batchmode`, which the live Editor's AssetImportWorker helpers also carry, LESSONS 12 amended), prints the first `run_tests` response before looping on it (LESSONS 13), and records `isApplicationActive` before and after each run. The parser and the band math are functions (`parse_rows`, `band`, `band_line`) with six unit tests in `.claude/scripts/factory/tests/test_perf_band.py`.
- `Assets/_Project/Tests/PlayMode/Perf/PerfBaselineHarness.cs` — the row gains ` focus=True|False` (`UnityEditorInternal.InternalEditorUtility.isApplicationActive` under `#if UNITY_EDITOR`, `n/a` in a player) appended LAST, so every existing parser still matches. A trace anchors it to this spec.
- `.claude/scripts/factory/README.md` — the `perf_band.py` row.

## Verification

- Python: the existing 35 factory tests and the 6 new ones pass (`python -m unittest discover -s .claude/scripts/factory/tests -t .claude/scripts/factory/tests`).
- Suite at the branch's frozen sha e3b9d5f7 on the rig (direct run via the scratch `rig_run.sh`, MCP package stripped as run-tests.sh does): EditMode 543/543, PlayMode 152/153 (the one documented skip), 0 failures. The rig's two harness rows carry `focus=True`: a `-nographics` batch Editor reports itself active, the unfocused live Editor reports `False`, so the column also tells the two rigs apart (ENGINEERING RULE 7).
- The instrument's first real use: the Garage settled band, 2026-09-18T04:58–05:01Z, live factory Editor, two warm-ups discarded then five: avg 2.09–2.60 ms (spread 0.51), median 1.97–2.43 ms (spread 0.46), p99 3.50–4.91 ms (spread 1.41), p99.9 4.41–7.28 ms (not a gate), GC 0 B/frame on all five, Editor unfocused throughout (rows 2026-09-18 00:00:04–00:01:17 local in docs/perf-captures/harness-log.txt; the band file is `.utmp/factory/perf-band-garage-2026-09-18.txt`). The August 2026 Garage row (avg 1.88 ms) was a different Editor state and is not compared.
- Red team N/A (D16b: tooling and test code).

## Aftermath

- The next Arena band should be taken with the row's own `focus=` field and a FOCUSED Editor once, to size the focus effect the earlier series only suggested.
- `run-tests.sh` still syncs the working tree; a direct run at a sha lives in the scratch `rig_run.sh` and belongs in `run-tests.sh --at <sha>` (a queued instrument, CHG-028 candidate), because three builders needed it this shift.
