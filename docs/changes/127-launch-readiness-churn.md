# 127 — Launch-readiness churn: scoreboard, match feel, balance, ammo config

Autonomous multi-feature pass ("churn") aimed at making the match loop
launch-ready. Four features + one balance change + one ADR draft.

## Per-combatant stats + Tab scoreboard

`MatchStatsTracker` (plain C#, mirrors `MatchController`'s testability):
per-combatant kills / deaths / nominal damage / scrap-banked rows keyed
by **stable display name** ("YOU", "BOT 1", "ALLY") so respawned chassis
re-bind to their accumulated line. Kill credit = last *opposing-side*
damager within 8 s of the death; teammates and stale damage never claim
kills. 11 EditMode tests encode the credit rules.

Feeds: new `Combat/DamageAttribution` static fan-in (reported from all
four ProjectileWorld damage paths + the ram handler — amounts are
*nominal* headline damage, not per-block HP), and a new
`ScrapDepot.ScrapDeposited` static event. Both consumed by
`ArenaController`, which owns the Robot→row map (registered through the
existing `RegisterChassis` funnel, now name-threaded).

`ScoreboardOverlay` rewritten from team-aggregates ("MATCH STATUS") to a
per-combatant table grouped YOU-side / ENEMY-side with dead-row dimming
and a timer + lives footer. Row strings cache against
`MatchStatsTracker.Version` — held-Tab repaints are allocation-free
(the old version built a `GUIStyle` per OnGUI call).

Deliberately NOT changed: `MatchController.RegisterKill` keeps its
other-side inference for the KillAnnouncer streak banner. A depot-
grinder or wall death is uncredited on the scoreboard (honest) but
still rolls the side-level frag counter (feel).

## Named kill feed

`KillFeedHud` gains a named mode ("YOU → BOT 2", "BOT 1 †" for
unattributed deaths) pushed from the kill-credit path; the side-level
`KillRegistered` subscription remains as sandbox fallback.

## Match-flow feel

- `RoundClockTick` cue (final 10 s, one per displayed second, riding
  ObjectiveHud's existing change gate). Cue declared; clip pending —
  the missing-cue logger will surface it (INV-8 pattern).
  **AudioCue is append-only now** — the library serialises rows by enum
  int value; a mid-enum insert silently remaps every cue below it.
  Guard comment added at the enum tail.
- `StartMatchHud` draws a FIGHT! banner (KillAnnouncer envelope) on
  `MatchStarted`, pairing the existing MatchStart sting — the round
  start has a moment now instead of the prompt just vanishing.

## Balance: cannon buff (survey-driven)

Sustained DPS/CPU survey (clip + reload included): SMG 9.4, cannon
1.27, mortar 0.82-vs-single-target. Mortar's 9 m area splash hits every
block in radius, so its real anti-ground output is fine; the cannon is
the true straggler — a slow single slug that couldn't break one 100 HP
cube. `Cannon_Default`: damage 60 → 110 (one-cubes on direct hit),
knockback 18 → 40. SMG untouched (don't nerf the staple pre-playtest).
Burst DPS/CPU is still SMG-dominant ~4:1 — flagged for a playtest
verdict, not further blind tuning.

## Per-instance ammo capacity (Obsidian: "more ammo = more weight + CPU")

SMG + Cannon get an ammo-multiplier variant slider (0.5×–2.5×, 0.25
steps), riding the rotor-RPM pattern end-to-end: new
`Block/WeaponAmmoDefaults` (single source for range, clip scaling, CPU
curve, mass scale) → `CpuBudget.EffectiveCpuCostCore` branch →
`Robot.EffectiveMass` ammo-fraction scaling (0.4 of block mass) →
`WeaponAmmoState` pools now sum per-instance clips. CPU price is linear
above 1× and shallower (0.5 + 0.5m) below so half-ammo gun-stacking
isn't a free burst-DPS arbitrage. Untouched weapons keep the 0 sentinel
= sticker price + sticker mass (zero-cost default, INV-5 spirit).
Explosive weapons excluded for now — their panel slot is the
concoction chooser; combining sections is future layout work.

Broke-and-fixed: `EffectiveCpuCost_NonRotorBlock_IsUnaffectedByBlockConfig`
probed with the SMG, whose config is now *deliberately* priced. Probe
swapped to Thruster (config-carrying, price-neutral); weapon pricing is
covered by the new `WeaponAmmoDefaultsTests`.

## ADR draft (awaiting user)

`decisions/0005-block-tiers-as-mk-variants.md` (Proposed): tiers as
authored Mk sibling definitions priced flat-power-per-CPU — buys
concentration, not efficiency; no unlock gate at launch. Not
implemented; first-wave roster needs user sign-off.

## Verification

EditMode 368/369 passed (1 pre-existing inconclusive:
`Preset_PassesValidation(Blueprint_DefaultBuggy)`), PlayMode 120/121
(1 pre-existing `[Ignore]`). Editor console clean after import.
Perf: no new physics objects, no per-frame allocations added
(scoreboard string caching is version-gated) — perf-checker skipped per
workflow rule.

## Known unknowns

- Cannon 110/40 and the ammo CPU curve need a playtest verdict.
- Garage live-edit of an ammo slider doesn't resize a live
  `WeaponAmmoState` pool until respawn/arena entry (pools rebuild on
  block place/remove + spawn, not on config change). Harmless today —
  the garage has no combat.
- `architecture.md` lags the code badly (no mortar / ammo / modules /
  kill feed / nameplates / Lab). Worth a regeneration pass.
