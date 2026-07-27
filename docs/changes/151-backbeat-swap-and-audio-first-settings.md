# 151 — Backbeat swap + Audio moved to the top of Settings

**Date.** 2026-07-27
**Intent.** User likes the glitch cut but the hand clap has to go.
Also asked for a music volume slider in Settings.

## Backbeat swap

The GM hand clap (39) on beat 3 read as dance-floor, not workshop.
Replaced with **side stick (37) layered under low wood block (77)** —
a tight mechanical click with a hollow wooden knock, which pairs with
the harpsichord's clockwork role. Both are named constants
(`BACKBEAT` / `BACKBEAT_LOW`) at the top of the kit block so retasting
is a one-line edit; near neighbours worth trying are 56 cowbell, 76 hi
wood block, 40 electric snare, 67/68 agogo.

Verified by parsing the drum channel of the emitted file: note 39 is
gone; 37 × 32 and 77 × 32 present (one per bar), kick 48, hats 224,
snare 32 (stutter bursts), open hat 2.

## The music slider already existed

`Audio.MusicVolume` has been a registered Tweakable all along, and
`SettingsHud.BuildTweakRows` auto-builds a slider for every non-bool
spec — so Master / SFX / **Music** / UI / Mute were already in the
panel. Live probe of `SettingsHud._allRows` confirmed the Music Volume
row exists and is enabled.

The real problem was **discoverability**: 56 rows, with Audio the 4th
group, stranded below ~40 physics knobs. Since registration order *is*
display order in this codebase, the fix was to move the Audio block to
the top of `Tweakables` registration. Settings now opens on
Master / SFX / Music / UI / Mute (verified: they are rows 1–5).

No new UI was written — adding a second music slider would have been
a duplicate control fighting the same Tweakable.

## Also confirmed

The slider genuinely drives the new MIDI music, end to end:
`Audio.MusicVolume` 0.8 → 0.2 moved `MPTK_Volume` 0.0337 → 0.0084
(exactly ¼), and restored. The small absolute number is not a bug —
**`Audio.MasterVolume` is currently 0.10** on this machine, and
0.42 (theme gain) × 0.8 × 0.10 = 0.0337. Worth knowing before anyone
files "the music is too quiet".

## Verification

- Emitted MIDI re-validated structurally; drum-channel histogram as
  above. Live: glitch cut plays, voices 11 → 22 → 50, console clean.
- Settings row order probed live post-recompile: Audio rows 1–5.
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed.

## Notes / follow-ups

- Group order is now Audio / Water / Rope / Stress / QoL / dev. If
  more player-facing settings arrive (resolution, sensitivity), they
  should join the top block rather than being appended.
