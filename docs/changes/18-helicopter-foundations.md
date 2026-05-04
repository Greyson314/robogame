# Session 18 — Helicopter foundations: stable frame + foil-dimension knobs

> Status: **closed.** Phase A (B1 garage gate) and Phase C (Aero
> tweakables) shipped in this session. B2/B3 frame stability ended up
> needing two more fixes that landed in [session 21](21-helicopter-spin-axis-lift.md):
> pure-axial rotor lift (kills induced-drag yaw) and foil-vs-chassis
> ignore-pair (kills collision-sweep yaw). The original session-17
> diagnostic candidates ranked correctly on the lift bug but missed
> the collision channel entirely.

## Multi-session goal (helicopter arc)

The shape of a "helicopter" in Robogame, expressed in design terms:

> A helicopter is a chassis with a top-mounted rotor block and four
> aerofoil blades placed in the rotor's spin-plane neighbours. At a
> moderate RPM, that rig produces enough lift to leave the ground and
> respond controllably to WASD. The chassis frame stays attached to its
> own orientation (it does not spin with the rotor). Only the foils
> spin.
>
> The player can tune each foil's *thickness* and *length* — make a
> blade longer for more lift, thinner for less drag — and see the result
> immediately. In the short term, foil dimensions are exposed as global
> Tweakables that re-skin every active foil. In the medium term, foil
> dimensions become per-block blueprint config edited in the garage,
> persisted with the save format, and authoritative for multiplayer.

This is the long-term shape. It maps onto two concrete pieces of work:

1. **Make the existing default helicopter blueprint actually fly stably**
   (close out session 17's B1 / B2 / B3).
2. **Expose foil thickness and length** so the player can iterate on a
   helicopter's lift characteristics without recompiling. Tweakables
   first, blueprint config second.

The blueprint-config piece touches the open `docs/PHYSICS_PLAN.md` § 6
"Per-block blueprint config" item — when it lands, the same wiring will
be used by future per-block knobs (rotor RPM, weapon variants, paint).
That is real architectural work and should not be rushed into session
18.

## Session 18 scope (this session)

Three commits' worth of work, in order:

### A. Stabilise the helicopter frame (closes B2 / B3)

Symptom: even at low RPM the chassis itself spins around the rotor's
spin axis and rapidly diverges into a destructive tumble. Per session
17's diagnosis, a symmetric four-blade ring with rotor-mode
drag/sideslip suppressed *should* produce zero net torque on the
chassis. The fact that yaw is being induced means one of three things,
in suspicion order:

1. Fewer than four foils are being adopted, breaking ring symmetry.
2. The new world-space radial pitch formulation is producing tilted
   lift whose tangential component doesn't cancel across the ring.
3. The chassis Rigidbody's `angularDamping` (driven by
   `Tweakables.ChassisAngDamp`, default 2.0) is not high enough to
   damp small floating-point asymmetries before they spiral.

The plan instruments first, fixes second. Per `BEST_PRACTICES`:
"Profile before claiming a perf characteristic" — extends to
"diagnose before fixing." The existing diagnostic
`Debug.Log` calls in `RotorBlock.AdoptAdjacentAerofoils` and
`BuildLiftRig` already report adopted foil count and per-foil world
position. Confirm all four log on game-start; if so, B1 is suspect.

### B. Garage render of the foils (closes B1)

The four `Aero` cells around the helicopter rotor at `(0,1,0)` don't
appear in the garage view. Likely cause per session 17: `BuildLiftRig`
runs in the garage (the factory unconditionally flips
`RotorsGenerateLift` after `SetActive`), reparenting the foils under
a scene-root hub. The garage's static-display path (Rigidbody
kinematic, `FreezeAll` constraints — see
[GarageController.ParkChassis](../../Assets/_Project/Scripts/Gameplay/GarageController.cs))
relies on the foils being children of the chassis transform.

Cleanest fix: gate `BuildLiftRig` on "is the chassis non-kinematic"
(i.e. arena, not garage). The garage shows foils at their placed cells
under the chassis grid root, which is what the rest of the static
display assumes. Hub + adoption only happens when physics is live.

### C. Foil thickness + length as global Tweakables

Re-introduce the rotor-shape knobs that session 16 deleted. The
session-16 deletion took out `RotorBladeLength` and `RotorBladeChord`
because the synthesised four-blade ring was replaced by player-placed
foil blocks, so per-rotor blade dims didn't make sense as a global.
What does still make sense as a global is:

- **`Aero.WingSpan`** — overrides each foil's `_wingSize.x` (the long
  axis along the placement neighbour direction).
- **`Aero.WingChord`** — overrides each foil's `_wingSize.z` (the
  fore/aft dimension).
- **`Aero.WingThickness`** — overrides each foil's `_wingSize.y`.

These wire into [`AeroSurfaceBlock.ApplyOrientationToVisual`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
so a slider drag re-skins every active foil's mesh, no rebuild
required. Per `PHYSICS_PLAN` § 5 these are **cosmetic only** as long
as they don't drive lift coefficient or hit area: today the foils
don't deal damage and the wing mesh isn't a collider, so changing
visual size is gameplay-neutral. A reviewer note next to the
registration block must call this out so a future PR doesn't
inadvertently couple the visual to a damage path without flipping the
data ownership to the blueprint.

This is the "Tweakables first" half of the multi-session goal. The
"per-block blueprint config" half is session 19+ and is gated on the
generic `{blockId, position, configBlob}` blueprint-entry change
described in `PHYSICS_PLAN` § 6.

### Out of scope for session 18

- Per-rotor RPM tuning (still the global `Rotor.RPM`).
- Per-foil dimensions (foils share the global thickness/length).
- Garage UI for foil dimensions. Session 19+, gated on per-block config.
- Helicopter-specific WASD remap. Today the helicopter shares
  `PlaneControlSubsystem` (pitch/roll/yaw torques on WASD) — that's
  acceptable arcade-feel for a hovering chassis; revisit if the play
  feel proves wrong after frame stability is solved.
- Tail rotor lift contribution. The tail rotor at `(1, 0, -3)` stays
  cosmetic for now per the session 16 design choice.

## Success criteria

- Hit play in arena with the default Helicopter preset → chassis lifts
  off at moderate RPM (default 60 rpm, or one slider tick up).
- Frame yaw is bounded — no runaway spin around the rotor's spin axis
  inside the first ten seconds of flight at any RPM ≤ 240.
- Garage view of the helicopter shows all four foils in their placed
  cells.
- New `Aero.WingSpan` / `Aero.WingChord` / `Aero.WingThickness` sliders
  appear in the settings panel under an "Aero" group, and dragging them
  visibly resizes the foil mesh on the helicopter in real time.
- Existing tests in `Assets/_Project/Tests/PlayMode/Movement/` still
  pass.

## What stays constant

- Foils remain first-class blocks placed in the grid (session 16
  decoupling).
- Rotor adoption logic is unchanged (session 17 world-space radial
  pitch stays).
- The single-Rigidbody-per-chassis invariant (`PHYSICS_PLAN` § 1.1)
  is unchanged. The kinematic hub still lives at scene root.
- `Tweakables` may not affect gameplay-observable outcomes
  (`PHYSICS_PLAN` § 1.5). Foil-dimension knobs are visual-only by
  contract for this session.

## Risks / honest unknowns

- **B2 root cause is genuinely uncertain.** Session 17's three suspects
  are reasoned, not proven. If diagnostics show all four foils adopted
  with consistent pitch, the fix may end up being "raise default
  `ChassisAngDamp` and live with it" rather than a cleaner root-cause
  fix. That would still close the user-visible bug but is a less
  satisfying answer architecturally.
- **The "frame steady" wording in the user brief** could mean either
  "doesn't spin out of control" (the B2/B3 reading I'm operating on)
  or "doesn't tilt under WASD pitch/roll input" (a different ask:
  invent helicopter-specific controls). Clarify with the user if
  session 18 work doesn't deliver on the intent.
- **Garage gate on `BuildLiftRig`** is the simplest fix for B1 but
  has a knock-on: the rotor visual won't spin in the garage. That's
  arguably correct (parked chassis = parked rotor) and matches the
  current `FixedUpdate` `frozen` branch; worth confirming with the
  user before shipping.

## Files likely to change

- [Assets/_Project/Scripts/Movement/RotorBlock.cs](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
  (B1 gate, possibly diagnostic strip)
- [Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
  (read tweakable dims into `ApplyOrientationToVisual`)
- [Assets/_Project/Scripts/Core/Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs)
  (register three new keys under group "Aero")
- New PlayMode tests under
  `Assets/_Project/Tests/PlayMode/Movement/` covering: garage gate,
  foil dimension live-resize, four-foil adoption count.
- This file (intent → outcome once shipped).
