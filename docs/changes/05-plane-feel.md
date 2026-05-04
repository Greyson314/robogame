# Session — Plane "feel" pass

**Intent.** Plane top speed felt too high, pitch was sluggish, then
later: a constant "buoyancy" pushing the tail up.

**What landed.**

- Bumped pitch/roll authority and tightened damping in
  [PlaneControlSubsystem.cs](../../Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs):
  pitch 3.2 → 7.5, roll 4.5 → 9.0, yaw-from-bank 1.4 → 2.0; damping
  pitch 2.6 → 3.5, roll 2.6 → 2.8, yaw 1.4 → 1.6.
- Lowered top speed in [ThrusterBlock.cs](../../Assets/_Project/Scripts/Movement/ThrusterBlock.cs):
  max thrust 220 → 155 N, idle throttle 0.5 → 0.4.
- **Aerodynamics rewrite** in [AeroSurfaceBlock.cs](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs).
  Was: `lift = speed² × coef × sign(forward)`. Every wing produced
  upward force unconditionally, so wings far from COM caused a
  permanent pitching moment — that was the "buoyancy on the tail" feel.
  Now: angle-of-attack-driven lift, `lift = speed² × coef × (clamp(aoa, ±stallAoA) + zeroLiftBias) × sign(forward)`,
  with soft stall past `stallAoA`. At level flight every surface
  produces only `zeroLiftBias × speed²` of lift evenly distributed →
  pitching moments cancel → plane self-trims. Pitching up raises AoA
  uniformly across the plane → all wings produce more lift → real
  elevator authority through aerodynamics, not just torque.

**Lesson learned.** First-pass attempt at fixing the "buoyancy" was to
*remove* the rear lifting surfaces (replace tailplane Aero with Cube).
That made the plane lift-deficient and dive. The AoA model alone fixes
the moment problem without removing surfaces. Reverted blueprint to
all-Aero tail.

**Tuning hooks.** `_zeroLiftBias` controls "how much free lift at zero
AoA." 0 = pure symmetric airfoil (must constantly pitch up to stay
level). 0.12 ≈ current — plane cruises level on its own at the right
speed. `_liftCoef` scales total lift; bump if it dives, drop if it
climbs unprompted.

**Tuning-iteration trap (resolved by the Tweakables session above).**
User reported "saving + Build All Pass A + Play, settings don't
change." Root cause: there are two parallel sets of defaults — inline
`[SerializeField]` on the component, and `*.Tuning.cs` SO defaults. The
runtime path through [ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
never assigns a tuning SO, so SO edits are dead code there. Build All
Pass A doesn't trigger recompile — only saving a .cs file *and*
focusing Unity does. Added `OnEnable` debug logs that print the
actually-effective values + their source. The Tweakables session
obsoletes both paths for the exposed knobs.
