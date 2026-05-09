# Session 40 — Garage features: mirror mode + leaf-block connectivity

> Status: **shipped, untested in-engine.** Two Robocraft-style build-mode
> additions landed as separate commits. Connectivity first ([bc567b5e](#)),
> mirror mode second ([173e387f](#)). Both are additive — no shipped
> chassis breaks because every preset attaches its specialty blocks
> (foils, weapons, rotors, ropes) to a non-leaf cube cap, so the new
> rule sees a valid host on every block.

## Why this session

User asked for two features in parallel:

1. *Mirror mode like Robocraft's, accounting for orientation and direction.*
2. *All blocks need connective and non-connective faces — can't place a block on top of a wing.*

Both target the same underlying gap: the build editor was too permissive
about *where* a block could attach, and too cumbersome for symmetric
chassis (every wing had to be placed twice).

## Connectivity — leaf blocks

[`Block/BlockConnectivity.cs`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
is a new static helper with a hardcoded leaf-id list (the conservative
default for shipped assets) and an `IsLeaf(BlockDefinition)` query that
also reads a new `IsLeafBlockRaw` SO field for future authored blocks.

Leaves: Aero, AeroFin, Thruster, Rudder, Rotor, Weapon, Cannon, BombBay,
Hook, Mace, Wheel, WheelSteer, Rope. Non-leaves: Cube, Cpu.

[`Gameplay/BlockEditor.cs`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
applies the rule in two places:

- **`IsValidPlacement`** — strict-host check (post-feedback refinement,
  2-way street): the placement's host is the specific cell at
  `cell − up` (i.e. the block whose face the player aimed at). That
  host must exist, be CPU-reachable, and be non-leaf. Aiming at a
  wing's face is rejected even if some *other* neighbouring cell
  happens to be a non-leaf cube — the player's aim is authoritative.
- **`BuildCpuReachableSet`** — leaves are reached but don't bridge.
  The BFS visits a wing (so the wing itself is "reachable"), then
  doesn't propagate further through it. Defence-in-depth: a chain
  that snakes through a leaf isn't considered connected, so
  blocks "downstream" of a leaf can't be used as a host even if
  some other path to them exists.

The validator (`BlueprintValidator`) is unchanged. Shipped helicopter /
bomber / etc. all attach foils to a non-leaf cube cap, so they pass
the new rule unchanged. If a future authored chassis tries to bridge
through a leaf, build-mode placement will catch the issue when the
player tries to extend through it.

Tests: [`BlockConnectivityTests`](../../Assets/_Project/Tests/EditMode/Blueprints/BlockConnectivityTests.cs)
locks down the hardcoded leaf-id list and the non-leaf cube/CPU contract.

## Mirror mode

Press **M** to toggle. Press **B** to cycle the mirror plane (X by
default, also Z). HUD banner at top-centre shows current state. Every
place + remove duplicates across the chosen plane; the ghost preview
shows two ghosts with independent valid/invalid colour.

[`Block/BlockMirror.cs`](../../Assets/_Project/Scripts/Block/BlockMirror.cs)
is the pure-data layer:

- `MirrorCell(cell, axis)` reflects an integer cell coordinate.
- `MirrorUp(up, axis)` reflects the mount-up — so a wing on the +X
  face mirrors to a wing on the -X face with up=-X, not up=+X.
  Mount-up is the input to the swept-bounds geometry, so the mirror
  correctly produces a symmetric foil rather than one rotated 180°
  about the mirror plane.
- `IsOnPlane(cell, axis)` flags cells that lie on the mirror plane —
  these skip the mirror copy because mirroring an on-plane cell
  yields the same cell.

[`Gameplay/BuildMirrorMode.cs`](../../Assets/_Project/Scripts/Gameplay/BuildMirrorMode.cs)
is the build-mode singleton. Holds `Enabled` + `Axis` + `Changed`
event, owns the M / B hotkeys (build-mode-gated so they don't fire in
the arena), draws the HUD banner. Fires `Changed` whenever state
changes so the editor can rebuild its ghost cache.

[`Gameplay/BlockEditor.cs`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
does the integration:

- **`IsValidPlacement(cell, up)` overload.** The existing parameter-less
  version derives `up` from the targeting state (place-cell − hit-cell)
  and forwards. Mirror placements call the new overload with their
  reflected up, so the swept-volume check sees the right candidate
  geometry.
- **`TryMirrorPlace` / `TryMirrorRemove`.** Best-effort: if the mirror
  copy would violate any rule (cell occupied, leaf neighbour, swept
  overlap, would-orphan-on-remove), it's silently skipped. The original
  is the source of truth — the player gets one click + one buzzer for
  the original, not a cacophony of "your mirror failed too" alerts.
  Cells on the mirror plane skip the copy entirely.
- **Mirror ghost.** A second `_mirrorGhost` GameObject builds whenever
  mirror is on and the placement isn't on-plane. Its valid/invalid
  state is computed independently from the original — so the player
  sees green/green (both will land), green/red (original lands, mirror
  blocked), or red/red (neither will land).
- **Cache invalidation.** `BuildMirrorMode.Changed` zeros the ghost
  cache key so the next frame rebuilds both ghosts with the new state.

[`GarageController.EnsureBuildModeWired`](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)
adds `BuildMirrorMode` to the build-mode singleton trio (controller +
editor + hotbar + variant panel + mirror).

Tests: [`BlockMirrorTests`](../../Assets/_Project/Tests/EditMode/Blueprints/BlockMirrorTests.cs)
covers the math — cell + up reflection, on-plane detection, mirror-twice
identity. Integration is in-engine.

## Notes for the next session

- **Per-face connectivity granularity** is deferred. The `IsLeaf`
  flag is binary; v1 says "a leaf has zero connective faces". A
  future need (e.g. a chest cube where one face is an opening) would
  upgrade to a per-axis mask on `BlockDefinition`.
- **Mirror placement audio** plays once, for the original. The mirror
  copy is silent — keeps the sound channel clean. Revisit if the
  mirror failing silently feels invisible.
- **Mirror Y-axis is intentionally not exposed.** Top/bottom symmetry
  isn't a thing players ask for in vehicle-builder games. Easy to add
  if needed (`MirrorAxis.Y` enum + switch arm).
- **Mirror plane gizmo** isn't drawn. Just the HUD banner. If
  positional clarity becomes an issue, a translucent quad at chassis
  X = 0 (or Z = 0) is the obvious follow-up.
- **Connectivity vs. validator** — only placement is gated. Authored
  blueprints loaded from disk still go through the chassis as written.
  If we ever want validator-level enforcement (e.g. "this saved
  blueprint has a block downstream of a wing"), add a BFS-with-leaves-
  as-sinks rule to `BlueprintValidator.Validate`. None of the shipped
  presets need it.
- **Foil orientation for non-±Y mounts** is still the lift-physics
  question from session 38: side / front / back wings get the
  vertical-fin treatment so visuals look right, but lift acts on
  chassis-right rather than the conventional axis. Phase 1.c
  follow-up. Mirror handles that quirk symmetrically — both copies
  get the same Vertical=true treatment — so flight-feel stays
  symmetric regardless.
