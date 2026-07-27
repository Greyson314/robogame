# 153 — Orchestral kill hit + combat music re-keyed to the garage's D minor

**Date.** 2026-07-27
**Intent.** User: orchestral hit when the player gets a kill, and bring
the in-game (arena) music thematically closer to the garage music.

## Orchestral hit on kill

- **MPTK path** (`MusicMidi`): fifth synth channel carrying GM 55
  **Orchestra Hit** — the patch LOG-152 already shortlisted for the
  garage blasts, so kill and garage share a timbre lineage. On a
  player kill the stab lands on the phrase's quantised downbeat
  (D4 + D3, velocity 120/96), with the existing 16th run climbing out
  of it into the held chord. Fixed pitch regardless of weapon: one
  recognisable kill sound, not four. Player deaths (incoming phrase)
  stay hit-less — the dark timpani mirror is untouched.
- **WAV fallback**: all four `stinger_*_phrase.wav` now open with a
  baked orchestral stab (`orchhit()` — unison D-minor brass + high
  strings over a timpani thump). The fallback serves both directions,
  so a player death through it gets the stab at half pitch; accepted
  as consistent with the register-mirror design.

## Combat music → garage DNA

The garage is the Gaslamp Waltz: D minor, Dm–F–Gm–A7 harmony,
clarinet/harp/bells. Combat was D **major** pentatonic on a static D.
Changes, all in `gen_music_assets.py` + the two C# scale tables:

- **Key**: global scale is now **D minor pentatonic** (D F G A C) —
  `MusicalSfx.s_scale`, `MusicMidi.s_pentSemis`, generator `PENT`.
  Weapon-fire arpeggios and all stinger tiers follow automatically;
  MIDI flourish/phrase runs re-spelled (+4→+3, +9→+10).
- **Harmony**: strings + brass stems walk `SKELETON`
  = D×4, F×2, G, A — the waltz's A section compressed to one 8-bar
  loop, over the unchanged D-pedal drone/timpani bed. Every skeleton
  root is a pentatonic degree, so stingers stay consonant. The A bar
  avoids its fifth (E rubs the pentatonic F): strings answer on the
  octave, brass doubles the low octave.
- **Motif**: the lute gallop now spells the waltz's opening clarinet
  phrase across each two-bar pair — A rising through D to F, then
  F–E–D sinking back (E only ever as a passing 16th pluck).
- **Bells**: tubular-bell voice (`bell()`, inharmonic partials with a
  minor-third tierce) chimes D at the loop head and F at the bar-5
  brightening, low in the always-on bed — the waltz's clocktower.

Filenames, lengths (19.200 s exact), BPM and stem list unchanged — no
asset rewiring, `CombatTrack.asset` untouched.

## Verification

- FFT self-check of emitted stems: strings bars 0/4/6/7 peak at
  D2/F2/G/A as authored; lute's first three onsets are A/D/F pitch
  classes; bell partials (D4 prime + F5 tierce) present in the bed.
- Kill stab: phrase-head RMS up 51 % on the quietest phrase (pluck) vs
  the previous LFS object, head low-band (timpani) energy ×224; the
  other three phrases show head ≫ mid directly.
- Live (bridge, play mode): forced recompile clean; kill phrase via
  `MusicMidi.PlayStinger(…Phrase, outgoing)` fired **8 voices** — 6
  phrase notes + 2 orchestra-hit notes. (`MPTK_Channels[i].PresetNum`
  reads 0 on every channel including the long-verified 0–3, so it is
  not a valid patch probe; the PatchChange path is proven by ear from
  148–152.)
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed (the usual
  1 inconclusive + 1 ignore carry-overs).

## Notes / follow-ups

- **Unjudged by ear** — key change, skeleton, motif, bells and stab
  velocities are all first-pass. Knobs: `SKELETON` /`RISE`/`FALL`
  tables and bell gains in the generator; stab velocities (120/96) in
  `MusicMidi`.
- The E passing tone in the lute FALL cycle is deliberately outside
  the pentatonic; if it ever reads as a rub against sustained stinger
  F's, swap `1.12246` for `PENT[1]` (F) and lose the melodic contour.
- Weapon-fire `ArpeggioUp`/`ScaleRandom` SFX are now minor game-wide
  (garage included) — intended, but worth an ear pass.
