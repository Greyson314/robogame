# 71 — Terraforming Phase 2d (.dig binary format + bake/load + content hash)

> Status: **shipped, machine gate green.** Phase 2 milestone
> complete: multi-chunk container, apron-based seam-free meshing,
> async MeshCollider bake, and now `.dig` binary serialisation +
> content-hash-stable round-trip.

## Why this session

User: *"yes, let's move onto phase 2c."* (which extended to 2c+2d
when 2c landed cleanly per the new "knock out multiple phases"
directive).

Phase 2d is the binary format that lets authored dig zones ship as
data (rather than the `InitializeHalfSpace` placeholder). Also the
Phase 6 netcode handshake hook — the content hash detects tampered
or mismatched `.dig` assets between client and server.

## What changed

### [`DigZoneFormat`](../../Assets/_Project/Scripts/Voxel/DigZoneFormat.cs)

Static `Write(DigZone) → byte[]` and `Read(byte[]) → DigZoneSnapshot`.
Fixed-layout 68-byte header (magic + version + chunkGridSize +
chunkSizeCells + cellSize + SHA-256 content hash + payload offset)
followed by `chunkGridSize.x*y*z` chunks in z-major order, each
chunk being `(chunkCoord, sdfBytes[dim³])`. Apron data is NOT
serialised — the runtime recomputes apron from neighbour samples
on every remesh (Phase 2b), so storing it would be redundant.

`Read` verifies magic, version, exact buffer length, and SHA-256
hash before returning. Tampered or truncated buffers throw
`InvalidDataException`.

### [`DigZoneSnapshot`](../../Assets/_Project/Scripts/Voxel/DigZoneSnapshot.cs)

Plain data class for the parsed contents. No Unity dependencies
beyond `Vector3Int`.

### [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs) loader integration

New serialised field `_digAsset` (`TextAsset`). If assigned, the
asset's header overrides `CellSize`, `ChunkSizeCells`, and
`ChunkGridSize` at `EnsureInitialised`, and `ApplySnapshot` seeds
each chunk's SDF from the asset payload. If null, falls back to
the existing `InitializeHalfSpace`.

`DigZone.ApplySnapshot(snapshot)` is public so tests and future
runtime loaders can apply snapshots after construction.

`DigZone.DigAsset` property setter throws if called after the zone
is initialised (same pattern as the other config setters), so
tests can configure `_digAsset` before `SetActive(true)`.

### EditMode tests — [`DigZoneFormatTests`](../../Assets/_Project/Tests/EditMode/Voxel/DigZoneFormatTests.cs)

Six tests pinning the format invariants:

- `Write_Read_SmallGrid_RoundTripByteIdentical` — synthetic
  random SDFs survive bake → read → byte-by-byte comparison.
- `Write_TwoRunsSameInput_ProducesIdenticalBytes` — content hash
  stability. Phase 6's handshake relies on this.
- `Write_DifferentInputs_ProduceDifferentHashes` — basic SHA-256
  sanity (different inputs ↦ different hashes with overwhelming
  probability).
- `Read_TamperedPayloadByte_ThrowsInvalidDataException` — flipping
  one payload byte trips hash verification.
- `Read_BadMagic_ThrowsInvalidDataException` — header validation.
- `Read_TruncatedBuffer_ThrowsInvalidDataException` — length check.

### PlayMode test — DigZone integration

`DigZoneTests.BakeAndLoad_ViaDigZone_SdfsByteIdentical`:
1. 2×1×1 zone. Half-space init + a brush at the chunk boundary.
2. Bake → bytes.
3. Tear down the source zone.
4. Make a fresh 2×1×1 zone. Read bytes → snapshot. Apply snapshot.
5. Assert SDF bytes match between snapshot and fresh zone's chunks.
6. Re-bake the fresh zone. Assert byte-identical to the original
   bake (content hash stable through the full round-trip).

## Decisions worth flagging

**SHA-256 for the content hash.** Overkill for tamper detection
but cheap (~200 µs for a 290 KB payload) and gives plenty of
collision resistance for the Phase 6 netcode handshake.
Alternatives considered: MD5 (deprecated for security), FNV-1a
(faster but weaker — fine for unintentional corruption but anyone
who wants to ship a "no-walls" `.dig` to a hacked server could
forge an FNV-1a hash trivially). SHA-256's overhead is invisible
next to the meshing budget.

**Apron data NOT serialised.** Apron is computed from neighbour
samples on every remesh (Phase 2b). Serialising it would double
chunk size on disk for zero gain — the runtime always recomputes
it from authoritative own-SDF data.

**`.dig` stored as TextAsset (`*.bytes` extension).** Considered
ScriptableObject with a `byte[]` field, but Unity's default YAML
serialisation for `byte[]` would inflate a 290 KB payload into
multi-MB YAML. `TextAsset.bytes` is the standard Unity path for
binary assets and skips the YAML cost.

**EditMode tests stub the write path.** `DigZoneFormat.Write`
takes a live `DigZone`, but the EditMode tests construct snapshots
directly and re-implement the write logic inline (35-line helper).
This keeps the format-correctness tests fully MonoBehaviour-free.
The DigZone-integration test in PlayMode exercises the actual
`Write(DigZone)` path.

**No editor baker tool yet.** The plan §9 describes a baker that
voxelises an authored Blender mesh into a `.dig`. For Phase 2d v1,
the round-trip works via `DigZoneFormat.Write(currentZone)` — the
zone's in-memory state can be exported. A "Bake to file" menu and
a "Voxelise mesh asset" tool are Phase 2d.5 follow-ups (or roll
into Phase 4's authoring polish).

## What I deliberately did NOT do

1. **No editor menu to bake the current scene's DigZone to a file.**
   Easy to add later; format functions are ready.
2. **No mesh-voxeliser baker.** The plan §9 baker (voxelise an
   authored mesh) is its own design effort. For now, designers
   can compose `.dig` payloads programmatically.
3. **No RLE / compression on the payload.** Current format is raw
   sbyte SDF. For a half-space the payload compresses ~90% under
   RLE; for fully-excavated terrain less. Format can grow a
   `compression` byte in the header without breaking v1 layout.
4. **No `.dig` content-version migration.** Version 1 is the only
   supported version; bumping requires either a one-way migration
   or a backward-compat reader. Cross that bridge when we get there.

## Phase 2 milestone reached

All four Phase 2 sub-phases shipped, machine gates green:

- 2a (session 67): multi-chunk container.
- 2b (session 69): apron + seam fix.
- 2c (session 70): async Physics.BakeMesh.
- 2d (this session): `.dig` format + content hash + bake/load.

The dig zone system is now: a `DigZone` that loads its initial SDF
state from a `.dig` asset (or falls back to a default seed),
manages multi-chunk SDF storage with seam-free meshing, applies
brush ops via `BrushApplicator`'s max-fold, refreshes the
MeshCollider asynchronously, and exposes a hash-stable bake path
for the Phase 6 netcode handshake.

## Files

- **Added:**
  - `Assets/_Project/Scripts/Voxel/DigZoneFormat.cs`
  - `Assets/_Project/Scripts/Voxel/DigZoneSnapshot.cs`
  - `Assets/_Project/Tests/EditMode/Voxel/DigZoneFormatTests.cs`
- **Modified:**
  - `Assets/_Project/Scripts/Voxel/DigZone.cs` — `_digAsset` field,
    snapshot-driven EnsureInitialised, ApplySnapshot public method.
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` — new
    `BakeAndLoad_ViaDigZone_SdfsByteIdentical` test.

## Hard-invariant check

- **No physics changes** beyond what Phase 2c landed.
- **No `Tweakable`s touched.**
- **No new failure modes for arenas without DigZones.**
- **No per-frame allocations** — format functions are off the hot
  path (asset load is once, bake is editor-only or once per save).

## Validation

`.claude/scripts/run-tests.sh All`:

- EditMode: 176/177 passed, 0 failed, 1 inconclusive (preset env).
  All 6 new `DigZoneFormatTests` pass.
- PlayMode: 39/41 passed, 2 failed (pre-existing). New
  `BakeAndLoad_ViaDigZone_SdfsByteIdentical` is among the passes.

## What Phase 3 needs from here

Phase 3 — drill block + bomb integration — is the next milestone.
Sub-split candidates:

- 3a: implement `CapsuleSubtract` in `BrushApplicator` (currently
  stubbed); EditMode test for capsule-shape correctness.
- 3b: `DrillBlock` tip-block emits `CapsuleSubtract` on contact
  with `DigZone` colliders; PlayMode test for emit-on-contact.
- 3c: bomb-detonation path inside a dig zone emits `SphereSubtract`;
  PlayMode test for crater on detonation.
- 3d: VFX (drill dust, bomb debris) + audio cues per CLAUDE.md's
  "every new feature ships with VFX + audio" invariant.

Phase 3 is where the dig zone becomes player-interactive — the
first gameplay-visible feature of the terraforming arc.
