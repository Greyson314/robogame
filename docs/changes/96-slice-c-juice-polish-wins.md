# 96 — Slice C: juice polish wins (×5)

> Status: **Slice C shipped.** Five HUD / feel polish items landed —
> live foil-pitch propagation, crosshair ammo state, damage-number
> clustering, low-HP vignette + audio, scrap-pickup magnet trail. Tests
> green throughout. Slice B (rope tip at chain end) was already shipped
> in session 55 — confirmed by code audit; stale carry-forward removed.

## Scope

Second slice of the polish + netcode QoL phase plan. The six "juice"
wins from the plan, minus the unified kill-feed (that moved into Slice D
because it overlaps with the networked scoreboard). Slice B was a no-op
— `RopeGeometry.TipCell` + `BlockEditor.UpdateTarget`'s rope-cylinder
hit redirect already implement the chain-end semantics shipped in
session 55. The README's "Known unknowns" entry was just stale doc.

## What landed

**C1 — Live mid-edit collective pitch propagation.**
`BuildSession.VariantChanged` now drives a propagation handler in
`BlockEditor` that walks the active chassis grid and pushes the new
pitch / dims to every placed block whose `Definition.Id` matches.
Foils' visual mesh tilts live (they were already subscribed to
`BlockBehaviour.PitchChanged`). The rotor side gets a second hop:
`RotorBlock` subscribes to its own `PitchChanged` and forwards the
new pitch to every `_adoptedFoils[i].Aero` so the collective tracks
through to the blades. In build mode the chassis is kinematic and no
foils are adopted so the rotor pass is a no-op there — but the wiring
is now correct for any future runtime-with-panel-open path, and the
foil case (the more common one) is fixed. Closes foil-rotation-plan
§10A.

**C2 — Crosshair + ammo state.**
`AimReticle` now resolves the chassis `WeaponAmmoState` via the
`FollowCamera` target and tints the crosshair: when at least one pool
exists and none can fire, the crosshair blends 70 % toward a new
`_reloadColor` (default desaturated grey) on top of whatever base /
enemy tint applied. A small total-loaded ammo count sits below the
crosshair when any pool exists. The full per-weapon breakdown stays
on `VehicleStatsHud` bottom-right — the crosshair is glance-state.

**C3 — Damage-number clustering + combo pop.**
`FloatingDamageOverlay` got two extensions. Cluster detection: a
reusable `_placedRects` list tracks every rect placed this OnGUI
event; if a new rect's centre lands within `_clusterThresholdPixels`
(default 60 px) of an existing one, it's shoved horizontally until
clear. Combo pop: when an accumulator's cumulative damage crosses
`_comboThreshold` (default 100), `ComboPopTime` is stamped and the
render scales 1.4× → 1.0× over `_comboPopDuration` (default 0.35 s).
One pop per accumulator lifetime — no retrigger from subsequent hits
over the bar.

**C4 — Low-health vignette + audio.**
New `LowHealthVignetteHud` MonoBehaviour (added to the camera in
`ArenaController.ConfigureCamera`'s HUD setup). Subscribes to the
chassis Robot via `FollowCamera.Target` like every other player HUD.
When the HP fraction drops under `_threshold` (default 30 % to match
`ObjectiveHud._hpAlertThreshold`), draws four edge bands with sliced
falloff for a "danger frame" effect. Severity scales the alpha as HP
goes 30 % → 0 %, modulated by an 0.85–1.0 sine pulse at
`_pulseHz`. New `AudioCue.LowHealthAlert` declared (clip not yet
authored, missing-cue logger surfaces it per invariant #8). Pings via
`AudioRouter.PlayUI` on the leading edge and every `_audioInterval`
seconds after.

**C5 — Scrap pickup magnetic-pull trail.**
New `VfxKind.MagnetTrail` with a tight palette-locked recipe in
`VfxSpawner.ConfigureMagnetTrail` (3-particle burst, 0.05 s duration,
HotCore→Hazard gradient — matches the scrap palette). `ScrapPickup.Update`
emits a trail at `_trailInterval` (default 0.15 s) while the pickup is
being pulled toward a chassis. Reuses the pooled spawner path; cadence
resets on the leading edge so re-entry pings fresh. Cost in the worst
case (6-bot kill burst, all pickups pulling) is bounded by the spawner's
`MaxConcurrentPerKind = 24`.

## Files

New:
- `Assets/_Project/Scripts/Player/LowHealthVignetteHud.cs`

Edited:
- `Assets/_Project/Scripts/Gameplay/BlockEditor.cs` (`VariantChanged` subscription + propagation)
- `Assets/_Project/Scripts/Movement/RotorBlock.cs` (`PitchChanged` → adopted-foil forwarding)
- `Assets/_Project/Scripts/Player/AimReticle.cs` (ammo-state mirror + reload tint + count)
- `Assets/_Project/Scripts/Player/FloatingDamageOverlay.cs` (cluster offset + combo pop)
- `Assets/_Project/Scripts/Core/AudioCue.cs` (`LowHealthAlert`)
- `Assets/_Project/Scripts/Core/VfxKind.cs` + `VfxSpawner.cs` (`MagnetTrail` kind + recipe)
- `Assets/_Project/Scripts/Gameplay/ArenaController.cs` (auto-attach `LowHealthVignetteHud`)
- `Assets/_Project/Scripts/Gameplay/ScrapPickup.cs` (trail emit on pull)
- `docs/changes/README.md` (drop stale rope-tip carry-forward, log this session)

## Tests

`EditMode: 252/253 passed, 0 failed, 1 inconclusive.`
`PlayMode: 92/93 passed, 0 failed, 0 inconclusive.`

No new tests landed for this slice — every change is UI / visual /
audio polish, exercised through playtest. The existing
`MatchControllerTests` + `FloatingDamageOverlay` integration coverage
catches any regression on the score / HP / damage paths the C-changes
read from.

## What's deferred

Per the plan: Slice D (unified scoreboard + persistent kill feed +
nameplates, with NGO replication scaffold) is the natural next move
and starts on the next checkpoint.
