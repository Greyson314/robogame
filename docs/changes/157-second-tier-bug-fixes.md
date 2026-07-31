# 157 — Second-tier bug fixes (spring-cleaning review batch)

Eleven verified bugs from the deep review that fell below the main
report's 15-finding cap. Each was adversarially confirmed against the
code before fixing.

## Fixes

- **Debris stops acting like chassis.** `Robot.DetachAsDebris` now
  disables every block MonoBehaviour on the detached subtree (a debris
  pogo kept its cached chassis Rigidbody and kicked the live chassis
  for its whole 4 s lifetime, stealing the shared bounce window).
  Opt-out via new `Block.IDetachAware` — `RopeBlock` implements it
  because its reparent-rebuild is designed debris behaviour.
- **Pogo no-op claims.** The bounce arbiter gained a read-only
  `CanClaim`; a foot now latches the shared window (+ cooldown + boing)
  only when `deltaV > 0` actually applies a bounce.
- **Air bots recover from LowHealth.** Added the missing exit
  transition (mirrors GroundBot's Retreat recovery); healing is real
  (RepairPad, RepairPulse), and both files' stale "no heal mechanic"
  comments are corrected.
- **AimReticle self-target.** Reparented rotor foils (no Robot
  ancestor) no longer turn the crosshair red — grid-membership
  self-check reused from `RobotDrive.ComputeAimPoint`.
- **Flip preserves heading when upside-down.** Antiparallel
  `FromToRotation` special-cased to a 180° roll about the projected
  forward axis.
- **Bot reloads silenced.** `WeaponAmmoState` reload cues gate on
  `_input is PlayerInputHandler` (2D UI channel is local-player-only);
  also gained the LOG-132 input late-re-resolve.
- **Repair module's green tint is visible.** Definition `TintColor`
  now composes into `BlockBehaviour`'s damage-visual MPB instead of a
  placement-time MPB that the full-health `SetPropertyBlock(null)`
  batcher-rejoin wiped. Untinted blocks keep the SRP-Batcher
  optimisation (§8.2); `BlockGrid.ApplyTint` deleted.
- **Latency dev-tool works in-Editor.** `?? AddComponent` fake-null
  traps replaced with explicit Unity-null checks in
  `NetcodeFakeLatencyController`; the three latent `??` parent-fallback
  sites (DeathOverlay, HitMarkerOverlay, AirBot lead-aim) simplified to
  plain `GetComponentInParent`.
- **Server input backlog drains.** `NetworkRobotMovement` catches up
  (max 4/tick, down to a 2-deep tolerance) instead of one-in/one-out
  after a jitter burst; `ServerCommandQueue.PendingCount` added.
- **Magnet pull is saturation-proof.** Pull field iterates the new
  `Core.ChassisRegistry` (Robot registers OnEnable/OnDisable) instead
  of a mask-~0 OverlapSphere whose 32-slot buffer the owner's own
  colliders could fill. Side effect (doc-conformant): the field pulls
  chassis only, no longer loose debris/scrap bodies.
- **Seeded gameplay RNG.** New `Core.GameplayRng` (§12.4): SMG spread
  and scrap-scatter positions now roll on a reseedable System.Random
  stream. ScrapDepot's `Random.value` stays — it throttles a VFX
  flicker, the cosmetic case §12.4 permits.

## Deliberately skipped

- Recoil/module impulses still fire from `Update` (§4.2 conventions
  breach; PhysX buffers impulses so behaviour is correct — deferring
  needs a fire-path restructure, not worth it standalone).
- TipBlock / MomentumImpactHandler cooldown dicts still grow with
  destroyed-object keys (bounded by chassis lifetime).
- No team filter on the magnet (needs MatchSide plumbing into
  Movement; FFA matches make it moot today).

## Open follow-ups

- Bot-AI shared base (dead-code batch item 14) is now unblocked by the
  LowHealth fix; extraction not started — the two brains' states differ
  enough that the split wants a sign-off.
- MP note: `WeaponAmmoState`'s local-player check needs the delegating
  `NetworkInputSource` to surface its inner source when ownership lands.
