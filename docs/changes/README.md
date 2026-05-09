# Robogame — dev log

This directory is the catch-up brief for any future contributor (human
or AI) landing on the project mid-stream.

- Read [architecture.md](architecture.md) first — that's what's true
  *right now*.
- Then skim sessions in **reverse** chronological order (highest number
  first) for the *why* behind recent shape changes.

Style: dev log, not changelog. Each session entry covers user intent,
what shipped, what we learned. File links use repo-relative paths.

## Recent batch — what landed since session 16

**Helicopter chassis is the headline.** Sessions 17–21 fix the
"helicopter frame spins with the rotor" bug end-to-end:

- Session 17: rotor / aerofoil decoupling — adopt-don't-synthesise.
- Session 18: garage gate (foils stay under the chassis grid root in
  the garage); `Aero.WingSpan/Chord/Thickness` tweakables for live
  foil resizing.
- Session 19: blueprint authoring overhaul + the rotor visual now
  reads as a 2-cell-tall stem + mechanism. Bigger helicopter (38
  cells, two side guns, foils as the absolute topmost cells). Hook
  + Mace tip blocks for ropes (PHYSICS_PLAN §3 contact damage).
  Barbell test dummy spawned in the default arena.
- Session 20: plane simplified to a single 8-segment rope with a
  hook for hot-testing the rope-tip damage path.
- Session 21: two coupled fixes for the helicopter spin-out — lift
  forced coplanar with the spin axis, and foil-vs-chassis colliders
  ignore-paired so the foil cubes don't impulse-yaw the chassis as
  they sweep through the mechanism cube's volume.

**New authoring infrastructure** (session 19, phase 1):

- [`BlueprintBuilder`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
  fluent API: `Block`, `Row`, `Box`, `MirrorX/Z`, `RotorWithFoils`,
  `RotorBare`, `RopeWithHook`, `RopeWithMace`. Replaces the old
  `entries.Add(new ChassisBlueprint.Entry(...))` boilerplate.
- [`BlueprintValidator`](../../Assets/_Project/Scripts/Block/BlueprintValidator.cs)
  catches no-CPU / duplicate-cell / orphan / unknown-id errors at
  scaffold time. Wired into `GameplayScaffolder.CreateOrUpdateBlueprint`.
- [`BlueprintAsciiDump`](../../Assets/_Project/Scripts/Block/BlueprintAsciiDump.cs)
  prints chassis layouts one Y-layer at a time. Run
  `PresetBlueprintTests.DumpAllPresets_WritesAsciiSnapshot` (EditMode)
  to regenerate [docs/blueprint-snapshots/presets.md](../blueprint-snapshots/presets.md).

**New self-tests** (session 19 + 21):

- `Tests/EditMode/Blueprints/` — unit tests for the builder,
  validator, and every preset (including the new helicopter +
  barbell). Auto-writes the snapshot file.
- `Tests/PlayMode/Movement/RotorBlockTests.cs` —
  `RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad` exercises
  both the lift-direction and collider-sweep yaw paths and asserts
  chassis yaw stays under 1 rad/s after 30 fixed steps at 240 RPM.

**Carry-forward / open threads** are listed in the "Known unknowns
going forward" section at the bottom of this file.

## Sessions (newest first)

| # | Title |
|---|---|
| 44 | [Foil pitch Phase 4 (live readouts) + 3 fixes: rotor/foil deletion, leaf-bridge over-rejection, foil panel layout flip](44-foil-pitch-phase4-and-fixes.md) |
| 43 | [Foil pitch Phase 3: VariantConfigPanel rebuild — preset cards + primary slider + Advanced expander, foil + rotor sections](43-foil-pitch-phase3-ui.md) |
| 42 | [Foil pitch Phase 0+1+2+5: per-instance pitch / incidence on every aerofoil + rotor adopt-pass + visual mesh tilt](42-foil-pitch-audit.md) |
| 41 | [Wheels: Robocraft-style side-mount stem + tyre rig; default Tank/Buggy/starter rebuilt](41-wheels-side-mount.md) |
| 40 | [Garage features: mirror toggle (M) + leaf-block connectivity (no building on wings)](40-garage-mirror-and-connectivity.md) |
| 39 | [Scalable parts Phase 1.5: lift scales with planform area (2× wing → 2× lift; default chassis preserved)](39-scalable-parts-phase1.5-lift-scaling.md) |
| 38 | [Scalable parts Phase 1: swept-volume occupancy check (block placements that interpenetrate are now rejected)](38-scalable-parts-phase1-occupancy.md) |
| 37 | [Scalable parts: Phase 0 audit (no code; lift-vs-dims gap surfaced)](37-scalable-parts-audit.md) |
| 36 | [Follow-ups: animated flip, repair-pad beacon, aero regen visual, rope re-adoption + max-stretch break, hook orphan-joint cleanup](36-followups.md) |
| 35 | [Scrap pickups: drop on chassis death, collect by overlap, magnetic pull, foundational ScrapHeld counter + HUD](35-scrap-pickups.md) |
| 34 | [Snap-rotate flip (H key) + repair pad (gradual rebuild from blueprint over 10 s)](34-flip-and-repair-pad.md) |
| 33 | [Rope is inert in build mode → Hook/Mace placeable + removable from chassis grid; ghost previews for Hook/Mace/Rope/Rotor](33-rope-build-mode-tip-blocks.md) |
| 32 | [Projectile-system unification — single custom-stepped integrator (ProjectileWorld) replaces three Rigidbody-based MBs](32-projectile-unification.md) |
| 31 | [Cannon weapon, bomb-jitter / hook-punt / camera-aim bug fixes, damage-number summation, kill announcer, pause-on-settings, aim-line preview](31-cannon-bugfixes-features.md) |
| 30 | [Audio v1: Universal Sound FX wired into 21 cues, pooled voices, rotor whine loop, mixer-ready](30-audio-v1.md) |
| 29 | [VFX feel pass + audio system bones (muzzle flashes, hit sparks, debris dust, thruster plume; Audio settings + AudioRouter)](29-vfx-and-audio-bones.md) |
| 28 | [Pillar 1: singleplayer game loop (MatchController, AI bots, objective HUD, end overlay)](28-pillar-1-game-loop.md) |
| 27 | [Performance pass: docs, diagnostics, and conservative fixes](27-performance-pass.md) |
| 26 | [MP-readiness pass: combat per-block migration, inertia tensor, Verlet ropes, polish](26-mp-readiness-pass.md) |
| 25 | [Rope re-anchor on enable + cursor lock in build mode](25-rope-anchor-cursor-lock.md) |
| 24 | [Build cam free-look, hook adoption, aim self-skip, arch dummy](24-build-cam-tip-binder-aim-arch.md) |
| 23 | [Feel pass: rope-tip lifecycle, J-hook, helicopter symmetry, larger garage, free build cam, scroll zoom](23-feel-pass.md) |
| 22 | [Grapple hook: scaled-up tips, dumbbell target, joint-based latch (in progress)](22-grapple-hook-and-tip-resize.md) |
| 21 | [Helicopter frame stability: pure-axial rotor lift](21-helicopter-spin-axis-lift.md) |
| 20 | [Plane reconfigured as rope-tip test sandbox](20-plane-rope-tip-sandbox.md) |
| 19 | [Blueprint authoring cleanup, rotor stem, bigger heli, hook/mace, barbell (autonomous, in-progress)](19-blueprint-authoring-and-helicopter-overhaul.md) |
| 18 | [Helicopter foundations: garage gate + Aero foil tweakables (in-progress)](18-helicopter-foundations.md) |
| 17 | [Rotor / aerofoil decoupling, follow-ups (lift works, three new bugs)](17-rotor-foil-decoupling-followups.md) |
| 16 | [Rotor / aerofoil decoupling (WIP — three regressions outstanding)](16-rotor-foil-decoupling.md) |
| 15 | [Rotor follow-ups: tip collider, plane rotor, stress tower, physics plan](15-rotor-followups.md) |
| 14 | [Rotor block + spinning-rope ring + perf-discipline note](14-rotor-block.md) |
| 13 | [Rope block + GUI tweaks polish + momentum impact damage](13-rope-and-momentum-damage.md) |
| 12 | [Bomber preset + Bomb Bay block + health check / docs split](12-bomber-bombbay-and-audit.md) |
| 11 | [Polish: foam wake on chassis + connectivity flood-fill at placement](11-foam-wake-connectivity.md) |
| 10 | [Water visuals: Bitgem shader + Gerstner mesh + DevHud waves slider](10-water-bitgem.md) |
| 09 | [Build mode: in-garage block editor (Pass B Phase 3a)](09-build-mode-editor.md) |
| 08 | [Save/load foundations + "+ New Robot" button (Pass B kickoff)](08-save-load-blueprints.md) |
| 07 | [Phase 1 art pass: cel-shading, post-FX, ambient, skybox](07-art-direction-phase1.md) |
| 06 | [Settings panel + Tweakables registry](06-settings-tweakables.md) |
| 05 | [Plane "feel" pass](05-plane-feel.md) |
| 04 | [HitscanGun MissingReferenceException on Stop](04-hitscan-gun-fix.md) |
| 03 | [Chassis dropdown (Tank / Plane / Buggy)](03-chassis-dropdown.md) |
| 02 | [Launch button, three rounds of debugging](02-launch-button-debug.md) |
| 01 | [Pass A + garage/arena visual identity](01-pass-a-visual-identity.md) |
| 00 | [Background — initial refactor pass (pre-log)](00-background-pre-log.md) |

## Architecture

- [architecture.md](architecture.md) — current modules, runtime flow,
  patterns and gotchas.

## Known unknowns going forward

These are real items the next session should be aware of. None block
shipping the current branch; flagged so they don't decay into
"why is this broken".

- **Foil pitch arc (sessions 42–44) — implementation still needs
  work.** The data model + adopt-pass + UI + live readouts all
  landed, but several items are explicitly deferred. Source of truth
  for what's left is [`docs/FOIL_ROTATION_PLAN.md`](../FOIL_ROTATION_PLAN.md)
  § 10 *Carry-forward*. Headline items: live mid-edit collective
  propagation to existing blades (slider feels inert until the rotor
  is re-placed), select-and-retune UX for already-placed blocks (a
  persistent Phase 1.b carry from session 38), pitch ghost-preview
  tilt, and a tuning playtest pass against the shipped helicopter +
  plane.

- **Helicopter session-21 fixes need in-game verification.** The new
  PlayMode test passes analytically; the user reported the chassis
  still spinning before the second (collider-sweep) fix landed but
  hasn't yet flown the chassis with both fixes in place. If the
  chassis still spins after both fixes, the next suspect is PhysX
  per-step kinematic-MoveRotation interactions, which would need
  per-FixedUpdate diagnostic logging on the chassis angular velocity.

- **Tip-block rope-detach lifecycle.** When an adopted Hook / Mace's
  HP drops to zero mid-flight, `Robot.DetachAsDebris` reparents the
  tip GameObject to scene root and adds a Rigidbody to it. The rope
  segment's mass was bumped by the tip's mass at adoption; that mass
  isn't reverted on detach, so the segment becomes overweight relative
  to the actual chain. Edge case — flagged in
  [session 19](19-blueprint-authoring-and-helicopter-overhaul.md).

- **Tweakables defaults vs persisted JSON.** Bumping a registered
  default in code does NOT take effect for users with a saved value
  in `Application.persistentDataPath/tweakables.json`. The session-20
  rope segment count default 5→8 is the most recent example.
  Documented in `architecture.md`'s gotchas table.

- **B1 garage render of the helicopter.** Session 18 phase A added
  the kinematic-chassis early-return in `RotorBlock.BuildLiftRig`,
  which keeps foils under the chassis grid root in the garage.
  Should now display correctly, but worth a visual check during the
  same session-21 verification pass.

- **Per-block blueprint config (PHYSICS_PLAN §6).** Still future work.
  Foil-dimension `Aero.*` Tweakables and `Combat.Rope*` damage
  constants are MP debt — they need to move to per-block / per-chassis
  config before netcode lands. Session 19 docs spell this out
  explicitly so the migration target is clear.

- **Tail rotor visual sweep.** The default helicopter still has a
  bare cosmetic tail rotor at `(1, 0, -4)` with spin axis +X. With
  the session-21 ignore-pair fix, foil-vs-chassis collisions are
  suppressed only when `AdoptAdjacentAerofoils` runs — for a bare
  rotor (zero foils adopted), nothing pairs. That's fine because
  bare rotors have no orbiting foil colliders, but worth noting if
  someone adds foils to the tail rotor later: re-run
  `IgnoreFoilChassisContacts` to keep the contract.
