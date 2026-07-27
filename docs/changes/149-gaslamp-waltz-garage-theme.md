# 149 — Gaslamp Waltz: an original Victorian garage theme

**Date.** 2026-07-27
**Intent.** User wants the garage scored Victorian, referencing
Marleybone (Wizard101). Original composition, "foggy/mysterious" lean
rather than the jauntier waltz side.

## Reference, not reproduction

Marleybone's theme (Nelson Everhart) is characterised by a clarinet
melody over fluttering harp and chiming bells, harpsichord keeping
time, strings crescendoing in, the melody handed to horns, a waltz
metre, minor key, and one modal interchange that brightens before
resolving back to mystery. **Only the palette and form are borrowed;
all melodic material is original**, so there's no copyright exposure.
Sources: [Ravenwood Academy analysis](https://ravenwoodacademy.com/mbmusic/),
[Wiki101](https://101universe.fandom.com/wiki/Marleybone_Main_Theme).

## What landed

- **`artgen/gen_garage_theme.py`** — composes
  `StreamingAssets/Midi/garage-gaslamp-waltz.mid`. Pure stdlib: it
  includes a ~50-line Standard MIDI File writer (no `mido` installed,
  and `artgen` stays dependency-free beyond numpy, which this script
  doesn't even need).
- **The piece.** D minor, 3/4, 88 BPM, 32 bars, 65.5 s loop. D minor
  both suits the fog and matches the project's global root.
  - **A (1–8)** — fog. Harpsichord oom-pah-pah, contrabass floor, low
    clarinet in its chalumeau register. Harp deliberately silent; the
    space is the point.
  - **A′ (9–16)** — harp arpeggios flutter in; Neapolitan **E♭** is
    sung outright by the clarinet, doubled by a distant bell.
  - **B (17–24)** — modal interchange to the relative major, strings
    swell, **horn** takes the melody with clarinet shadowing an octave
    below. The one bright moment.
  - **A″ (25–32)** — sinks back, Neapolitan returns unresolved, ends
    on a bare A that hands back to the loop's opening Dm.
  - Instruments (GM): clarinet 71, horn 60, harp 46, harpsichord 6,
    tubular bells 14, strings 48, contrabass 43. Reverb sends per part
    to sell the damp street.
- `GarageMusic.StreamingRelativePath` → the new waltz. The seven
  public-domain pieces stay as F7 audition candidates; folder README
  updated to mark which file is ours vs downloaded.

## Verification

- Structural self-check of the emitted file: format 1, 8 tracks,
  517 note-ons / 517 note-offs, **zero hanging notes**, every track
  ending before the bar-32 seam, all chunk lengths exact.
- Live (bridge, play mode): renders through GeneralUser GS with the
  intended arc — 11 voices in the sparse opening, 26 once the harp
  enters, 43 through the string/horn section. Passed 517 cumulative
  notes, proving `MPTK_MidiAutoRestart` loops it. Console clean.
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed.

## Notes / follow-ups

- **Unjudged by ear.** Voices fire and the structure is right; whether
  it *feels* like Marleybone is the user's call. Everything is a
  parameter edit away in the generator — harmony in `PROG`, melodies in
  the `MELODY_*` tables, tempo/velocities at the top.
- Peak ~43 simultaneous voices in section B. Comfortably inside MPTK's
  polyphony budget but the busiest thing the synth has been asked to
  do; profile if the garage ever gets heavier (INV-7).
- Starting the theme outside the garage logs "no audio listeners"
  (MainMenu has none). Harmless here — only reachable by forcing
  playback from a non-garage scene, as the live test did.
