# 204 — -factory-mute: a command-line flag silences every first-party audio path, so a headless player run is quiet (LOG-204)

Landed by the factory 2026-09-19T05:19:02Z. Change CHG-034, branch `chg/034-factory-mute` @ be4c488e3c. Gate: suite PASS, perf N/A, red team PASS — Suite at be4c488e via run-tests.sh --at (foreground, .utmp/factory/gate/CHG-034-suite.txt): EditMode 568/568, PlayMode 164/165 (1 documented skip). Red team sonnet/medium PASS 84k (.utmp/factory/gate/CHG-034-redteam.md): whole-token parse, cached (INV-6), not a Tweakable (INV-1), statics reset; FMOD channels are delayed 0.25 s so the unpause-before-ApplyVolume window is silent; the two playOnAwake MPTK prefabs carry no clip. Arbitration of its note: the new PlayMode test's SetUp writes Audio.Mute=false through Tweakables.SetBool (F-063's path); a no-op on this machine today (the real value is 0, Set returns before Save), made moot by CHG-036 (the persistence seam), built next this shift. Perf N/A: no per-frame path..

## What changed

New `Robogame.Core.CommandLineMute`: a static, lazily cached parse of `Environment.GetCommandLineArgs()` for `-factory-mute` (whole token, case-insensitive). The four places that turn `Tweakables.AudioMute` into a volume of 0 now OR it in: `MusicConductor.ApplyVolume` (FMOD stem group + AudioSource fallback), `GarageMusic.ApplyVolume`, `AudioRouter.ApplyVolumesFromTweakables` (every SFX / UI / stinger call goes through it) and `MusicMidi.PlayStinger` (the fourth site, found by the audio-path census the spec asked for). It is not a Tweakable: the flag never reaches the player's tweakables.json, and without the flag nothing changes. When the flag is seen, one `[CommandLineMute]` line goes to the log. Source: F-062; unblocks LAUNCH-READINESS B3.

## Evidence

- Tests first: commit 398aa6f9 carries only `CommandLineMuteTests` (6 EditMode cases) and `CommandLineMuteAudioTests` (4 PlayMode cases), compile-red because the type did not exist.
- Suite at the gated sha via `run-tests.sh --at`: see the gate record (`.utmp/factory/gate/CHG-034.json`).
- Census: MusicConductor, GarageMusic, AudioRouter (incl. ChassisWindAudio through PlayLoop) and MusicMidi honour the flag; FMOD's own device init (third-party RuntimeManager.cs) is untouched by design, every channel this project creates sits under the muted group.
- Foreground review fix-up (be4c488e): an invalid `TRACE[F-062]` id removed (F-ids are not a trace kind), the proof log line added.

## Revert

`git revert` the merge. No saved data, scene or format is touched.
