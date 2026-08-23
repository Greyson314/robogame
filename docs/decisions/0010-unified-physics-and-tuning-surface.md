# ADR-0010 — Unified physics & tuning surface best practices

- Status: **Accepted** (2026-08-23, session 169; user directive "come up
  with a set of unified best practices as you see fit, and implement
  them")
- Extends: [ADR-0009](0009-movement-authority-from-block-geometry.md),
  invariant #11, [ADR-0008] (schema-side classification), TuneSchema
  registry (session 163).

## Decision

Six rules unify how bot physics and player tuning are built from here on.

1. **Forces act at blocks.** New movement behaviour applies its force via
   `AddForceAtPosition` at the acting block's geometric centre, reading
   `RobotDrive.LastControl` (raw axes or `DriveIntent`). Chassis-level
   force/torque is allowed only for (a) genuinely chassis-global trim
   state (hover altitude setpoint), or (b) stability assists explicitly
   commented as grandfathered pending migration. Session 169 moved
   ground drive thrust to the grounded wheels and hover thrust to the
   in-contact pads; steering / grip / self-right remain documented
   grandfather cases (a prior per-wheel grip attempt caused spurious
   roll torque — that migration needs its own probe-gated pass).
2. **Player tuning is per-instance blueprint data.** Tunables ride
   `ChassisBlueprint.Entry` (Dims / Pitch / Teeter / BlockConfig /
   ConcoctionId) with the 0-sentinel meaning "authored default", and a
   static resolver next to the block's other constants
   (`RotorDefaults.ResolveRpm`, `FoilDefaults.ResolveControlThrow`,
   `ModuleTuning`). Never Tweakables (invariant #1); server-authoritative
   for free since blueprints are.
3. **Slider UX standard.** Every player-facing slider: a unit on the
   value (`Suffix`, or `SuffixFor` when the unit varies per block id),
   a hover tip (`Tip`/`TipFor` — mandatory for jargon like Collective /
   Teeter), and a sentinel-aware `Resolve` so the display shows the
   EFFECTIVE value, never a misleading 0. Preset buttons carry tips too.
4. **Dev surfaces never ship.** Player-facing Tweakable groups are
   exactly {Audio, QoL} (`Tweakables.IsPlayerFacing`); everything else
   renders only when `Tweakables.DevSurfacesVisible` (editor /
   development builds), and the `Dev.*` group is compile-stripped on
   top. A slider whose consumer dies is deleted in the same change —
   dead knobs mislead (the Dev.Plane.* set outlived
   `PlaneControlSubsystem` by two sessions and read as a bug).
5. **Build verbs are BuildSession atomics.** Grid mutations flow through
   `TryPlace` / `TryRemove` / `TryMove`; a multi-step verb must be
   rollback-safe (TryMove restores the source on a rejected drop) and
   every per-instance setting survives every verb — losing a player's
   tuned config is data loss, not a physics quirk.
6. **Feel changes are probe-gated.** Any change to where or how movement
   forces apply ships with before/after live probes on the shipped
   presets (symmetric behaviour within noise of the baseline) plus an
   asymmetry probe proving the invariant-#11 payoff (losing parts costs
   control), and a PlayMode test pinning both.

## Consequences

- The remaining chassis-level ground/hover behaviours (steer torque,
  lateral grip, self-right, jump, speed caps; hover yaw) are explicit
  debt with named risks, not silent architecture.
- `PogoBlock` reads the raw `Move` axes off `DriveControl` by decision:
  extending the Ground scheme's intent with pitch/roll would deflect
  free foils on every ground bot (see the TRACE at the field).
- Rotor throttle consumes `DriveIntent` — on a Plane-scheme hybrid a
  lift rotor no longer answers Space (Space is pitch there); scheme
  resolution keeps that combination out of Auto.
- Hooked chassis: rotor throttle holds and pogo tilt goes inert
  (LastControl zeroes while hooked) — previously both kept responding.
