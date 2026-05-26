# 99 — Hover blade v1

> Status: **Shipped.** Robogame's first raycast-based propulsion block.
> Variable footprint (N×N×1 cells, N ∈ {2,3,4}), single-Rigidbody-clean,
> no joints, no per-frame allocations. New `HoverTank` preset blueprint
> demonstrates the canonical four-corner config.

## Design (design-pilot grounded in Robocraft + Steam discussions)

Hovers in Robocraft are *position-stabilisers*, not thrusters. The
single raycast along `GravityField.SampleAt()` works on flat and
spherical arenas with one code path. Force is `F = max(0, springK × (targetAlt − gap)) − dampingC × verticalVel`,
clamped ≥ 0, with damping gated to active spring. Two properties fall
out for free:

- **No stratosphere.** Above target altitude, spring force is zero —
  the blade can never push the chassis past where it wants to be.
  Gravity caps it.
- **Terraformed-pit fall-through.** Ray miss → zero lift. Corner falls.
  No fallback clamp, no soft fail.

Passive banking, passive auto-leveling, and dramatic per-corner
failure all emerge from the `AddForceAtPosition` at each blade's
world-space attach point — no explicit torque code.

## Numerics (N=2 baseline; scales with N²)

| Knob | N=2 | N=3 | N=4 |
| --- | --- | --- | --- |
| Spring constant (N/m) | 800 | 1800 | 3200 |
| Damping (N·s/m)       | 60  | 135  | 240  |
| Target altitude (m)   | 2.5 (constant) | | |
| Max raycast (m)       | 4.0 (constant) | | |

**v1 limitation, mass + CPU don't scale per-instance.** The
`BlockDefinition` carries a single `Mass = 10 kg` / `CpuCost = 50`
constant across all sizes — `Robot.RecalculateAggregates` reads
`Definition.Mass` once per block and has no per-instance hook today.
Lift scales with N² but the cost of a bigger blade is flat, so
size-4 is the meta. v2 fix: add an `IEffectiveMass` interface
queried by `RecalculateAggregates`; defer until we have a balance
pass with playtest data.

## Files

New:
- `Assets/_Project/Scripts/Movement/HoverBladeBlock.cs` — core spring-damper + raycast + AddForceAtPosition + audio loop + persistent dust particle system
- `Assets/_Project/Scripts/Movement/RobotHoverBladeBinder.cs` — attaches `HoverBladeBlock` to placed blocks (RobotRotorBinder pattern)
- `Assets/_Project/ScriptableObjects/BlockDefinitions/BlockDef_HoverBlade.asset` — `_id = block.movement.hoverblade`, mass 10, CPU 50, leaf block, variant config on
- `Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultHoverTank.asset` — 5×5 chassis structure, 4 corner size-2 hover blades, weapon, CPU (no thrusters — hover-only bot for demo)
- `Assets/_Project/Tests/PlayMode/Movement/HoverBladeBlockTests.cs` — 4 tests covering the invariants (no stratosphere, ray-miss fall, N² scaling, destroyed-blade silence)

Edited:
- `Assets/_Project/Scripts/Block/BlockIds.cs` — `BlockIds.HoverBlade`
- `Assets/_Project/Scripts/Block/BlockVariants.cs` — registered in `s_hardcodedVariableIds`
- `Assets/_Project/Scripts/Block/BlockConnectivity.cs` — registered in `s_hardcodedLeafIds`
- `Assets/_Project/Scripts/Block/BlockOccupancy.cs` — `ComputeHoverBladeSweptBoundsLocal` for N×N×1 AABB (perpendicular axes derived from mount-up so it works on any face); shared `ResolveHoverBladeSize` helper
- `Assets/_Project/Scripts/Block/DevTuningOverride.cs` — `ApplyHoverBlade` + `HoverBladeTuningConfig` struct
- `Assets/_Project/Scripts/Core/AudioCue.cs` — `HoverBladeLoop`, `HoverBladeContactLost` (placeholder clips, missing-cue logger surfaces them at runtime)
- `Assets/_Project/Scripts/Core/PerfMarkers.cs` — `HoverBladeFixedUpdate`
- `Assets/_Project/Scripts/Core/Tweakables.cs` — `Dev.HoverBlade.SpringK / DampingC / TargetAltitude` (dev-guard wrapped)
- `Assets/_Project/Scripts/Gameplay/ChassisAssembler.cs` — `EnsureComponent<RobotHoverBladeBinder>` in Phase 3
- `Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs` — new `_hoverSection` with integer size 2–4 slider + lift-multiplier readout
- `Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs` — `ForBlockId` returns `BlockMat_Aero` for hover blade
- `Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs` — `DefaultHoverTankPath` + preset slot 8
- `Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset` — added `BlockDef_HoverBlade` reference

## Invariant compliance

- **#1 — no Tweakable affects gameplay outcomes.** Spring/damping/altitude
  knobs are baked into `HoverBladeTuningConfig.Default`; the dev-tuning
  override layer is `#if UNITY_EDITOR || DEVELOPMENT_BUILD` and
  compile-stripped from shipping builds, same pattern as session 98.
- **#4 — single Rigidbody per chassis.** Blade has no Rigidbody; force
  applied via `chassisRb.AddForceAtPosition` at the blade's world
  position.
- **#5 — zero baseline cost for new physics blocks.** No new Rigidbody,
  no joints. One raycast per blade per `FixedUpdate` — same cost class
  as a wheel's suspension cast.
- **#6 — no per-frame allocations.** Static `s_hitBuffer` for
  `Physics.RaycastNonAlloc`. Persistent ParticleSystem child for VFX
  (toggling emission rate, not spawning per-frame).
- **#8 — every new feature ships with VFX + audio.** Dust-blow plume
  via `RuntimePalette.DustLight → SmokeDark`. Two new audio cues
  declared in `AudioCueLibrary` (clips placeholder for now —
  missing-cue logger flags them at runtime).
- **physics.md §2 — no new joint chains.** Spring-damper is math on
  the chassis Rigidbody; zero `ConfigurableJoint` instances added.

## To pick up the new preset in the bootstrap scene

The hover-tank blueprint asset is on disk and `GameplayScaffolder` is
updated to wire it as preset slot 8. Run **Robogame → Scaffold → Build
Bootstrap (Pass A)** in the editor to populate the live Bootstrap.unity
scene's `_presetBlueprints` array; until that runs, the new bot won't
appear in the HUD picker.

## Tests

4 new playmode tests under
`Assets/_Project/Tests/PlayMode/Movement/HoverBladeBlockTests.cs`:

- `…_AppliesZeroForce_WhenGapExceedsTargetAltitude` — no-stratosphere clamp
- `…_AppliesZeroForce_WhenRaycastMisses` — terraformed-pit fall
- `…_SpringForceScalesWithNSquared` — size-4 produces ~4× size-2 lift
- `…_DestroyedBlade_AppliesNoForce` — per-corner failure
