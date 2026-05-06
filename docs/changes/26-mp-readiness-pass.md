# Session 26 — MP-readiness pass: combat per-block migration, inertia tensor, Verlet ropes, polish

> Status: **shipped**, large session. Three big architectural wins for MP
> plus three polish features. Verlet rope behaviour required several
> iterations after the initial implementation; the final state is solid
> but the underlying solver is basic PBD — XPBD is the natural follow-up
> if rope feel under aggressive damping ever becomes a serious quality
> bar (see "Known follow-ups" below).

## Combat per-block migration (PHYSICS_PLAN § 5)

Tweakables that drive gameplay outcomes (damage, fire rate, recoil, etc.)
desync the moment netcode lands — the per-machine JSON values diverge
between clients. Migrated three groups out of `Tweakables` into per-block
data:

- New `WeaponDefinition` + `BombDefinition` ScriptableObjects (see
  `Assets/_Project/Scripts/Combat/`). Carry SMG / bomb stats per
  weapon kind.
- `BlockDefinition` gained a generic
  `ScriptableObject _componentData` slot. Block components cast at the
  consumer (`GetComponentData<T>()`) — keeps `Robogame.Block` from
  taking an asmdef edge on `Robogame.Combat`.
- `ProjectileGun`, `BombBayBlock`, `TipBlock` (Hook + Mace damage)
  read from the new sources with inline `SerializeField` fallbacks.
- `BlockDefinitionWizard` authors default `Weapon_Smg.asset` +
  `Bomb_Default.asset` and wires them onto the relevant
  `BlockDefinition`s.
- Removed Tweakables: `Combat.SmgFireRate / SmgMuzzleSpeed / SmgSpread /
  SmgDamage / SmgRecoilImpulse`, `Combat.BombDropInterval / BombDamage /
  BombRadius / BombInitialSpeed`, `Combat.RopeDamagePerKj /
  RopeMinSpeed / RopeHitCooldown`. Audit comment in
  `Tweakables.cs` updated to reflect what's still pending vs done.

Settings panel no longer shows Combat / Bomb / Rope-tip sliders. Edit
the `.asset` files in the Inspector to rebalance — server-canonical
ready.

## Inertia tensor explicit management (session-25 latent fix)

`Robot.RecalculateAggregates` now computes the mass-weighted COM from
the block grid + a diagonal inertia tensor via the parallel-axis
theorem (each block treated as a uniform cube of side = `cellSize`).
Sets `automaticCenterOfMass = false`, `automaticInertiaTensor = false`,
`inertiaTensorRotation = identity`. RobotDrive stops writing
`centerOfMass` directly; Robot is the single source of truth.

Effects:

- Asymmetric chassis (helicopter, future asymmetric builds) now have
  decoupled angular axes — yaw input produces yaw, no yaw-into-roll
  bleed. Session-25's latent mechanism is fixed structurally.
- Foil adoption (rotor blades reparented off the chassis) no longer
  shifts the inertia tensor — locked tensor is computed from blocks-by-
  mass, not from collider distribution.
- Same input → same response across machines. Deterministic.

The legacy `RobotDrive.CenterOfMassOffset` SerializeField (default
`(0, -0.5, 0)`) still works as a tip-resistance bias for ground
vehicles; Robot pulls it via `RobotDrive.GetCenterOfMassOffset()` and
adds it to the mass-weighted COM.

## Verlet rope (PHYSICS_PLAN § 2)

`ConfigurableJoint` chain replaced by a Verlet particle solver. New
files: `VerletRopeSimulator.cs` (scene-root singleton, batched
`FixedUpdate` integration), `VerletRopeChain.cs` (data class).
`RopeBlock.cs` substantially rewritten.

### Architecture

- 1 chassis Rigidbody (existing) + 1 tip-end Rigidbody (new, per-rope,
  scene-root) + N particles in between. Per PHYSICS_PLAN § 2's
  "replicate hub-pose + tip-pose, simulate the chain locally" plan.
- Distance constraints (adjacent pairs) maintain segment length.
- **Bending stiffness** via skip-one distance constraints (P[i] ↔
  P[i+2] toward `2 × segmentLength`) — keeps the rope from folding
  into discrete Z-shapes; drapes into smooth catenary S-curves
  instead. SerializeField on RopeBlock, default `0.4`.
- **Sub-stepping** (4 sub-steps × 8 iterations = 32 constraint passes
  per FixedUpdate) for stability when the chassis moves fast.
- **Per-particle damping ramp** (linearly from 0% at hub to 100% at
  particle 3+) so high-damping settings don't kill the inertia of the
  chassis-end particles that need to track the moving anchor.
- **Visual layer**: rope cylinders interpolate in `LateUpdate` between
  prev/cur particle snapshots using the same alpha as Unity's
  `Rigidbody.Interpolate`. Plus a per-particle temporal low-pass
  filter (heavy near hub, near-zero at tip) to absorb PBD's residual
  oscillation under stiff damping.
- **Chassis-tip distance joint** (`ConfigurableJoint`, linear motion
  Limited at `totalRopeLength`, soft spring `8000 / 250`). Always-on;
  inactive when the chain is slack, applies tension when the chassis
  flies past max range. Without this the Verlet particles only
  enforce their own distances — the chassis Rigidbody was never told,
  so a grappled hook would let the plane fly off forever. The joint
  bridges the simulation to PhysX's force solver. When grappled, the
  two-joint chain (chassis ↔ tipRb ↔ target) propagates force to the
  grappled body via the tipRb intermediary.

### Tip Rigidbody state machine

Tip is **kinematic** in free flight: simulator drives via MovePosition
+ MoveRotation each step, no PhysX integration to fight, no speed-
correlated jitter. `HookBlock.Attach` flips it to non-kinematic before
adding the grapple joint (so the joint can pull the chassis) and
`Release` flips it back. A latent bug — PhysX-broken joints leaving
the tip stuck non-kinematic forever — was patched in
`HookBlock.FixedUpdate`.

### Stress rope tower

New `Blueprint_StressRopeTower.asset` (5 rotor levels × 4 ropes ringed
around each = 20 chains × 8 segments = 160 particles) for Verlet
profiling per PHYSICS_PLAN § 2 trigger #1. Picked from the Garage
chassis dropdown.

## Polish features

1. **AimReticle flips red on enemy target**. Per-frame screen-centre
   raycast against `_targetMask`; tints crosshair `_enemyColor` (rust-
   red default) when a non-self `IDamageable` is in the line.
2. **FloatingDamageOverlay** subscribes to a new
   `BlockBehaviour.DamageDealt` static event, draws short-lived
   floating numbers above damaged blocks. Disabled by default per
   user feedback (file kept; flip on by adding the component manually
   to the main camera or restoring the line in `ArenaController`).
3. **DeathOverlay** — full-screen "DESTROYED — press K to respawn"
   when the local chassis loses all blocks.

## Iteration trail (rope feel)

Initial Verlet implementation worked structurally but feel was wrong
in several specific ways. Each was diagnosed + fixed in sequence:

1. **Rope had no tension** — pinned both ends to physics-state, chain
   couldn't pull tip. Fix: pin only hub in free flight; tip particle
   integrates with gravity, simulator drives tipRb to follow.
2. **Hook orientation locked** — tipRb rotation never updated. Fix:
   simulator MoveRotation along chain direction.
3. **Hook jitter (1st attempt)** — FreezeRotation + direct
   `Rigidbody.rotation` writes confused Unity's Interpolate cache.
   Fix: removed FreezeRotation, switched to MoveRotation.
4. **Hook jitter (2nd attempt, speed-correlated)** — non-kinematic
   tipRb's PhysX integration fought the simulator's drive at high
   speeds. Fix: tipRb kinematic in free flight.
5. **Rope Z-folded into discrete segments**. Fix: bending stiffness
   skip-one constraints.
6. **Near-hub jitter at high damping**. Tried distance-scaled damping
   (didn't help), live-anchor override (helped some), temporal
   smoothing (final fix). Underlying cause: PBD residual error +
   no inertia at high damping.
7. **Grapple didn't pull chassis**. Fix: chassis-tip
   `ConfigurableJoint` for physics-side force transmission.

## Known follow-ups

- **XPBD upgrade** for the rope solver. Current basic PBD has residual
  oscillation at stiff constraints (high damping); the smoothing
  layer covers it visually but the underlying numerical issue
  remains. XPBD adds explicit compliance terms and would handle stiff
  constraints without residual error. Modest port from current PBD.
- **Per-block migration still pending** for `Plane.* / Ground.* /
  Chassis.*` (per-blueprint), `Thruster.* / Rudder.*` (per-block),
  `Rotor.RPM` (per-block). Documented in the audit comment in
  `Tweakables.cs`.
- **Server-authoritative damage** — `ProjectileGun` uses `Time.time`
  for fire-rate gating + `Random.insideUnitCircle` for spread; both
  are local-clock / local-RNG and need to move to a server-canonical
  source when MP infrastructure lands.
- **JSON round-trip test** for `BlueprintSerializer`. Schema v2 was
  bumped this PR (Aero/Rope dims + previously-dropped `Up` and
  `RotorsGenerateLift`); a round-trip test would catch any future
  schema regressions.
- **`RopeTip.cs` is orphaned** (no longer attached as a component
  anywhere; tip-end body now hosts the collider directly). Safe to
  delete in a follow-up.
- **`FloatingDamageOverlay`** wiring: hidden by default per user
  request. Re-enable by adding the component manually or restoring
  the auto-add line in `ArenaController.BindLocalChassisHud`.

## Future-session starter

Same as session 25's recipe:

1. Read this file (latest).
2. `docs/changes/architecture.md` for the current modules table.
3. `docs/PHYSICS_PLAN.md` § 1.5 + § 2 (Verlet rope spec — now
   implemented; remaining is XPBD upgrade).
4. `Tweakables.cs`'s "MP DEBT AUDIT" comment for what's left.
