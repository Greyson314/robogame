# 145 — Adaptive music stack: FMOD Core track + two-clocks re-anchor

**Date.** 2026-07-17
**Intent.** User installed the FMOD Unity integration (2.03.14) and
MPTK. Priority ask: solve the two-clocks problem (conductor grid vs
FMOD's DSP clock), then land the rest of the adaptive-music pass.
ADR-0007 written and accepted.

## What landed

- **Two-clocks bridge.** `MusicClock` (Core, pure): offset estimator
  between FMOD's DSP clock and `AudioSettings.dspTime` — warmup mean
  over 8 samples, then EMA with per-sample correction clamped to 5 ms
  so an outlier can't yank the grid. `MusicClockTests` (8 tests).
- **Conductor FMOD backend.** `MusicConductor` now prefers FMOD Core:
  three intensity stems from `StreamingAssets/Music/` played as
  channels released by one shared `setDelay` master-clock tick
  (sample-locked start), grid anchor = start tick mapped through
  `MusicClock`. `NextSlotDsp` API unchanged; returns −1 during
  ~8-frame warmup. Unity `PlayScheduled` single-clip path kept as
  fallback (missing stems / FMOD failure / domain reload restart).
  Tweakables volume applies to the FMOD channel group (Music mixer
  bus is bypassed in FMOD mode).
- **Intensity.** `MusicConductor.SetIntensity(0..2)` fades strings
  (0→1) and brass (1→2) layers, fast rise / slow fall;
  `MusicalHitDirector` drives it from decaying combat heat
  (half-life 4 s, kills +80).
- **Stems.** `gen_music_assets.py` renders bed (timpani track),
  low-string 8th ostinato, brass stabs — all sample-exact 19.200 s,
  D-rooted. Scaffolder writes the stem list onto `CombatTrack.asset`.
- **MPTK stinger path.** `MusicMidi` (Core): GM patches per weapon
  instrument, note/flourish/phrase as pentatonic note tables,
  ms-delay scheduling off the same grid. Gated on
  `MPTK_SoundFontLoaded` — **no soundfont is imported yet**, so the
  WAV path still plays; nothing MPTK-side has been heard.
- **Docs.** ADR-0007 accepted; audio.md "No FMOD" + "no procedural
  synthesis" rules amended; music.md updated to v2.

## Verification

- Edit-mode tests 449/449 (MusicClock suite included).
- Live (Bootstrap → EnterArena): `FmodActive=true`, slot queries
  valid with correct lead; grid error 0.0 ms across repeated
  cross-frame queries; injected bomb 80 → timpani flourish @ 1.0,
  incoming cannon 60 → brass flourish @ 0.5 on pool voices (v1
  semantics on the new grid); intensity heat → strings volume followed
  exactly (0.70/0.70), brass correctly silent below 1. Console clean.
- Clock facts measured live: both clocks 48 kHz, constant offset,
  ±21 ms staircase jitter, no drift.

## Notes / follow-ups

- FMOD Studio desktop app is NOT installed (integration only) — bank
  authoring deferred; Core channels deliver layering meanwhile.
- To hear real stinger timbres: import a soundfont via Maestro's
  SoundFont Setup, then profile MPTK synth CPU + timing (INV-7).
- Perf, measured in-arena with music + both layers active (INV-7):
  CPU frame 3.0 ms / main thread 2.4 ms (budget < 8 ms); 1000
  invocations of MusicConductor.Update + MusicalHitDirector.Update =
  0 B allocated, ~1.1 µs per pair incl. reflection overhead. Within
  budget. Rotor-Tower stress soak still pending from 144.
- Editor Game-view Mute button didn't reach FMOD's native output
  (integration's mirror needs a Studio master bank we don't load) —
  conductor now mirrors `EditorUtility.audioMasterMute` onto the
  music channel group, editor-only; verified live.
