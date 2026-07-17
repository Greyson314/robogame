# Robogame — Combat Music & Musical Damage Feedback

> **Status.** v2 shipped (session 145, ADR-0007): FMOD Core plays the
> backing track as intensity-layer stems, beat grid re-anchored across
> the two clocks. Stinger timbres remain generated placeholders until
> a soundfont is imported for the MPTK path (built, gated, unheard).

## What this is

The slapstick damage-feedback layer: a rolling war-drum backing track
in arenas, and instrument stingers when projectile hits land — each
weapon voiced by an instrument, quantised to the track's beat grid,
consonant by construction. Sea of Thieves' trick, Unity-native.
Rationale and alternatives: [ADR-0006](../decisions/0006-musical-damage-feedback.md).

## Architecture

- **`MusicConductor`** (Core) — owns the backing track, two backends
  (ADR-0007). Preferred: **FMOD Core** plays the intensity-layer stems
  from `StreamingAssets/Music/` as channels released by one shared
  `setDelay` tick (sample-locked); `SetIntensity(0..2)` fades layer 2
  over 0→1 and layer 3 over 1→2 (fast rise, slow fall). Fallback:
  single clip via `PlayScheduled` (v1). Either way the grid is
  `startDsp + n × (60/BPM)` arithmetic on `AudioSettings.dspTime`;
  in FMOD mode `startDsp` is the FMOD start tick mapped through
  **`MusicClock`** (Core, pure) — the two-clocks offset estimator
  (warmup mean, then clamped-innovation EMA; `MusicClockTests`).
  `NextSlotDsp(subdivision, offset, lead)` answers quantise queries;
  off-beat 8th = `(1, 0.5, …)`; returns −1 while the bridge warms up
  (~8 frames). Stops on scene unload. Track metadata + stem list:
  `MusicTrackDefinition` at `Resources/Music/CombatTrack.asset`.
- **`MusicMidi`** (Core) — MPTK soundfont voice for stingers: one
  synth channel per instrument (GM pizzicato/brass/piano/timpani),
  runs as note tables in the same D pentatonic. Active only when
  `MPTK_SoundFontLoaded`; otherwise the director stays on the WAV
  path. ms-delay scheduling — synth-buffer accurate, not sample-exact.
- **`MusicalHits`** (Combat) — static fan-in, sibling of
  `DamageAttribution` but carrying `ProjectileKind`. Reported from
  `ProjectileWorld`'s three damage paths (direct / ring / area).
- **`MusicalHitDirector`** (Gameplay, on the arena camera, bound by
  `ArenaController` with `LookupSide`) — accumulates hits per
  instrument per quantise window, flushes ONE stinger per window via
  `AudioRouter.PlayScheduled`. Tiers by window damage
  (`MusicMath.TierFor`): note → flourish → phrase (kill, on-beat).
  Outgoing hits walk the global pentatonic; incoming hits play the
  same instrument an octave down (register separates teams, not key).
- **`MusicMath`** (Core, pure) — grid + tier math, covered by
  `MusicMathTests`.

Instrument map (in `MusicalHitDirector.InstrumentFor`): SMG → pluck,
cannon → brass, mortar → piano, bomb → timpani.

## Authoring contract

- Backing tracks are rendered at **exactly** `bars × beatsPerBar ×
  (60/BPM)` seconds — a sloppy loop seam breaks the grid.
- All pitched material (track drone + stinger clips) shares one root
  (currently D). Stinger note clips are recorded at the root;
  `MusicalSfx`'s pentatonic multipliers stay within one octave.
  Flourish/phrase clips are baked runs in key and play at pitch 1.
- Stinger cue rows live in `AudioCueWizard.s_rows` (Music bus, 2D,
  jitter 0 — the director owns pitch). `Assets/`-rooted paths bypass
  the USFX root.

## Regenerating the placeholder assets

Clips are synthesised offline:
[`artgen/gen_music_assets.py`](../../artgen/gen_music_assets.py)
(python + numpy) writes stinger WAVs + the fallback track into
`Assets/_Project/Audio/Generated/` and the three intensity stems
(bed / strings / brass, identical sample-exact length) into
`Assets/StreamingAssets/Music/`, then **Robogame → Scaffold → Music →
Build Combat Music** wires the track asset (clip + stem list) and cue
rows. Replacing placeholders with real recordings
is a pure asset swap: drop in same-named files (or edit the wizard
rows), respect the root-note contract, rebuild.

## Performance

Steady state allocates nothing: fixed per-instrument buckets, pooled
router voices, no LINQ/strings on the hit path. The director's
`Update` is two 4-element array scans. The conductor is one looping
`AudioSource`. Everything is client-side cosmetic (INV-3 safe);
quantisation delays presentation, never damage.

## Known gaps / next steps

- **No soundfont imported yet** — the MPTK stinger path is built but
  dormant (and unheard). Import one via Maestro's SoundFont Setup
  window (Menu → Maestro); `MusicMidi` activates itself. Until then
  stingers stay on the placeholder WAVs.
- MPTK note timing (ms-delay, synth-buffer accuracy) and synth CPU
  cost are unverified with a live soundfont — profile on first use
  (INV-7).
- FMOD Studio desktop app isn't installed; authored events/banks
  (transition regions, snapshots, per-arena reverb) are the upgrade
  path from Core channels when it is (ADR-0007).
- Tip weapons (hook/mace), rams and drills don't report musical hits
  yet — `MusicalHits.Report` at their damage sites when wanted.
- Kill "wahoo!" cartoon animation hook (ADR-0006 Notes) — not built.
- Garage theme — still out of scope.
