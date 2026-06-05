# 118 — Art & UI pass, part 2 (outlines + migrations + menu restyle)

> Continuation of [117](117-ui-theme-consolidation.md). Three items, each
> committed separately. Driven via the Unity MCP bridge (live editor) with
> ScreenCapture-based visual verification where the bridge stayed up.

## 1 — Hero-block outlines enabled
`Assets/Settings/PC_Renderer.asset`: the `MKToonPerObjectOutlines` renderer
feature was present but `m_Active: 0`, so hero blocks (CPU / weapon / thruster) —
whose materials are correctly the MK `+ Outline` variant (black, size 85) — drew
no ink line. Flipped to `m_Active: 1` (completes the art-direction Phase-2 item).
**Needs an arena eyeball**: a black outline reads poorly against the dark garage;
confirm in the bright arena, and watch for a native-render-pass interaction.

## 2 — BuildHotbar colour migration
All 14 hardcoded literals → `UguiPalette` tokens (tabs, slots, CPU readout/bar,
VAR badge, slot numbers). The build hotbar now reskins from one place like the
rest. Remaining migration debt = zero-visual-change token swaps in the IMGUI
stragglers (AimReticle, DeathOverlay, HitMarker, LowHealthVignette,
FloatingDamage, ScrapCarried, CenterOverlay, debug shadows) — tracked in
[ui-direction.md](../subsystems/ui-direction.md).

## 3 — Main-menu restyle
The 110pt "ROBOGAME" overflowed the 560px column (no overflow mode) → clipped to
"ROBOGA" with the accent line striking *through* the letters. Fix in
`MainMenuController`: 96pt + `HorizontalWrapMode.Overflow` (full word, one line),
underline moved below the wordmark (y -158→-190, wider), tagline + button stack
spaced down, buttons widened 320→360, last hardcoded pressed-colour →
`UguiPalette.AccentPressed`. Compile-verified; **exact spacing wants a visual
confirm** once the bridge is re-approved.

## Tooling note — bridge revokes on recompile
Confirmed live: entering play mode and recompiling (domain reload) repeatedly
drops the `com.unity.ai.assistant` MCP bridge into "awaiting approval" (the
named-pipe relay's signature validation flips to Pending — see the diagnosis in
the session transcript). Re-approve via Project Settings → AI → Unity MCP. The
headless rig (`run-tests.sh`) is bridge-independent and carried compile
verification when the bridge was down. ScreenCapture-to-PNG in a play-mode
`RunCommand` is the way to actually *see* the game view (incl. UGUI overlay).
