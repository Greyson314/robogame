# 209 — Concoction Identity, the missing half: the kill-feed names the concoction that made the kill (LOG-209)

Landed by the factory 2026-09-19T20:21:30Z. Change CHG-039, branch `chg/039-killfeed-concoction` @ eb9a8f3af2. Gate: suite PASS, perf N/A, red team PASS — suite 582/582 + 165/166 at eb9a8f3a (builder run chg039-2; red team re-run rt039, same counts); perf N/A: a string reference on a per-hit event, no physics objects, nothing per-frame; red team sonnet/medium PASS 66k.

## What changed

The first FEATURE-SLOT landing (CHARTER D8 S2): the missing half of the pre-approved "Concoction Identity" (idea-backlog § Approved). The planner's read found that the name and the pigment tint on trail and explosion already shipped under ADR-0005 (`Concoction.MixedColor` → `ProjectileSpec.VisualTint`/`TintImpact`), and that ADR-0005's Consequences name the kill-feed chip as the one piece not done ("needs damage-source attribution plumbing"). This change is that plumbing, and nothing else: no palette index, no new serialized field, no save-format change.

An optional concoction name rides the existing single-player kill-credit chain: `ProjectileSpec.WeaponName` (set from the already-fetched concoction's `DisplayName` in `BombBayBlock`, `CannonBlock`, `MortarBlock`, `ProjectileGun`; null for a bare weapon) → `DamageAttribution.Report`/`Reported` (one signature, a defaulted fourth argument; the ram site passes null) → `MatchStatsTracker.RecordDamage` (stored beside the last attacker) → a `RecordDeath(victim, now, out weaponName)` overload → `ArenaController` → `KillFeedHud.PushKill`, which renders `"{killer}  →  {victim}  ({weapon})"` when a name is present and today's line, byte for byte, when it is not. The side-only kill path and `PushDeath` are unchanged.

## Evidence

- Tests first: commit 9008ae46 carries only the tests (4 cases in `MatchStatsTrackerTests`: inside the credit window, after it expires, last attacker wins, a null name stays null; 3 in the new `KillFeedHudTests` on the pure format helper) and is compile-red against the API that did not exist yet.
- Suite at eb9a8f3a via `run-tests.sh --at`: EditMode 582/582 (575 + 7 new), PlayMode 165/166 (the one documented skip). Re-run by the red team at the same sha: same counts.
- Red team (sonnet/medium, 66k) PASS: all five `Report` call sites traced (every one sits behind the existing self-damage and friendly-fire guards); `LastAttackerWeaponName` is set and cleared only where `LastAttacker` is, so a reused robot cannot carry a stale name; INV-3: the chain stays behind `ArenaController`'s `IsOnline` early return and the MP projectile payload is untouched; INV-6: a string reference set at fire time, no new allocation on the damage path, the kill-feed already allocated per kill; `ProjectileSpec` is a managed struct, never in a NativeArray or a Burst job (`.utmp/factory/gate/CHG-039-redteam.md`).
- INV-8: no new cue is declared; the entry rides the kill-feed line's existing presentation.
- MP debt, stated plainly: the whole attribution chain is single-player only today. When the networked kill-feed is built, the name is one more string beside the kill event (`FireCommand.cs`'s `ProjectileSpawnPayload` does not carry it).
- Not proven by any test: what it looks like on screen. That is PT-003.

## Revert

`git revert` of the merge. The kill-feed loses the suffix; nothing persisted changes.
