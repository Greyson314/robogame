# 118 — Art & UI pass, part 2 (outlines + migrations + menu restyle)

> Continuation of [117](117-ui-theme-consolidation.md). Three items, each
> committed separately. Driven via the Unity MCP bridge (live editor) with
> ScreenCapture-based visual verification where the bridge stayed up.

## 1 — Hero-block outlines: enabled, then REVERTED (perf)
`Assets/Settings/PC_Renderer.asset`: the `MKToonPerObjectOutlines` feature was
`m_Active: 0`; I flipped it to `1` thinking it was an unfinished TODO. **It was
disabled on purpose** — the per-object inverted-hull pass adds a draw per block
and tanks FPS at this game's block counts. Reverted to `m_Active: 0` in the same
session. The hero materials keep the `+ Outline` variant for a future *performant*
outline (screen-space edge-detect, or a hero-only layer). Do not re-enable without
a profiled cheaper path — see the note in art-direction.md Phase 2.

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
