# Robogame — Audio Plan

> **Status.** v1 shipped (session 30). 21 USFX cues wired,
> [`AudioRouter`](../Assets/_Project/Scripts/Core/AudioRouter.cs)
> implementation real (pooled one-shots + persistent loops), every
> in-game button + combat / movement / match-state event is hooked.
> The AudioMixer asset is still TBD — per-source volume routing
> covers v1, mixer snapshots get added when ducking matters.
> Authored Universal Sound FX clips live under
> `Assets/Universal Sound FX/`; the cue → clip mapping is data-driven
> via [`AudioCueWizard`](../Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs).

## Why this doc exists

Audio is a system whose performance footprint is invisible until the
project lights it up at full scale — and at that point the wrong
choice (every gunshot allocates a new `AudioSource`, every UI click
spawns a fresh GameObject, the master volume is wired through
`Renderer.material` for some reason) is much harder to tear out than to
prevent. This doc sets the rules now. Implementation can proceed with
or without revisiting it; if a rule needs to bend, update it here in
the same PR.

It is also the answer to "how do I add a sound effect?" once clips
arrive: declare the cue in `AudioCue.cs`, add a row to the library
asset, call `AudioRouter.PlayOneShot(cue, position)` at the gameplay
site. Three steps, no scene authoring.

## Architecture

### The four-bus mixer

```
Master
├─ SFX    — combat / movement / world cues
├─ Music  — background score
└─ UI     — menu clicks, HUD ticks, settings panel
```

Bus volumes ride on the `Audio` group in
[`Tweakables.cs`](../Assets/_Project/Scripts/Core/Tweakables.cs). Every
slider is 0–1 linear gain; `AudioRouter.LinearToDb` converts to the
mixer's dB attenuation at apply time so the slider reads as
"perceived loudness" rather than "exponential nightmare". The Mute
toggle hard-cuts every bus on top of the slider values (the slider
state is preserved underneath, so mute/unmute round-trips cleanly).

### The cue catalogue

`AudioCue` is a flat enum. Each value names a gameplay event, not a
clip. Mapping enum → clip + bus + spatialisation lives in
`AudioCueLibrary` (a ScriptableObject in `Resources/`), so:

- Gameplay code says `AudioRouter.PlayOneShot(AudioCue.WeaponFire, muzzlePos)`.
- A future audio designer wires `WeaponFire → smg_fire_v3.wav, bus = SFX, spatialBlend = 1.0`.
- Swapping clips is a library edit — no recompile, no caller changes.

Same separation we use for blocks (BlockIds.Cpu) and weapons
(WeaponDefinition by string id). It's load-bearing once netcode lands:
servers and clients agree on cues by name; clip assets stay
client-local.

### The pool

The hot-path API is `PlayOneShot(cue, position)`. Once clips ship,
the implementation pools `AudioSource` components on the
`[AudioRouter]` GameObject:

```
[AudioRouter]
└─ [Voices]
   ├─ Voice_0  (AudioSource, 3D)
   ├─ Voice_1  (AudioSource, 3D)
   ├─ ...      up to MaxConcurrentVoices = 24
   └─ Voice_UI (AudioSource, 2D, persistent)
```

Each `PlayOneShot` checks out a free voice, configures clip + group +
spatialBlend + pitch jitter, calls `Play`, and registers an expire
callback. No `Instantiate`, no GC, no `AudioSource.PlayOneShot(...)` —
that helper allocates and bypasses our per-source configuration.

The cap is **per-cue**: rapid-fire SMG hits don't blow past 24
concurrent muzzle-flash sounds because the cue's `Solo` flag (for the
fire loop) or a "max N voices for this cue" gate (for hit-spark)
short-circuits the spawn.

## Performance contract

These are non-negotiables. Audio violations show up as stutter, never
as a frame-time spike, so a well-meaning regression can ship without
firing the perf HUD.

### Steady-state zero allocations

`PlayOneShot` runs on the SMG's fire callback at up to 12 Hz × 16
chassis = 192 calls/sec at 16-player MP. At zero allocations per call
this is invisible; at 64 bytes/call it produces ~12 KB/s of garbage
and a measurable hitch every few seconds.

Specifically:

- No `AudioSource.PlayOneShot(...)` (allocates).
- No `new AudioSource(...)` per call.
- No string formatting in the missing-cue path during normal play —
  the `s_loggedMissing` HashSet de-dupes warnings to once per cue per
  session.
- No LINQ on the cue library's entry list — first-match `for` loop is
  fine (cue count is bounded, < 50 forever).

### Bounded voice count

`MaxConcurrentVoices = 24`. When the pool is full, the **oldest live
voice for the same cue** is recycled (same eviction the
[`VfxSpawner`](../Assets/_Project/Scripts/Core/VfxSpawner.cs) uses
for particle systems). If the pool is full *across all cues* — i.e.
the game is generating real audio chaos — newer one-shots silently
drop. That's the right behaviour: 24 simultaneous voices is already
past the perceptual threshold where adding more makes things louder
rather than richer.

### Spatialisation

Combat events are 3D (`spatialBlend = 1`); UI / music / match-state
cues are 2D (`spatialBlend = 0`). The 2D voices skip
`AudioSource.minDistance` / `maxDistance` work entirely. We avoid the
"3D-positioned UI click" trap that costs ~0.05 ms per call for
nothing.

### Mixer-driven volume, not per-source

Every voice routes through its `AudioMixerGroup` (set by the cue's
`Bus` field, applied at voice configuration). Volume sliders in the
settings UI write to the **mixer's exposed parameters**, not to
`AudioSource.volume` on every live voice. One write, every voice
attenuates.

### LOD: distance + culling

A 16-chassis arena will have many simultaneous engine loops, foam
splashes, and rotor whines. Strategy when needed (not before):

1. **Distance-attenuated culling.** Voices past
   `AudioSource.maxDistance` already produce no audible signal but
   still consume a voice slot. Set `priority` on the AudioSource so
   the mixer culls them when the pool is full.
2. **Importance gating.** Player chassis cues = priority 0 (always
   audible). Bot chassis cues = priority 128 (cullable). Distant
   debris = priority 256 (cull first).
3. **One looped voice per chassis, not per block.** A 30-block
   chassis must not produce 30 simultaneous engine loops. Each
   chassis owns one engine loop voice; pitch / volume modulate from
   chassis state.

### What we will NOT do

- **No FMOD / Wwise.** AudioMixer covers our needs; adding a
  middleware integration is months of work for a cosmetic upgrade.
- **No procedural audio synthesis.** Authored clips only. Engine
  loops can be tuned via pitch shifting on a single source.
- **No per-frame `Camera.main` lookups** for audio listener position
  (cf. PERFORMANCE.md § 2.4).

## Adding a sound (when clips arrive)

1. **Declare the cue.** Add a value to
   [`AudioCue`](../Assets/_Project/Scripts/Core/AudioCue.cs).
2. **Author the row.** Open `Resources/AudioCueLibrary.asset`, add a
   new `Entry`, drop in the clip + bus + spatial blend.
3. **Call from gameplay.** `AudioRouter.PlayOneShot(AudioCue.X, worldPos)`
   for combat / movement; `AudioRouter.PlayUI(AudioCue.X)` for menu /
   HUD. The same locations the VFX hooks already live (see session
   29) are the right places.
4. **Verify in a stress scenario** (PERFORMANCE.md § 3.4). The Rotor
   Tower with bot fire enabled exercises ~20 concurrent SFX
   simultaneously — a representative torture test.

## Open research items 🔬

- **Mixer asset authoring.** The asset itself isn't shipped yet
  (Unity authors `.mixer` files only via the editor; we'll add a
  scaffolder once clips are ready, mirroring `CombatVfxWizard`).
- **Snapshot ducking.** Whether to use mixer snapshots or
  cross-fade per-bus volumes for the "match-end overlay ducks SFX
  6 dB" effect. Snapshots are the canonical way; needs
  experimentation.
- **Music system.** Layered stems vs. continuous track, intensity
  scaling, transition crossfades — all deferred. Probably its own
  doc when it's a real concern.
- **Voice chat.** Out of scope until MP. When it lands, voice gets
  its own bus + push-to-talk gating; not a row in `AudioCue`.

---

*Last updated: 2026-05-06 (session 29 — bones).*
