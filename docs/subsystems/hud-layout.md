# HUD layout — screen-region reservations

> Living doc. One line per screen-anchored HUD element so the next element
> lands in free space instead of on top of an existing one (that's how the
> arena's Garage button got buried — session 128). Update this table when
> adding/moving any screen-anchored element. World-space projections
> (nameplates, floating damage, scrap indicators) and full-screen modals
> are exempt.

## Two UI families, one z-order rule

IMGUI (`OnGUI`) **always draws over** UGUI overlay canvases, regardless of
`sortingOrder`. And the EventSystem cannot see IMGUI, so raycast checks
pass straight through IMGUI panels. Bridges:

- `Robogame.Core.HudPointerGuard` — interactive IMGUI overlays register
  their rects each OnGUI; cursor-capture / fire / camera-drag gates query
  the union of both families. Modal screens (pause, settings, match end)
  also register as modals there.
- `DevHud`'s transparent UGUI raycast-blocker — stops clicks *through* an
  interactive IMGUI panel from also hitting UGUI buttons underneath.

## Region map (formulas in W=Screen.width, H=Screen.height, GUI top-left coords)

| Region | Element | Rect | Visible |
|---|---|---|---|
| top-left | FpsCounter (IMGUI) | x 8–248, y 8–32 | always |
| top-left | NetDevHud status line (IMGUI) | x 8–728, y 34–54 | dev builds |
| left edge | DevHud panel (IMGUI) | x 8–288, y 8–(H-8) | F1 toggle, dev |
| left edge (mid) | CenterOverlay legend (IMGUI) | x 18–258, y H/2–(H/2+82) | build mode, G toggle |
| top-center | ObjectiveHud (IMGUI) | x (W−520)/2 … +520, y 18–142 | arena match |
| top-center | StartMatchHud / KillAnnouncer banners (IMGUI) | centered, y ≈ 0.20H | transient |
| top-right | PerformanceHud (IMGUI) | x (W−288)–(W−8), y 8–~300 | F3 toggle |
| top-right | DrillBlock debug readout (IMGUI) | x (W−330)–(W−10), y 10–250 | debug flag, off by default — collides with PerformanceHud if both on |
| right edge | KillFeedHud (IMGUI) | x (W−258)–(W−18), y 160–~340 | arena match |
| bottom-right | VehicleStatsHud (IMGUI) | x (W−238)–(W−18), y (H−170)–(H−18) | arena |
| bottom-right | SceneTransitionHud main button (UGUI) | x (W−200)–(W−20), y (H−60)–(H−20) | **garage only** ("Launch ▶") |
| bottom-left | SceneTransitionHud stack (UGUI) | x 20–200 (name field 260), y (H−394)–(H−20) | garage |
| bottom-center | ModuleBarHud (IMGUI) | centered tiles, y (H−78)–(H−18) | arena match |
| bottom-center | BuildHotbar (UGUI) | centered, y (H−100)–(H−28) | build mode |
| center | AimReticle / HitMarker (IMGUI) | screen center | arena |
| center | ScoreboardOverlay (IMGUI) | centered 560w, from 0.40H | Tab held |
| modal | PauseMenuHud (UGUI, order 400) | full-screen dim + centered 360×330 | Escape |
| modal | SettingsHud (UGUI, order 500) | full-screen dim + centered 900×720 | via pause/main menu |
| modal | MatchEndOverlay / DeathOverlay (IMGUI) | full screen | match end / dead |

UGUI sorting orders: MainMenu 50 · BuildHotbar 95 · SceneTransitionHud 100
· PauseMenu 400 · SettingsHud 500.

## Reserved-but-empty

- Right edge y ~340–(H−190): free (kill feed growth + future arena HUD).
- Bottom-left in arenas: free (garage stack is garage-only).

## Cursor / Escape contract (session 128)

- `PauseMenuHud` is the **sole Escape owner**: gameplay → pause menu →
  settings, one level per press. Nothing else may poll Escape.
- Hold **LeftAlt** (FollowCamera + BuildFreeCam) lends the cursor to the
  HUD while held; release re-locks unless a modal opened meanwhile.
- Fire (`PlayerInputHandler`) is gated on `Cursor.lockState == Locked` —
  a free cursor is always UI mode.
- `FollowCamera.OnEnable` releases a stale lock it doesn't own, so scene
  transitions can't strand a locked cursor.
