# 171 — Tree-swing grapple: static geometry is a swing anchor (LOG-171)

User idea: a Spiderman swing off trees using the existing grapple.
Planner verdict: the shipped `GrappleMagnetBlock` already has the aim /
fire / state machine / leash / Verlet chain; the only fork point is
"what did the flight cast hit." User approved extending the shipped
block (no fork), reel on the existing `Vertical` axis (no `IInputSource`
change), own drive stays active while swinging.

## Design

A flight hit whose collider has no `attachedRigidbody` (trees, terrain,
arena walls — backdrop mountains strip their colliders and are naturally
excluded) latches instead of retracting. The tip **stays kinematic** and
is simply left at the hit point: `VerletRopeChain.PinTip` (already set
unconditionally) treats the tip position as authoritative, and a
kinematic body is a legal immovable `ConfigurableJoint` anchor. The
existing chassis↔tip leash is the entire pendulum constraint — no new
joint, no `SpringJoint` tether, no pull field. Rejected
`SpringJoint(connectedBody = null)`: a spring toward a fixed point
oscillates; a hard pin costs nothing.

The swing branch runs stiffer leash constants (`_swingLeashSpring`
14000 N / `_swingLeashDamper` 350 vs the enemy-drag 8000/250) — the drag
pair was tuned as a towing cushion and reads as bungee under sustained
centripetal load. **Untested guess; playtest owed** (this codebase's
documented failure class — tip-blocks.md "why the old design broke",
session-100 rope retune).

Reel: `IInputSource.Vertical` while swinging (climb = in, dive = out),
`_reelSpeed` 6 m/s, clamped to [`_swingMinLength` 3 m, length at latch].
Writes `Joint.linearLimit` + `chain.SegmentLength` only on frames with
live input. Release = tap fire (existing latched verb); destroying the
joint carries momentum with no extra code.

## Files

- `Assets/_Project/Scripts/Combat/GrappleMagnetBlock.cs` — the feature:
  swing fields, `BeginSwingLatch()`, `TickSwingReel()`, static-hit
  branch in `TickFiring`, constant pick in `BuildChassisLeash`,
  `_isStaticSwing` resets in both teardown paths, `IsSwinging` accessor.
  `BeginRetract` now skips velocity zeroing for still-kinematic tips.
- `Assets/_Project/Tests/PlayMode/Movement/GrappleSwingTests.cs` — new
  (3 tests): kinematic tip + swing constants + no tether; reel clamps
  both ends; release leaves chassis velocity bit-clean.

Audio/VFX (INV-8): reuses `AudioCue.TipImpact` + `VfxKind.FlipBurst` at
the anchor, same as the enemy latch. Bespoke thwip is a follow-up.

## Invariants

No ADR needed. All tunables are `[SerializeField]`s (INV-1); tip body is
the existing per-fire scene-root projectile (INV-4); zero cost unfired
(INV-5); reel writes are input-gated, no allocations (INV-6). The swing
path is strictly cheaper than the enemy latch (no SpringJoint, no
`OverlapSphereNonAlloc` pull field).

## Open items

- Playtest the swing leash constants (bungee vs rope feel); fix space is
  spring up / damper toward 0 per session-100 precedent.
- Profiler capture with an active swing (INV-7) — perf-checker pass.
- Follow-ups parked: bespoke thwip audio, HUD reel hint, TuneSchema
  per-instance swing knobs.
