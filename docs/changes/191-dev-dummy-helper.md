# 191 — ArenaController: one dev-dummy spawn/despawn helper replaces three copies (LOG-191)

Landed by the factory 2026-09-17T23:13:44Z. Change CHG-022, branch `chg/022-dev-dummy-helper` @ 0a5b30b81b. Gate: suite PASS, perf N/A, red team PASS — Suite on the branch's checkout by the builder: EditMode 543/543, PlayMode 152/153 (documented skip), 0 failures (.utmp/factory/gate/CHG-022-build.md; rig XML timestamps there). Zero-behaviour proof: before/after hierarchy dumps over the bridge for all three dummies, names/active flags/component lists identical (CHG-022-before-*.json / -after-*.json). Red team sonnet/medium PASS, 46k tokens: step-by-step per path, the stress tower's SetActive bracket verified against ChassisAssembler.Assemble's own deactivate/restore (CHG-022-redteam.md). Line count: net +9 in the file, three copies → one; stated in the entry. Perf N/A (state-transition code only)..

Factory shift 7, 2026-09-17, branch `chg/022-dev-dummy-helper` (a builder on the checkout; CHARTER D8 S1: de-duplication).
Class AUTO (I1: a refactor with zero behaviour change proven by the suite and by before/after hierarchy dumps). Runtime file → red team sonnet/medium (D16b). One file.

## Intent

`ArenaController` spawned its three dev dummies (the tank, the air dummy, the stress tower)
through three copies of one sequence, and despawned two of them through two copies of another
(FINDINGS F-046, from the code-size census). Three copies of a spawn shape are three places a
future change to how a dummy is built has to be made, and two of them will be missed.

## What shipped

- One private generic helper, `SpawnDevDummy<TAi>`, builds a dummy the way all three did: scrub
  any stale object of that name, new inactive GameObject at the spawn point, attach and
  configure the AI through a callback when there is one, `ChassisFactory.Build` (or
  `BuildTarget` for the passive stress tower), activate. `DespawnDevDummy<TAi>` replaces the
  two despawns. The call sites keep what differed (fire wiring, registration, the tower's
  `Robot` lookup, the logs).
- The number, stated plainly because the spec called it informational: the file did not
  shrink. Net +9 lines, because the three old bodies were shorter than the census estimated and
  the helper carries its own comment. What changed is that there is one spawn shape now, not
  three.

## Verification

- Before and after the edit, over the bridge with the session's connector dead
  (`mcp_http.py`): Arena loaded through Bootstrap in play mode, each of the three dummy
  Tweakables toggled, the hierarchy dumped per dummy. Names, active flags and component lists
  identical for all three; despawn verified both ways. Dumps:
  `.utmp/factory/gate/CHG-022-before-*.json`, `-after-*.json`; report
  `.utmp/factory/gate/CHG-022-build.md`.
- Suite on the branch's checkout: EditMode 543/543, PlayMode 152/153 (the documented skip).
- Red team (sonnet/medium; the step-by-step comparison per path):
  `.utmp/factory/gate/CHG-022-redteam.md`.

## Aftermath

F-046 closed. F-047 (the larger split of the dev-dummy block out of `ArenaController`) is
cheaper now and stays queued; under I1's calibration it is AUTO once the scene wiring is
verified over the bridge.
