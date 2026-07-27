# 146 — Tasteful music layers: six-stem stack, authored fade windows

**Date.** 2026-07-27
**Intent.** User asked for more (tasteful) layers on the adaptive
combat music, delegating the music-theory direction. Three new stems +
per-stem fade windows replacing the index-formula layer gains.

## Theory constraints that shaped the design

- Existing stack lived entirely below ~450 Hz — new layers claim the
  empty top registers instead of stacking the same octave.
- The player's stingers ARE the melody; new layers are ostinato /
  texture only, never tunes in the stinger register.
- No chord progressions: D pedal is load-bearing (G would put a
  semitone against the F# in every pentatonic stinger). Color comes
  from register and rhythmic subdivision instead.

## What landed

- **Shimmer stem (inverse layer, fade 1→0).** Music-box bell arpeggio
  in D5+, plays when the arena is CALM and ducks out as combat heats
  up — its disappearance announces the fight. New `bell()` voice
  (inharmonic partials) in the generator.
- **Clockwork-lute stem (fade 2→3).** Karplus-Strong 16th-note gallop
  in D4–D5: escalates rhythmic subdivision (bed/strings own 8ths).
  Odd bars climb E–F#–A into the downbeat; bar 7 walks down to
  resolve at the loop seam.
- **War-percussion stem (fade 2.5→3).** Tight snare 16ths (high-tilted
  noise, no low body — sits above the timpani bed), military accents,
  downbeat flams, doubled-stroke rolls under the timpani-roll bars.
  Enters last so the top of the range has two gears.
- **Per-stem fade windows.** `MusicTrackDefinition.Stem {File,
  FadeStart, FadeEnd}` replaces `StemFiles`; `MusicMath.LayerGain`
  (pure) maps intensity → gain: equal endpoints = always-on bed,
  ascending = riser, descending = calm layer. Conductor clamps
  `SetIntensity` to the track's authored `MaxIntensity` (now 3);
  `MusicalHitDirector` feeds `heat / 45` (same per-step scale as
  before — `HeatAtFullIntensity 90/2` → `HeatPerIntensityStep 45`).
  A mid-brawl kill (~60 heat + 80) tops out the range.
- **Docs.** music.md status → v2.1, stem stack + fade semantics.

## Verification

- Headless suites: EditMode 453/454 (0 failed; the 1 inconclusive is
  the known session-95 carry-over), PlayMode 120/121 (0 failed; known
  `[Ignore]` SpawnBot). New `MusicMathTests` cover LayerGain riser /
  calm / bed / clamp. Batch compile clean.
- Live listen + scaffolder re-run: PENDING — Unity MCP bridge was down
  during the session; `CombatTrack.asset` still carries the old
  `StemFiles` field until **Robogame → Scaffold → Music → Build Combat
  Music** runs (conductor falls back to the single-clip path until
  then, by design).

## Notes / follow-ups

- Mix balance (stem normalize levels 0.35/0.5/0.45) is a first guess —
  judge by ear in-arena and retune in `gen_music_assets.py`.
- Rotor-Tower stress soak from 144 still pending.
- Soundfont import for MPTK stingers still pending (145 follow-up).
