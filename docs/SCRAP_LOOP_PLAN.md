# Scrap Loop V1 — Plan & Handoff

> **Audience.** A future Claude Code session picking up the next six
> scrap / team / ammo features cold.
>
> **TL;DR.** Six features that move the scrap loop from "v1 pickups
> exist" to "first playable team-objective gameplay with ammo
> discipline." Implementation order chosen to minimise rework: build
> the testing scaffolding first (friendly AI), then ship isolated
> tweaks before the larger restructures.
>
> **Status.** Plan. No code in this branch yet.

---

## 1. Implementation order

The order below is dependency-driven, not user-priority-driven.
Each phase is independently shippable.

1. **Friendly AI tank** — smallest change, unblocks every later phase by
   letting us actually test team mechanics in singleplayer.
2. **Scrap-slows-you-down** — small, isolated, builds on existing
   pickup code.
3. **Deposit AOE + instant-transfer + score-tick** — restructures the
   deposit; load-bearing for phase 4.
4. **Deposit AOE damage + double-scrap-on-kill** — strict extension of
   phase 3; the "grinder" mechanic.
5. **Max ammo + clip system** — biggest change by surface area; touches
   every weapon.
6. **Reload mechanic (auto-on-empty + press R)** — depends on phase 5.

Phases 3 and 4 *could* land together as one session if the user
prefers. Phases 5 and 6 effectively must.

---

## 2. Phase 1 — Friendly AI tank

Clone the existing enemy AI tank, swap team allegiance, drop into the
default arena alongside the player. This unblocks team-mechanics
testing (phases 2–4 are hard to evaluate solo).

**Key decisions.** Reuse the existing `AIController` and tank
blueprint wholesale; the only deltas should be team ID and spawn
position. If team ID isn't yet a first-class field on the chassis,
add it as a small enum (`TeamId { Player, Enemy }` initially; expand
to N teams later).

**Files likely touched.** `AIController`, `MatchController` (spawn
list), `GameplayScaffolder` (default-arena setup), wherever IFF
queries currently happen (damage filters, projectile target checks).

**Exit criterion.** Press play in the default arena: one friendly AI
tank spawns on your team and engages the existing enemy. Damage
filters respect team — friendly AI never hits you, enemy never hits
friendly AI ally.

**Risks.** If the codebase has implicit "all AI is enemy" assumptions
(damage filters, weapon target acquisition), each one needs an audit.
Likely 30 minutes of cleanup; flag any discoveries in the session log.

---

## 3. Phase 2 — Scrap carry-weight penalty

Apply a movement-speed multiplier to a chassis based on the amount
of scrap currently carried. Curve, not linear — small loads should
feel free; big loads should feel committed.

**Key decisions.** Recommended curve:

| Scrap carried | Speed multiplier |
|---|---|
| 0–2 | 1.00 |
| 3–5 | 0.95 |
| 6–9 | 0.85 |
| 10+ | 0.70 |

Apply to drive-force output across all movement providers
(`WheelDrive`, `HoverDrive`, `JetDrive`, rotor lift if it gets that
far). Single multiplier surfaces in the existing
`IMovementProvider` abstraction.

**Files likely touched.** Wherever scrap-carried lives today (probably
a component on `Robot`), every `IMovementProvider` implementation, the
HUD readout that shows scrap held.

**Exit criterion.** Hauling 10+ scrap visibly slows the chassis.
Dropping or depositing immediately restores full speed.

**Risks.** None significant. Pure tuning change.

---

## 4. Phase 3 — Deposit AOE + instant-transfer + score-tick

The deposit becomes a volume trigger (AABB or sphere) instead of a
contact point. Carried scrap transfers from the bot to the depot
*instantly* on entry. The depot then ticks its banked scrap down into
the team score at a rate of `~1 scrap / second`, giving the enemy a
window to raid before it banks.

**Key decisions.** Two layers of state:

- `Robot.carriedScrap` — scrap currently on the bot. Drops on death.
- `Depot.bankedScrap` — scrap in the depot waiting to score. Persists
  across player respawns. Transferred to `Team.score` at a fixed tick
  rate.

Raiding: the depot is destructible? Or just contestable? Recommend
**contestable, not destructible** for v1 — keeping the depot itself
permanent simplifies match flow. Enemies raid by killing the team's
bots near the depot or by using the AOE damage (phase 4) against
defenders. A future phase could add depot HP if needed.

**Files likely touched.** Current deposit trigger (probably a small
`MonoBehaviour` on the depot prefab), the score tally on
`MatchController`, the scrap-carry component on `Robot`.

**Exit criterion.** Walking into the depot volume transfers scrap
instantly. The depot's banked total ticks down into the team score
over visible seconds. Score readout updates live.

**Risks.** The tick rate is the design knob — too fast and the depot
isn't a defendable point; too slow and a kill at the depot feels
crushing. Start at 1/sec; expect tuning. If the user later wants
"banked scrap can be stolen on raid," that's a Phase 3.5 — needs a
distinct theft mechanic (carry it back out?) and a UI cue. Out of
scope here.

---

## 5. Phase 4 — Depot AOE damage + double-scrap-on-kill

Inside the depot volume:

- Enemy bots take heavy damage-over-time (`~50 HP/sec` baseline, tune
  in playtest). Reads as a "grinder" hazard zone.
- Friendly bots are immune (IFF on the volume).
- Any enemy bot killed *while inside the depot volume* drops doubled
  scrap.

This is the high-leverage mechanic from the earlier design
conversation — it gives every grappling/ramming build a clear use
case (drag enemy haulers into your grinder), creates a defender's
"win button," and makes the depot a contested high-stakes location
rather than a passive scoreboard.

**Key decisions.** Damage is server-authoritative (per
`NETCODE_PLAN.md` — visible damage outcomes must be), applies on a
tick (`FixedUpdate`-rate or slower; pick a sane interval). The
2× scrap multiplier on death-inside is checked at the moment of
death, against the position at time of last damage.

The damage is **flat HP/sec on the chassis**, not per-block. Per-block
damage application inside an AOE volume would need a per-block
in-volume check per tick, which is expensive. Chassis-level damage,
distributed to the closest block(s), is the cheaper and more
readable shape. Confirm with `MomentumImpactHandler` damage routing
conventions.

**Files likely touched.** Same files as Phase 3 (depot volume), plus
the damage application path on `Robot` / `BlockGrid`.

**Exit criterion.** Walking an enemy bot into your depot volume kills
it in ~3–5 seconds. Killing that enemy inside the volume yields
double scrap to the killer's team.

**Risks.** Tuning the damage rate is the design knob — too high and
any contact is death (no skill expression for raiders); too low and
the deterrent doesn't work. Start at 50 HP/sec, expect tuning.

---

## 6. Phase 5 — Max ammo + per-weapon-type clip system

Every weapon gains:

- A **clip size** (rounds per clip, per `WeaponDefinition`).
- A **max ammo pool** that scales with the count of that weapon
  type on the chassis: `max = clipSize × instancesOnChassis`.
- A **current ammo** counter that depletes on fire, refills on
  reload.

User's proposed shape is per-weapon-type pool (all SMGs share, all
cannons share). I think that's the right call — it gives a single
visual reload cue per weapon type, scales meaningfully with build
investment, and reads as one stat per weapon family in the HUD.

**Alternative considered and rejected:** per-weapon-instance pool
(each gun has its own clip). Visually noisy with 4 guns; players
have to track 4 reload states.

**Key decisions.**

- Storage: `WeaponDefinition` SO gets a `ClipSize` field. Per-chassis
  ammo state lives on a new `WeaponAmmoState` component on the
  `Robot`, indexed by weapon-type ID.
- Spawn defaults to full. Match start = full ammo for everyone.
- Firing decrements. Empty = can't fire that weapon type.
- A weapon-type with N instances on the chassis fires its instances
  in round-robin or all-at-once (existing behaviour); each shot
  decrements the shared pool by 1 regardless of which instance
  fired.

**Files likely touched.** `WeaponDefinition`, every `IWeapon`
implementation's `Fire` path, the HUD ammo readout, the
`ChassisFactory` build path (initialise `WeaponAmmoState` from the
blueprint's weapon counts).

**Exit criterion.** Firing a weapon depletes a visible counter.
Hitting zero stops fire until phase 6 reload lands. The counter scales
correctly with weapon-count on the chassis — a 3-SMG bot has 3× the
pool of a 1-SMG bot.

**Risks.** Touches every weapon, so the audit + migration is the
biggest scope item here. Recommend landing the data-model change
first (clip size on SO, ammo state on robot) before wiring fire-path
consumption.

---

## 7. Phase 6 — Reload mechanic

Two reload paths:

- **Auto-reload-when-empty.** Firing the last round in a clip starts
  an automatic reload after a short delay (~0.3s).
- **Press R to reload.** Manual override; refills the *currently
  selected weapon type's* pool. Partial reloads keep the unspent
  rounds (no Modern Warfare-style chamber-loss penalty).

Reload time is per `WeaponDefinition` (slow for cannons, fast for
SMGs). During reload, the weapon-type cannot fire. A HUD cue (icon
spinner + audio cue) reads the reload state.

**Open question for the user.** Does reload happen *anywhere on the
map*, or *only at the depot* (per the earlier ammo-depot design
discussion)?

- **Anywhere** is the simpler v1 and what most players expect.
- **Depot-only** for heavy weapons (cannons, bombs) is the more
  distinctive shape and ties back into the depot's role as a
  positional anchor.

Recommend shipping v1 as **anywhere, simpler**, then revisit
depot-only for heavy weapons as a Phase 6.5 once the depot is
proven as a destination players actually go to.

**Files likely touched.** Same as Phase 5, plus an input action for
the R key (existing input system), plus the HUD reload indicator.

**Exit criterion.** Firing a weapon to empty triggers a visible
reload; R triggers a manual reload on the selected weapon type;
reload completion restores fire capability.

---

## 8. Invariants to respect

Carried from `CLAUDE.md`, `NETCODE_PLAN.md`, and `PHYSICS_PLAN.md`.

1. **Server-authoritative.** Scrap state, deposit transfers, damage,
   ammo counts — all of these are gameplay outcomes. They live on
   the server (per `NETCODE_PLAN.md` §3) and replicate via existing
   `NetworkVariable` / `ClientRpc` patterns when netcode lands.
   Today they live on the local authoritative instance.
2. **No Tweakable affects gameplay.** Damage rates, tick rates, clip
   sizes, reload times — none of these are `Tweakable`s. They live
   on `WeaponDefinition` SOs, `DepotConfig` SOs, or per-arena server-
   pushed config (per `PHYSICS_PLAN.md` §5).
3. **Default to zero baseline cost.** The depot's AOE damage check
   runs only on enemy chassis inside the volume; not a global tick.
4. **No per-frame allocations.** Ammo-state mutation, depot-tick,
   carry-weight curve lookup — none of these allocate in steady state.
5. **VFX + audio with every new gameplay event.** Per `CLAUDE.md`
   invariant 8. Each of these phases ships at minimum a call site
   for the relevant cue / VFX kind, even if the asset is deferred.
   The missing-cue logger surfaces what the audio pass owes.

---

## 9. Open questions for the user

- **Depot tick rate** (Phase 3). 1 scrap/sec start? Slower for more
  defend-window tension?
- **Depot damage rate** (Phase 4). 50 HP/sec start? Per-tick or
  continuous?
- **Reload location** (Phase 6). Anywhere vs. depot-only-for-heavies.
  Recommend anywhere for v1.
- **Death drops on bot destruction.** When you die carrying scrap,
  does it drop at your last position, or transfer to the killer's
  bot directly? Recommend drop-at-position (preserves the
  contest-around-corpse loop).
- **Friendly scrap drop behaviour.** Earlier conversation suggested
  picking up friendly scrap denies it from the enemy. Confirm this
  is still in scope, or simplify v1 to "scrap auto-returns to team
  bank after 10s if not picked up."

---

## 10. References

- `docs/NETCODE_PLAN.md` §3 (authority), §6 Bucket D (discrete
  events — ammo, score, deposit ticks all fit here).
- `docs/PHYSICS_PLAN.md` §5 (Tweakables vs blueprint / SO data).
- `docs/GAME_FEEL_PLAN.md` — every new event in these phases is a
  feel hook. Reload audio, deposit-tick audio, grinder-damage VFX,
  scrap-pickup pulse.
- `docs/SCALABLE_PARTS_PLAN.md` and `docs/FOIL_ROTATION_PLAN.md` —
  unrelated but the source pattern for these handoff docs.
- `CLAUDE.md` — hard invariants.

---

*Plan written: 2026-05-08.*
