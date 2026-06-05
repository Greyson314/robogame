# 115 — Explosive knockback actually lands (self + enemies)

> Bugfix. The Laboratory knockback slider (and bombs/mortars/mines in general)
> read as "no knockback" in playtest. Two independent causes, both fixed.

## Symptom

Dropping a bomb — including a maxed-knockback concoction — visibly shoved
nothing: not the dropper, not enemies, not dummies. Damage and crater worked.

## Root causes

1. **Too weak to perceive.** `ProjectileWorld.ExplosiveKnockbackDeltaVPerUnit`
   was `0.03`. The prior fix ([fc6a55ff]) correctly made knockback mass-aware
   (target Δv → impulse of `mass × Δv`, so heavy bots move as much as light
   ones) but tuned it to ~1 m/s at the blast centre for a base bomb — below the
   threshold of "did anything happen?" against driving speeds and ground
   friction. Raised to `0.12`: base bomb ≈ 4.8 m/s, maxed-knockback concoction
   (×2) ≈ 9.6 m/s, maxed mortar ≈ 13.2 → clamped.

2. **Self-knockback structurally impossible.** `ApplyAreaSplash` `continue`d the
   owner (and teammates) out of the loop *before* the knockback call, so a bomb
   at your own feet could never launch you. Bomb-jumping was unreachable.

## Fix

- `KnockbackReceiver`: new `MaxExplosiveDeltaV = 12` ceiling + an
  `ApplyImmediate(impulse, maxDeltaV)` overload. Explosions clamp to 12 m/s;
  kinetic (cannon/SMG) keeps its 3 m/s cap — raising the explosive ceiling does
  **not** let a big cannon launch a light bot.
- `ProjectileWorld.ApplyAreaSplash`: friendly (owner **or** teammate) now skips
  *damage* but still receives *knockback*. Per-robot dedup (`_splashRobots`) and
  the hit-marker gate (`hitAny`) are preserved — friendly knockback flashes no
  hit marker. `TRACE[LOG-115]` anchors the carve-out from the friendly-fire rule.
- `ExplosiveKnockbackDeltaVPerUnit` 0.03 → 0.12.

This is the **second** dedicated knockback patch (after fc6a55ff). If a playtest
shows it *still* not landing, the next step is a research turn, not a third
number bump — the mass-aware model would then be suspect, not the tuning.

## Scope / side-effects (intended)

- Mines (`ProjectileWorld.Detonate` → `ApplyAreaSplash`) inherit the same
  behaviour: the mine's owner gets shoved (no self-damage) if standing in the
  blast. Acceptable — and consistent.
- A ground bomb-bay vehicle will now pop itself slightly on each drop. This is
  the requested self-knockback, not a regression.

## Verification

- `run-tests.sh EditMode`: 302/303 passed, 0 failed (1 pre-existing
  inconclusive). Compiles clean.
- **Needs a playtest** to confirm the *feel* (Δv math is sound but unobserved in
  motion): drop a bomb at your feet → expect a pop; bomb a dummy → expect a
  shove. INV-7: no perf claim made.

[fc6a55ff]: fix(combat) make explosive knockback mass-aware + cap crater depth
