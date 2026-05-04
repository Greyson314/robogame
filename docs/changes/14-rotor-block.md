# Session 14 — Rotor block + spinning-rope ring + perf-discipline note

> Status: shipped. Compiles clean against Unity 6 / Robogame Pass A.

## Intent

Add a **rotor block** that spins, and use it to demonstrate the
"helicopter blades / chained flail" use case:

> *A rotor block with 4 ropes attached to it, spinning the ropes
> around in circles.*

The user emphasised that **performance is paramount** as physics
features pile on, and asked for a note in the docs that performance
checking will become more important as more physics changes ship.

## What shipped

### New runtime files

- [`Assets/_Project/Scripts/Movement/RotorBlock.cs`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
  — the spinning block. Visual rig (mast + cyan hub disc + crossed
  bars) is a child transform rotated each FixedUpdate — pure transform
  writes, **zero rigidbody cost**. When `Tweakables.RotorRopeCount > 0`
  it adds **one** scene-root kinematic Rigidbody (the "hub") and N
  rope chains attached to it via `ConfigurableJoint`. The hub is
  driven each FixedUpdate via `MovePosition` / `MoveRotation`, so
  PhysX synthesises the tangential velocity at the joint anchors
  from the kinematic displacement and the ropes whip around with
  zero `AddForce` calls on our side.
- [`Assets/_Project/Scripts/Movement/RobotRotorBinder.cs`](../../Assets/_Project/Scripts/Movement/RobotRotorBinder.cs)
  — `BlockBinder` subclass; mirrors `RobotRopeBinder` exactly.

### Wiring into existing systems

- [`BlockIds.cs`](../../Assets/_Project/Scripts/Block/BlockIds.cs) —
  added `Rotor = "block.cosmetic.rotor"`.
- [`Tweakables.cs`](../../Assets/_Project/Scripts/Core/Tweakables.cs) —
  new "Rotor" group with `RotorRpm` (60), `RotorRopeCount` (4),
  `RotorRopeRadius` (0.6 m), `RotorRopeSegments` (4). Reuses the
  existing `Rope*` keys for per-segment dimensions / damping so a
  single set of sliders governs all ropes in the project.
- [`ChassisFactory.cs`](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs) —
  unconditional `EnsureComponent<RobotRotorBinder>(root)` next to
  the existing rope binder, so rotors dragged onto an existing
  chassis in build mode wake up immediately.
- [`BlockDefinitionWizard.cs`](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs) —
  authors `BlockDef_Rotor` (Cosmetic category, mass 0.6, CPU 10).
- [`BlockMaterials.cs`](../../Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs) —
  authors `BlockMat_Rotor` (cyan-tinted slate, slight metallic) and
  routes `BlockIds.Rotor` to it in `ForBlockId`.

### Documentation

- [`docs/BEST_PRACTICES.md`](../BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
  — added the **"Performance discipline scales with physics
  complexity"** callout at the top of §16. Concrete checklist:
  Profiler capture under a populated chassis, active-rigidbody
  count, PhysX simulate time, zero-GC steady state. Documents the
  established pattern of "visual-only by default, physics opt-in
  via Tweakable" using `RotorBlock` as the reference.
- [`README.md`](../../README.md#best-practices) — short bullet
  pointing at the new BEST_PRACTICES section.
- This file + index row.

## Why these specific design choices

### Kinematic hub instead of `AddForce` per segment

Each rope segment is a free-body Rigidbody connected to its
neighbour via `ConfigurableJoint`. To make the chain spin, we have
two options:

1. **Manually push each segment.** `AddForce` per segment per
   FixedUpdate. Cost scales with segment count × rope count and
   fights the joint solver — every push has to be reconciled
   against the joint constraints in the same step.
2. **Move the kinematic anchor.** Set the hub's pose via
   `MovePosition` / `MoveRotation`. PhysX uses the anchor's
   displacement to compute joint impulses for free. Cost is
   constant regardless of segment count.

Option 2 is what kinematic platforms use to carry players, just
applied to joint impulses instead of contact friction. Same
principle, much cheaper, and the rope's own physics behaviour
(centrifugal straightening, drag-induced trail) emerges naturally.

### Visual-only mode (rope count = 0) costs nothing

If a builder sets `Tweakables.RotorRopeCount` to 0, **no Rigidbody
is added at all** — the rotor becomes a pure transform-write
spinner suitable for fan vents, antenna decorations, etc. This is
the pattern I want every future physics-flavoured cosmetic block
to follow: zero baseline cost, opt-in to the heavier physics
version per chassis.

### Hub at scene root (per BEST_PRACTICES §3.1)

The chassis itself is a Rigidbody. Per §3.1, child Rigidbodies of
a moving Rigidbody fight the solver (the parent yanks them around
via transform writes every frame). The hub therefore lives at
scene root, and its world pose is computed each tick from the
host block's transform. Same setup as `RopeBlock`'s segment
container.

## Cost (default config)

Per rotor with 4 ropes × 4 segments:

- 1 kinematic Rigidbody (hub) — costs nothing in PhysX simulate.
- 16 dynamic Rigidbodies (segments) — well under the §16 alarm
  of 64 active rigidbodies. No colliders on segments (cosmetic
  chain), so no contact-solver work.
- ~5 visual Renderers per rope (cylinder per segment) plus the
  hub mast + disc + 2 bars. SRP Batcher friendly via `MK Toon`.

If a chassis stacks 4 rotors at default settings: 4 hubs + 64
dynamic rbs. Still under alarm. **At 8 rotors we'd hit the
threshold** — when that scenario shows up in a real build,
profile and tighten.

## How to try it

1. In Unity, run **Robogame → Scaffold → Gameplay → Build All
   Pass A** to author the new `BlockDef_Rotor` and `BlockMat_Rotor`
   assets.
2. Press Play, enter Build mode, switch to the **Cosmetic** tab in
   the hotbar — you'll see Rope (1) and Rotor (2). Drag a rotor
   onto an existing chassis (e.g. on top of the plane's CPU).
3. Exit build mode and watch it spin. Open the dev settings panel
   (default `~`/`F10` per `SettingsHud`) and adjust the **Rotor**
   group sliders live — RPM, rope count, radius, segments — to
   tune the look. Set rope count to 0 for a visual-only spinner.

## Files touched

```
A Assets/_Project/Scripts/Movement/RotorBlock.cs
A Assets/_Project/Scripts/Movement/RobotRotorBinder.cs
M Assets/_Project/Scripts/Block/BlockIds.cs
M Assets/_Project/Scripts/Core/Tweakables.cs
M Assets/_Project/Scripts/Gameplay/ChassisFactory.cs
M Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs
M Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs
M docs/BEST_PRACTICES.md
M docs/changes/README.md
M README.md
A docs/changes/14-rotor-block.md
```

## Follow-ups (not done this session)

- A "helicopter" or "flail" preset in `GameplayScaffolder.cs` that
  spawns a chassis pre-loaded with a rotor + 4 ropes — would let
  the user press Play and see the use case immediately without
  going through build mode. Holding off because the existing
  presets are already at five entries; adding a sixth deserves
  its own design pass.
- Optional collision on rope segments so a spinning flail can
  actually damage things — touches `MomentumImpactHandler` /
  `Combat`, not just movement, and needs a damage budget design.
- Audio: a low rotor whirr looped at pitch ∝ RPM. Trivial but
  out of scope for this session.
