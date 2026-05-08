# Session 30 — Audio v1 (Universal Sound FX wired)

> Status: **shipped, ready for in-engine playtest**. The full audio
> path is live: settings → mixer multipliers → pooled
> `AudioSource` voices → 21 wired cue clips from Universal Sound FX.
> First playtest will tell whether the cue selections feel right;
> swapping a clip is a one-line table edit + re-run the wizard.

## Why this session

Session 29 shipped the audio *bones* — `AudioRouter` was a no-op
stub, `AudioCueLibrary` was empty, no clips were authored. With the
USFX pack now installed, this session fills in v1: real one-shot
voices for every gameplay event, looped voices for the rotor whine,
and click feedback on every button.

## What landed

### Cue library asset, scaffolded from code

[`AudioCueWizard`](../../Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs)
authors `Resources/AudioCueLibrary.asset` from a static cue-row
table inline in the wizard. Each row maps an `AudioCue` to a USFX
`.wav` path plus per-cue tuning (bus, spatial blend, base volume,
pitch jitter, solo flag). 21 of 21 cues wired.

The wizard ships with two ways to fire:

- **`Robogame → Scaffold → Audio → Build Cue Library`** — explicit
  menu item for re-runs after USFX upgrades or table edits.
- **`[InitializeOnLoadMethod]` first-time auto-build** — if the asset
  is missing on editor load and USFX is present, the wizard auto-
  creates the asset. No-op once the asset exists; the wizard remains
  the source of truth.

This mirrors `CombatVfxWizard` exactly so the workflow matches
"how do I scaffold X?" muscle memory across the project.

### Cue → clip table

| Cue | USFX clip | Bus | Spatial | Solo |
|---|---|---|---|---|
| `WeaponFire` | `WEAPONS/SciFi/.../BLASTER_Bright_Short_mono.wav` | SFX | 3D | yes |
| `ProjectileImpact` | `BREAKS_SNAPS/SNAP_Clean_mono.wav` | SFX | 3D | no |
| `BlockDamaged` | `ROBOTICS/.../ROBOTIC_Short_Burst_12_Digital_Air_Lock_mono.wav` | SFX | 3D | no |
| `BlockDestroyed` | `DEMOLISH/DEMOLISH_Short_01_mono.wav` | SFX | 3D | no |
| `ChassisRam` | `IMPACTS/Metal/IMPACT_Metal_Cling_Deep_mono.wav` | SFX | 3D | no |
| `BombExplosion` | `EXPLOSIONS/Arcade/EXPLOSION_Arcade_03_mono.wav` | SFX | 3D | no |
| `ThrusterIgnite` | `CHARGE_UPS_DOWNS/.../Semi_Up_1000ms_mono.wav` | SFX | 3D | yes |
| `ThrusterShutdown` | `CHARGE_UPS_DOWNS/.../Semi_Down_1000ms_mono.wav` | SFX | 3D | yes |
| `WheelRoll` *(loop, unhooked v1)* | `ENGINES_MOTORS_GENERATORS/ENGINE_Generic_01_loop_mono.wav` | SFX | 3D | yes |
| `RotorSpin` *(loop)* | `VEHICLES/Air/Helicopters/HELICOPTER_Hover_Fast_loop_mono.wav` | SFX | 3D | yes |
| `WaterSplash` | `ELEMENTS/Water/Splashes/SPLASH_Designed_Medium_01_mono.wav` | SFX | 3D | no |
| `UiHover` *(unhooked v1)* | `USER_INTERFACES/Beeps/UI_Beep_Bend_Short_stereo.wav` | UI | 2D | no |
| `UiClick` | `USER_INTERFACES/Clicks_Taps/UI_Click_Metallic_Bright_mono.wav` | UI | 2D | no |
| `UiBack` | `USER_INTERFACES/Clicks_Taps/UI_Click_TapBack_01_mono.wav` | UI | 2D | no |
| `MatchStart` | `8BIT/Coin_Collect/...Two_Note_Bright_Twinkle_mono.wav` | UI | 2D | yes |
| `MatchEndVictory` | `MUSIC_EFFECTS/MUSIC_EFFECT_Platform_Positive_01_stereo.wav` | UI | 2D | yes |
| `MatchEndDefeat` | `MUSIC_EFFECTS/MUSIC_EFFECT_Platform_Negative_01_stereo.wav` | UI | 2D | yes |
| `MatchEndDraw` | `MUSIC_EFFECTS/MUSIC_EFFECT_Orchestral_Battle_Neutral_stereo.wav` | UI | 2D | yes |
| `BlockPlace` | `TOOLS/Impact_Wrench/...Compressed_Air_Short_Burst_01_mono.wav` | UI | 2D | no |
| `BlockRemove` | `ROBOTICS/.../ROBOTIC_Short_Burst_05_Shut_Down_mono.wav` | UI | 2D | no |
| `InvalidPlacement` | `USER_INTERFACES/Errors/UI_Error_Double_Tone_01_mono.wav` | UI | 2D | yes |

Two cues are wired in the library but **not yet hooked** at gameplay
sites — they're parked for v2:

- **`WheelRoll`** — needs an aggregate "is this chassis rolling on
  the ground" signal. `WheelBlock` is per-wheel; the right hook is
  on `GroundDriveSubsystem` after it lands a chassis-level "speed +
  on-ground" pair.
- **`UiHover`** — would require wiring `EventTrigger` PointerEnter
  on every Button across SettingsHud / SceneTransitionHud /
  MainMenuController. Cheap individually; not worth the 50-line
  per-HUD footprint until the player asks for it.

### AudioRouter v1 — pooled voices, no mixer

[`AudioRouter.cs`](../../Assets/_Project/Scripts/Core/AudioRouter.cs)
is now a real implementation:

**One-shot pool.** 24 `AudioSource` components on child GameObjects
of `[AudioRouter]`. `PlayOneShot(cue, pos)` claims a free voice
(stack pop) or evicts the soonest-to-expire one if the pool is
empty. `Update` sweeps for expired voices and returns them to the
free list.

**Solo dedup.** Cues with `Solo = true` stop any in-flight voice
playing the same cue before claiming a new voice. `WeaponFire` is
solo so a held-trigger 12 Hz burst doesn't stack 12 simultaneous
fire voices per second; `MatchStart` is solo so a frantic
SPACE-spamming player doesn't trigger five overlapping coin chimes.

**Loop voices.** `PlayLoop(cue, parent)` allocates a separately-
owned `AudioSource` parented to the caller's transform, returns an
[`AudioLoopHandle`](../../Assets/_Project/Scripts/Core/AudioRouter.cs)
the caller stops explicitly. Loops live outside the one-shot pool
because they outlive any reasonable pool turnover. One alloc per
loop start; rotor blocks aren't built per-frame.

**Volume routing without a mixer.** v1 ships without an
`AudioMixer` asset (Unity authors mixers only via the editor; we
don't ship a wizard for it yet). Per-bus volume = cue base × bus
multiplier × master × mute gate, applied directly to
`AudioSource.volume`. `Tweakables.Changed` re-applies to every
live voice + every loop handle so a slider drag is heard
immediately.

The mixer wiring is plumbed: the moment `Resources/AudioMixer.mixer`
exists with exposed parameters `MasterVol` / `SfxVol` / `MusicVol` /
`UIVol`, `AudioRouter.Mixer` resolves it, and the per-bus path
switches to `mixer.SetFloat(...)` automatically.

### Gameplay hooks

| Cue | Where hooked | Notes |
|---|---|---|
| `WeaponFire` | [`ProjectileGun.Fire:204`](../../Assets/_Project/Scripts/Combat/ProjectileGun.cs) | Solo cue — held-fire chops cleanly |
| `ProjectileImpact` | [`Projectile.SpawnHitSpark:215`](../../Assets/_Project/Scripts/Combat/Projectile.cs) | At hit point with normal-reflected snap |
| `BombExplosion` | [`Bomb.SpawnVfx:147`](../../Assets/_Project/Scripts/Combat/Bomb.cs) | 3D rolloff carries the "big" feel |
| `ChassisRam` | [`MomentumImpactHandler:152`](../../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs) | Pair-cooldown already dedupes the multi-OnCollisionEnter case |
| `BlockDestroyed` | [`Robot.DetachAsDebris:393`](../../Assets/_Project/Scripts/Robot/Robot.cs) | Per-block; pool eviction caps the chassis-shatter case |
| `ThrusterIgnite` / `ThrusterShutdown` | [`ThrusterBlock.UpdatePlumeEmission`](../../Assets/_Project/Scripts/Movement/ThrusterBlock.cs) | Threshold-cross with hysteresis (0.55 / 0.45) |
| `RotorSpin` (loop) | [`RotorBlock.Start` + `FixedUpdate`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs) | Pitch and volume scale with `LiveRpm`; muted on kinematic chassis (garage) |
| `WaterSplash` | [`BuoyancyController.UpdateSplashAudio`](../../Assets/_Project/Scripts/Gameplay/BuoyancyController.cs) | Dry → wet transition with 1.5 s rearm cooldown |
| `UiClick` | `SettingsHud.AddButton`, `SceneTransitionHud.{button,BuildSmallButton}`, `MainMenuController.BuildButton` | Hooked at the helper, not per-call-site |
| `UiBack` | [`MatchEndOverlay.OnGUI`](../../Assets/_Project/Scripts/Gameplay/MatchEndOverlay.cs) | Return-to-Garage button |
| `MatchStart` | [`ArenaController.HandleMatchStarted`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs) | Stinger when the round goes hot |
| `MatchEndVictory/Defeat/Draw` | [`ArenaController.HandleMatchEnded`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs) | Switch on `MatchEndedArgs.WinnerSide` |
| `BlockPlace` / `BlockRemove` / `InvalidPlacement` | [`BlockEditor.TryPlace` / `TryRemove`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs) | Invalid covers CPU-removal-blocked, orphan-blocked, and grid-rejected paths |

### Performance contract (verified in code, not yet measured)

Per [`docs/AUDIO_PLAN.md`](../AUDIO_PLAN.md):

- `PlayOneShot` hot path: one library lookup (linear over 21
  entries), one stack pop, transform writes, AudioSource
  configure + Play. **No allocations.**
- Solo cues replace stacking voices — the worst-case 16-chassis MP
  arena with sustained SMG fire produces ~16 simultaneous
  WeaponFire voices, not 16 × 12 = 192.
- Hard cap of 24 concurrent one-shot voices. Eviction picks the
  voice that will expire first (rather than oldest-started) so the
  last second of a long-tail clip isn't cut for a fresh tick.
- Loop voices own their own `AudioSource` GameObject (one alloc per
  start, one destroy per stop) — they don't pin a slot in the main
  pool.
- `Tweakables.Changed` rewrites every live voice's volume in one
  pass; with the master + mute path multiplied in, audio response
  to a slider drag is one frame.

## Action required after pulling

1. Open the project. The `[InitializeOnLoadMethod]` will auto-build
   `Resources/AudioCueLibrary.asset` on first editor focus (no menu
   click needed).
2. Press Play from `Bootstrap.unity`. Verify:
   - Click any UI button → metallic click.
   - Open Settings (Esc) → drag Master / SFX / UI sliders → audio
     responds in real time.
   - Enter the arena → spawn a tank dummy (Settings → Stress →
     Spawn Tank Dummy + Tank Fires Player) → fire the SMG → hear
     blaster pop + impact snaps + dummy taking damage via the
     destroy crunch.
   - Drop a bomb → arcade boom.
   - Helicopter chassis → rotor whine in the arena, silent in the
     garage.
   - WaterArena → splash on first entry.
   - End a round → victory / defeat / draw stinger.

If a cue feels too loud / soft, swap the per-cue `Volume` in
[`AudioCueWizard.s_rows`](../../Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs)
and re-run the menu (`Robogame → Scaffold → Audio → Build Cue Library`).
The library asset re-generates from the table.

## Open questions / follow-ups

- **`AudioMixer` asset.** Not shipped — the per-source volume path
  works for v1 but mixer snapshots ("duck SFX 6 dB during
  match-end overlay") will want it. When clips arrive that need
  ducking, add a `Robogame → Scaffold → Audio → Build Mixer`
  wizard or hand-author the asset and exposed parameters.
- **`WheelRoll` engine loop.** Needs a chassis-level "is rolling +
  speed" signal exposed by `GroundDriveSubsystem` or `RobotDrive`.
  Then the hook mirrors `RotorBlock`'s pattern (PlayLoop on enable,
  pitch/volume from speed, Stop on destroy).
- **`UiHover`.** Library entry exists; not hooked. Adding it
  requires wiring `EventTrigger.PointerEnter` on every Button or
  introducing a shared `HoverableButton` wrapper. Defer until the
  user asks.
- **Multiplayer SMG voice cap.** `WeaponFire` is solo cue-globally,
  meaning my SMG and an enemy's SMG fight for one voice. With
  16-player MP this becomes wrong — a future audio pass will
  introduce per-source SMG voices via a "Voice = enum × ownerId"
  bucket, or accept the cap and use distance attenuation to make
  whichever fire is closer "win".
- **Looped clip pitch correctness.** The rotor loop's clip plays
  back at 0.5–1.6× pitch as RPM ramps. At extreme pitches the loop
  point may glitch; if it sounds wrong, tighten the pitch range or
  pick a less-tonal clip from `MACHINES`/`HVAC`.
- **Block destruction cluster sound.** A chassis shedding 30 blocks
  in one frame plays 30 simultaneous `BlockDestroyed` cues, of
  which ~24 are audible (pool cap) — sounds like a crackling
  collapse. If we want a single big "chassis exploded" boom, add a
  `ChassisDestroyed` cue and play it once in `Robot.MarkDestroyed`.
- **GC pressure of the `() => onClick?.Invoke()` lambda.** The
  `MainMenuController.BuildButton` and `SceneTransitionHud.BuildSmallButton`
  patterns add a closure per button. Negligible (one-time alloc at
  HUD construction). Flagged for future cleanup.

## Architecture notes (for the catch-up brief)

- `AudioCueLibrary` lives in `Robogame.Core` (no editor
  dependencies). `AudioCueWizard` lives in `Robogame.Tools.Editor`
  — same pattern as `CombatVfxLibrary` / `CombatVfxWizard`.
- `AudioLoopHandle` is `Robogame.Core` so any movement / combat
  block can hold one without taking an editor dep.
- Gameplay-tier hooks (Bomb, Robot, ArenaController, BuoyancyController)
  use fully-qualified `Robogame.Core.AudioRouter` to avoid adding
  more `using` lines; the qualified form is fine and matches the
  VFX hook style from session 29.
- All UI hooks land at the central button helper of each HUD
  (`AddButton` / `BuildButton` / `BuildSmallButton`) so adding a
  new button anywhere automatically gets its click cue.

## File-by-file diff summary

```
+ Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs
+ docs/changes/30-audio-v1.md                          (this file)

~ Assets/_Project/Scripts/Core/AudioRouter.cs          (real pooled implementation)
~ Assets/_Project/Scripts/Combat/ProjectileGun.cs      (+ WeaponFire)
~ Assets/_Project/Scripts/Combat/Projectile.cs         (+ ProjectileImpact)
~ Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs (+ ChassisRam)
~ Assets/_Project/Scripts/Combat/Bomb.cs               (+ BombExplosion)
~ Assets/_Project/Scripts/Robot/Robot.cs               (+ BlockDestroyed)
~ Assets/_Project/Scripts/Movement/ThrusterBlock.cs    (+ ThrusterIgnite/Shutdown threshold-cross)
~ Assets/_Project/Scripts/Movement/RotorBlock.cs       (+ RotorSpin loop, garage-mute)
~ Assets/_Project/Scripts/Gameplay/BuoyancyController.cs (+ WaterSplash on dry→wet)
~ Assets/_Project/Scripts/Gameplay/ArenaController.cs  (+ MatchStart + Victory/Defeat/Draw)
~ Assets/_Project/Scripts/Gameplay/MatchEndOverlay.cs  (+ UiBack on Return)
~ Assets/_Project/Scripts/Gameplay/SettingsHud.cs      (+ UiClick on every button via AddButton)
~ Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs (+ UiClick on every button)
~ Assets/_Project/Scripts/Gameplay/MainMenuController.cs (+ UiClick on every button)
~ Assets/_Project/Scripts/Gameplay/BlockEditor.cs      (+ BlockPlace/Remove/InvalidPlacement)
```

No asmdef edits. No new third-party packages.

## Future-session starter

1. Read this file (latest in `docs/changes/`).
2. `docs/AUDIO_PLAN.md` — perf rules + future-shape plan.
3. Tune cues by editing `AudioCueWizard.s_rows`, then run
   `Robogame → Scaffold → Audio → Build Cue Library`.
4. Add a new cue: declare it in `AudioCue.cs`, add a row in
   `AudioCueWizard.s_rows`, hook the call site. Three-line workflow.

## Addendum — deeper gun + propeller cue

User feedback after first listen: gun was too high-pitched and
rotors needed audio when they were acting as propellers (i.e.
rotor + adopted foils). Two changes:

### Gun: `BLASTER_Bright_Short` → `BLASTER_Deep_Muffled`

Same folder (`WEAPONS/SciFi/Blasters_Simple/`), heavier sound.
Volume bumped from 0.65 to 0.75 because deep clips need more
headroom to feel equivalently loud.

### New `PropellerLoop` cue, branched at runtime in `RotorBlock`

Bare rotors (tail rotors, cosmetic spinners) keep the existing
helicopter-blade whine (`HELICOPTER_Hover_Fast_loop_mono.wav`).
Rotors with adopted foils — i.e. functional propellers
generating lift — switch to a propeller-engine loop
(`VEHICLES/Air/Airplanes/PROPELLER_ENGINE_Loop_01_loop_mono.wav`).

The branch happens once at
[`RotorBlock.StartRotorAudioLoop`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
(called from `Start`), based on `_adoptedFoils.Count > 0`. By
`Start()` the lift rig's adoption pass (in `OnEnable`) has
already run, so the count is final at branch time.

Per-cue base volumes diverge here: bare-rotor whine at 0.45,
propeller engine at 0.55 — props are core to chassis identity
on a heli-style build, the whine is supporting motif on a
tail rotor.

Both still gate to silent on a kinematic chassis (garage), and
both pitch-scale linearly from 0.5 → 1.6× over 0 → 600 RPM.

## Addendum 2 — Tip impact "thonk" + grapple-hook physics fix

User reported two issues:

1. The hook / mace had no contact audio (a swung weapon should
   "thonk").
2. The grappling hook was either destroying the target on contact
   or yeeting the whole rig (chassis + tip + target) off into the
   distance.

### TipImpact audio

New `AudioCue.TipImpact` mapped to
`IMPACTS/Metal/IMPACT_Metal_Cling_Deep_Damped_mono.wav` —
metallic + deep + damped (no sustain ring) reads as a "thonk".
Played from
[`TipBlock.HandleCollision`](../../Assets/_Project/Scripts/Movement/TipBlock.cs)
right after the speed gate, before the damage branch. Both Hook
(damage-suppressed — see below) and Mace play it because the
audio runs in the base.

### Grapple bug 1 — first contact instakilled the target block

**Root cause.** `HookBlock.HandleCollision` ran the base damage
path BEFORE attempting to grapple. With `_damagePerKj = 2.0` (the
TipBlock default) and a hook tip moving fast, the kinetic-energy
damage path could one-shot the target block — leaving the joint
with nothing to anchor to. The user saw "the thing the hook
catches gets destroyed".

**Fix.** Added a virtual
[`TipBlock.DamagePerKj`](../../Assets/_Project/Scripts/Movement/TipBlock.cs)
property; HookBlock overrides it to return 0. The hook now
deals zero contact damage. Audio + cooldown + speed-gate paths
still run so the swing reads as a hit. The mace remains the
dedicated contact-damage tip.

This matches design intent that wasn't explicit in the original
code: hook = grappling tool, mace = bashing tool.

### Grapple bug 2 — target launched into the chassis

**Root cause.** Two locked-distance constraints on the rope tip's
~0.5 kg Rigidbody:

- The grapple joint locks tip ↔ target distance.
- The chassis-tip joint (`spring=8000 N/m`, `damper=250`) limits
  chassis ↔ tip distance.

When the player flies away while grappled, the chassis-tip
joint applies very large restoring forces in a single FixedUpdate
step. The grapple joint transmits those forces as impulses to the
target body. PhysX checks `breakForce` AFTER the constraint
solver applies its impulse — so for a low-mass target, the
impulse arrives in full before the grapple breaks. The target
gets a several-tens-of-m/s velocity kick toward the chassis,
collides with it, and the whole rig "hurdles into the distance
while glitching out."

**Fix.** While grappled, soften the chassis-tip joint:

```
spring  8000 → 1500
damper   250 →  600
```

Smears the restoring force over more frames so it stays inside
PhysX's normal-force envelope and the grapple's `breakForce`
trips before the target accumulates catastrophic velocity. The
rope still pulls the chassis back toward the grapple anchor —
swinging works the same — just without the spring spike.

[`HookBlock`](../../Assets/_Project/Scripts/Movement/HookBlock.cs)
caches the chassis-tip joint at `AttachToHost` (when there's
exactly one ConfigurableJoint on the host). `Attach` swaps in
the soft spring; `Release` restores the original. The cached ref
is replaced on each adoption so a rope rebuild can't leave a
stale joint pointer behind.

### Files touched

```
~ Assets/_Project/Scripts/Core/AudioCue.cs                  (+ TipImpact)
~ Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs    (+ TipImpact row)
~ Assets/_Project/Scripts/Movement/TipBlock.cs              (audio + virtual DamagePerKj)
~ Assets/_Project/Scripts/Movement/HookBlock.cs             (zero damage + soft chassis-tip spring while grappled)
```

The wizard's `[InitializeOnLoadMethod]` rebuild check (added in
addendum 1) detects the new cue and re-builds
`Resources/AudioCueLibrary.asset` automatically on next editor
focus — no menu click required.

## Addendum 3 — clang ↔ thoonk swap, backtick fight starter, wind cue

Three independent asks bundled together.

### Chassis ram → thoonk; tip impact → clang

Audio identity swap. Previously:

- `ChassisRam` = `IMPACT_Metal_Cling_Deep` (ringy clang)
- `TipImpact` = `IMPACT_Metal_Cling_Deep_Damped` (damped, no ring)

A bot landing on the ground sounded like a swung weapon. Reversed:

- **`ChassisRam`** → `THUDS_THUMPS/THUD_Deep_Noisy_01_mono.wav` —
  deep + noisy reads as a heavy mass landing, no metallic
  resonance.
- **`TipImpact`** → `IMPACTS/Metal/IMPACT_Metal_Cling_Deep_mono.wav` —
  the metallic clang now lives where it belongs (hook / mace).

Wizard auto-rebuild detects the clip-path swap on next editor
focus.

### Backtick (`` ` ``) starts the round, replaces Space

`ArenaController._startMatchKey` default flipped from `Key.Space`
to `Key.Backquote`. The serialized value in `Arena.unity` was
also updated (`_startMatchKey: 1` → `4`) so the existing scene
picks up the change without manual inspector edits.

The on-screen prompt that
[`StartMatchHud`](../../Assets/_Project/Scripts/Gameplay/StartMatchHud.cs)
draws renders the literal `` ` `` character rather than
`"BACKQUOTE"` — `ArenaController` maps the key name through a
small switch so the player sees `Press [`] to begin combat`.

The
[`SettingsHud` keybinds reference](../../Assets/_Project/Scripts/Gameplay/SettingsHud.cs)
gained a row documenting the new binding.

### Wind loop scaling with chassis speed

New `AudioCue.WindLoop` mapped to
`WIND/WIND_Storm_Blowing_Deep_01_loop_mono.wav`. Volume + pitch
ramp linearly with chassis speed:

| Speed | Volume | Pitch |
|---|---|---|
| < 5 m/s | 0 (silent) | — |
| 5 → 35 m/s | 0 → 0.5 base | 0.85 → 1.15 |
| > 35 m/s | clamped at 0.5 base | clamped at 1.15 |

Pitch range stays narrow (±15 %) so the loop's harmonic content
doesn't audibly drift into "broken sound" territory.

Lives on a new
[`Robogame.Movement.ChassisWindAudio`](../../Assets/_Project/Scripts/Movement/ChassisWindAudio.cs)
component, auto-attached to every chassis by
[`ChassisFactory.Build`](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs).
Same lifecycle pattern as `RotorBlock`'s loop:
`OnEnable` starts the loop, `OnDisable` / `OnDestroy` stop it
(so `Robot.CaptureTemplate`'s deactivate-clone-reactivate dance
doesn't leave a phantom audio source on a hidden template).

The cue is **3D** (`spatialBlend = 1`). Reasoning: a fast bot
whooshing past should pan and attenuate naturally; the local
player's camera sits inside the wind source's `minDistance` (4 m)
so player wind reads at full strength regardless of camera orbit.
A 2D wind would compete for the centre of the mix with every
other chassis's wind — bad at MP scale.

### Files touched

```
+ Assets/_Project/Scripts/Movement/ChassisWindAudio.cs
~ Assets/_Project/Scripts/Core/AudioCue.cs                       (+ WindLoop)
~ Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs         (clip swap + WindLoop row)
~ Assets/_Project/Scripts/Gameplay/ChassisFactory.cs             (auto-attach ChassisWindAudio)
~ Assets/_Project/Scripts/Gameplay/ArenaController.cs            (Backquote default + friendly key label)
~ Assets/_Project/Scripts/Gameplay/SettingsHud.cs                (keybinds reference row for `)
~ Assets/_Project/Scenes/Arena.unity                             (serialized _startMatchKey: 1 → 4)
```
