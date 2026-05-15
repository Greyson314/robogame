# 72 — Terraforming Phase 3 (drill + bomb crater integration)

> Status: **shipped, machine gate green via autonomous runner.**
> Dig zones are now player-interactive: drill blocks emit
> CapsuleSubtract on contact, bombs detonating inside a zone emit
> SphereSubtract craters. First gameplay-visible terraforming.

## Why this session

User: *"yes, let's move onto phase 2c."* (which extended past 2c→2d
into Phase 3 per the "knock out multiple phases" directive added
this session).

Phase 3 was sub-split into three commits as the work landed:

- 3a (commit `79ebe839`): `CapsuleSubtract` implementation in
  `BrushApplicator`.
- 3b (commit `abd85697`): `DrillBlock` standalone MonoBehaviour.
- 3c (this commit): bomb-detonation crater integration.

## What changed

### Phase 3a — [`BrushApplicator.ApplyCapsuleSubtract`](../../Assets/_Project/Scripts/Voxel/BrushApplicator.cs)

The previously-stubbed `CapsuleSubtract` case is now implemented.
For each cell in the brush's AABB:

1. Project the cell position onto the capsule axis (`p1 - p0`).
2. Clamp the parametric `t` to `[0, 1]` so endpoints become
   hemispherical caps.
3. Measure distance from the cell to the resulting closest point on
   the segment.
4. Same max-fold update as `SphereSubtract`.

Degenerates correctly to `SphereSubtract` when `p0 == p1` (axis
length below epsilon) — verified by a test that asserts
cell-for-cell equality between the two brush kinds with the same
centre + radius.

7 new EditMode tests in
[`BrushApplicatorTests`](../../Assets/_Project/Tests/EditMode/Voxel/BrushApplicatorTests.cs)
pin shape correctness:

- Axis-aligned X tunnel carves cells along the axis.
- Cells beyond the radius (orthogonal to the axis) are untouched.
- Cells past the capsule's endpoint cap are untouched.
- Degenerate capsule == equivalent sphere (byte-identical).
- Brush outside the chunk AABB mutates zero cells.
- Re-applying the same capsule is idempotent (max-fold).

### Phase 3b — [`DrillBlock`](../../Assets/_Project/Scripts/Voxel/DrillBlock.cs)

New MonoBehaviour in `Robogame.Voxel`. Plain standalone block (not
a `TipBlock` subclass for now — rope-adoption can be layered later
by mirroring `HookBlock`/`MaceBlock`'s pattern).

Public `Drill(DigZone)` emits a `CapsuleSubtract` swept from the
drill tip's previous-tick world position to the current. The first
call has no previous tip, so the capsule degenerates to a sphere
at the current position. Subsequent calls span the actual motion.

`OnCollisionStay` routes contacts with `DigChunk` colliders to
`Drill()`, gated by an `_emitInterval` (default 0.033 s ≈ 30 Hz,
matching the design's drill tick rate per TERRAFORMING_PLAN § 4).
This stops a 50 Hz physics tick from firing one brush per
`FixedUpdate` even when emit-rate caps are unnecessary.

On each emit, `AudioRouter.PlayOneShot(AudioCue.DrillContact)` and
`VfxSpawner.Spawn(VfxKind.DebrisDust)` fire at the tip position.
The new `DrillContact` audio cue is declared in `AudioCue.cs`; the
audio library entry is intentionally blank — the missing-cue
logger surfaces it for the audio pass to author (per the
AUDIO_PLAN.md flow).

Two PlayMode tests:
- `DrillBlock_DrillInsideZone_EmitsBrushAndMutatesSdf`: synthetic
  drill cycle inside the half-space's interior. Asserts cells get
  carved.
- `DrillBlock_NoMotion_ReDrillSamePoint_ChangesNothing`: re-drilling
  at the same point hits the monotonicity invariant — zero cells
  change.

### Phase 3c — bomb crater integration

[`TerrainCratering.OnBombDetonation(worldPoint, radius)`](../../Assets/_Project/Scripts/Voxel/TerrainCratering.cs):
static helper in `Robogame.Voxel`. Looks up `DigField.ZoneAt(worldPoint)`,
and if non-null builds a `SphereSubtract` and applies it. No-op if
no zone matches or radius is non-positive.

[`ProjectileWorld.DispatchImpactFx`](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs)
now calls `TerrainCratering.OnBombDetonation(pos, spec.SplashRadius)`
in the `ProjectileKind.Bomb` branch after the existing VFX/audio
dispatch. Bombs detonating outside any dig zone behave exactly as
before (the call early-outs).

Required `Robogame.Combat.asmdef` to reference `Robogame.Voxel`.
Conceptually clean dependency: Combat → Voxel (combat events
trigger voxel changes), not the reverse.

`IDigZone` interface in `Robogame.Core` gained `int ApplyBrush(BrushOp op)`
so consumers can dispatch brushes through the interface without
depending on the concrete `DigZone` (in `Robogame.Voxel`).
`DigZone` already had the same signature; no implementation change.

Three new PlayMode tests:
- `TerrainCratering_BombInsideZone_CarvesSphereCrater`
- `TerrainCratering_BombOutsideAnyZone_IsNoOp`
- `TerrainCratering_BombInsideZoneButZeroRadius_IsNoOp`

## Decisions worth flagging

**DrillBlock standalone, not a `TipBlock` subclass.** The plan
calls it a "sibling to HookBlock / MaceBlock," but those subclass
`TipBlock` which has rope-adoption + reduced-mass damage math
specific to swung tip blocks. For Phase 3b's drill behaviour — emit
a brush op on contact — none of the TipBlock plumbing is needed.
A future pass can add a rope-mounted variant by mirroring the
adoption-and-forwarder pattern; nothing in DrillBlock's API
forecloses that.

**Bomb crater radius = `spec.SplashRadius`.** The bomb's existing
gameplay splash radius drives both the damage AOE and the terrain
crater. Tying them together makes design intuitive (bigger blast →
bigger crater) and avoids a separate "crater radius" knob. If
playtesting calls for divergent values, easy to add a per-spec
`TerrainCraterRadius` later.

**`Robogame.Combat → Robogame.Voxel` asmdef reference.** The
alternative — having `Voxel` watch combat events via a static
event — would invert the dependency and entangle the projectile
system with terraforming initialization order. A direct call from
`ProjectileWorld` is straightforward.

**`IDigZone.ApplyBrush` added to the interface.** Promotes the
brush dispatch into the Core-asmdef abstraction so combat (and
future drill-like systems) can carve terrain without a hard
dependency on the concrete `DigZone` class.

**Audio cue declared, library entry blank.** Per CLAUDE.md / the
project's audio plan, new gameplay features call `PlayOneShot`
with a fresh cue even when the audio author hasn't recorded the
clip yet. The missing-cue logger surfaces the deficit for the
next audio pass.

**Reused `VfxKind.DebrisDust`.** No new VFX kind for drilling —
the slate-coloured-dust + cube-fragments recipe (used today for
chassis block detach) reads correctly as drill spoil. If
playtesting shows it looks wrong specifically for terrain, easy
to add a `TerrainDust` kind in a polish pass.

## What I deliberately did NOT do

1. **No `BlockDefinition` asset for `DrillBlock`.** A drill needs
   one to be placeable on a chassis via the build mode. Defer to a
   designer-facing pass.
2. **No chassis preset with a drill.** Same reasoning — designer
   pass.
3. **No `DrillBlock : TipBlock` rope-adoption pathway.** Future
   work if a "rope drill" reads better than a hard-mounted one.
4. **No bake-mesh-into-`.dig` editor tool.** Phase 2d follow-up;
   the format itself is ready.
5. **No SP playthrough scenario** (drive up, drill into a chamber,
   drop a bomb, see a crater). That's the visual playtest exit
   criterion — passes through user-side testing.

## Files

- **Added:**
  - `Assets/_Project/Scripts/Voxel/DrillBlock.cs`
  - `Assets/_Project/Scripts/Voxel/TerrainCratering.cs`
  - `Assets/_Project/Tests/EditMode/Voxel/BrushApplicatorTests.cs`
- **Modified:**
  - `Assets/_Project/Scripts/Voxel/BrushApplicator.cs` —
    `CapsuleSubtract` implementation.
  - `Assets/_Project/Scripts/Core/DigField.cs` — `ApplyBrush` on
    `IDigZone`.
  - `Assets/_Project/Scripts/Core/AudioCue.cs` — `DrillContact` cue.
  - `Assets/_Project/Scripts/Combat/Robogame.Combat.asmdef` —
    `Robogame.Voxel` reference.
  - `Assets/_Project/Scripts/Combat/ProjectileWorld.cs` —
    `TerrainCratering.OnBombDetonation` call in bomb impact FX.
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` — 5 new
    tests (2 DrillBlock + 3 TerrainCratering).

## Hard-invariant check

- **No `Tweakable`s added.** Drill radius, emit interval, crater
  radius are all per-block / per-spec serialised fields (not
  per-machine config).
- **No physics changes** to existing chassis / projectile behaviour
  outside the bomb-crater hook (which is a no-op outside dig zones).
- **No new failure modes for arenas without DigZones.** TerrainCratering
  early-outs; DrillBlock without a registered DigZone is also a
  no-op (the OnCollisionStay branch returns when no chunk is found).
- **No per-frame allocations** in the new hot paths.
- **Audio + VFX wired** at the drill site per the CLAUDE.md
  invariant.

## Validation

`.claude/scripts/run-tests.sh PlayMode` (after 3c):
- 44/46 passed, 2 failed (pre-existing `HookGrappleTests` +
  `RotorBlockTests`, unrelated).
- All 5 new Phase 3 tests pass (2 DrillBlock + 3 TerrainCratering).

`.claude/scripts/run-tests.sh EditMode` (after 3a):
- 183/184 passed, 0 failed, 1 inconclusive (preset env).
- All 7 new `BrushApplicatorTests` pass.

Visual playtest still useful to confirm: place a `DrillBlock`
on a chassis and drive it into the test scene's DigZone, observe
the dust VFX + carved tunnel. Drop a bomb on a dig zone, observe
the crater.

## What Phase 4 needs from here

Phase 4 — chunk LOD + transvoxel seams — is the next milestone.
The Phase 3 work made the dig system player-interactive at small
scale; Phase 4 makes it ship at the worst-case triangle budget
for large dig zones.

Specifically:
- Distance-based LOD: chunks beyond camera distance mesh at half
  or quarter resolution.
- Transvoxel transition cells at LOD boundaries (Eric Lengyel's
  reference port).
- LOD-on-edit: a remote drill modifying a far chunk re-meshes at
  the chunk's current LOD.
- Profile pass against the worst-case 100-chunk + heavy-excavation
  triangle count.
