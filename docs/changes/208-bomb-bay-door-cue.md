# 208 — Bomb-bay doors: an audible open, a silent slam (INV-8 gap) (LOG-208)

Landed by the factory 2026-09-19T20:20:35Z. Change CHG-004, branch `chg/004-bomb-bay-door-cue` @ 60c74ee736. Gate: suite PASS, perf N/A, red team PASS — suite 577/577 + 166/167 at 60c74ee7 (builder run chg004-2; red team re-run rt004, same counts); perf N/A: one one-shot per drop, nothing per-frame; red team sonnet/medium PASS 51k; class note arbitrated in the entry (Grey's approve CHG-004 is on file).

## What changed

`AudioCue.BombBayDoorOpen` (appended after `UiPageTurn`, the enum's true last member — append-only rule, AudioCue.cs:99-103). `BombBayDoors.Drop()` requests it as a one-shot at `transform.position` through the existing `AudioRouter.PlayOneShot` path; the slam stays silent (Grey, shift-11 amendment: "minimal or nothing for slam" — nothing won). `AudioRouter` gains a test-only `internal static event System.Action<AudioCue> OneShotRequested`, fired first in `PlayOneShotInternal` (before the library/clip lookup, since the new cue's clip can never resolve — Universal Sound FX is absent from this clone). `AudioCueWizard.cs` and `AudioCueLibrary.asset` both gain one entry by text (no wizard run): clip `MECHANICS/MECHANICS_Metal_Mechanism_01_mono.wav` (UiToggleOn's clip, reused), `AudioBus.Sfx`, spatial 1, vol 0.60, jitter 0.05. Source: F-008.

## Evidence

- Tests first: commit `22ce507a` carries only `AudioCueWizardTests` (2 EditMode cases) and `BombBayDoorsTests` (1 PlayMode case), compile-red (`CS0117: 'AudioCue' does not contain a definition for 'BombBayDoorOpen'`).
- Suite at the final sha (`60c74ee7`) via `run-tests.sh --at`: EditMode 577/577 (+2), PlayMode 166/167, 1 documented skip (+1, was 165/166).
- `Drop_RequestsOpenCue_OncePerDrop` fails if the seam or the `Drop()` call is removed; it also proves `Update()` alone (idle, and through the full 1.55s swing past the slam) never requests a second cue.
- `EveryAudioCue_HasAWizardRow` carries a commented exception list for three pre-existing, already-documented gaps (`ThrusterIgnite`, `ThrusterShutdown`, `ReloadStart`) — unrelated to this change, not fixed here.
- Must-not-break: `BombBayBlock.cs` (explosion cue, drop cadence) untouched; `BombBayDoors`' keyframes/re-drop pose logic untouched.

## Revert

`git revert` the merge; the doors go quiet again. No data or save format touched.

## Gate

- Red team (sonnet/medium, 51k) PASS: the enum member is the last of 75 (index 74) and the library's new `Cue: 74` remaps nothing; its clip GUID equals UiToggleOn's; the PlayMode test asserts zero requests when idle, one at `Drop()`, still one after the whole 1.6 s swing; the seam is a null-check when nobody subscribes (INV-6); the test removes its handler in TearDown. Suite re-run by the red team at 60c74ee7: EditMode 577/577, PlayMode 166/167 (`.utmp/factory/gate/CHG-004-redteam.md`).
- Arbitration (class): the red team read this as ASK (a player hears it). Agreed that a player hears it; the check-off exists: Grey, INBOX 2026-09-19T05:34:30Z, verbatim: "approve CHG-004: yes open, minimal or nothing for slam". The slam is silent by that word. It lands with a PLAY entry (PT-002).
- Not provable here: the clip itself. `Assets/Universal Sound FX` is absent from the factory clone, so nobody has HEARD this cue yet; in Grey's checkout the wizard rebuilds the library from the same row. The person is the test (PT-002).
