# 120 — Playtest pass (3 categories, single session)

A long autonomous session working through the post-playtest note dump. The
notes were grouped into three categories; this session executes all three.
This file is the **live tracking doc** — the highest-numbered change file, so
it's the current WIP state. Update the checklist as items land.

## The working pattern (plan → work → test via MCP → commit)

Every item, large or small, follows the same loop. This is the durable
deliverable the user asked for as much as the fixes themselves.

1. **Plan.** Read the relevant subsystem doc(s) + the actual code/callers
   before editing. For anything *visible, architectural, or hard to reverse*
   (asset-pack imports, new modules, scene re-layouts) — surface a one-line
   plan/decision to the user *before* executing. Trivial value/feel tweaks
   don't need sign-off.
2. **Work.** Edit on the **main checkout** (`...\robogame\`, never a worktree —
   Unity only watches main). Prefer direct file edits for C#; use Unity MCP
   (`ManageAsset` / `ManageGameObject` / `RunCommand`) for ScriptableObject
   values, scene objects, and prefab wiring that don't live in editable text.
3. **Test via MCP.** After edits:
   - `Assets/Refresh` (menu) → `ReadConsole(Error)` for a clean compile. Zero
     errors is the gate before moving on.
   - Behavioural change → `ManageEditor Play`, then `Camera_Capture` /
     `CaptureMultiAngleSceneView` to eyeball, then `Stop`.
   - Visual change → `CaptureMultiAngleSceneView` / `SceneView_Capture2DScene`.
   - Fallback when the MCP bridge is revoked: `.claude/scripts/run-tests.sh`
     (headless batch Unity — independent of the bridge).
4. **Commit** at each green checkpoint (work-on-main, commit-freely workflow).

### Model-work rule

For any item needing a real 3-D model (weapons, modules, garage shell), use an
**actual asset pack that matches the stylized low-poly aesthetic** — not
primitive cubes. Packs already in-project that fit: `Polytope Studio`
(low-poly village/environments), `Bitgem` (stylized water/props). Neither
ships weapon/turret models, so **weapon + module models need an asset-pack
decision** — see Open Questions. Procedural stylized meshes built in the game's
existing `BlockVisuals` idiom are an acceptable fallback when no pack fits.

## Open questions (need user input before those items execute)

- **Weapon / module models** (Cat-B): no in-project pack has cannon / mortar /
  SMG / bomb / module models. Options: (a) source a stylized low-poly weapons
  pack, (b) build procedural low-poly weapon meshes in the `BlockVisuals` idiom.
  Defaulting to (b) if no pack is provided, since it's reversible and
  dependency-free. **Flagging, not blocking** — model items are sequenced last.

## Checklist

### Category A — Combat & physics tuning
- [x] Thruster power override (dev Tweakable, global ×-multiplier, compile-stripped)
- [x] Thrusters feel too weak — baseline `DefaultMaxThrust` 620 → 900
- [x] Bomb knockback stronger — `Bomb_Default` knockback 40 → 80 (Δv ~9.6 m/s, sub-ceiling)
- [ ] ⚠ Mortar not fireable + aim laser missing — **DEFERRED, needs repro
      input.** Investigated: the mortar's fire path (`Update` → `_input.FireHeld`
      → `_gate.TryFire`) and arc-preview path (`show = _input != null`) are
      functionally *identical* to the working `CannonBlock`; both gate on
      `_input`. `BlockDef_Mortar` is category 3 (Weapon) and binds the same way
      as the cannon in `RobotWeaponBinder`. `Awake` (where `_input` resolves)
      runs only after the chassis hierarchy is active, so assembly order
      shouldn't null it. No default blueprint contains a mortar, so there's no
      ready repro. **Discriminating question for the user:** on the same bot
      where the mortar won't fire, *does the cannon fire?* If no → shared input
      regression; if yes → mortar-specific runtime state. Not blind-patching.
      "aim laser" = the mortar's orange arc preview (no laser system exists).
- [ ] ⚠ Bases deal ≥2× damage to enemies — **NEEDS CLARIFICATION**: no arena
      base / turret / objective damage system exists in the codebase. What is
      "base"? (home base, capture point, defensive turret, ramming?) Flagged,
      skipped pending answer.
- [x] Increase default spring power — Spring module `DefaultPower` 70 → 120
- [ ] Battery mechanic (large — likely its own ADR)

### Category B — Visuals, VFX & rendering
- [ ] Module in-game models not showing in garage
- [ ] Weapons are red cubes → real models *(model decision)*
- [ ] Garage = bubble shield in space
- [x] Mines barely visible — bigger lighter disc (0.5→0.85), bigger glow dot
      (0.13→0.26), + a tall amber beacon so they read from distance/above.
- [x] Smoke too opaque / small / short-lived — alpha 0.55→0.32, size 2-4→3.5-6.5,
      lifetime 3.5-5.5→5.5-8.5s, radius 1.6→2.6; module radius 6→9, duration 5→8s.
- [ ] Deeper dirt darker
- [ ] Distinct visual per module *(model decision)*
- [ ] Ropes vanish on play→garage return
- [x] ADS: whole bot now goes invisible. Root cause: the ADS hide walked
      `grid.Blocks` only, missing the `ChassisInstancedRenderer`'s per-group
      child meshes (the bulk hull, not parented under any block) → "random"
      cubes stayed visible. Fix: `EnsureChassisRendererCache` now unions the
      full target hierarchy (catches instanced group meshes) with grid-block
      renderers (catches reparented foils), deduped. *Eyeball in Play.*

### Category C — Garage & module UX / systems
- [x] Garage entry starts in drive mode → build mode (`GarageController.Start`
      calls `_buildMode.Enter()` after wiring; idempotent)
- [x] Concoctions default "Mix N" (next free index) not "New Mix"
      (`LabController.NextFreeMixName`). Field-editability part: the `_nameField`
      is a live input that `Save` already reads — verify typing works in Play.
- [x] Modules usable / weapons fireable in garage → disabled. `DisableWeapons`
      only killed `ProjectileGun`; now `DisableCombat` also disables
      Cannon/Mortar/BombBay/GrappleMagnet blocks + the `ModuleSystem`.
- [ ] Remove blink module → forward burst-of-speed module
- [ ] Maybe remove healing module *(decision)*
- [x] Duplicate-robot button — `GameStateController.DuplicateCurrentBlueprint`
      (clone → unique "<name> Copy" → new slot → save) + a "Duplicate" button
      in `SceneTransitionHud` (row 9).
- [ ] ⚠ Deleting CPU explodes the bot — **DEFERRED, can't repro statically.**
      The only garage delete path is `BlockEditor.TryRemove` → `BuildSession.
      TryRemove`, which already rejects `Category == Cpu` ("CPU is sacred"), and
      `BlockDef_Cpu` IS category 1. So the CPU can't be removed via the normal
      path. **Question for the user:** what exact action deletes it / what does
      "explode" look like (VFX? blocks fly apart on launch)? Possibly deleting a
      *support* block under the CPU, or a stale build. Not blind-patching.
- [ ] Block rotation (e.g. thrusters)

### Backlog (not this session)
- Planet arena diggable + terrain noise (user marked "for future")

## Log

- **Play-mode validation pass (MCP).** Loaded Bootstrap → Play → main menu
  (clean, 0 errors) → `SceneManager.LoadScene("Garage")` via `RunCommand` →
  Garage loaded with **0 errors/warnings**, and a reflection probe confirmed
  `BuildModeController.IsActive == true` — i.e. the garage opens directly in
  build mode at runtime (not just in code). Validates the build-mode entry,
  `DisableCombat`, and Lab/HUD init don't throw on the real startup path.
  Still wants a human eyeball for the *visual* items (smoke/mine/ADS look).

- **Pattern established + Cat-A item 1 (thruster power override).** Added
  `Dev.Thruster.Power` Tweakable (1.0×, range 0–5×) under the existing
  "Dev (Override Chassis Tuning)" master toggle, a compile-stripped
  `DevTuningOverride.ThrusterPowerMultiplier`, and applied it on the thruster
  hot path. Shipping builds always read 1.0 (no MP-desync risk, invariant #1).
