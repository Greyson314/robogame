# 112 — Weapon-fork refactor: phases C, D, A, B (ADR-0003)

> Closes item 4 of the [110 audit queue](110-audit-remediation-queue.md). Phase E
> (predicates) shipped in `3d547c2d`; this session lands the remaining four
> phases of [ADR-0003](../decisions/0003-weapon-fork-refactor.md). Pure-code
> phases (C/D) are test-rig verified; the SO-touching phases (A/B) are verified
> by asset-YAML reasoning + the EditMode suite (the Unity MCP bridge was down).

## What shipped

### Phase C — `TurretYoke` + spherical-aim fix (audit #8)

New `Combat/TurretYoke.cs` (readonly struct). The four turret bodies
(`WeaponBlock`, `CannonBlock`, `GrappleMagnetBlock`, `MortarBlock`) each yawed
with `LookRotation(flatXZ, Vector3.up)` after zeroing the aim's world-Y — so on
the planet arena the base swung toward a *world*-horizontal projection of the
target, not a *surface*-level one. `TurretYoke`:

- `UpAt(pos)` = `-GravityField.SampleAt(pos).normalized` (falls back to
  `Vector3.up` at ~0 g; flat arenas already return `Physics.gravity`, so the fix
  is a no-op off the planet).
- `TryYawTargetLocal` projects the aim onto the plane ⟂ local-up and yaws about
  it. `PitchDegrees` is frame-relative (inherits the corrected yaw). `Track`
  (full look-at: SMG/cannon/grapple) and `Yaw` (mortar drives its own lob pitch).

The pure static helpers make the regression unit-testable: `TurretYokeTests`
pins the surface-aligned yaw, the flat-arena equivalence, the degenerate
overhead case, and the pitch convention.

### Phase D — `WeaponFireGate` (cooldown + ammo + dry-click)

New `Combat/WeaponFireGate.cs` (mutable struct, held as a field — no alloc).
Replaces the copy-pasted `Update` gate in `ProjectileGun`, `CannonBlock`,
`MortarBlock`, `BombBayBlock`. The per-weapon fire-interval floor and dry-click
throttle stay at the call site (they differ per weapon); the gate owns the
cooldown + ammo consume + throttled empty cue. `WeaponFireGateTests` pins
"a held trigger fires at most once per interval, not bypassable by re-pressing."

### Phase A — `IWeaponStats` + `WeaponStatsDefinition` base

New `Combat/IWeaponStats.cs` + `Combat/WeaponStatsDefinition.cs`. The base holds
the five universally-shared serialized fields (`_damage`, `_knockbackImpulse`,
`_clipSize`, `_reloadDuration`, `_autoReloadDelay`) + an abstract `FireInterval`.
The four `*Definition`s inherit it, drop those declarations, and map their own
interval field (`WeaponDefinition` → `1f/_fireRate`, cannon/mortar →
`_fireInterval`, bomb → `_dropInterval`). `_muzzleSpeed`/`_recoilImpulse` stay on
the three that have them (bomb has neither).

**Serialization round-trip.** Unity serializes fields by name flat across the
hierarchy, so moving fields up keeps the YAML keys identical and authored assets
round-trip. Captured the four assets' values first (Weapon_Smg / Cannon_Default
/ Mortar_Default / Bomb_Default) — confirmed the moved keys still match the base
field names exactly. New-asset defaults now come from the base (25 dmg / 3 kb /
10 clip), not per-kind — a minor authoring change, not a runtime one.

### Phase B — `IClientSilenceable` marker + `IWeaponStats` ammo registry

- New `Combat/IClientSilenceable.cs` (empty marker), implemented by all five
  firers. `NetworkRobotCombat`'s silence loop is now one
  `GetComponentsInChildren<IClientSilenceable>` walk — which finally silences
  `MortarBlock` + `GrappleMagnetBlock` (audit #4: the old gun/cannon/bomb loop
  missed them, so a client could still drive those two locally).
- `WeaponAmmoState.IsWeaponBlock` and `ResolveAmmoConfig` now key off
  `BlockDefinition.ComponentData is IWeaponStats` — one cast replaces the
  four-way try-cast and the hand-synced id list.

**Deviation from the ADR's `Category == Weapon`.** The ADR proposed gating the
ammo registry on `BlockCategory.Weapon`. That would be *wrong*: grapple + the
tip blocks are all category `Weapon` (`block.weapon.*`) but carry no ammo, so a
category gate would mint phantom pools for them. `ComponentData is IWeaponStats`
is exact parity with the old `{Weapon, BombBay, Cannon, Mortar}` list — verified
against the assets: those four block defs reference a `WeaponStatsDefinition` SO
(GUIDs matched), and `BlockDef_GrappleMagnet._componentData` is null → correctly
excluded. The category enum is left untouched.

NETCODE Phase 6 markers (#9 server-authority fire gate, #31 per-weapon cooldown)
folded into the silence loop as comments — no behaviour change.

## Verification

- EditMode suite via `run-tests.sh` (test-rig worktree; Unity batch): **288/289
  passed, 0 failed, 1 inconclusive** (the inconclusive is a pre-existing skipped
  test). The full suite running = clean compile across Combat/Network/Tests with
  all four phases applied; `TurretYokeTests` (8) + `WeaponFireGateTests` (5) green.
- Phase A/B asset round-trip + registry parity verified by reading the `.asset`
  + `.meta` YAML directly (captured values, matched component-data GUIDs) and by
  the test-rig's full asset import. By-name serialization is reliable by
  construction; a live main-editor reimport is the only thing the batch run
  doesn't cover.

## Known gaps (deliberate)

- **Mortar lob elevation** still measures the aim's pitch against world-Y
  (`ComputeLaunchElevationDeg`), so on a planet the launch *angle* is slightly
  off even though the base yaw is now surface-correct. Out of Phase C's scope
  (the `LookRotation` yaw bug); the lob offset is a heuristic and planet-mortar
  isn't a shipped concern. Note for a later spherical pass.
- `#25` module-fork is out of scope (separate debt, per the ADR).
