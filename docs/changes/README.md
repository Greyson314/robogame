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
