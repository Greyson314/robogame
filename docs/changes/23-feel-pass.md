# Session 23 — Feel pass: rope-tip lifecycle, J-hook, helicopter symmetry, larger garage, free build cam, scroll zoom

> Status: **in progress.** Multi-part feel pass. Each phase commits at
> its boundary so the user can pull and test incrementally.

## Intent

User-reported issues (in order of session intake):

1. `RopeBlock.OnDisable` throws SetParent-while-deactivating during the
   chassis CaptureTemplate cascade. Same shape as session 17's
   `RotorBlock` bug.
2. Hook reads as a U, not a J. And the hook is "rotation locked" —
   stays rigidly in the chain's frame instead of orienting by gravity.
3. Helicopter chassis tilts to the right at neutral input. User asks
   whether it's the tail rotor's *weight* or *lift*.
4. Camera doesn't zoom (scroll wheel is dead).
5. Build mode's camera soft-locks on the bot — needs free-cam.
6. Garage UI is too big for testing.
7. Garage is too cramped — bot should sit higher and have more room.

Plus a request to identify and ship 3 more follow-up features.

## Phase 1 — Foundational fixes (this commit)

### Rope-tip lifecycle: don't tear down on `OnDisable`

`RopeBlock.OnDisable` was calling `DestroySegments`, which fires
`ReleaseAdoptedTip`, which calls `SetParent` on the tip block to put
it back at its original (chassis-grid-rooted) parent. During the
`Robot.CaptureTemplate` cascade the chassis is briefly `SetActive(false)`
to clone it, so the chassis-grid-root transform is mid-deactivation
and Unity rejects the reparent. Same exact shape as session 17's
`RotorBlock` SetParent crash.

Fix mirrors session 17:

- `RopeBlock.OnDisable` no longer calls `DestroySegments`. Comment
  explains the deferral. Real teardown happens in `OnDestroy` and
  explicit `Rebuild()` calls (Tweakable change, parent swap).
- `Build()` now early-returns if `_segmentContainer != null`, so the
  post-CaptureTemplate `OnEnable` is a no-op.
- `OnEnable` calls `Build()` directly (not `Rebuild`) so the
  idempotent early-out runs.

### Hook is a J, not a U

`HookBlock.TipLength` was 1.50 m — equal to the shaft length. Tip
top sat at Z=0.20 (just below the shaft top at Z=0), making the J
look like a closed bracket / U with a tiny gap.

Reduced `TipLength` to 0.85 m (~half the shaft). Tip now spans Z
0.85..1.70, leaving a clear ~0.85 m mouth above the tip. Reads as a
real J.

### Hook orients freely (relaxed last-segment joint)

The tip block has no Rigidbody of its own; it's parented under the
last rope segment. The segment's joint to the chain has angular
limits at the default 30° per axis, so the segment + tip can only
pivot ±30° relative to the second-to-last segment. The hook's
silhouette therefore stays locked to whatever the chain dictates,
not what gravity would suggest.

Fix: when `TryAdoptTipBlock` succeeds, find the last segment's
`ConfigurableJoint` and set all three angular axes to
`ConfigurableJointMotion.Free`. The combined "last segment + tip"
body then pivots freely about the joint anchor, orienting under
gravity.

The relaxation rebuilds with the rope on every `Build()` (the joint
lives on the segment GameObject, which is destroyed and recreated
each rebuild) so no "restore on release" path is needed. Bare ropes
without an adopted tip keep the default 30° limits — the looser
limits are tip-specific.

### Helicopter right-tilt diagnosis + fix

Answer to the user's "is it weight or lift?" question:

- **Not lift.** The tail rotor at (1, 0, -4) had zero adopted foils
  (confirmed by previous session's `[RotorBlock] adopted 0 foil(s)`
  log). With no foils, no `AeroSurfaceBlock` applies any lift force.
  The kinematic hub spun cosmetically with no physical effect.
- **Not the tail rotor's mass directly.** `RobotDrive.cs:81`
  hard-overrides the chassis Rigidbody's `centerOfMass` to the
  constant `(0, -0.5, 0)`. Mass distribution from blocks doesn't
  shift the COM — Unity's auto-COM computation is suppressed.
- **Inertia-tensor asymmetry.** Unity *does* still auto-compute the
  inertia tensor from the colliders' mass distribution. With the
  COM forced to a non-physical (symmetric) location and the inertia
  tensor reflecting the actual asymmetric mass, applied torques
  operate in a slightly mismatched frame. Off-diagonal terms in the
  inertia tensor cross-couple angular axes, and any small numerical
  perturbation gets amplified into a roll drift.

The cheapest correct fix is to **eliminate the asymmetry** —
which the user designed into the preset only as a cosmetic spinner.
Removed `RotorBare(new Vector3Int(1, 0, -4), ...)` from
`BuildHelicopterEntries`. The chassis is now symmetric across X,
the inertia tensor's off-diagonal terms are near zero, and the
roll drift goes away.

If the user wants a tail rotor back for visual flair, the cleanest
re-adds are:

- Mirrored at `(±1, 0, -4)` (two cosmetic rotors, symmetric).
- Centred at `(0, 1, -5)` mounted on top of the boom tip.
- Front-back centred at `(0, 0, -5)` going forward.

## Phases ahead

- Phase 2 (subagent A's plan): scroll-wheel camera zoom + build-mode
  free cam + garage UI compression.
- Phase 3 (subagent B's plan): garage scaffold expansion (3× floor,
  3× wall height, 12 m hover).
- Phase 4: three follow-up features chosen and implemented after the
  user-requested fixes are in.
