# Session 25 — Rope re-anchor on enable + cursor lock in build mode

> Status: **shipped.** Two user-reported follow-ups from session 24's
> playtest, plus a reflective pass on the running "open threads" list
> for the next session.

## Fix 1 — Rope falls off the plane

User reported the hook adoption now works (rope ↔ hook), but the rope
isn't attached to the chassis: rope + hook fall to the ground.

Root cause was session 23's idempotency experiment. `RopeBlock.OnEnable`
was changed to call `Build()` instead of `Rebuild()`, with `Build`
early-returning if `_segmentContainer != null`. The intent was to skip
work during the `Robot.CaptureTemplate` SetActive(false→true) cascade.
Side effect: when the chassis re-activates with a new / re-acquired
Rigidbody (or the post-cascade transform stack subtly differs), the
existing segments at scene root keep their old joint connections. If
those joints' `connectedBody` is now stale (fake-null after a chassis
hot-swap), the segment chain falls under gravity while still visually
intact with the hook.

**Fix.** Reverted the idempotency. `OnEnable` calls `Rebuild()` again,
which destroys + re-creates segments fresh against the *current*
chassis Rigidbody. The original `OnDisable` SetParent crash that
idempotency was meant to dodge is already covered by the
`OnDisable` no-op (which sessions 23 left in place) — destroys now
happen at `OnEnable` time against an active chassis, where
`ReleaseAdoptedTip`'s `SetParent` call doesn't fight a transitioning
parent.

## Fix 2 — Cursor hidden in build mode

`BuildFreeCam` previously left the OS cursor visible. The user wanted
it hidden so the screen-center reticle is the only input target — the
mouse delta steers the camera, the cursor itself doesn't appear.

`BuildFreeCam.OnEnable` now does `Cursor.lockState = Locked; visible =
false`. `OnDisable` releases.

To preserve HUD interaction (Build button, chassis dropdown, settings
panel) the click-to-capture pattern from `FollowCamera` is grafted in:
`Esc` releases the cursor; left-clicking in the game view (not over
UI) re-locks. Camera rotation + WASD translation + scroll dolly are
all gated on `Cursor.lockState == Locked` so HUD interactions don't
also fly the camera.

## Anything not previously logged

Going through the running session log to make sure everything's
captured. Items NOT covered in earlier session docs that the next
session should know:

- **Multiple binder-order constraints have accumulated.**
  `ChassisFactory.Build` now requires:
    1. `RobotTipBlockBinder` BEFORE `RobotRopeBinder` (session 24:
       ropes adopt tips at activation; tip components must exist first).
    2. `RotorsGenerateLift` flag flipped AFTER the SetActive cascade
       (session 17: `RotorBlock` is added by binder, doesn't exist
       yet at flag-set time otherwise).
  Both invariants are commented in the code, but the broader takeaway
  is "binder ordering is load-bearing." A future binder addition
  should be checked against both rules.

- **`OrbitCamera` is dead code.** Replaced by `BuildFreeCam` in
  session 23. The component file is still in
  `Assets/_Project/Scripts/Player/OrbitCamera.cs` but nothing
  references it (verified by grep in session 23). GC candidate.

- **`Tweakables.json` overrides code defaults.** Bumping a registered
  default in code does NOT take effect for users with a saved value
  on disk. `Load()` clamps the saved value into the new spec range
  but doesn't overwrite it. If a future session bumps a default and
  expects users to see it, either (a) tell the user to wipe
  `Application.persistentDataPath/tweakables.json`, (b) bump the
  slider in-game, or (c) add a migration version field to the save
  format. Documented in `architecture.md` gotchas table since
  session 22.

- **Three asset-file rename hops on the test dummy** —
  `Blueprint_BarbellDummy` (s19) → `Blueprint_DumbbellDummy` (s22) →
  `Blueprint_ArchDummy` (s24). Done via `git mv` each time so the GUID
  is preserved; `[FormerlySerializedAs]` chain on `ArenaController`'s
  field stack carries scene wire-up across all three names. Future
  rename: keep the chain intact, or accept that pre-rename scenes
  lose the wire-up and require a `Build Everything` pass.

- **Helicopter inertia-tensor asymmetry was an analysis-only diagnosis
  in session 23.** The "fix" was removing the asymmetric tail rotor.
  The underlying mechanism (Unity auto-computes inertia tensor from
  collider distribution; `RobotDrive`'s constant `centerOfMass`
  override creates a frame mismatch with cross-coupled angular axes)
  is still latent — any future asymmetric chassis will have the same
  drift unless `Rigidbody.inertiaTensor` is explicitly managed.

- **`Combat.RopeDamagePerKj` and friends are MP debt.** PHYSICS_PLAN
  §1.5 forbids gameplay-affecting Tweakables, but rope-tip damage
  knobs are currently exactly that. Documented in PHYSICS_PLAN §3 as
  "MP debt — server picks canonical values when netcode lands."
  Same status as `Impact.*` and `Combat.Smg*` / `Combat.Bomb*`.

- **`HookBlock._grappleBreakForce` etc. are NOT Tweakables.** They're
  `[SerializeField]` inspector fields on the hook component — by
  design, since they affect gameplay outcomes (whether a hook
  detaches under a pull). Migrate to per-block blueprint config when
  PHYSICS_PLAN §6 lands.

- **Bullets passing through chassis on far cross-fire** (session 24
  open thread). The `RobotDrive.ComputeAimPoint` self-skip now covers
  reparented foils, but a bullet fired from the +X gun toward a
  far-left target still travels through cabin cells on the way out.
  Fix would be projectile-vs-chassis ignore-pair at fire time.
  Defer until reported in a real combat scenario.

- **Verlet rope migration is teed up** (PHYSICS_PLAN §2 + session 22
  Phase C). The current PhysX-joint chain has known stability and
  networking-cost issues. Migration triggers: profiler shows >1.5 ms
  PhysX simulate at the rotor stress tower at 600 RPM, OR a
  flail-style weapon needs full-chain world collision, OR networking
  lands. None have fired yet. Estimated ~weekend of work.

- **B1 garage render of the helicopter** (session 17 report) was
  closed by session 18's kinematic-chassis early-return in
  `RotorBlock.BuildLiftRig`. Last known status: should display
  correctly. Worth a visual check in any session that touches
  `RotorBlock`.

## Future-session readme

Most useful starting sequence for a new AI session:

1. Read `docs/changes/architecture.md` — current modules + gotchas.
2. Read the highest-numbered `docs/changes/NN-*.md` file —
   that's the most recent session's intent + outcome.
3. Read `docs/changes/README.md`'s "Recent batch" + "Known
   unknowns going forward" sections — the running open-threads list.
4. Skim `docs/PHYSICS_PLAN.md` § 1 (non-negotiables, §1.5 in
   particular — Tweakables can't drive gameplay outcomes).
5. Glance at `CLAUDE.md` user-prefs section.

If the user asks for a feature touching rope, hook, mace, or
helicopter:
- Check `docs/changes/19`, `21`, `22`, `23`, `24`, `25` — the
  helicopter + rope-tip arc.
- The block grid + binder ordering is load-bearing for adoption-style
  features (rotor adopts foils, rope adopts tip).
