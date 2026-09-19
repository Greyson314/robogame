# 205 — No test run writes the real tweakables.json: Tweakables.PersistenceSuspended + a SetUpFixture per test assembly (LOG-205)

Landed by the factory 2026-09-19T05:26:42Z. Change CHG-036, branch `chg/036-tweakables-persistence-seam` @ c869aa4eaa. Gate: suite PASS, perf N/A, red team PASS — Suite via run-tests.sh --at (.utmp/factory/gate/CHG-036-suite.txt): 28834cf7 compile-red (CS0117 x5), c869aa4e EditMode 575/575 + PlayMode 164/165 (1 documented skip); the real tweakables.json stamp and size identical before and after the full run. Red team sonnet/medium PASS 56k (.utmp/factory/gate/CHG-036-redteam.md): every write funnels through the gated Save(); the namespace-less SetUpFixture covers both assemblies; no shipped path sets the flag; INV-1 holds. Residual accepted: an aborted in-Editor run leaves the flag set until the next domain reload (fails safe). Perf N/A..

## What changed

`Tweakables.PersistenceSuspended` (a public static flag, false in every shipped path): while it is true, `Save()` returns at once, so `Set`, `Reset` and `ResetAll` change memory only. A namespace-less `[SetUpFixture]` (`TweakablesPersistenceGuard`) in each test assembly sets it for the whole run and clears it afterwards. Reason: the test rig, the Editors and Grey's own game share one `persistentDataPath`, and the PlayMode suite rewrote his real `tweakables.json` on every run (`ArenaDevDummiesSpawnShapeTests` toggles three dev dummies; CHG-034's audio test writes `Audio.Mute`). A run that died mid-test could have left a dev dummy on, or unmuted a muted game. Source: F-063.

## Evidence

- Tests first: commit 28834cf7 carries only the tests and the two fixtures, compile-red (CS0117 ×5: the flag did not exist).
- c869aa4e via `run-tests.sh --at`: EditMode 575/575 (+2), PlayMode 164/165 (1 documented skip).
- Live proof: the real file's LastWriteTime and size were identical before and after that full `All` run (2026-09-19 05:21:51Z, 1,983 B; `.utmp/factory/gate/CHG-036-suite.txt`). Earlier the same day the file's stamp moved with every PlayMode run (04:57:01Z after rung 0, 05:21:51Z after CHG-034's gate).
- `TestRun_SuspendsPersistence` fails if a fixture is deleted; `Set_WhileSuspended_ChangesMemoryButNeverTheSaveFile` reads the real file's bytes and stamp around a Set.
- Red team (sonnet/medium, 56k) PASS. Its residual: an aborted test run inside a live Editor leaves the flag true until the next domain reload (dev-HUD tweaks stop persisting; fails safe). Its note: the seam is public, wider than the reflection convention.

## Revert

`git revert` the merge.
