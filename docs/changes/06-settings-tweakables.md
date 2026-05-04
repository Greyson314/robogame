# Session — Settings panel + Tweakables registry

**Intent.** "Tuning by recompile" was killing iteration speed. The user
wanted a runtime settings UI that owns the physics knobs — no more
edit-save-wait-recompile-Play loops.

**What shipped.**

- New [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs).
  Static registry of named float specs (key, group, label, default, min,
  max). Persisted as JSON to `Application.persistentDataPath/tweakables.json`
  so values survive runs *and* editor restarts. Public API:
  `Get(key)`, `Set(key, v)`, `Reset(key)`, `ResetAll()`, `event Changed`.
  Lazy-initialised, range-clamped on every set.

- 14 specs registered today, grouped by category:
  - **Plane** — pitch / roll / yaw-from-bank power, pitch / roll / yaw damping
  - **Thruster** — max thrust, idle throttle, throttle response
  - **Ground** — acceleration, max speed, turn rate
  - **Chassis** — linear damping, angular damping

- Subsystems migrated to read through the registry:
  - [PlaneControlSubsystem.cs](../../Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs)
    reads every FixedUpdate — changes are live with zero plumbing.
  - [ThrusterBlock.cs](../../Assets/_Project/Scripts/Movement/ThrusterBlock.cs)
    same pattern.
  - [GroundDriveSubsystem.cs](../../Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs)
    only the three exposed knobs (accel/max/turn) routed through;
    jump/upright/grip stayed on the SO/inline path because they're not
    in the UI yet.
  - [RobotDrive.cs](../../Assets/_Project/Scripts/Movement/RobotDrive.cs)
    reads damping through registry. Subscribes to `Tweakables.Changed`
    and re-pushes `_rb.linearDamping`/`angularDamping` because rigidbody
    damping is cached on the body, not read each frame.

- New [SettingsHud.cs](../../Assets/_Project/Scripts/Gameplay/SettingsHud.cs).
  Esc toggles a procedurally-built UGUI panel. Scrollable body builds
  one row per `Tweakables.Spec` with label + slider + live value text +
  per-row reset (↺) button. Header: title, "Tweaks" tab pill,
  Reset-All, ✕ close. Sits on the persistent Bootstrap GameObject so
  one instance covers Garage and Arena. Wired in by
  [GameplayScaffolder.BuildBootstrapPassA](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs).

**Workflow now.** Press Esc → drag slider → effect is immediate. Values
persist across sessions (JSON in `persistentDataPath`). Adding a new
tweak is two lines: a `Register(...)` call in `Tweakables.EnsureInitialized`
and a `Tweakables.Get(key)` at the consumer. The UI rebuilds itself
from `Tweakables.All`.

**Notes for future tabs.** The "Tweaks" tab pill in the HUD is a stub —
adding Audio / Graphics / Bindings tabs means making the body content
swappable per active tab. Right now there's only one tab so the body
is always tweaks. The slider, button, and tab-pill helpers in
[SettingsHud.cs](../../Assets/_Project/Scripts/Gameplay/SettingsHud.cs)
(`AddButton`, `AddTabPill`, `BuildSlider`, `NewChild`, `FillParent`)
are designed to be reused.

**Esc collision.** [FollowCamera.cs](../../Assets/_Project/Scripts/Player/FollowCamera.cs)
also reacts to Esc to release the cursor. Both want the cursor freed,
so this is fine. The settings HUD doesn't try to re-capture; left-click
in the world re-locks via FollowCamera as before.
