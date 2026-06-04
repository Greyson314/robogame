# 0003 — Unify the weapon fork (shared stats, registry, turret/fire helpers)

- **Status.** Accepted
- **Date.** 2026-06-03

## Context

The combat code grew one weapon at a time (SMG, cannon, mortar, bomb,
grapple-magnet), and each addition copy-pasted the previous one's shape.
The session-109 audit (#3/#4/#8/#15) found five parallel-maintenance
patterns that now drift independently:

1. **Duplicated stat fields.** Each `*Definition` ScriptableObject
   re-declares clip size, reload, knockback, recoil, muzzle speed, etc.
2. **Hand-synced weapon-kind lists.** Seven-plus sites each enumerate the
   weapon types by hand — `WeaponAmmoState.IsWeaponBlock`,
   `WeaponAmmoState.ResolveAmmoConfig` (a four-way try-cast),
   `NetworkRobotCombat`'s client-silence loop (which *misses*
   `GrappleMagnetBlock` — audit #4), plus binder/connectivity/wizard sites.
   Adding a weapon means remembering all of them.
3. **Duplicated turret aim** across four block classes, each with a
   `Quaternion.LookRotation(flat, Vector3.up)` that assumes world-up — so
   aim swings wrongly on the spherical/planet arena (audit #8).
4. **Duplicated fire gate** (cooldown + ammo + empty-click) in each
   held-fire weapon.
5. **Forked predicates** — hostility (`ProjectileWorld.IsFriendlyFire`
   vs `ModuleEffects.EmpBurst`'s `== owner`, which lets EMP hit teammates,
   audit #15) and muzzle gravity (cannon/mortar use chassis-relative
   `-transform.parent.up`; bomb already uses the correct
   `GravityField.SampleAt`).

This spans `Robogame.Combat` and `Robogame.Network`, touches the
server-authority path, and is the largest debt in the audit queue — hence
an ADR before code (per ADR-0001 + the workflow). Plan produced by the
planner subagent, session 110 follow-up.

## Decision

Replace the five forks with unified contracts, delivered in five phases
that each compile and commit independently:

- **A — shared stats.** `IWeaponStats` interface + `abstract
  WeaponStatsDefinition : ScriptableObject` holding the common fields; the
  four concrete `*Definition`s inherit it and map their existing
  `[SerializeField]` names onto the interface (field names unchanged, so
  authored SO assets round-trip).
- **B — registry + silence marker.** A `BlockCategory.Weapon` +
  `IClientSilenceable` marker interface replaces the hand-synced lists.
  `NetworkRobotCombat` silences via `GetComponentsInChildren<IClientSilenceable>`
  (one walk, and it finally includes the grapple). `WeaponAmmoState`
  resolves ammo via a single `IWeaponStats` cast.
- **C — `TurretYoke`.** A pure-math struct (`Track(aimPoint, localUp, dt)`)
  extracted from the four aim bodies. The spherical fix: `localUp =
  -GravityField.SampleAt(pos).normalized` (falling back to `Vector3.up`
  when gravity ≈ 0), and the aim is projected onto the plane perpendicular
  to `localUp` instead of zeroing world-Y.
- **D — `WeaponFireGate`.** A struct encapsulating "can I fire this tick?"
  (cooldown, ammo, empty-click), used by the held-fire weapons (not the
  grapple, which is a one-shot state machine).
- **E — predicates.** `Teams.AreHostile(a,b)` (one definition; EMP and
  projectiles both route through it, fixing the teammate-EMP bug) and
  `ProjectileGravity.ForMuzzle(muzzle)` (unifies on the bomb's correct
  `GravityField.SampleAt`).

Phase-6 netcode debt (#9 server-authority gate, #31 per-weapon cooldown)
gets one-line `// NETCODE Phase 6` marker comments folded into this pass —
no behaviour change, just signposts. Module-fork debt (#25) is **out of
scope** (separate from weapon-fork; queue keeps them apart).

## Alternatives considered

**Enum-keyed weapon registry** (a `WeaponKind` enum + dictionary) instead
of the `Category == Weapon` + marker-interface approach. Rejected: it
recreates a central list that every new weapon must edit — the exact
coupling we're removing. Marker interfaces let a new weapon opt in locally.

**Leave the turret aim per-class and just patch the spherical bug in four
places.** Rejected: the bug exists *because* the logic is duplicated;
patching four copies guarantees the fifth weapon reintroduces it.

**Put `Teams.AreHostile` in `Robogame.Core`.** Rejected: `Core` doesn't
reference `Robogame.Robots` (where `TeamId`/`Robot` live) and adding it
would be a circular asmdef edge. It goes in `Robogame.Robots` instead,
which `Combat` already references.

## Consequences

- **Adding a weapon stops being a checklist.** New stats inherit the base;
  silence/ammo/registry pick it up via the marker + category; aim and fire
  reuse the shared helpers.
- **Two real bugs fixed in passing:** spherical turret aim (#8) and EMP
  hitting teammates (#15). The grapple is now silenced on clients (#4).
- **Invariants hold.** #1 — stats stay on server-authoritative definition
  SOs, not Tweakables. #3 — no gameplay state moves to a client path; the
  marker silence is *stricter* than today. #4/#5 — zero new physics
  objects (`TurretYoke`/`WeaponFireGate` are structs over existing
  transforms). #6 — structs + static predicates, no per-frame alloc; the
  silence walk is once at chassis build.
- **Serialization risk (the load-bearing one).** Moving fields to
  `WeaponStatsDefinition` only round-trips if the concrete classes *remove*
  their local declarations (no duplicate field names) and keep the same
  names. **Phase A requires a manual Unity verify**: open each weapon
  definition + blueprint asset after the change and confirm values are
  intact. Capture current values first (the assets are already dirty in the
  working tree). Note `WeaponDefinition` uses `_fireRate` (shots/sec) while
  cannon/mortar use `_fireInterval` — the base is canonical `_fireInterval`,
  `WeaponDefinition.FireInterval => 1f/_fireRate`; do not rename `_fireRate`.
- **Tests** (test-drafter, alongside code): `Teams.AreHostile` truth table;
  `ProjectileGravity.ForMuzzle` flat-vs-planet; `TurretYoke.Track` spherical
  aim (the regression that motivated the extraction); `WeaponFireGate`
  cooldown/ammo/empty-click.
- **No new Tweakables, no scaffolder rewrite, no blueprint-schema change.**

## Notes

- **Phase B shipped with a refinement to the registry key.** The Decision
  above proposed gating the ammo registry on `BlockCategory.Weapon`. In
  implementation that proved wrong: the grapple and tip blocks are all
  category `Weapon` (`block.weapon.*`) yet carry no ammo, so a category gate
  would mint phantom ammo pools for them. Shipped instead with
  `BlockDefinition.ComponentData is IWeaponStats` — exact parity with the old
  `{Weapon, BombBay, Cannon, Mortar}` id-list and locally opt-in (a new ammo
  weapon authors a `WeaponStatsDefinition`). `BlockCategory` is untouched. The
  `IClientSilenceable` marker (the silence half of phase B) shipped as planned.
  See session log [112](../changes/112-weapon-fork-phases-cdab.md).
- Full file:line map and per-phase step list in the planner output folded
  into session log [111](../changes/111-prediction-scene-csp.md) →
  [112](../changes/112-weapon-fork-phases-cdab.md).
- Riskiest phase: **A** (SO serialization) and **B** (touches
  `NetworkRobotCombat` server-authority silence). Land + verify each before
  the next.
- Builds on the conventions in ADR-0001; uses the
  [Continual Traces](../../CLAUDE.md) markers for the new contracts.
