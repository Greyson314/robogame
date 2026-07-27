# 147 — Taiko percussion: kuchi shōga stems, shimmer removed

**Date.** 2026-07-27
**Intent.** User (a percussionist) reviewed 146: shimmer calm layer is
the wrong vibe — removed. Taiko/war drums should be the core feeling
of battle, with real findable patterns, ghost notes, and syncopation
as the foundation for the other elements.

## What landed

- **Shimmer removed.** Generator block, stem, scaffolder entry gone.
  Inverse-fade support stays in `MusicMath.LayerGain` (pure, tested,
  three lines) in case a calm layer returns elsewhere.
- **Kuchi shōga sequencer** in `gen_music_assets.py`: `STROKES` table
  + `kuchi()` — one token per 8th slot, `don`/`DON` center,
  `doko` = two 16ths, `ka`/`kara` rim, `tsu`/`tsuku` ghost strokes,
  `su` rest, uppercase = accent. Real patterns transcribe verbatim.
- **Taiko kit voices**: `odaiko()` (dark 84→44 Hz membrane sweep +
  skin slap, deliberately unpitched — timpani own the tuned lows),
  `chudaiko()` (mid punch), `shime()` (tight rim crack). Per-hit
  seeds so ghosts don't machine-gun.
- **Three stems** replacing the 146 war-snare, paired one-pitched +
  one-taiko per intensity unit:
  - `stem_taiko_ji` (0→1, with strings): horsebeat ji on shime, tsu
    ghosts between accents, kara doubles into phrase bars.
  - `stem_taiko_chu` (1→2, with brass): matsuri ji core, ghost-note
    variant on odd bars, accents displaced off-beat in bars 2/6,
    horsebeat drive through phrase bars.
  - `stem_taiko_odaiko` (2→3, with lute): ma + sparse booms, oroshi
    accelerating roll across bars 3/7 arriving on the next downbeat.
- Pattern sources: horsebeat "DON doko" (San Jose Taiko Conservatory),
  matsuri ji "DON doko don DON" (Wikipedia: Jiuchi), oroshi (Taiko
  Colorado glossary), kuchi shōga vocabulary (Wikipedia).
- **Docs.** music.md → v2.2.

## Verification

- Headless: EditMode 453/454 (0 failed, known inconclusive), PlayMode
  120/121 (0 failed, known ignore). Compile clean.
- Live (bridge, play mode): asset carries all seven stems + windows;
  `FmodActive=true`, 7 channels; rest = bed only; settled max = all
  seven at 1.00; mid-fall at intensity 1.223 → chū 0.22 + brass 0.22
  together, ji/strings full, ō-daiko/lute silent. No exceptions.

## Round 2 — taiko into the bed

User listened: shimmer's absence audible (so the build was fine), but
drums "sound the same" — because round 1 left the bed untouched and
gated all taiko behind intensity; at low heat you heard the old track.
Restructured for overtness:

- **Bed now carries the core taiko groove always-on** (chū matsuri +
  shime horsebeat ji with ghosts); timpani slimmed to tuned anchors
  (downbeat, beat-3 fifth, phrase roll); old faint rim ticks removed.
  The Unity-fallback single track inherits all of it (same buffer).
- Stems re-cut: **uchi** off-beat chū answers (0→1), **ō-daiko** ma +
  booms + oroshi moved DOWN to 1→2 (mid-fight payoff, 0.85 mix),
  **frenzy** wall-to-wall horsebeat + kara 16ths (2→3). ji/chu stems
  deleted (content absorbed into bed/uchi/frenzy).
- Verified: asset carries the new seven-stem lineup, compile clean,
  FmodActive with 7 channels, rest = bed only.

## Round 3 — depth + SMG hybrid

Percussionist verdict on round 2: instrumentation too lightweight —
wants deep thuds and booming, rolling taikos; SMG plucks out of place
with both rhythm and instrumentation.

- **Voices deepened**: o-daiko 88→42 Hz sweep, 1.8 s boomy tail, slap
  restrained; chu-daiko rebuilt as a 95 Hz barrel with shell modes and
  a 0.9 s tail (8th-spacing tails overlap into a roll); shime leaned
  down. Oroshi hits lengthened to 0.55 s so rolls boom.
- **Bed gets the deep line**: o-daiko pulse on the matsuri accents in
  every bar + rolling oroshi through every phrase bar (replaces the
  timpani roll; timpani now downbeat + fifth only). Groove mix up to
  0.7, timpani anchors down to 0.4.
- **SMG hybrid** (user-picked): note tier = shime rim shot (pitch-walk
  varies the tick), flourish = pizzicato-strings run (new `pizz`
  voice, detuned KS pair), phrase = pizz run landing on a chu-daiko
  don with a grace rim tick. Same filenames — cue rows untouched, no
  C# changes.
- Known divergence: `MusicMidi` GM patch for SMG is still pizzicato —
  when a soundfont lands, the note tier reverts to melodic pizz on
  the MPTK path; reconcile at soundfont import.
- Verified: regen clean, reimport clean, FmodActive with the new
  stems, console clean.

## Round 4 — drums reverted, SMG kept

Ear verdict on round 3: the taiko rework made the underlying
percussion worse. Reverted ALL backing-percussion changes to the
pre-taiko state — original timpani bed (full war pattern + rim
ticks), stem lineup bed / strings 0→1 / brass 1→2 / lute 2→3 /
war-snare 2.5→3 — verified byte-identical to the 146 LFS oids
(lesson: WAVs are LFS pointers; compare `oid sha256` not blob
hashes). Kept: SMG hybrid stingers (shime note / pizz flourish /
pizz+chū phrase) and the kuchi shōga sequencer + taiko voices as an
unused generator toolkit for future pattern work.

## Notes / follow-ups

- Mix levels (ji 0.3 / chū 0.55 / ō-daiko 0.7) and all timbres are
  unheard by the user — judge by ear, retune in the generator.
- Ji stem doubles the bed's rim-tick role; if the top feels cluttered
  at low intensity, drop the bed's faint off-beat ticks next pass.
- Kuchi shōga makes new patterns cheap: transcribe, paste, regen.
  Candidates when wanted: swing/dongo ji, Yatai-bayashi-style drive.
- Rotor-Tower stress soak (144) and MPTK soundfont (145) still open.
