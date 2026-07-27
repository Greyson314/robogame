# 150 — Gaslamp Waltz: blown-speaker glitch pass

**Date.** 2026-07-27
**Intent.** User liked the 149 waltz but wants a "blow-out-speaker,
slightly electronified, slightly higher BPM, glitched-out inventor"
cut — plus more sauce on the melody instrument (the clarinet).

## Approach

The bones were approved, so nothing about the harmony (`PROG`) or the
tunes (`MELODY_*`) changed. `gen_garage_theme.py` now emits **two cuts
of the same piece** from one source of truth, selected by a `STYLES`
table:

| Cut | File | BPM | Loop | Tracks |
| --- | --- | --- | --- | --- |
| `fog` | `garage-gaslamp-waltz.mid` | 88 | 65.5 s | 8 |
| `glitch` | `garage-gaslamp-glitch.mid` | 104 | 55.4 s | 12 |

Keeping both means the approved version isn't lost and the two are
A/B-able on F7.

## What the glitch pass adds

- **Tempo** 88 → 104 BPM ("slightly higher" — +18%, enough to shove
  the waltz forward without turning it into a dance).
- **Distortion Guitar (GM 30)** — power chords (root + fifth) slammed
  on downbeats at velocity 104, rising to **118 through section B**,
  deliberately near clipping. That's the blown speaker.
- **Synth Bass 2 (GM 39)** doubling the contrabass, with an octave
  stab on the "and" of 3 for forward drive.
- **Electro kit** (GM percussion, ch 9): kick / clap / 8th hats, plus
  **32nd-note buffer-stutter retriggers** on the last bar of every
  4-bar phrase — the ramping hat/snare burst is what reads as
  "glitch". Open hat accents at the two phrase seams.
- **Saw lead (GM 81)** shadowing the clarinet an octave up at low
  velocity, so it reads as an artefact rather than a second melody,
  with **pitch-bend dips** on four bars for tape/wobble.
- **Melody sauce** — new `ornament()` decorates every melodic line:
  grace notes leaning in a step below, mordents (main/upper/main) on
  sustained notes, and trills on anything held ≥ 2.5 beats. Seeded
  per-line so regeneration is deterministic.
- Bar 27's unresolved Neapolitan now gets a choked distortion triplet.

## Verification

- Structural self-check of both files: fog 8 tracks / 517 on / 517
  off; glitch 12 tracks / **1092 on / 1092 off**; zero hanging notes,
  all chunk lengths exact in both.
- Live (bridge): glitch cut renders through GeneralUser GS, voices
  climbing 12 → 22 → 50 → peak **53**, cumulative notes past 1092 so
  the loop restarts. No exceptions.
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed.

## Notes / follow-ups

- **Unjudged by ear** — the user asked for a vibe, and vibe is not
  something the voice counters can confirm.
- Peak ~53 simultaneous voices (up from 43). Still inside MPTK's
  polyphony budget, but this is now the heaviest synth load in the
  game; profile before shipping if the garage gains more audio (INV-7).
- Knobs if it needs tuning: `STYLES[...]["bpm"]`, the distortion
  velocities and bar filter in `build_grit`, stutter density in the
  `bar % 4 == 3` block, and the ornament probabilities in `ornament()`.
