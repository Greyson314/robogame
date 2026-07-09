# 137 — Projectile render interpolation + build-HUD readability pass

Four user reports in one session: jittery falling bombs, opaque tweak
sliders, white-on-cream HUD text, oversized WARMUP pill / all-caps habit.

## 1. Bomb fall jitter → fixed-step render interpolation

`ProjectileWorld` integrates at 50 Hz and `ProjectileVisual.SyncTo` was
called from `FixedUpdate`, snapping visuals to raw step positions — the
documented "v1 renders at the exact step position" deferral in
`ProjectileVisual`. At render rates that aren't multiples of the tick this
stutters, worst for slow near-camera objects (falling bombs).

Fix (`ProjectileWorld.cs`): `Live` gains `PrevPos`; `FixedUpdate` stores it
before integrating and no longer touches visuals; a new `Update` lerps each
visual between `PrevPos` and `Pos` with
`alpha = (Time.time − Time.fixedTime) / fixedDeltaTime` — same render-one-
tick-behind scheme as Rigidbody interpolation. Alloc-free. Applies to all
projectile kinds (bombs, cannon, mortar, SMG tracers get it for free).

## 2. Variant-slider "?" tooltips

New `HoverTip` component (Gameplay) — pointer-enter/exit relay. Every
`BuildLabeledSlider` call site in `VariantConfigPanel` now passes a `tip`
string; the row grows a "?" chip between label and slider, and hovering it
shows the text in a shared strip docked to the panel bottom (built once,
raycast-transparent). Tips written from the actual mechanics: lift ∝
span × chord, thickness is shape/hitbox only, teeter cosmetic in v1,
hover lift ∝ size², RPM/ammo/power point at their live readouts.

## 3. White-on-cream contrast fixes

Session 132 moved panels to cream, but pre-cream `Color.white` text
survived in: variant-panel slider labels + handle + track (the reported
span slider), preset-button labels, concoction caption + list rows,
hotbar tab/slot labels, and the Edit-block button. All now use
`UguiPalette` ink/cream pairs. Variant title while instance-editing was
white → now Danger vermilion.

## 4. HUD sanity pass (light — user will style-pass later)

- "WARMUP" pill: now "Warmup" at 14 pt (was 24 pt bold countdown style) —
  new `_warmupStyle` in `ObjectiveHud`.
- Sentence-cased persistent chrome: You/Enemy headers, Frags, Scoreboard +
  Dmg/Scrap columns, Time footer, kill-feed You/Enemy, module tile labels
  (EMP stays an acronym), variant-panel titles/Advanced/Concoction,
  Edit-block button, Lab screen labels.
- Left all-caps deliberately: gauge abbreviations (SPD/ALT/BLK/SCR/AMO),
  K/D, and moment banners (FIGHT!, FIRST BLOOD!, VICTORY/DEFEAT,
  DESTROYED). The last three are `[SerializeField]` defaults — code edits
  wouldn't reach already-serialized scenes; they belong to the user's own
  style pass.

## 5. UI scale sanity (follow-up in same session)

User report: variant sliders, Warmup, and settings reset buttons "very
small". Root cause for the uGUI side: the build-HUD canvases
(VariantConfigPanel, BuildHotbar, BuildEditMode, BuildMirrorMode,
PlacementFeedbackHud, LabController, SceneTransitionHud) used
`ConstantPixelSize` while Settings/Pause/MainMenu use
`ScaleWithScreenSize(1920×1080, match 0.5)` — above 1080p the build HUD
rendered proportionally smaller. All seven now use the same
ScaleWithScreenSize config. None of them did raw `Screen.width/height`
layout math, so anchors carry the change.

Point fixes: Warmup pill 14 → 18 pt (14 overshot); settings per-row "↺"
reset buttons 40×32 → 44×36 with the glyph at 26 pt (was the button
default 18 pt).

Known remainder: the IMGUI HUDs (ObjectiveHud, VehicleStatsHud,
ScoreboardOverlay, ...) still draw in raw screen pixels — no global IMGUI
scale exists. Only Warmup was flagged, so no IMGUI scaling system was
invented; if the whole combat HUD reads small on high-res, that's a
follow-up (GUI.matrix scale per OnGUI).

## Verification

EditMode + PlayMode suites via run-tests.sh; Unity console compile check.
(Results recorded in the commit messages.)
