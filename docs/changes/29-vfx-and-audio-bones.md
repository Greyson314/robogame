# Session 29 — VFX feel pass + audio system bones

> Status: **shipped, ready for in-engine playtest**. Compiles against
> the existing asmdef graph (Core → Block → Movement/Combat → Robot →
> Gameplay), no new asmdef references introduced. First in-engine run
> will tell whether the procedural particle bursts read as "tactile"
> or as "stylised line noise"; tuning knobs are concentrated in
> [`VfxSpawner`](../../Assets/_Project/Scripts/Core/VfxSpawner.cs) so
> a single editor session can dial the whole feel.

## Why this session

The game had every gameplay system but read flat. Shots flew without
recoil punctuation; ramming impacts registered in HP only; CPU
destruction and block detachment were silent geometric events; the
thruster nozzle was a static cube. Two things were on the table:

1. **A "feel pass" of VFX** — muzzle flashes, hit sparks, debris
   dust, ramming bursts, bomb shockwaves, thruster plumes — sized
   and palette-locked to the game's stylised-arcade direction.
2. **The bones for an audio pass** — settings UI page, mixer
   architecture, performance discipline. No clips yet; just the
   plumbing so a future session adds wav files and the rest is
   already wired.

## What landed — VFX

### Core: a procedural pooled spawner

- **[`Robogame.Core.VfxKind`](../../Assets/_Project/Scripts/Core/VfxKind.cs)**
  — flat enum of one-shot bursts (`MuzzleFlash`, `HitSpark`,
  `RamSpark`, `BombShockwave`, `DebrisDust`).
- **[`Robogame.Core.VfxSpawner`](../../Assets/_Project/Scripts/Core/VfxSpawner.cs)**
  — scene-root singleton, auto-bootstrapped via
  `RuntimeInitializeOnLoadMethod`. Builds a procedural
  `ParticleSystem` template per kind on first touch, pools instances
  per kind (cap 24), and sweeps a parallel `List<KindPool>` in
  `Update` to recycle expired bursts. Steady-state allocation-free
  per the [PERFORMANCE.md § 2.1](../PERFORMANCE.md#21-zero-allocations-per-steady-state-frame)
  rule (Dictionary.Values would allocate an enumerator — the index
  walk avoids it).
- **[`Robogame.Core.RuntimePalette`](../../Assets/_Project/Scripts/Core/RuntimePalette.cs)**
  — runtime-accessible mirror of the editor-only `WorldPalette`.
  Same 12 tokens, plus a few derived shades the procedural particles
  reach for (`HotCore`, `SmokeDark`, `DustLight`). Lives in
  `Robogame.Core` so every gameplay asmdef can author palette-locked
  VFX without taking an editor dependency.

The spawner is the *single* place a future tuning session changes
particle counts / sizes / colours; gameplay code only knows about
`VfxKind` enum values, not particle module values.

### Hooked at five gameplay sites

| Site | File | Hook point | Kind |
|---|---|---|---|
| Weapon fire | [`ProjectileGun.Fire`](../../Assets/_Project/Scripts/Combat/ProjectileGun.cs) | After projectile spawn, scaled by recoil impulse | `MuzzleFlash` |
| Bullet impact | [`Projectile.ApplyHit`](../../Assets/_Project/Scripts/Combat/Projectile.cs) | At `RaycastHit.point`, oriented by `RaycastHit.normal` | `HitSpark` |
| Chassis ram | [`MomentumImpactHandler.OnCollisionEnter`](../../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs) | Once per logical impact (pair-cooldown dedupe), scaled by kJ | `RamSpark` |
| Bomb explode | [`Bomb.SpawnVfx`](../../Assets/_Project/Scripts/Combat/Bomb.cs) | In addition to the CFXR fireball, scaled by blast radius | `BombShockwave` |
| Block detach | [`Robot.DetachAsDebris`](../../Assets/_Project/Scripts/Robot/Robot.cs) | Per detached block, before the new Rigidbody is added | `DebrisDust` |

Sizes are deliberately tight at the chassis-cube scale — a blockwide
muzzle flash reads cartoonish, not punchy. Hit sparks reflect off
the surface normal so they scatter into screen space rather than
bury into the cube they hit. Ram sparks bias downward so debris
settles. Bomb shockwave runs *alongside* the existing CFXR
explosion (which carries the smoke / fireball mood) — palette-locked
fragments augment, don't replace.

### Thruster plume — continuous, throttle-driven

[`ThrusterBlock`](../../Assets/_Project/Scripts/Movement/ThrusterBlock.cs)
gained a procedural particle plume parented to its transform. The
plume is **looping**, not pooled (one persistent ParticleSystem per
thruster); emission rate is driven from `CurrentThrottle` in
`Tick(in DriveControl)`. Idle = no particles, full throttle = ~60
particles/sec at 0.3 s lifetime. World-space simulation makes the
plume trail behind a moving chassis correctly.

The plume mesh is the built-in cube (re-used across every
thruster), the material is a single shared
`ParticleSystem`-compatible URP unlit material — SRP-batchable.
**Zero extra Rigidbodies, zero extra colliders** per the
[BEST_PRACTICES § 16](../BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
zero-baseline rule.

### Performance contract

- **VfxSpawner.Update**: allocation-free; profiled marker
  `Robogame.Vfx.SpawnerUpdate` shipped in
  [`PerfMarkers`](../../Assets/_Project/Scripts/Core/PerfMarkers.cs).
- **Hard cap per kind**: 24 concurrent bursts. When exceeded, the
  oldest live instance is recycled; we drop visual fidelity, never
  blow GC.
- **Spawn path cost**: one dictionary lookup, one stack pop (or
  Instantiate on first warmup), three transform writes, one Play().
  No new strings, no closures, no LINQ.
- **Predicted MP-scale stress**: 16 chassis × ~12 fps fire =
  ~192 muzzle-flash spawns/sec. Pool tops out at 24 active flashes
  simultaneously; the rest recycle in place. This is the
  documented [PERFORMANCE.md § 8.4 "Damage VFX storm"](../PERFORMANCE.md#84-damage-vfx-storm)
  mitigation, applied preemptively rather than after a stutter
  shows up.

## What landed — audio bones

No clips, no music. The plumbing.

### Settings: a real Audio group in the tweaks menu

[`Tweakables.cs`](../../Assets/_Project/Scripts/Core/Tweakables.cs)
gained an `Audio` group: `Master`, `Sfx`, `Music`, `UI` volume
sliders (0–1 linear), plus a `Mute All` toggle. The sliders auto-
appear in [`SettingsHud`](../../Assets/_Project/Scripts/Gameplay/SettingsHud.cs)
because that HUD iterates `Tweakables.All` — no UI authoring
needed.

The "no Tweakable affects gameplay outcomes"
([PHYSICS_PLAN § 1.5](../PHYSICS_PLAN.md)) rule is satisfied by
construction: audio levels are pure presentation, never read by
combat / damage / movement.

### Plumbing: AudioRouter + AudioCue + AudioCueLibrary

- **[`AudioCue`](../../Assets/_Project/Scripts/Core/AudioCue.cs)**
  — flat enum cataloguing every gameplay event that wants a sound:
  `WeaponFire`, `ProjectileImpact`, `BlockDestroyed`, `BombExplosion`,
  `MatchStart`, etc. 21 cues; trivially extensible.
- **[`AudioBus`](../../Assets/_Project/Scripts/Core/AudioBus.cs)**
  — four-bus enum (`Master / Sfx / Music / UI`) routed via
  `AudioMixer`.
- **[`AudioCueLibrary`](../../Assets/_Project/Scripts/Core/AudioCueLibrary.cs)**
  — ScriptableObject mirroring the `CombatVfxLibrary` pattern. Each
  cue → clip + bus + spatialBlend + per-cue volume + pitch jitter +
  solo flag. Empty by design until clips arrive.
- **[`AudioRouter`](../../Assets/_Project/Scripts/Core/AudioRouter.cs)**
  — scene-root singleton, auto-bootstrapped. Subscribes to
  `Tweakables.Changed`, applies the bus volumes to the mixer
  (linear → dB conversion shipped). When no mixer is wired, falls
  back to `AudioListener.volume = master * (mute ? 0 : 1)` so Mute
  works regardless. Public API:
  - `AudioRouter.PlayOneShot(AudioCue cue, Vector3 worldPos)` — 3D
  - `AudioRouter.PlayUI(AudioCue cue)` — 2D
  - Both no-op stubs today (logged once per missing cue at warning
    level so future audio-pass shows you exactly which cues need
    clips first).

### Performance plan documented

[`docs/AUDIO_PLAN.md`](../AUDIO_PLAN.md) is the audio-side companion
to PERFORMANCE.md. The headlines:

- Pooled `AudioSource` components on the `[AudioRouter]` GameObject,
  cap = 24 concurrent voices (matches VfxSpawner's cap by design).
- **No `AudioSource.PlayOneShot(...)`** — that helper allocates
  and bypasses our per-source configuration. Use `AudioSource.Play()`
  on a checked-out pooled source.
- Combat = 3D, UI / music = 2D. Skip the spatialisation cost where
  it gives nothing.
- Mixer-driven volume, never per-source `AudioSource.volume`. One
  parameter write, every voice attenuates.
- LOD strategy (priority + distance culling) parked until 16-
  chassis stress tests it.

## File-by-file diff summary

```
+ Assets/_Project/Scripts/Core/VfxKind.cs
+ Assets/_Project/Scripts/Core/VfxSpawner.cs
+ Assets/_Project/Scripts/Core/RuntimePalette.cs
+ Assets/_Project/Scripts/Core/AudioCue.cs
+ Assets/_Project/Scripts/Core/AudioBus.cs
+ Assets/_Project/Scripts/Core/AudioRouter.cs
+ Assets/_Project/Scripts/Core/AudioCueLibrary.cs
+ docs/AUDIO_PLAN.md
+ docs/changes/29-vfx-and-audio-bones.md          (this file)

~ Assets/_Project/Scripts/Core/PerfMarkers.cs     (+ VfxSpawnerUpdate marker)
~ Assets/_Project/Scripts/Core/Tweakables.cs      (+ Audio group: Master/Sfx/Music/UI/Mute)
~ Assets/_Project/Scripts/Combat/ProjectileGun.cs (+ MuzzleFlash spawn)
~ Assets/_Project/Scripts/Combat/Projectile.cs    (+ HitSpark spawn on impact)
~ Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs (+ RamSpark spawn)
~ Assets/_Project/Scripts/Combat/Bomb.cs          (+ BombShockwave alongside CFXR)
~ Assets/_Project/Scripts/Robot/Robot.cs          (+ DebrisDust on detach)
~ Assets/_Project/Scripts/Movement/ThrusterBlock.cs (+ procedural plume driven by throttle)
```

No asmdef edits. No new third-party packages.

## Open questions / follow-ups

- **In-engine playtest is the gate.** Procedural bursts on paper
  match the art direction (palette-locked, hard-edged, alpha-clipped,
  cube mesh particles). They might still feel either too small (need
  to bump scale on muzzle flash) or too busy (need to drop burst
  counts). Tune in `VfxSpawner.Configure*Kind` methods.
- **Damaged-block hit flash.** `BlockBehaviour.DamageDealt` is the
  obvious next hook — a tiny "I'm taking fire" flash on the block
  surface itself (separate from the projectile's hit-spark which
  fires on the projectile end). Deferred to keep this session
  focused.
- **Audio scaffolder for the AudioMixer asset.** Unity authors
  `.mixer` files only via the editor; we'll need a wizard analogous
  to `CombatVfxWizard` to create the mixer asset with the four
  groups + exposed parameters at the right path. Deferred — when
  clips ship, the wizard ships with them.
- **Damage-source tracking for explosions.** `Bomb.SpawnVfx` is
  static; when an audio-pass adds a bomb-explosion sound, it'll want
  the `_owner` Robot for "did I get the kill" feedback. Trivial to
  expose; not needed today.

## Architecture notes (for the catch-up brief)

- VFX dependency direction: `Robogame.Core` declares the spawner
  and the palette. Every gameplay asmdef references `Core` already,
  so muzzle flashes / hit sparks / etc. introduce no new asmdef
  edges.
- Audio same shape: `AudioRouter` lives in `Core`, the cue library
  is a `ScriptableObject` loaded from `Resources/`, every gameplay
  asmdef can fire cues without taking an audio dependency tree.
- Both systems auto-bootstrap (no scene authoring), so worktrees /
  fresh clones / scaffolder reruns inherit them for free.

## Future-session starter

1. Read this file (latest in `docs/changes/`).
2. `docs/changes/architecture.md` — modules table now includes Vfx /
   Audio routing.
3. `docs/AUDIO_PLAN.md` — audio rules (read this when adding clips).
4. Tune the procedural bursts: every knob lives in
   `VfxSpawner.Configure*Kind` — particle count, lifetime, palette,
   shape angle. One file, ~80 lines, tune in a single editor session.
