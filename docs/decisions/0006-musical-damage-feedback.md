# 0006 — Musical damage feedback: Unity-native conductor + runtime MIDI stingers

- **Status.** Accepted (2026-07-14; implemented in session 144 — see
  amendment in § Decision)
- **Date.** 2026-07-14

## Context

The inventor aesthetic commits to a "deeply musical vibe" — firing a
mortar volley should read as a piano flourish on shot + landing
(docs/research/inventor-aesthetic.md), and combat feel is slapstick
over realism (game-design-pillars). The concrete design: a rolling
orchestral backing track during combat; landing a hit on an enemy
chassis triggers a bright stinger in the *attacker's weapon's
instrument*, quantized to the backing track's beat grid (target: the
off-beat), always consonant with the track's key. Normal thuds/booms
stay — the musical layer sits on top.

Prior art: Sea of Thieves does exactly this on **Wwise** (Rare adopted
it studio-wide; music director Robin Beanland credits its interactive
music tooling). Wwise stingers schedule at "next beat / next bar /
next custom cue," and key-consonance is authorial — stinger variants
are registered per music section, each written in that section's key.
Hi-Fi Rush broadcasts one conductor's beat to all subscribers; Rez
delays shot SFX to the next quantize boundary. The industry-standard
mechanism is: *one clock, events quantized onto it, assets authored
in key*. Nothing about it requires middleware — middleware buys the
authoring workflow, not the capability.

Tension: docs/subsystems/audio.md rules out FMOD/Wwise. That rule was
written when audio meant one-shot SFX; this feature is the first real
test of it.

## Decision

Build the musical layer Unity-native, in two pieces:

1. **`MusicConductor`** — owns the combat backing track. Starts it
   with `AudioSource.PlayScheduled` and derives the beat grid from
   stored start-dspTime + beat-duration math (`AudioSettings.dspTime`
   only; never `Time.time`). Exposes `NextSlot(subdivision, offset)`
   queries — "next off-beat 8th" is the default stinger slot. Per-track
   metadata (BPM, key, scale/pitch set, section cues) lives in a
   ScriptableObject alongside the clip.

2. **`MusicalHitDirector`** — subscribes to damage attribution (the
   `DamageAttribution.Report(owner, target, damage)` chokepoint in
   ProjectileWorld already sees every hit with attacker + victim
   identity; expose an event there). Accumulates hits per
   (attacker, instrument) within a beat window, then schedules ONE
   stinger at the next quantize slot, with intensity tiers:
   chip damage → single note; block destroyed → short flourish;
   chassis kill → full phrase (later: + cartoon kill-animation hook).

Stinger notes are played at runtime via **Maestro MIDI Player Tool Kit
(MPTK)** — a maintained (v2.19, 2026-03) SoundFont synth for Unity —
so "trombone, D minor, off-beat" is data, not an authored wav per
weapon × key × intensity. Prototype on the free tier; Pro is ~$65.
Each `WeaponDefinition` gains an `InstrumentId`; the conductor's
track metadata supplies the legal pitch set.

**Amendment (as implemented, session 144).** During implementation we
found the codebase had already shipped the note-selection layer:
`MusicalSfx` pitch-shifts root-recorded clips along a global major
pentatonic (within one octave, explicitly sanctioned by audio.md).
v1 therefore rides that pattern instead of importing MPTK: one
offline-generated root-note clip per instrument × tier, pitch chosen
by the director at play time. This is the "pre-rendered wavs"
alternative upgraded by the existing pitch-shift machinery — the
combinatorics objection collapses to 12 clips total. MPTK (whose
import needs the user's Asset Store session anyway) remains the
upgrade path if multi-key/multi-mode flexibility is ever wanted.
Instrument identity derives from `ProjectileKind` rather than a new
`WeaponDefinition` field — kinds and weapons are 1:1 today.

Scope guards: the layer is **client-side cosmetic only** — quantize
delays presentation, never damage state (invariant #3-safe for MP).
One key/mode per match, shared by both teams; teams differentiate by
instrument register (your hits bright/high, hits on you darker/lower),
not by key — two simultaneous keys is reliable dissonance, not
reliable comedy. Max one stinger per instrument per half-bar.

## Alternatives considered

- **Wwise.** The technically best fit (MIDI stingers + per-cue sync,
  and the SoT-proven path) — but a middleware integration is months
  of work and violates the audio.md rule for a feature a ~200-line
  conductor covers. Revisit only if the music system outgrows one
  backing track + stingers (e.g., full vertical re-orchestration).
- **FMOD.** The sanctioned escalation if MPTK's SoundFont timbre
  disappoints: free at our scale (revenue < $200k/yr, budget < $500k),
  best-in-class Unity ergonomics, stingers become authored recordings
  quantized by the middleware with a key-index parameter. The
  conductor/director gameplay seams port unchanged.
- **Koreographer ($195).** Beat-event mapping only; solves the part
  we can write ourselves and nothing about pitch/key.
- **Pre-rendered stinger wavs, no MIDI.** Viable fallback through the
  existing AudioRouter pool, but every weapon × key × intensity cell
  is an authored asset; combinatorics get ugly the moment a second
  backing track lands.
- **Pitch-shifting one sample per instrument.** Artifacts past a few
  semitones; kills the "bright orchestral hit" quality bar.

## Consequences

- audio.md's "No FMOD / Wwise" rule survives, with FMOD named as the
  single sanctioned escalation path; audio.md's deferred "Music
  system" item resolves to a new `docs/subsystems/music.md` when
  implementation starts.
- Commits us to sourcing a decent orchestral SoundFont (CC-licensed,
  e.g. VSCO2-Community-derived) and to backing tracks authored at a
  known fixed BPM and key.
- MPTK's synth runs on the audio thread: profile before trusting
  (invariant #7), and hold the zero-alloc steady-state rule
  (invariant #6 — preloaded banks, pooled note events). If it can't
  hold budget, fall back to pre-rendered wavs on the same conductor.
- New dependency on a paid third-party asset (MPTK Pro) once past
  prototype.

## Notes

Research session log: docs/changes/143-musical-damage-feedback-design.md.
Key sources: Wwise stinger docs; Rare/Beanland interviews (Microsoft
2019-06, gamemusic.net); MPTK asset store page (v2.19); Unity
PlayScheduled docs; Jakob Schmid's *140* music-sync talk (NGC 2014).
