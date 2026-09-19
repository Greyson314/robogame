# 203 — Atomic save writes: blueprints, concoctions and tweakables go through .tmp + File.Replace (LOG-203)

Landed by the factory 2026-09-19T05:14:37Z. Change CHG-006, branch `chg/006-atomic-writes` @ bf76f322f2. Gate: suite PASS, perf N/A, red team PASS — Suite at bf76f322 via run-tests.sh --at: builder EditMode 567/567 + PlayMode 160/161 (1 documented skip); red team's own EditMode rerun 567/567. Red team sonnet/medium PASS 47k (.utmp/factory/gate/CHG-006-redteam.md): stale .bak/.tmp handled, Tweakables bytes BOM-less as before (control repro), callers' error handling unchanged, enumeration ignores .bak/.tmp; INV-6 and the save format hold. Perf N/A: event-driven saves, no second serialization..

## What changed

New `Robogame.Core.AtomicFile.WriteAllText(path, text, encoding)`: it writes a `.tmp` sibling, then swaps it in with `File.Replace(tmp, path, path + ".bak")` when `path` exists, else `File.Move`. On any failure it removes the `.tmp` best-effort and rethrows, so callers' error handling is unchanged. `UserBlueprintLibrary.Save`, `ConcoctionLibrary.Save` and `Tweakables.Save` call it instead of a bare `File.WriteAllText`. No format, path or file-name change; Tweakables keeps its BOM-less UTF-8 bytes through an explicit `UTF8Encoding(false)`. A `.bak` sibling now appears beside an overwritten save. `docs/best-practices.md` § 11.3 and its open-items line say what the code does. Source: BACKLOG 4, LAUNCH-READINESS T4 (the atomic-writes half).

## Evidence

- Tests first: commit 1e954ecb carries only `AtomicFileTests` (compile-red: `AtomicFile` did not exist).
- Final bf76f322: EditMode 567/567 (+5), PlayMode 160/161 (1 documented skip), via `run-tests.sh --at`.
- The five tests run in a temp dir only: new file; overwrite leaves `.bak` with the previous content and no `.tmp`; a `File.Replace` against a destination held open with `FileShare.None` leaves the original intact and surfaces the `IOException` (proven on the Unity Mono runtime on Windows); both libraries' listing patterns ignore `.bak` and `.tmp` siblings.
- Deviation, approved mid-build: no test saves through the real libraries, because they write into the real `persistentDataPath` (Grey's own saves; F-063).
- Out of scope, now real: `Delete` leaves the `.bak` sibling behind (inert).

## Revert

`git revert` the merge. `.bak` files left on disk are inert.
