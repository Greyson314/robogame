# 152 — Blast retimbre: tuba + timpani instead of distortion guitar

**Date.** 2026-07-27
**Intent.** User: the crunchy synth hits are "a little too crunchy" —
wants something closer to a blown-out tuba or timpani.

## Change

The glitch cut's accent stabs were a GM Distortion Guitar (30) at
velocity 104–118. Guitar fuzz was the one element fighting the
Victorian/orchestral palette everything else lives in. Replaced with:

- **Tuba (GM 58)**, root + fifth, dropped an octave from where the
  guitar sat so it plays in the register where a tuba actually blats
  rather than buzzes. Velocity 106, and **120 through section B** —
  the blown-out quality now comes from pushing a brass patch to its
  limit instead of from a distortion algorithm.
- **Timpani (GM 47)** on channel 11, doubling the octave above for the
  thump the guitar never had.
- Bar 27's choked Neapolitan triplet is now tuba + timpani too.

Rhythm and placement are unchanged — downbeat of every other bar, and
every bar through section B — so only the timbre moved.

## Verification

- Program-change audit of the emitted file: ch8 → 58 Tuba, ch11 → 47
  Timpani, **no program 30 anywhere**. 13 tracks, 1144 note-ons /
  1144 note-offs, zero hanging notes, chunk lengths exact.
- Live: renders through GeneralUser GS, voices 15 → 27 → 40 → 50,
  console clean, loop intact.
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed.
- Docs de-staled: folder README and music.md both said "distortion".

## Notes / follow-ups

- Still unjudged by ear. If the tuba reads as too polite, the knobs
  are the two velocities in `build_grit` (106 / 120) and the register
  (`root = CHORDS[name]["bass"]` — add 12 to lift it back up).
  Synth Brass 2 (63) or Orchestra Hit (55) are the next stops if a
  little synthetic edge turns out to be wanted after all.
