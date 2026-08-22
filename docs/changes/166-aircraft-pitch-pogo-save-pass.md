# 166 — Aircraft pitch diagnosis, pogo-tune/save bugs, autosave, prop throttle axis

User report: plane control "super muted / flying through mud", wing size
seemingly irrelevant; pogo tuning "doesn't stick or translate in-game";
a forward-pointing prop "takes the helicopter keybinds". All three were
measured live (Unity MCP, play mode, scripted `StubInputSource` on
throwaway chassis — see the memory note `project_live_physics_probe_pattern`).

## Aircraft pitch — measured, NOT changed (design call pending)

Stock `Plane` preset (mass 20.5, I=(72,105,61), pitch authority scale
1.46), spawned at 250 m / 14 m/s, 7 s runs, sampled 4 Hz:

| run | result |
|---|---|
| W held, no pitch | noses DOWN at +0.8 → +0.55 rad/s, vertical dive by t≈2 s |
| thrust off, no pitch | gentle nose-UP −0.12 rad/s (aero alone is neutral/stable) |
| W + Space from t=0 | −0.9 rad/s → full loop in 3.5 s |
| span ×2 (I=(83,309,258), roll scale 0.30) | −0.5 rad/s with Space |
| chord ×2 | −0.5 rad/s |
| span ×0.5 | −1.5 rad/s |
| user prop plane (86 kg, I=(368,544,252), all three axes at the 0.3 floor) | Space changes nothing: −25° dive either way |

Causes, in order of weight:

1. **Thruster/rotor offset moment.** The preset thruster sits 1.23 m above
   the COM (`thrusterArm=(0,1.23,-2.42)`), so thrust alone pitches the nose
   down at ~0.8 rad/s. The pilot spends roughly half the stick just holding
   level. Wings are not what pitches the plane — pitch is the chassis torque
   in `PlaneControlSubsystem`; wings only add lift (∝ span·chord) and damping.
2. **Aero damping dominates the explicit damping.** Measured total pitch
   damping ≈ 16 rad/s² per rad/s at 50 m/s vs `PitchDamping` 3.5 — the
   tail/canard surfaces supply 3–4× more (∝ v × Σ area·r²). Authority is a
   fixed angular acceleration (10 rad/s² × inertia scale), so max pitch rate
   collapses as speed or wing area rises.
3. **Inertia floor.** `clamp(105/Ixx, 0.3, 2.5)` hits 0.3 on any
   user-scale plane (the user's is 3.5× the reference on every axis): 3 rad/s²
   against ~10 rad/s² per rad/s of damping ⇒ ~17°/s max pitch rate. Mud.

So "bigger wings = more lift, less roll" IS implemented (roll scale 1.16 →
0.30 at 2× span) but the same mechanism also crushes pitch, and it stacks
with (1) and (2). Options, not taken without sign-off: (a) rate-command
control (torque = Kp·I·(stick·maxRate − ω), aero damping then only sets
settling time); (b) cancel thrust-offset pitching moment (arcade trim); (c)
re-anchor the authority floor. Also noted: `_zeroLiftBias` (0.12) is dead
code for every foil since the binder sets `Vertical=true` on all aero ids
(wings lift only from AoA + incidence; every preset wing has pitch 0), and
`Robot.IsAero` omits `BlockIds.Wing` so the Wing never gets size-scaled
mass/inertia.

## Pogo tuning — power works; the garage lost the edits

End-to-end the value is honoured: 48-pogo bot apex 34.7 m at power 4 vs
14.0 m at 1; single pogo 32.4 vs 11.8. The user's saved 50-pogo file has
4.0 on every pogo (placement copies the cache). What broke was tune-mode:

- **Save respawned the chassis mid-build** (`SaveCurrentBlueprint` fired
  `PresetChanged` → `GarageController.Respawn`). The bound instance then
  pointed at a destroyed block, `PropagateVariantToLiveBlocks` early-returned,
  the panel still said EDITING, and every later slider edit vanished.
  Verified live before and after.
- Fix A: `SaveCurrentBlueprint` no longer fires `PresetChanged` (only
  `DuplicateCurrentBlueprint` does — it swaps the blueprint object).
- Fix B: `BlockEditor.Update` notices a chassis swap under a live build
  session, re-points `_grid`, and re-binds the tune instance to the same
  cell/id on the new chassis (`RebindInstanceAfterRespawn`).

## Save pass — autosave

`ChassisBlueprint.Revision` (runtime counter, bumped by `SetEntries` /
`DisplayName`) + `GameStateController.IsDirty` / `AutosaveIfDirty()`.
Autosave fires on build-mode exit, on every launch, and on application
quit — **only for user blueprints** (a file already exists). Preset clones
and unsaved new robots still need the Save button (which forks them); no
silent file creation. Verified live: edit → exit build → file updated,
"Autosaved blueprint to …" logged, user files untouched afterwards.

## Prop throttle axis — bug fixed

`RotorBlock` chose the throttle input from `_spinAxisLocal` (rotor-local,
always +Y) instead of the chassis-frame axis, so a prop placed with
Up=(0,0,1) still throttled on Space. Now transforms into chassis space:
forward-mounted prop → W/S, lift rotor → Space. Two things remain by
design and are worth a control-scheme pass later: rotor throttle starts at
1.0, so W is inert on a fresh prop (only S does anything visible), and a
heli (rotor + adopted foils ⇒ `PlaneControlSubsystem` present) gets
pitch-up torque AND collective from the same Space key.

## Verification

EditMode `ChassisBlueprintRevisionTests` 3/3; PlayMode `RotorThrottleTests`
7/7 incl. new forward-MOUNTED case (fails on the old axis test). Headless
rig: EditMode 503/504 (pre-existing inconclusive), PlayMode 136/137
(pre-existing skip), 0 failed. Editor console clean. Invariants: no
Tweakables, no per-frame allocs (two TransformDirection calls per rotor
tick), no new physics objects.

## Files

`Movement/RotorBlock.cs`, `Gameplay/BlockEditor.cs`,
`Gameplay/GameStateController.cs`, `Gameplay/GarageController.cs`,
`Block/ChassisBlueprint.cs`, tests as above.
