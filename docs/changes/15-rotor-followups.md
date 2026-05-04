# Session 15 — Rotor follow-ups: tip collider, plane rotor, stress tower, physics plan

> Status: shipped. Compiles clean against Unity 6 / Robogame Pass A.

## Intent

Reviewer feedback on the session-14 rotor block flagged three real
follow-up items:

1. Ropes can't simply phase through the world forever — even if rope
   tips don't deal damage, they need to interact physically with the
   environment.
2. The `Tweakables.RotorRopeCount` knob is currently a per-machine
   slider that affects what shows up in the world. That's fine while
   the rotor is purely cosmetic; it becomes a desync vector the
   moment ropes affect gameplay.
3. The "kinematic-hub trick" claim from session 14 (PhysX synthesises
   tangential velocity for free) needs an empirical stress test, not
   just confidence.

Plus two product-side requests:

4. Add a rotor with ropes to the back of the default plane so the
   feature is visible on every fresh install.
5. Build a stress-test target — a tower of high-RPM rotors — and
   make it an optional spawn from the tweaks menu.

And finally: a documentation pass codifying when the current
PhysX-joint rope tech needs to migrate to a custom Verlet solver,
so the next "is it time yet?" decision has an explicit checklist.

## What shipped

### New runtime files

- [`Assets/_Project/Scripts/Movement/RopeTip.cs`](../../Assets/_Project/Scripts/Movement/RopeTip.cs)
  — marker component on the LAST segment of every rope chain.
  Carries a non-trigger sphere collider sized at ~ 1.6 × the segment
  radius. Rope tips now bounce off arena geometry, the dummy, and
  other chassis. `IgnoreChassisCollisions` walks the owning
  chassis's collider tree once at build time and pairs each
  collider with the tip via `Physics.IgnoreCollision` (PhysX
  caches the ignore pair internally, so the cost is one-shot).
  `DealsDamage` is the future damage hook — currently a stub
  hard-wired to `false`. See PHYSICS_PLAN.md § 3.

### Wiring

- **Rotor + rope per-segment behaviour.** Both
  [`RopeBlock`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
  and [`RotorBlock`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
  now attach a `RopeTip` to the last segment of every chain they
  build. Mid-chain segments stay collider-free.
- **`RotorBlock.RpmOverride`.** New public C# property (default
  `-1f` = "use the global tweakable"). The stress-tower spawner
  sets this to `Tweakables.StressRotorTowerRpm` so the tower runs
  at high RPM independently of the player's chassis rotors.
  When per-block blueprint config lands, this folds back into a
  serialized field.
- **Default plane gets a tail rotor.** A `Rotor` block at
  `(0, 1, -2)` — top of the rear fuselage, one cell forward of the
  vertical fin so the spinning ring of ropes doesn't intersect the
  fin visual. Press Play, look up, see the use case.
- **`Stress` tweakables group.** Two new keys —
  `Stress.RotorTower` (0/1 toggle, treated as bool ≥ 0.5) and
  `Stress.RotorTowerRpm` (default 300, range 0–600). Both live
  in their own "Stress" tab in the settings panel and dev HUD,
  sliders auto-build off `Tweakables.All` like every other knob.
- **`Blueprint_StressRotorTower.asset`.** New default blueprint
  authored by `GameplayScaffolder` — 1×1 column, 10 cells tall,
  with rotors at every odd y (5 rotors total). Each rotor at
  default rope settings adds 16 dynamic Rigidbodies, so the tower
  intentionally exceeds the BEST_PRACTICES § 16 alarm of 64 active
  rigidbodies under a single chassis. That's the *point*: it's the
  loadout where the budget actually means something.
- **`ArenaController` lifecycle.** New `_stressTowerBlueprint` /
  `_stressTowerPosition` fields, `ApplyStressTowerState` /
  `RespawnStressTower` / `DespawnStressTower` methods, and a
  `Tweakables.Changed` subscription so dragging the
  `Stress.RotorTower` slider in the settings panel pops the tower
  in / out of the arena live without a scene reload. RPM changes
  push onto the tower's rotors via `RpmOverride` the same way.
- **DevHud buttons.** "Spawn / Refresh Rotor Tower" and
  "Despawn Rotor Tower" land next to "Rebuild Combat Dummy".
  Required adding `Robogame.Gameplay` to the `Robogame.UI` asmdef
  (no cycle — `Gameplay` doesn't depend on `UI`).
- **`Tweakables.cs` doc.** Explicit "GAMEPLAY CONTRACT" comment
  on the rotor keys spelling out that `RotorRopeCount`,
  `RotorRopeRadius`, `RotorRopeSegments` are graphics-only — the
  contract reviewer / future-AI checklist for not letting them
  silently graduate into gameplay-affecting values.

### Documentation

- [`docs/PHYSICS_PLAN.md`](../PHYSICS_PLAN.md) — new file. Six
  sections:
  1. Non-negotiables (read first).
  2. Rope tech: PhysX joints today, Verlet later, **named
     migration triggers**.
  3. Damage model checklist for any future kinetic weapon (mass-
     velocity rule, threshold, cooldown, visual cue, authority).
  4. Stress-test discipline — when to use the rotor tower, what
     to look at in the Profiler, what counts as a regression.
  5. Tweakables vs blueprint data — the desync-prevention rule
     and a status table for every existing tweakable group.
  6. Open items — Verlet prototype, baseline captures,
     per-block blueprint config.
- This file + index update.

## Why these specific design choices

### Tip-only collision (not per-segment)

Two reasons documented in `RopeTip` and `PHYSICS_PLAN.md` § 2.
Short version: per-segment colliders quadruple the active-collider
budget, AND under sustained spin the joint solver and contact
solver fight every step (segment lodges against contact normal,
joint pulls back, angular limits violated, chain visibly snaps to
a mangled pose). Tip-only sidesteps the pathology entirely. The
known cost is "long ropes can clip through walls in the middle"
— acceptable v0.5 trade-off, fixed by the Verlet migration.

### `RpmOverride` instead of new blueprint config

The clean answer is "extend the blueprint `Entry` struct to carry
per-instance config." That touches `ChassisBlueprint`, the
serializer, every block's `Configure` path — a big enough refactor
that it deserves its own session, not a side-effect of the stress
tower. `RpmOverride` is the smallest hatch that lets the tower
spin at high RPM today without polluting the player's chassis
rotors. Marked as a known debt in `PHYSICS_PLAN.md` § 6 so we
don't forget to fold it in when blueprint config lands.

### Tweakable-driven spawn (not a button-only feature)

The user asked for "optional spawn-in, maybe in the tweaks menu."
A `Stress.RotorTower` tweakable hits the requirement and gives
the bonus that the dev-HUD button just sets the slider — single
source of truth, no separate state. Subscribing the
`ArenaController` to `Tweakables.Changed` means the slider drives
the tower live; no scene reload, no menu trip.

### Stress tower defaults intentionally exceed § 16

If the stress test stays under the alarm, it's not stressing
anything. The tower at 5 rotors × 16 dynamic-rb-per-rotor = 80
active rigidbodies, which is past the 64 alarm but well under
the 256 cliff. Add the player's chassis rotor and dummy and you're
firmly in alarm territory — exactly the regime where a Profiler
capture is informative. RPM defaults to 300 (high enough to
exercise the joint solver, low enough that the visual is still
readable).

## Cost (defaults)

Compared to session 14:

- Default plane: was 1 kinematic + 16 dynamic rbs (bottom rope
  block + tail rope hanging down). Now 2 kinematic + 32 dynamic
  rbs + 8 sphere colliders (the new tail rotor adds another hub
  + 4 ropes + 4 tip colliders).
- Stress tower (when spawned): 5 kinematic + 80 dynamic rbs +
  20 sphere colliders. *Exceeds* the § 16 alarm, by design.
- Combat dummy: unchanged.

Total worst-case loadout (player plane + dummy + stress tower):
~ 7 kinematic, ~ 112 dynamic, ~ 28 colliders. The next § 14
profiling pass (PHYSICS_PLAN.md § 4) will tell us whether that
holds up.

## Files touched

```
A Assets/_Project/Scripts/Movement/RopeTip.cs
M Assets/_Project/Scripts/Movement/RotorBlock.cs   (RpmOverride, tip wiring)
M Assets/_Project/Scripts/Movement/RopeBlock.cs    (tip wiring on last seg)
M Assets/_Project/Scripts/Core/Tweakables.cs       (Stress group + rotor doc)
M Assets/_Project/Scripts/Gameplay/ArenaController.cs
M Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs
M Assets/_Project/Scripts/UI/DevHud.cs             (stress tower buttons)
M Assets/_Project/Scripts/UI/Robogame.UI.asmdef    (+ Robogame.Gameplay)
A docs/PHYSICS_PLAN.md
M docs/changes/README.md
A docs/changes/15-rotor-followups.md
```

## Follow-ups (not done this session)

- **Profile the stress tower.** PHYSICS_PLAN.md § 4 names the
  workflow and § 6 names the missing baseline capture. Next time
  you're in Unity, spawn the tower, RPM to 600, hit the Profiler,
  paste the numbers into a `docs/perf-baselines/` file. That
  number is the trigger gauge for the Verlet migration.
- **Verlet sandbox prototype.** A 30-minute sketch of
  `RopeSimulator` would let us A/B against the joint chain on
  the same stress tower and stop arguing in the abstract.
- **Per-block blueprint config.** The known refactor that lets
  `RpmOverride` retire and lets weapons carry per-instance
  config (firing mode, cooldown overrides, etc.).
- **RopeTip ignore set is stale on debris detach.** When a block
  detaches as debris, the rope tip's ignore list still excludes
  the now-separate debris colliders. Acceptable today (the rope
  flailing past flying debris reads as "physics, neat") but worth
  noting.
