# 162 — Wave-1 FX/audio pass

Closes the invariant #8 debt recorded in
[155-wave1-prototype-suite.md](155-wave1-prototype-suite.md) § Known gaps:
gyro/pogo had no audio, spike/wedge armor had neither bespoke VFX nor
audio, and `AudioCue.WeaponOverheat` had no clip.

## Shipped

Five new library rows (AudioCueWizard table → auto-rebuilt asset, now
53 wired / 0 missing). Four new cues appended to `AudioCue` (append-only
zone honoured):

- **WeaponOverheat** (clip only — cue + call site existed): impact-wrench
  compressed-air medium burst, the steam-off-hot-metal pressure release.
- **GyroLoop** + `GyroBlock` loop lifecycle (HoverBladeBlock pattern:
  IsValid re-check on enable, Stop on disable/destroy). Electric
  press-machine hum, idles at 0.10 base volume and swells to the 0.30
  library ceiling with |steer| via `SetBaseVolume` in `Tick`. Skipped
  when `_replayBody` is set — CSP replay re-runs many ticks per frame
  and audio is presentation.
- **PogoBounce**: real cartoon spring-bounce clip replaces the
  SpringLaunch 8-bit placeholder at the bounce site. Only the
  arbiter-winning foot plays it. Plus VFX: `SpringBurst` dust kick at
  `hit.point` aimed along the launch axis (closest authored kind).
- **ArmorSpikeHit**: metal-crowbar jab + 1.5× `HitSpark` when the spike
  ring-0 bonus procs in `MomentumImpactHandler` — layered over the
  existing ChassisRam/RamSpark pair so being spiked reads nastier than
  a plain ram. Fires on the victim's handler, localised to the hurt
  chassis.
- **ArmorDeflect**: ricochet ping (0.15 pitch jitter) when wedge
  deflection sheds meaningful damage. `ProjectileWorld.PlayDeflectFeedback`
  gates at deflect < 0.9 and covers both ApplyDirect and
  ApplyRingSplashOnHit; the ram-path deflection stays silent by design
  (RamSpark + ChassisRam already cover it).

No new physics objects, no per-frame allocations (loop voice is
one-alloc-on-start; one-shots ride the existing pooled router).

## Verification

- Live editor via MCP bridge (raw HTTP :8080): all 16 assemblies
  compiled, 0 errors; wizard rebuild logged 53/0.
- Play-mode probe: all five cues resolve to their intended clips and
  fire; a spawned `GyroBlock` creates a playing `Loop_GyroLoop` voice
  at idle volume; zero exceptions, zero missing-cue logs.
- Headless rig + qa-verifier: see verdicts at session end.

## Follow-up (same session): unwired-cue audit

The pre-existing gap flagged above was picked up immediately: 13 cues
fired from gameplay code had no wizard rows and silently no-oped
(HoverBladeLoop + ContactLost, FlipActivate, the four RepairPad cues,
the three Scrap cues, LabSave, LowHealthAlert, RoundClockTick). Audit
confirmed none are documented intentional omissions — those are only
ThrusterIgnite/Shutdown and ReloadStart. All 13 got rows; library now
**66 wired / 0 missing**.

Notable calls: LowHealthAlert is wired as a solo whisper-volume UI
double-note (the HUD fires discrete PlayUI pulses, not a PlayLoop —
the enum comment says "looped" but the code pulses); HoverBladeLoop's
row volume is just the pre-ramp default since HoverBladeBlock drives
SetBaseVolume itself; RepairPadEnter/Cancel use the 1000ms-up/500ms-down
charge pair so enter/cancel read as one field energising and dying.

All clip choices this session were filename-picked, not auditioned —
an ear pass in a live session is the remaining polish item.
