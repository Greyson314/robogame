# Session — Launch button, three rounds of debugging

**Intent.** "Click Launch button → goes to Arena" wasn't working. Bug
required three orthogonal fixes, none obvious in isolation.

**Round 1: PlayerInputHandler null-deref on respawn.**
`AddComponent<T>` runs `OnEnable` immediately. `PlayerInputHandler.OnEnable`
was looking up the action map and bailing because `_actions` hadn't
been set via reflection yet. Fix in [ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
`Build()`: `bool wasActive = root.activeSelf; root.SetActive(false); try { ...add components, reflect refs in... } finally { root.SetActive(wasActive); }`.
Now `OnEnable` runs once, after wiring. **General lesson: any reflection-based serialised-field assignment must happen with the root deactivated.**

**Round 2: Gun fired through UI buttons.**
Input System actions don't auto-block over UI like legacy input does.
Fix in [PlayerInputHandler.cs](../../Assets/_Project/Scripts/Input/PlayerInputHandler.cs):
`FireHeld` getter returns false when `EventSystem.current.IsPointerOverGameObject()`
is true. Same trick is reusable for any other "is mouse on the world?"
gate.

**Round 3: Cursor lock ate UI clicks.**
[FollowCamera.cs](../../Assets/_Project/Scripts/Player/FollowCamera.cs)
locked the cursor on the same left-click frame that the UI raycaster
needed. Once locked, cursor is at screen center invisible — UI sees
nothing. Fix: same pattern, the click-to-capture condition now also
requires `!EventSystem.current.IsPointerOverGameObject()`.

**Side fix on the way: SceneTransitionHud rebuilt as UGUI.**
Was OnGUI/IMGUI. Input System UI module doesn't route to IMGUI. Full
rewrite to procedural UGUI: `EventSystem` (with
`InputSystemUIInputModule`, deletes legacy `StandaloneInputModule` if
present), `Canvas` (ScreenSpaceOverlay, sortingOrder 100),
`CanvasScaler`, `GraphicRaycaster`, `Button` with `Image` background +
child `Text` label. Uses `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.
