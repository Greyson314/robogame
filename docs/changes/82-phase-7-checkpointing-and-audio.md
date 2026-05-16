# 82 — Phase 7 op-log checkpointing + audio cue pass + drill aim widen

> Status: **shipped, machine gates green.** Three threads, all surgical:
> drill aim cone widened so straight-down / straight-up drilling reads
> right, four declared-but-unmapped audio cues wired up, and Phase 7
> data plumbing (snapshots + op-log compaction) shipped to match the
> Phase 6 pattern from session 81. Transport for Phase 6 + 7 still
> parked behind NETCODE Phases 1–4.

## What changed

### Drill aim cone 30° → 50°

[`DrillBlock.cs:61`](../../Assets/_Project/Scripts/Voxel/DrillBlock.cs)
default `_maxAimAngle` bumped. At 30° the drill couldn't actually dig
straight down or up — the cone bottomed out before the camera reached
the floor, so the only practical drill direction was roughly horizontal.
50° lets a forward-mounted drill aim past vertical with room to spare.
SerializeField default only; no logic change, no test reference to the
old value.

### Audio cue pass — 4 cues mapped

[`AudioCueWizard.cs`](../../Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs)
`s_rows` table grew from 26 to 30 entries:

- `DrillContact` → `PICKAXE_Impact_Dirt_Hard_01_RR4.wav` (per-strike
  bite, SFX bus, 3D, vol 0.55, jitter 0.12, not solo — stacks during
  held-fire).
- `DrillActive` → `MACHINE_Construction_Stone_Crusher_loop_mono.wav`
  (the quiet motor bed under the bite, SFX bus, 3D, vol 0.40, jitter 0,
  solo — one motor per drill).
- `BotDetected` → `ROBOTIC_Short_Burst_13_Digital_Worm_mono.wav`
  (digital lock-on tone, SFX bus, 3D, vol 0.50, solo — replaces if the
  path edge flickers).
- `BotStep` → `FOOTSTEP_Metal_Walk_01_RR06_mono.wav` (heavy mech
  footfall, SFX bus, 3D, vol 0.40, jitter 0.12 — varies per step).

`EnsureLibraryOnFirstLoad` notices the row count diverged from the
asset (26 vs. 30 rows) and rebuilds `AudioCueLibrary.asset` on next
editor load. No asset hand-edit required.

### Phase 7 — `DigZone.Checkpoint(ushort serverTick)`

[`DigZone.cs`](../../Assets/_Project/Scripts/Voxel/DigZone.cs) gains:

- `Checkpoint(serverTick)` — captures the current chunk SDFs by calling
  the existing `DigZoneFormat.Write(this)` (reuses the Phase 2d `.dig`
  wire format byte-for-byte). Then compacts `_opLog` by dropping entries
  with tick at-or-before the snapshot. The "at-or-before" predicate uses
  serial-number arithmetic so an op at tick 5 after a snapshot at tick
  65 530 is retained correctly (wraparound-safe as long as snapshots
  happen at least every 2¹⁵ = 32 768 ticks ≈ 18 min at 30 Hz).
- `HasSnapshot`, `SnapshotTick`, `SnapshotBytes` read-only surface for
  the late-join transport layer.

Late-join replication = (`SnapshotBytes` + post-snapshot `OpLog`) instead
of the from-match-start trail. The joiner feeds the bytes to
`DigZoneFormat.Read` + `ApplySnapshot`, then `ReplayLog` over the rest.

The scheduler that decides *when* to checkpoint (every N seconds vs. every
N ops) lives at the netcode transport layer and stays parked behind
NETCODE Phases 1–4. Phase 7 here is data plumbing only — same shape as
session 81's Phase 6 data plumbing.

## Tests (4 new PlayMode)

[`DigZoneTests.cs`](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs):

- `DigZone_Checkpoint_DropsOpsAtOrBeforeSnapshotTick` — apply ops with
  ticks 1..5, checkpoint at tick 3, assert OpLog retains exactly the
  ticks 4, 5 entries.
- `DigZone_Checkpoint_SnapshotBytesParseableAsDigFormat` — verify
  `DigZoneFormat.Read(SnapshotBytes)` succeeds and yields the expected
  dimensions.
- `DigZone_SnapshotPlusReplay_OnFreshZone_ConvergesToOriginal` — **Phase
  7 machine gate**. Apply ops 1–5, checkpoint at tick 5, apply ops 6–10
  on top. On a fresh zone, `ApplySnapshot` + `ReplayLog(post-snapshot
  ops)` must yield byte-identical SDF to the source. The midpoint
  checkpoint is load-bearing — checkpointing after the final op would
  pass only because sphere subtract happens to be idempotent.
- `DigZone_Checkpoint_TickWraparound_RetainsPostSnapshotOps` — checkpoint
  at tick 65 530 with an op at tick 5 (post-wraparound); asserts the
  tick-5 op survives compaction because serial-number arithmetic treats
  it as "after" 65 530.

## Files

- New: `docs/changes/82-phase-7-checkpointing-and-audio.md`.
- Modified:
  `Assets/_Project/Scripts/Voxel/DigZone.cs` (Checkpoint + 3 fields + 3 properties),
  `Assets/_Project/Scripts/Voxel/DrillBlock.cs` (cone default 30 → 50),
  `Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs` (4 new rows),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` (4 new tests),
  `docs/TERRAFORMING_PLAN.md` (Phase 7 marked shipped).

The library asset `Assets/_Project/Resources/AudioCueLibrary.asset` will
be rebuilt by the wizard's `EnsureLibraryOnFirstLoad` on next editor
load.

## Validation

- `.claude/scripts/run-tests.sh EditMode`: 209/210 passed, 0 failed,
  1 inconclusive (pre-existing unscaffolded preset).
- `.claude/scripts/run-tests.sh PlayMode`: 68/70 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
- All 4 new Phase 7 PlayMode tests pass; the SDF byte-identity machine
  gate is the load-bearing assertion.

## Drill-feel follow-ups (same session)

Two playtest-driven follow-ups after the aim-cone widen didn't land the
"drill up/down" feel:

1. **Aim-direction capsule** (commit `ff757ffd`). The brush was a
   `CapsuleSubtract` from prev-tip to current-tip — for a stationary
   chassis that collapsed to a 3 m-radius sphere shifted only 0.6 m by
   `_tipForwardOffset`, so aim direction barely moved the carve volume.
   `Drill()` now builds a fresh capsule each emit: `p0` = drill cell,
   `p1` = cell + aim · `_brushReach` (new SerializeField, default 4 m).
   Looking up 50° carves a ~10 m elongated capsule along the aim
   direction. Swept-motion tracking dropped (consecutive 30 Hz emits
   overlap > 90 % at any plausible chassis speed). Audio still anchors
   near the cell; VFX dust moved to the capsule midpoint.

2. **Dig-pull.** Even with a directional capsule, a wheeled chassis
   stayed ground-locked. `DrillBlock` now applies
   `AddForceAtPosition(aim · _digPullForce, drillCell, Force)` to the
   chassis Rigidbody whenever `changed > 0`. Applied at the off-COM
   drill cell, it both translates the body toward the dig direction and
   yaws/pitches the nose to follow — the "worm through terrain" feel.
   `_digPullForce` is a SerializeField (default 400 N; not a Tweakable —
   gameplay-outcome rule). Gated on `changed > 0` so it never acts as a
   free thruster in air. Single chassis Rigidbody resolved lazily via
   `GetComponentInParent<Rigidbody>()`, mirroring the `_input` pattern
   (PHYSICS_PLAN § 1.4).

2 new PlayMode tests: `DrillBlock_CarvingPullsChassisAlongAimDirection`
(carve → +aim velocity) and `DrillBlock_DrillingAir_DoesNotPullChassis`
(no carve → no motion). PlayMode after follow-ups: 70/72, same 2
pre-existing failures.
