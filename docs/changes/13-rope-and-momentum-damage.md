# Session — Rope block, GUI tweaks polish, momentum impact damage

**Intent.** Three threads in one sitting. *(1)* "Make a free-body link
block, like a rope, that can be attached to a bot. For example, a plane
may be allowed to have a rope hanging down below it as it flies."
*(2)* Once the rope shipped: "Are there any rope-related sliders we
could add to the GUI tweaks settings? Also, could we add a scroll bar
to the GUI tweaks menu?" *(3)* Then: "We need to build a momentum
damage rule — if a plane flies into the target dummy, both should take
damage based on weight and velocity."

## Rope block

**New cosmetic block.** `block.cosmetic.rope` registered in
[BlockIds.cs](../../Assets/_Project/Scripts/Block/BlockIds.cs).
[BlockDefinitionWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
authors `BlockDef_Rope` (Cosmetic, 40 HP, mass 0.4, CpuCost 5).
[BlockMaterials.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs)
adds a dark-slate matte `BlockMat_Rope` for the build-ghost preview;
the host cube itself is hidden at runtime so the material only matters
in placement mode.

**Runtime behaviour — two new components:**

- [RopeBlock.cs](../../Assets/_Project/Scripts/Movement/RopeBlock.cs).
  Spawns N capsule rigidbodies linked by `ConfigurableJoint`s. Joint
  motion is fully `Locked` translationally and `Limited` on all three
  angular axes — a true free-swinging chain without the floppy
  degeneracy of fully `Free` joints. Defaults: 5 segments × 0.5 m ×
  0.08 m radius, 0.04 kg per segment, ±30° per joint, colliders OFF
  (cosmetic-only — flip `_segmentColliders` for a tow-rope variant).
- [RobotRopeBinder.cs](../../Assets/_Project/Scripts/Movement/RobotRopeBinder.cs).
  `BlockBinder` subclass — auto-attaches a `RopeBlock` to any placed
  block whose id is `BlockIds.Rope`. Idempotent on re-bind.

**Why segments live at scene root, not under the chassis.** Per
[BEST_PRACTICES.md §3.1](../BEST_PRACTICES.md), the chassis is one
Rigidbody with child colliders — child *Rigidbodies* of a moving
parent get kinematically yanked every frame and fight the solver. So
`RopeBlock.Build()` creates a scene-root container
(`Rope_<host>_Segments`) and parents the segments there. The joint
to the chassis carries them along while the solver does the rest.
Detached-debris flow (`Robot.DetachAsDebris`) is handled by an
`OnTransformParentChanged` rebuild and a per-frame
`GetComponentInParent<Rigidbody>()` safety check that reattaches the
rope to whichever body now owns the host block.

**Anchor at the top face, not the bottom.** First playtest exposed a
1-cell gap between the plane's tail and the rope's first link. Cause:
the rope cell is placed at `(0, -1, -3)` (one cell below the rear
thruster) and the original anchor was the *bottom* face of that cell —
so the chain actually started 1.5 m below the thruster. Switched the
anchor to the *top* face so the chain visually flush-attaches to the
underside of the block above the rope cell. See
[RopeBlock.cs#L130-L142](../../Assets/_Project/Scripts/Movement/RopeBlock.cs#L130-L142).

**Default plane has a tail rope.** [GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
adds a `BlockIds.Rope` entry at `(0, -1, -3)` in `BuildPlaneEntries`,
directly under the rear thruster cell — adjacent on the y-axis so the
CPU-connectivity flood-fill still reaches it.

**ChassisFactory unconditional binder.** [ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
adds `EnsureComponent<RobotRopeBinder>(root)` for *every* player
chassis (not gated on the blueprint having a rope today). Avoids the
"I dragged a rope onto an existing plane in build mode and nothing
happened" trap, and the binder is a no-op when no rope blocks are
present.

**Garage hover bumped 4 → 7 cells.** [GarageController.cs](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)'s
`_hoverHeightCells` default raised so the longer rope clears the
podium floor with margin. Caveat: the field is serialised on the
GarageController GameObject in the Bootstrap scene — existing scene
values win until the user re-runs the scaffolder or bumps the field
manually.

## GUI tweaks polish

**Rope sliders.** Added a new "Rope" group to the Tweakables registry
in [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs):
segment count (2–32), length (0.10–1.50 m), radius, mass, angular
limit, and per-segment linear / angular damping. `RopeBlock` now reads
these live and subscribes to `Tweakables.Changed`, so dragging a
slider in Settings ▸ Tweaks rebuilds every active rope on the spot —
rebuilds are O(N segments), N ≤ 32, basically free.

**Vertical scrollbar on the settings panel.** The panel had grown
past one screen by the time Combat / Water / Rope groups were all
registered, so [SettingsHud.cs](../../Assets/_Project/Scripts/Gameplay/SettingsHud.cs)
gained a procedurally-built `Scrollbar` pinned to the right edge of
the scroll area and wired into the existing `ScrollRect`. Visibility
is `AutoHideAndExpandViewport` so it disappears (and reclaims its
strip) when content fits. Mouse-wheel `scrollSensitivity` bumped to
30 so a single notch moves more than one row. New
`BuildVerticalScrollbar` helper matches the dark / orange palette
used by sliders.

## Momentum impact damage

**New component — [MomentumImpactHandler.cs](../../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs).**
Lives on the chassis root Rigidbody; hooks `OnCollisionEnter` (which
bubbles up from child colliders, exactly the contract a compound
chassis needs). Damage formula:

- Take the **normal-component** of relative velocity (tangential
  scrapes don't ram-kill).
- Compute **reduced mass** `μ = m₁·m₂ / (m₁+m₂)`. Symmetric, gracefully
  caps when either side is tiny, collapses to `m_self` against
  static / kinematic geometry — i.e. wall slams dump all of the
  chassis's KE into the chassis, exactly what you want.
- Energy `E = ½ μ v²`, convert to kJ, multiply by `Impact.DamagePerKj`.
- Distribute across a 3-ring splash profile (default 1.0 / 0.30 / 0.10)
  routed through `BlockGrid.ApplySplashDamage`, so connectivity /
  debris bookkeeping stays correct.

**Both sides take damage.** Each chassis owns a handler and damages
its *own* grid only. Newton's third law: the impulse traded is
identical, so applying the same energy budget on both sides is the
right physics. Non-`Robot` `IDamageable` props on the other side (no
handler present) get one direct hit so destructibles still break.

**Per-opponent cooldown.** PhysX fires multiple `OnCollisionEnter`
messages for one logical impact (compound colliders, bounce-and-touch
on the same frame). A 0.20 s `Dictionary<Object, float>` cooldown
keyed on the other Rigidbody (or collider, when static) dedupes those
into one damage event. `UnityEngine.Object` keys handle destroyed
opponents safely — equality is a `==` operator that knows about
fake-null.

**Self-collision guard.** Debris freshly detached from the same
chassis carries enough relative velocity to one-shot its former
mothership on the way out; the handler skips collisions where the
other body's `Robot` reference equals our own.

**Tweakables.** New "Impact" group in
[Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs):
`Damage per kJ` (5), `Min Speed (m/s)` (2 — kills harmless garage
taxi scrapes), and the three ring scales. All live in Settings ▸
Tweaks.

**ChassisFactory wiring.** Added to *both* `Build` (player) and
`BuildTarget` (combat dummy) so plane-vs-dummy collisions deal mutual
damage by default.

**Sanity check.** 50 kg plane vs 50 kg dummy at 30 m/s closing →
μ=25, KE≈11.25 kJ → 56 dmg at the impact cell, 17 / 6 in the next two
rings. Plenty of bite without instakilling either chassis; tune via
the new Impact sliders.

## Lessons / patterns

- **`BlockBinder` is the right shape for "drag-on cosmetic add-ons."**
  A binder per behaviour (rope / wheel / aero / weapon), all
  unconditional on the chassis, all subscribed to `BlockGrid.BlockPlaced`.
  Build-mode placement Just Works without each blueprint having to
  pre-declare every possible component.
- **Free-body Rigidbodies must live at scene root when their anchor
  is a moving Rigidbody.** Codified the rule in BEST_PRACTICES §3.1
  back when we did the chassis compound-collider refactor; the rope
  is the first feature that depends on it. Keep it codified.
- **Splash routing is the right damage primitive for any blocky
  impact, not just bombs.** The momentum handler reuses
  `BlockGrid.ApplySplashDamage` verbatim — so connectivity/debris
  semantics already match player expectations from gunfire and bomb
  bays.
- **Reduced mass is the cleanest "fair" damage scalar.** Treating
  `μ` as the energy donor instead of either body's mass alone makes
  ramming feel right whether you're a fly hitting an elephant or a
  wrecking ball hitting a paper plane — and the static-geometry case
  falls out of the formula for free.
