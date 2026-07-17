# 0007 — Adaptive music stack: FMOD Core track + MPTK stingers

- **Status.** Accepted (2026-07-17; implemented in session 145)
- **Date.** 2026-07-17

## Context

ADR-0006 shipped the musical damage-feedback layer Unity-native and
upheld audio.md's "No FMOD / Wwise" rule, naming FMOD the single
sanctioned escalation *if authored per-key music assets become the
bottleneck* and MPTK (Maestro MIDI Player Toolkit) the multi-key
upgrade path. The user has since purchased and installed both: the
FMOD Unity integration (2.03.14) and MPTK now live in the project.
Note the FMOD **Studio desktop authoring app is not installed** —
only the runtime — and MPTK ships without a soundfont until one is
imported through its setup window.

Wanting FMOD to own the backing track creates the **two-clocks
problem**: FMOD mixes on its own DSP clock while every stinger is
scheduled on `AudioSettings.dspTime`, and the ADR-0006 beat grid is
pure dsp-time arithmetic. Measured live: the two clocks advance at
the same rate (both 48 kHz) with a constant offset, jittering by one
mixer block (~21 ms) per main-thread read, no drift.

## Decision

Split the stack by shape, keeping `MusicConductor.NextSlotDsp` as the
single grid API in dsp-time:

1. **FMOD Core owns the backing track** (mix-shaped work). The track
   is authored as intensity-layer stems (bed / strings / brass,
   generator-rendered, sample-exact loop length) in
   `StreamingAssets/Music/`, played as FMOD Core channels released by
   one shared `setDelay` clock tick — sample-locked by construction.
   `MusicConductor.SetIntensity(0..2)` fades layers; combat heat in
   `MusicalHitDirector` drives it. **No banks, no Studio events**:
   with no authoring app on the machine, Core channels deliver the
   same layering in code. Migrating to an authored FMOD Studio event
   later only replaces the conductor's backend.
2. **The two-clocks bridge is `MusicClock`**: a pure estimator fed one
   (FMOD-clock, dspTime) pair per frame — warmup mean, then an EMA
   whose per-sample correction is clamped so one wild read cannot
   yank the grid audibly. The grid anchor is the FMOD start tick
   mapped through it. Verified live: grid error 0.0 ms across frames.
3. **MPTK owns note-shaped stingers** (`MusicMidi`): soundfont notes
   on one synth channel per instrument, runs authored as note tables
   in the same D pentatonic. Gated on `MPTK_SoundFontLoaded`; until a
   soundfont is imported the ADR-0006 WAV path plays unchanged. MPTK
   scheduling converts slot-dsp to millisecond delay — accurate to a
   synth buffer, not sample-exact; timbre is worth those few ms.
4. **Fallbacks stay.** Missing stems or FMOD failure → v1 Unity
   `PlayScheduled` single-clip path (grid semantics identical).

## Consequences

- audio.md's "No FMOD / Wwise" and "no procedural audio synthesis"
  rules are amended (see audio.md § What we will NOT do): the FMOD
  *runtime* and sampler-playback of authored material are in; adopting
  FMOD *Studio bank authoring* stays out until the desktop app exists.
- The FMOD path bypasses the Music AudioMixer bus; Tweakables volume
  is applied to the FMOD channel group directly.
- Domain reload cannot preserve native FMOD handles; the conductor
  restarts the track after reload instead of resuming the grid.
- Mid-play device changes shift the clock offset; the clamped
  estimator re-converges at ≤ 5 ms per frame rather than jumping.
