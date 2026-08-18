# 165 — MPTK batch-mode gate (Garage_Idle_Baseline unbroken)

`PerfBaselineHarness.Garage_Idle_Baseline` began failing deterministically
(3 consecutive rig runs, incl. clean HEAD) on an unhandled MPTK error:
`Channel:8 Sample:Tuba-D1 Generator:[9 GEN_FILTERQ] Object reference not
set`. This closes the "MPTK flake elevated" thread from session 163 — it
was never random.

## Diagnosis

- **Who voices Tuba in garage idle:** [GarageMusic](../../Assets/_Project/Scripts/Core/GarageMusic.cs)
  renders the garage theme MIDI live through the GM soundfont; Tuba is one
  of its patches. The perf test idles in the garage past the point the
  theme starts voicing.
- **Soundfont did NOT regress:** `StreamingAssets/SoundFont/GeneralUser-GS.sf2`
  is LFS-tracked, byte-identical in main and the test-rig worktree,
  unchanged since Jul 27. The editor's "No global SoundFont ready" banner
  is Maestro nagging about its own global config, which this project
  deliberately never uses (per-synth loads, LOG-148) — by-design noise.
- **Root cause:** the rig's batch Unity runs with audio disabled; MPTK
  still schedules voices against sample data that never gets built and
  throws per note. "Flake" was pure timing — whether the ~seconds-long
  async bank load finished inside the 600-frame capture window. Warm
  caches made it reliable-fast today, so reliably red.

## Fix

One gate at the single choke point both music players share:
[MusicSoundFont.AttachTo](../../Assets/_Project/Scripts/Core/MusicSoundFont.cs)
returns false under `Application.isBatchMode` (logged once). GarageMusic
and MusicMidi are documented no-ops without a ready bank, callers keep
their WAV fallbacks, players hear no difference. Preferred over
`LogAssert.Expect` in the harness — that would have muted a real error
class instead of removing it.

## Verification

Headless rig PlayMode: **135/136 passed, 0 failed** (the 1 skip is the
pre-existing annotated SpawnBot ignore). Live-editor console clean.
