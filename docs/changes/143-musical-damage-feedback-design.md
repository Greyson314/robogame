# 143 — Musical damage feedback: research + design (no code)

**Date.** 2026-07-14
**Intent.** Design session for the "instrument stingers on hits" idea
from the inventor-aesthetic doc: research how Sea of Thieves does it,
survey tooling, and draft the system as an ADR.

## What happened

- Web research (subagent): SoT confirmed on **Wwise**; its stingers
  quantize to next beat/bar/custom-cue and stay in key by authoring
  variants per music section. Middleware (FMOD/Wwise) is what big
  studios use; both have free indie tiers covering this project.
  Unity-native package find: **Maestro MPTK** (runtime SoundFont
  synth, maintained, ~$65 Pro, free tier) — can play arbitrary
  instrument notes in a chosen key at runtime. Verified
  `AudioSettings.dspTime` + `PlayScheduled` as the sample-accurate
  scheduling pattern (never mix with `Time.time`; schedule ≥1 audio
  buffer ahead).
- Codebase seams identified: `DamageAttribution.Report` in
  ProjectileWorld sees every hit with attacker + victim identity —
  single hook point, no per-weapon call-site edits. `ProjectileSpec`
  already carries owner + audio hints as precedent.
- Drafted **ADR-0006** (Proposed): `MusicConductor` (dspTime beat
  grid, per-track key/BPM metadata SO) + `MusicalHitDirector`
  (hit accumulation per beat window → one intensity-tiered stinger on
  the next off-beat slot, notes via MPTK). Client-side cosmetic only.
  One key per match; teams differ by register, not key. FMOD named
  as the only sanctioned escalation; audio.md's no-middleware rule
  otherwise stands.

## Open questions (for user)

- Approve / amend ADR-0006 before any implementation.
- Off-beat 8th as the default stinger slot — taste call, prototype
  will tell.
- Cartoon kill-animations (the "wahoo!" spin) noted as a future hook
  on the chassis-kill tier; not designed yet.
- ADR numbering collision: two files share 0005
  (block-tiers / concoction-scope) — needs a renumber.

## Known unknowns

- MPTK synth quality + audio-thread cost vs invariants #6/#7 —
  prototype gate.
- SoundFont sourcing (CC-licensed orchestral, e.g. VSCO2-derived).
