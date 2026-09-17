# 189 — First-party obsolete-API and unused-symbol warnings: 23 sites fixed as the compiler asks (LOG-189)

Landed by the factory 2026-09-17T22:46:18Z. Change CHG-009, branch `chg/009-obsolete-api` @ fcace01782. Gate: suite PASS, perf N/A, red team PASS — Suite on the branch's checkout: EditMode 542/542 (the branch predates CHG-016's test), PlayMode 152/153 (documented skip), 0 failures (.utmp/factory/gate/CHG-009-suite.txt). Red team sonnet/medium PASS, 50k tokens: every FindFirstObjectByType→FindAnyObjectByType site is a singleton or a null-guarded fallback, every FindObjectsByType site already passed SortMode.None, no test indexed by order (.utmp/factory/gate/CHG-009-redteam.md). Forced CleanBuildCache recompile of the branch's checkout in the factory Editor: first-party warning lines 12 (6 sites, each logged twice) = CHG-021's three fields (not on this branch; gone after the merge) + the three deliberately left alone (ChassisInstancedRenderer GetInstanceID→GetEntityId is a key-type change; PerformanceMenu's two UnityStats reads have no drop-in), from 46 sites on the census baseline (.utmp/factory/gate/CHG-009-recompile.txt)..

Factory shift 7, 2026-09-17, branch `chg/009-obsolete-api` (a builder on git plumbing, no checkout; gated on the foreground's checkout).
Class AUTO (I1: console warnings fixed; zero behaviour change proven by the suite). Runtime files → red team sonnet/medium (D16b). 16 files, 23 sites.

## Intent

Every full recompile printed 46 first-party compiler warnings (FINDINGS F-024, F-027; the census
`.utmp/factory/sweeps/warnings-census-editor-2026-09-17.txt`). Unity 6000.4 deprecated
`FindFirstObjectByType` ("relies on instance ID ordering; use FindAnyObjectByType") and the
`FindObjectsByType` overloads that take a `FindObjectsSortMode`, and the code also carried a
few unused fields and locals. Warnings that scroll past on every build are how the next real
one gets missed, and the census had already become a standing row in the health sweep.

## What shipped

- 23 call and declaration sites in 16 first-party files replaced with exactly what the
  compiler's warning text names: `FindAnyObjectByType` where the old call was a singleton or
  a per-scene single lookup (each site's ordering argument is in the builder's report), the
  sort-mode-free `FindObjectsByType` overload where the old call already passed
  `FindObjectsSortMode.None`, and the dead symbols removed.
- Three warning lines left alone on purpose (the report says which and why).
- `GroundDriveSubsystem`'s three CS0414 lines were CHG-021's (docs/changes/187).
- Third-party code (MidiPlayer, the water pack) untouched: their warnings are a package
  question, not ours.

## Verification

- Suite on the branch's checkout: `.utmp/factory/gate/CHG-009-suite.txt`.
- Red team (sonnet/medium; the ordering question per site): `.utmp/factory/gate/CHG-009-redteam.md`.
- Warning count on a forced full recompile in the factory Editor over the bridge, before and
  after, in the gate notes.

## Aftermath

F-024 and F-027 closed for first-party code; the `warning CS` count is a number the
suite-and-perf sweep reads, and the third-party remainder is recorded as its own line.
