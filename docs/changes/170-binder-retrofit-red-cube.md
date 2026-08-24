# 170 — Red-cube fix: binders are unconditional (LOG-170)

User report: placing blocks in the garage — most recently mortars and
drills — sometimes yields a large textured red cube instead of the
block's real visual.

## Root cause (reproduced live in the user's running garage)

`ChassisAssembler` gated the weapon mount + `RobotWeaponBinder` on
"blueprint contains a Weapon-category block", and `RobotWheelBinder` on
"blueprint contains a Ground-drive block" — both evaluated once, at
spawn. The FIRST weapon placed on a weaponless-at-spawn bot in the
garage therefore fired `BlockPlaced` with nobody listening: no
`MortarBlock` (etc.) was attached, nothing built the rig or hid the
host primitive, and the player saw the bare `BlockMat_Weapon` "alert
red" cube. Blueprint data was recorded correctly, so relaunching or
re-entering the garage rebuilt the bot fine — hence "may not actually
place". Session-132's mortar bug one level up: 132 fixed the detection
LIST; detection still ran only at spawn. Live probe on a weaponless
clone: mortar → `hostVisible=True, kids=0, hasMortarBlock=False`;
first wheel → bare cube, no `WheelBlock`.

The drill did NOT reproduce (its auger rides the binder-independent
`VisualModelStatic` path; both live probes rendered it) — its sighting
was almost certainly session-169's wedged-editor import window. But the
armed-clone probe surfaced an adjacent real bug: the drill is
Weapon-category (hotbar), so `RobotWeaponBinder`'s generic fallthrough
stacked `WeaponBlock + ProjectileGun` (+ aim yoke) onto every drill on
any bot that had the binder — drills yaw-tracked the reticle and fired
hitscan on the dig trigger.

## Fix

- `ChassisAssembler`: `EnsureWeaponMountAndBinder` and
  `RobotWheelBinder` are now unconditional (same drag-on rationale as
  the aero/gyro/pogo/module binders; an idle binder costs nothing).
  `hasWeapon` deleted. `GroundDriveSubsystem` / `HoverDriveSubsystem` /
  `ModuleSystem` stay presence-gated (invariant #5): the garage is
  parked-kinematic and launch reassembles, so only the binder gap was
  player-visible.
- `RobotWeaponBinder.ShouldBind`: skips `BlockIds.Drill` alongside the
  tip-block skip; `RobotDrillBinder` owns the drill.

## Verification

New PlayMode `BinderRetrofitTests` (3 tests): weaponless bot →
post-assembly mortar placement attaches `MortarBlock` + rig; wheel-less
bot → wheel placement attaches `WheelBlock`; armed bot → drill gets
`DrillBlock` but never `WeaponBlock`. Rig setup gotcha: Bot options
add a PlayerController, which errors without an IInputSource — the
tests attach a stub, same as real bot spawns do.

Rig (final): EditMode 526/527 passed, PlayMode 149/150 passed (+3 new),
0 failed either mode; the two non-passes are the pre-existing
inconclusive/ignored cases. The user's editor was live in garage play
mode throughout; it picks the change up on its next recompile
(deliberately not force-refreshed — a reload would have ended their
session). Console clean via the raw-HTTP bridge at every check.
