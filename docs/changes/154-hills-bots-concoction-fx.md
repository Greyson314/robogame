# 154 — Hill-capable wheels, fleshed-out default bots, per-concoction FX

**Date.** 2026-07-27
**Intent.** User: (1) wheels struggle a lot on hills and get stuck;
(2) replace the default bots with more fleshed-out versions; (3)
different FX for different concoctions.

## 1. Wheels on hills

Two root causes found by reading `GroundDriveSubsystem` + `WheelBlock`:

- **Drive force was horizontal** (`fwd.y = 0`): on a slope of angle θ,
  sin θ of the throttle plowed into the hill face and only cos θ
  climbed. Nose-into-slope plowing is also how bots wedged themselves.
- **Raycast suspension has no contact friction**: an idle bot on a
  hill slid until a chassis cube collider snagged terrain (the wheel
  cube carries friction 1.6, Maximum combine) — the "stuck" state.

Fixes, all in the existing chassis-level force architecture:

- `WheelBlock` exposes `GroundNormal` from its suspension ray.
- `GroundDriveSubsystem.ProbeGround` averages grounded wheels'
  normals; **drive, lateral grip and the speed cap now act in the
  slope plane** (identical to the old math on flat ground).
- **Parking brake** (new, SO-tunable like jump/upright): while
  grounded with no throttle, bleeds in-plane creep
  (`IdleBrake` = 0.2/step, on `GroundDriveTuning`). No gravity-cancel
  term: the suspension pushes world-VERTICAL, so at rest it already
  cancels all of gravity including the along-slope component — a
  stopped bot holds the hill by construction. (A first-draft
  `SlopeHold` gravity-cancel was built and removed same-session: it
  double-counted against that equilibrium and made idle bots creep
  uphill. Caught by the new slope test rig.)

Side effect (intended): bots now roll to a stop when the throttle is
released instead of coasting downhill or into obstacles.

## 2. Fleshed-out default bots

Design grounded by the design-pilot subagent against the pillars +
Robocraft/TerraTech/Crossout references. Existing bot AI
(`GroundBotInputSource` / `AirBotInputSource`) drives any held-fire
weapon via `WeaponFireGate`, so all upgrades are pure blueprint work —
zero AI changes. Internal names kept ("Tank", "Plane"…) because
name-based lookups (`ClearPlayerChassis(keepName: "Plane")`, the
tank-dummy resolver) depend on them. Plans in `GameplayScaffolder`,
assets regenerated via `CreateDefaultBlueprints()`:

- **Tank** → brawler: flank armor walls above the side strips + aft
  turret cannon layered over the bow SMG.
- **Plane** → interceptor: nose SMG → cannon (one hard shot per pass
  fits the 0.6 facing gate better than spray).
- **Helicopter** → wrecking gunship: rope + mace slung under the
  cabin, SMGs kept — the pillars' own "chopper with a wrecking ball"
  line, now on the roster.
- **Boat** → gunboat: bow cannon + amidships mortar (arc ignores the
  facing gate; area denial over open water).
- **DrillBot** → borer: roof SMG so it fights back; drill stays
  terrain utility. Terrain-breaching pursuit AI flagged as follow-up.

Not touched: Grappler, Bomber, PropPlane, SpringBot, HoverTank.

## 3. Per-concoction FX

Recipes previously differed only by tint. Now each lever spiked above
neutral adds its own read at the impact (below-neutral levers add
nothing, mirroring the pigment rule):

- damage → dense ember burst (RamSpark), knockback → shock ring
  (BombShockwave), spread → debris scatter (DebrisDust), speed →
  streak back along the approach path (HitSpark), size → fatter base
  spark on the kinetic kinds (bombs already scale their ring via
  SplashRadius).
- `ProjectileSpec` carries five baked weights (`SetConcoctionFx`,
  called at all four fire sites); `ProjectileWorld.SpawnConcoctionExtras`
  layers pooled, pigment-tinted VfxSpawner kinds. Thresholded at 0.15
  weight; SMG pellets pay only every third impact so a 12 Hz stream
  doesn't turn to soup.

## Verification

- Compile clean after each stage (bridge force-refresh + console).
- Blueprints: exactly the five intended assets changed; grep confirms
  cannon ×3, mace, mortar, drill-bot SMG in the regenerated assets;
  zero placement warnings from the scaffolder run.
- Headless: EditMode 453/454, PlayMode 122/123 (two new
  `GroundDriveSlopeTests`), 0 failed — usual 1 inconclusive + 1
  ignore carry-overs. The slope suite earned its keep immediately:
  it caught the SlopeHold uphill-creep bug and the superball test
  rig before either shipped.
- **Not yet play-felt**: hill feel, bot fight difficulty, and FX looks
  are first-pass numbers awaiting the user's playtest. Knobs:
  `GroundDriveTuning` slope fields; extras scales/threshold in
  `ProjectileWorld`; loadouts in the scaffolder plans.

## Notes / follow-ups

- Borer terrain-breaching pursuit (route through dig zones) — flagged,
  not built.
- Bot difficulty: three bots gained cannons/mortars; if fights get too
  hot, the cheap lever is reverting Plane's nose cannon to SMG.
- `GroundDriveSubsystem` still steers/self-rights around world up —
  fine on flat arenas; planet-arena wheel steering is pre-existing
  tech debt, unchanged here.
