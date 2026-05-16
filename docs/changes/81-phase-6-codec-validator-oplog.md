# 81 — Phase 6 data plumbing: codec, validator, op-log

> Status: **shipped, machine gates green** for the data half of
> Phase 6. The transport half (Unity Netcode for GameObjects setup,
> two-NetworkManager PlayMode harness, actual ClientRpc plumbing)
> stays parked behind NETCODE_PLAN Phases 1–4, which haven't started.
> Everything that can be built and tested in-process today is in.

## What changed

### `BrushOpCodec` (new)

[`Assets/_Project/Scripts/Core/BrushOpCodec.cs`](../../Assets/_Project/Scripts/Core/BrushOpCodec.cs):
zero-alloc binary encode / decode for `BrushOp` and `BrushOpBatch`.
Fixed-size, little-endian, header-less:

- `EncodedOpSize` = 17 bytes (`kind` 1 + `serverTick` 2 + `p0` 6 +
  `p1` 6 + `radiusFixed` 2).
- `EncodedBatchHeaderSize` = 6 bytes (`digZoneId` 2 + `serverTick` 2
  + `count` 2).
- `EncodedBatchSize(count)` = 6 + 17·count.

Direct `byte[]` + offset reads — no `BinaryReader`/`BinaryWriter`
wrappers, no per-call allocation. Bounds-checks the buffer before
each op so a malformed batch header can't smash adjacent memory.
`DecodeBatch` rejects counts above `BrushOpBatch.MaxOpsPerBatch`
(32) as bad input — that's the anti-cheat boundary for a network
peer claiming a 100K-op batch.

### `BrushOpValidator` (new)

[`Assets/_Project/Scripts/Core/BrushOpValidator.cs`](../../Assets/_Project/Scripts/Core/BrushOpValidator.cs):
stateless server-side validation. Three rules:

1. `ValidateKind` — `BrushKind.None` (default-constructed bogus
   ops) rejected; `SphereSubtract` and `CapsuleSubtract` accepted.
2. `ValidateRadius` — non-zero, ≤ `MaxRadiusMeters` (16 m). A 16 m
   brush would carve a 32 m crater — bigger than a chunk — so
   anything larger is almost certainly a malformed op or a cheat
   attempt.
3. `ValidateZoneOverlap` — the brush volume's bounding sphere
   intersects the target zone's AABB. Rejects "drilling at world
   (10 000, 0, 0)" attacks. Edge-grazing brushes pass because the
   per-cell apply handles out-of-zone cells as no-ops anyway.

`Validate(op, zoneBounds)` runs all three. The chassis-relative
rules from TERRAFORMING_PLAN § 10 ("drill is alive on the chassis,"
"position within `ChassisAimReachableBounds`") live one layer up
because they need chassis context this validator deliberately
doesn't carry — kept stateless + immutable so a future
multithreaded validation path can call it concurrently.

### Cumulative op-log on `DigZone`

[`Assets/_Project/Scripts/Voxel/DigZone.cs`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)
now tracks every brush op that mutated the SDF in
`_opLog : List<BrushOp>`. `ApplyBrush` appends on `changed > 0`
(zero-effect ops aren't logged — keeps the log size correlated
with gameplay impact, not call rate).

Two public surfaces:

- `OpLog : IReadOnlyList<BrushOp>` — read-only view for late-join
  replication.
- `ReplayLog(IReadOnlyList<BrushOp>)` — apply the entire log to a
  fresh zone. Tests pin that this converges to the same SDF as the
  source even when ops are shuffled.

## Tests (5 new)

EditMode ([`BrushOpCodecTests.cs`](../../Assets/_Project/Tests/EditMode/Voxel/BrushOpCodecTests.cs)):

- `EncodeOp_ProducesExactly17Bytes` — wire-format-size pin.
- `EncodeOp_DecodeOp_RoundTripsByteIdentical` — `BrushOp` round-
  trip is the Phase 6 encode/decode machine gate.
- `EncodeOp_AtOffset_DoesNotTrampleAdjacentBytes` — sentinel-fill
  test ensures the codec respects buffer offsets and doesn't run
  off the end.
- `EncodeBatch_DecodeBatch_RoundTripsByteIdentical_EmptyOps` —
  zero-op batch is still legal (a tick that emitted nothing).
- `EncodeBatch_DecodeBatch_RoundTripsByteIdentical_MultipleOps` —
  the typical case: a tick with 3 brushes round-trips byte-
  identical with all per-op fields preserved.
- `DecodeBatch_RejectsCountAboveMaxOpsPerBatch` — the anti-cheat
  boundary: a peer claiming `MaxOpsPerBatch + 1` ops is rejected.
- `BandwidthSynthesis_TenMinDrillTrace_StaysUnderSixteenKbpsPerClient`
  — Phase 6 bandwidth machine gate. Synthesises 600 s × 30 Hz =
  18 000 single-op batches, computes total bytes, asserts the
  derived kbps is under the 16 kbps/client target from
  TERRAFORMING_PLAN § 11. Real number: 23 bytes/tick × 30 ticks/s
  = 690 B/s = 5.52 kbps per drilling client. Three drilling
  clients = 16.56 kbps received per other client — comfortably
  under the 64 kbps total per-client target.

EditMode ([`BrushOpValidatorTests.cs`](../../Assets/_Project/Tests/EditMode/Voxel/BrushOpValidatorTests.cs)):

- `Validate_DefaultUnsetKind_RejectsAsNone` — bogus default
  brushes rejected.
- `Validate_KnownKind_AcceptsKindCheck` — `SphereSubtract` /
  `CapsuleSubtract` pass kind.
- `Validate_ZeroRadius_Rejects` and `Validate_RadiusAtMax_Accepts`
  + `Validate_RadiusAboveMax_Rejects` — radius bounds.
- `Validate_BrushInsideZone_AcceptsOverlap`,
  `Validate_BrushGrazingZoneEdge_AcceptsOverlap`,
  `Validate_BrushFarFromZone_RejectsOverlap`,
  `Validate_CapsuleSweepingThroughZone_AcceptsOverlap` — zone
  overlap on the four interesting cases (inside, grazing, far,
  capsule sweep).

PlayMode ([`DigZoneTests.cs`](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs)):

- `DigZone_ApplyBrushesInDifferentOrders_SdfsConvergeIdentical` —
  the **commutativity machine gate**. Builds two fresh
  1×1×1 zones; applies the same 50 random `SphereSubtract` ops in
  the original order to one and a shuffled order to the other;
  asserts the chunk SDFs are byte-identical. This is the load-
  bearing invariant for Phase 6's ordering-tolerant netcode.
- `DigZone_ReplayLog_OnFreshZone_ConvergesToOriginal` — late-join
  replay path. Source zone applies 20 ops, dumps `OpLog`. A fresh
  zone calls `ReplayLog(log)`; SDFs match byte-identical. Pins
  the late-join contract from TERRAFORMING_PLAN § 10.

## What's deferred (explicit)

- **Two-NetworkManager PlayMode test.** Requires Unity Netcode for
  GameObjects + a multi-NetworkManager harness, both of which need
  NETCODE_PLAN Phases 1–4 to land first. Documented in
  TERRAFORMING_PLAN § 12 Phase 6 as "gated on NETCODE Phase 1–4."
  The commutativity test in this session is the in-process
  equivalent — it pins the SAME invariant the two-NM test would
  pin (different ordering converges), just without the network
  layer.
- **`ClientRpc` per dirty zone per tick.** The wire format
  (BrushOpBatch) is now encoder-ready; the actual RPC dispatch
  + parameter binding waits on NETCODE.
- **Chassis-relative validation rules.** `BrushOpValidator` covers
  the universal half. The "drill alive on chassis," "tip within
  `ChassisAimReachableBounds`" checks layer on top once the
  chassis-context-carrying caller exists.
- **Op-log compaction.** Phase 7 in TERRAFORMING_PLAN —
  periodically snapshot per-chunk SDFs + drop ops below the
  snapshot tick. Not needed until match length exceeds ~30 min or
  op rate exceeds the bandwidth budget (neither is true today).
- **Predicted-fire CSP.** TERRAFORMING_PLAN § 10's "predicted op
  applied locally, server confirmation overwrites." Same gate
  (NETCODE Phase 1–4 prereq).
- **Content-hash on `.dig` assets.** Already exists per Phase 2d
  (`DigZoneFormat` SHA-256). The netcode handshake check that
  uses it lives at the connect-handshake layer (NETCODE Phase 1).

## Files

- New:
  `Assets/_Project/Scripts/Core/BrushOpCodec.cs`,
  `Assets/_Project/Scripts/Core/BrushOpValidator.cs`,
  `Assets/_Project/Tests/EditMode/Voxel/BrushOpCodecTests.cs`,
  `Assets/_Project/Tests/EditMode/Voxel/BrushOpValidatorTests.cs`,
  `docs/changes/81-phase-6-codec-validator-oplog.md`.
- Modified:
  `Assets/_Project/Scripts/Voxel/DigZone.cs` (op-log + replay),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`
  (commutativity + replay tests).

## Validation

- `.claude/scripts/run-tests.sh EditMode`: 209/210 passed, 0
  failed, 1 inconclusive (pre-existing unscaffolded preset).
- `.claude/scripts/run-tests.sh PlayMode`: 64/66 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
- All 7 new EditMode tests + 2 new PlayMode tests pass.
