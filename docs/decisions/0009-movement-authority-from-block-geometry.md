# 0009 — Movement authority comes from block geometry; retire chassis-level controllers

- **Status.** Accepted (user direction in the session-167 conversation,
  2026-08-23; first step — aero — landed the same session)
- **Date.** 2026-08-23

## Context

Until session 166, three drive subsystems lived on the chassis root and
claimed input keys because a *category* of block existed somewhere on
the bot: `PlaneControlSubsystem` (any aero block → Space = pitch torque,
A/D = roll torque, auto-yaw from bank), `GroundDriveSubsystem` (any
wheel), `HoverDriveSubsystem` (any hover pad). Their torques were
layout-blind: a plane pitched the same whether its tail was at the back
or on the nose, and kept full authority with a wing shot off. Session
166 measured the consequences on planes — thrust-offset moment eating
half the stick, aero damping 3–4× the explicit damping, a 0.3
inertia-authority floor, ~17°/s pitch on any user-scale plane — and a
hybrid-input mess (a heli's Space was collective AND pitch torque; a
tank with a spoiler got plane pitch).

The user named two things as core to the game's fun, not negotiable:
the size **and position** of parts must change how a bot performs
(tinkering with wing span/placement until the plane flies right IS the
game), and getting a wing shot off must cost control. A torque-on-
chassis model cannot satisfy either; the session-106 inertia-ratio
scaling was a patch over exactly that gap.

The codebase already had the right pattern in `RudderBlock`,
`ThrusterBlock`, `GyroBlock`: a placed block applies its own force at
its own position. Aero is where a physical model already exists
underneath the arcade layer (`AeroSurfaceBlock`'s lift formula), so it
is the cheapest and most honest place to start.

## Decision

1. **Intent layer.** `RobotDrive` turns the three raw player axes into a
   six-DOF `DriveIntent` (surge / sway / heave / pitch / roll / yaw)
   exactly once per tick through the chassis' `ControlScheme` (Ground /
   Plane / Helicopter; `Auto` resolves from blueprint composition,
   persisted as an explicit per-blueprint override). Blocks consume
   demands, never keys. AI bots and netcode keep feeding raw axes; the
   scheme is blueprint data, so every peer maps them identically.
2. **Aero control surfaces.** Every free (non-rotor) foil deflects by
   `AeroControl.Deflection(intent, r, liftAxis)` — the demand dotted
   with the surface's own moment direction `r × liftAxis` about the live
   CoM, capped at `FoilDefaults.ControlThrowDeg`. Elevator / canard /
   aileron / rudder roles, their signs, and their share of authority all
   fall out of position; lift, speed² and lever arm set the magnitude.
3. **Retire `PlaneControlSubsystem`.** Deleted. No chassis-level pitch /
   roll / yaw torque, no auto-yaw-from-bank, no explicit angular
   damping — coordination and damping come from the fin and the foils'
   own lift slopes. `PlaneTuningConfig` stays on the blueprint only so
   existing saves round-trip.
4. **Ground and hover follow the same rule** in a later step (per-wheel
   / per-pad actuation through the intent layer) once the aero case has
   proven the model. Until then they keep reading the raw axes from
   `DriveControl`, which still carries them.

This creates **invariant #11**: movement authority derives from block
geometry; no layout-independent control authority may be added.

## Alternatives considered

- **Keep the torque model, re-tune the numbers** (raise `PitchPower`,
  lift the 0.3 floor). Cheapest, but it keeps authority layout-blind and
  keeps the wing-loss case a no-op. Fails the fun requirement outright.
- **Rate-command controller** (torque = Kp·I·(stick·maxRate − ω)).
  Fixes "mud" and makes wing size set settling time, but it is still a
  chassis torque: placement and damage stay cosmetic. Rejected for the
  same reason; it remains a candidate for an opt-in low-speed *assist*
  block.
- **Blocks read raw keys directly** (no intent layer). Every new block
  adds a private interpretation of W; hybrids get worse, and the
  heli-vs-plane key conflict has no single place to be resolved.
- **Cyclic / thrust-vectoring first.** Would help helis, but aero is
  where the physics already pays for itself and where the user's pain
  was measured. Helis get the scheme now and actuation later.

## Consequences

- Pitch/roll/yaw authority is zero at zero airspeed (real; Robocraft
  accepted the same). Planes need a runway roll, thrust, or an opt-in
  assist block to rotate on the ground. Spawn logic already gives planes
  forward speed.
- Static stability is now the builder's problem: wings ahead of the CoM
  make a twitchy plane, a tail makes a docile one. The CoM/CoL overlay
  (session 107) becomes a real design tool. Presets were laid out for
  the torque model and need a retune pass (main wing aft, tail out).
- Thrust-offset moments are no longer masked; preset thrusters must sit
  near the CoM line or the plane needs trim.
- Tests: `ControlSchemeTests` (resolution, mapping, deflection signs,
  persistence), `FoilControlTests` (pitch sign, roll sign, wing loss ⇒
  uncommanded roll), `RotorThrottleTests` unchanged.
- Netcode: no wire change. `BlueprintBlob` carries the scheme in two
  previously-zero flag bits; JSON schema v10 adds `controlScheme`.
- Open: per-foil control throw as a tune slider; a garage dropdown for
  the scheme override; rotor cyclic for helis; ground/hover migration.

## Notes

Session logs [166](../changes/166-aircraft-pitch-pogo-save-pass.md)
(measurements) and [167](../changes/167-foil-control-surfaces.md)
(landing). Invariant: [invariants.md § 11](../invariants.md). Design
pillar: "generic propulsion primitives, no special-case archetype
blocks" in
[research/game-design-pillars.md](../research/game-design-pillars.md).
