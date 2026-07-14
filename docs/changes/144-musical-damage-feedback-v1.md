# 144 — Musical damage feedback v1 (ADR-0006 implemented)

**Date.** 2026-07-14
**Intent.** Implement ADR-0006 (user-approved): combat backing track +
beat-quantised instrument stingers on projectile hits.

## What landed

- **Core.** `MusicMath` (pure grid/tier math), `MusicTrackDefinition`
  (SO at `Resources/Music/CombatTrack.asset`), `MusicConductor`
  (PlayScheduled-anchored beat grid, AudioRouter-style bootstrap,
  stops on scene unload). `AudioRouter.PlayScheduled(cue, dsp, pitch,
  volScale)` added for scheduled stingers. 12 stinger cues appended to
  `AudioCue`. `MusicalSfx.ScalePitch/ScaleSteps` exposed.
- **Combat.** `MusicalHits` static fan-in (sibling of
  `DamageAttribution`, + `ProjectileKind`); reported from
  ProjectileWorld's direct / ring / area damage paths.
- **Gameplay.** `MusicalHitDirector` on the arena camera (bound by
  ArenaController): per-instrument hit accumulation per quantise
  window → one stinger on the next off-beat 8th; kills → phrase on
  the beat; incoming fire → same instrument, octave down, quieter.
  Instruments: SMG pluck, cannon brass, mortar piano, bomb timpani.
- **Editor.** `MusicScaffolder` menu (Robogame → Scaffold → Music);
  AudioCueWizard learned `Assets/`-rooted rows + the 12 stinger rows.
- **Assets.** Placeholder clips synthesised offline (numpy):
  D-rooted 100 BPM war-drum loop (sample-exact 8 bars) + 4×3 stinger
  clips in `Assets/_Project/Audio/Generated/`.
- **Docs.** ADR-0006 → Accepted with as-implemented amendment (rides
  shipped `MusicalSfx` pitch-shift instead of importing MPTK — import
  needs the user's Asset Store session; MPTK stays the multi-key
  upgrade path). New `docs/subsystems/music.md`; audio.md pointers.

## Verification

- `MusicMathTests` 9/9 (one real epsilon bug caught: exactly-on-slot
  quantise skipped a subdivision; fixed in MusicMath, not the test).
- Live play-mode (Bootstrap → EnterArena): conductor playing, grid
  queries valid; injected `MusicalHits.Report` → correct cue/tier/
  pitch on pool voices (bomb 80 → timpani flourish @1.0; incoming
  cannon → brass note @0.5; SMG chip → pluck note @1.26 pentatonic).
- Console clean (0 errors / 0 warnings).

## Known gaps

See music.md § Known gaps: placeholder timbre (asset-swap path
documented), tip/ram/drill weapons don't sting yet, kill cartoon
animation not built, perf soak in the Rotor Tower stress scene not
yet run (predicted cost ≈ one AudioSource + two 4-element scans).
