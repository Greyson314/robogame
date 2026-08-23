# 167 — Foil control surfaces: intent layer, ADR-0009, PlaneControlSubsystem retired

User direction after the session-166 measurements: retire every chassis-
level, category-gated drive controller, starting with aero as the test
case; size AND position of parts must change how a bot performs, and a
shot-off wing must cost control. Landed as
[ADR-0009](../decisions/0009-movement-authority-from-block-geometry.md)
+ [invariant #11](../invariants.md).

## What shipped

- **Intent layer.** `DriveIntent` (surge/sway/heave/pitch/roll/yaw, ±1)
  + `ControlScheme` enum (Auto / Ground / Plane / Helicopter) on the
  blueprint (JSON schema v10 `controlScheme`, blob flag bits 1–2,
  cloned, Revision-bumping). `ControlSchemes.Resolve` derives Auto from
  composition (lift rotor → Heli; Kind.Plane or aero + forward thrust or
  aero-without-wheels → Plane; a tank with a spoiler stays Ground);
  `ChassisAssembler` feeds it the ADR-0008 wheel/hover/aero flags.
  `RobotDrive` maps the raw axes through the scheme once per tick,
  stamps `DriveControl.Intent`, and exposes `LastControl` for passive
  consumers. Legacy drives keep reading `Move` / `Vertical`.
- **Foil control surfaces.** `AeroControl.Deflection(intent, r,
  liftAxis, max)` = demand · (r × liftAxis)̂ with the sign conventions of
  `DriveIntent`, measured from the LIVE CoM. Every free (non-rotor)
  `AeroSurfaceBlock` adds it to its AoA each tick and tilts its mesh by
  the deflection (no allocs; re-posed only on ≥0.25° change).
  `FoilDefaults.ControlThrowDeg = 4` — these are all-moving surfaces;
  10° gave 3.5 rad/s pitch / 6 rad/s roll on the stock plane.
- **`PlaneControlSubsystem` deleted.** No chassis torque, no auto-yaw,
  no explicit angular damping; `PlaneTuningConfig` stays on the
  blueprint for save compatibility. `DevTuningOverride.ApplyPlane` and
  the `Dev.Plane*` sliders are now inert (left; dev-only).
- **Preset retune** (Plane / Grappler / Bomber / Prop Plane via
  `GameplayScaffolder`, rebaked): thruster planes get the main wing one
  cell aft (z=0 → −1), tail stabs 3 × 0.9 at −2.5° trim, thrusters
  belly-mounted (−Y; the `OrientationFromUp` math keeps forward = +Z for
  both ±Y mounts). The Prop Plane is nose-heavy (rotor assembly, CoM
  z≈+0.9) so it gets the classic prop layout instead: main wing at z=+1
  replacing the canards, big tail at +1° (19 entries, was 21). Every
  other preset asset only gained the serialized `_controlScheme: 0`.
  Baked presets as shipped (probe, W held, 14 m/s spawn): Plane hands-off
  drifts nose-up −0.14 rad/s, Space −0.4 → −0.8 (slow at 25–30 m/s,
  ~−1.2 at cruise); Bomber hands-off near level (phugoid), Space −0.5 →
  −1.0; Grappler (twin belly thrusters) phugoid, Space −1.0 → −2.1; Prop
  Plane settles into a −20° descent hands-off, Space −0.35, A/D −1.9.

## Measured (stock Plane preset unless noted; W held; spawn 250 m / 14 m/s)

| layout / input | hands-off | full Space | full A/D |
|---|---|---|---|
| old torque model (166) | nose-down 0.8 rad/s (dive by 2 s) | −0.9 rad/s | ~1 rad/s |
| new, 10° throw, stock layout | nose-down 1.2 → loops | −2.0 … −4 | −6 (!) |
| new, 4°, stock layout | nose-down 1.0–1.3 → loops | −1.2 | −2.2 … −2.6 |
| new, 4°, wing aft + big tail (L3) | nose-down 0.4–0.6 | −0.7 | −2.0 … −2.9 |
| new, 4°, L3 + belly thruster (L5) | nose-UP 0.4 → slow loop | −1.1 … −1.8 | −2.0 … −2.4 |
| **new, 4°, L5 + tail −2.5° (L8 = shipped)** | **gentle phugoid, trims ~35–40 m/s** | **−0.9 … −1.3** | **−2.2 … −2.6** |
| L8, thrust off | stable glide, +0.1 rad/s nose-down | — | — |
| user's 86 kg prop plane, new model | dives; Space now −0.3 … −0.5 (was 0); hands-off rolls ~1.3 rad/s (sideslip × wings 0.6 m above CoM — geometry, not a bug) | | |

Wing-size effect is now physical: span ×2 raises roll inertia 61 → 258
and the same 4° throw gives the same order of roll rate only because the
bigger wing also has more aileron area — the tradeoff the user asked for.

## Hybrid-input issues found while doing this (all still open)

Helicopter scheme: W/S = pitch demand, but nothing on a heli serves it
until rotor cyclic exists (W is inert, as before). Rotor throttle still
spawns at 1.0. `RotorBlock` / `PogoBlock` read raw `IInputSource`, not
`DriveIntent`. No garage dropdown for the scheme override yet. AirBot
gains are unverified on foil control (the default arena has no air bot).
Ground / hover drives are grandfathered chassis-level (invariant #11
text). The probe's W gave 0.5 throttle, not 0.9 — the dev thruster
override appears to be on in this editor; players at 0.9 will see a
touch more belly-thruster nose-up than the −2.5° trim cancels.

## Verification

EditMode: `ControlSchemeTests` (23) + serializer/blob/revision suites
green in-editor; full EditMode + PlayMode headless rig green (1
pre-existing inconclusive, 1 pre-existing skip) before the preset
rebake, re-run after. PlayMode `FoilControlTests`: Space pitches nose
UP through the foils, D banks right, losing the right wing produces an
uncommanded roll (the invariant-#11 test), no chassis-level plane
controller attached. Console clean. No Tweakables, no per-frame allocs
(two transforms + one cross product per free foil per tick), no new
physics objects.

## Files

New: `Block/ControlScheme.cs`, `Movement/DriveIntent.cs`,
`Movement/AeroControl.cs`, `Tests/EditMode/Movement/ControlSchemeTests.cs`,
`Tests/PlayMode/Movement/FoilControlTests.cs`, `docs/decisions/0009-*`.
Edited: `DriveControl`, `RobotDrive`, `AeroSurfaceBlock`, `FoilDefaults`,
`ChassisBlueprint`, `BlueprintSerializer` (v10), `BlueprintBlob`,
`GameStateController.CloneBlueprint`, `ChassisAssembler`,
`GameplayScaffolder` (+ 4 rebaked `Blueprint_Default*.asset`),
`invariants.md` (#11), `physics.md` §2.2, `architecture.md`,
`best-practices.md`, `spherical-arenas.md`, README known-unknowns.
Deleted: `Movement/PlaneControlSubsystem.cs`.
