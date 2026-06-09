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
- [ ] Thrusters feel too weak — raise baseline (separate from the override knob)
- [ ] Bomb knockback stronger
- [ ] Mortar not fireable + aim laser missing
- [ ] Bases deal ≥2× damage to enemies
- [ ] Increase default spring power
- [ ] Battery mechanic (large — likely its own ADR)

### Category B — Visuals, VFX & rendering
- [ ] Module in-game models not showing in garage
- [ ] Weapons are red cubes → real models *(model decision)*
- [ ] Garage = bubble shield in space
- [ ] Mines barely visible
- [ ] Smoke too opaque / small / short-lived
- [ ] Deeper dirt darker
- [ ] Distinct visual per module *(model decision)*
- [ ] Ropes vanish on play→garage return
- [ ] ADS: whole bot should go invisible (currently inverted/partial)

### Category C — Garage & module UX / systems
- [ ] Garage entry starts in drive mode → should be build mode
- [ ] Concoctions nameable; default "Mix N" (next free index), not "new mix"
- [ ] Modules usable / weapons fireable in garage → should be disabled
- [ ] Remove blink module → forward burst-of-speed module
- [ ] Maybe remove healing module *(decision)*
- [ ] Duplicate-robot button (clone into new slot)
- [ ] Deleting CPU in garage explodes the bot
- [ ] Block rotation (e.g. thrusters)

### Backlog (not this session)
- Planet arena diggable + terrain noise (user marked "for future")

## Log

- **Pattern established + Cat-A item 1 (thruster power override).** Added
  `Dev.Thruster.Power` Tweakable (1.0×, range 0–5×) under the existing
  "Dev (Override Chassis Tuning)" master toggle, a compile-stripped
  `DevTuningOverride.ThrusterPowerMultiplier`, and applied it on the thruster
  hot path. Shipping builds always read 1.0 (no MP-desync risk, invariant #1).
