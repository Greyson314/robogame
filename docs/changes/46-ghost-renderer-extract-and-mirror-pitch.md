# 46 — BlockGhostRenderer extract + mirror-pitch fix + placement HUD

> Follow-up to session 45's structural refactor. Lands the
> BlockGhostRenderer extraction (queued in 45's follow-ups), fixes the
> mirror-pitch sign behaviour the user reported visually, and adds the
> §3a Bug 4 / §3.10 cheap-fix HUD overlay so placement-rule rejections
> are diagnosable from inside the game.

## What changed

### BlockGhostRenderer + PlacementFeedbackHud extracted

- New [`BlockGhostRenderer.cs`](../../Assets/_Project/Scripts/Gameplay/BlockGhostRenderer.cs)
  owns the primary + mirror ghost objects, materials, and the
  rebuild-when-inputs-change cache. Driven by `BlockEditor` per-frame
  via a `GhostRequest` struct.
- New [`PlacementFeedbackHud.cs`](../../Assets/_Project/Scripts/Gameplay/PlacementFeedbackHud.cs)
  renders a bottom-right label with the placement-rule rejection
  reason + cell coordinates. Maps `PlacementRules.PlacementError`
  enum to one short user-readable line per case.
- [`BlockEditor`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  drops ~150 lines of ghost lifecycle code, replaced with two
  delegating methods (`DriveGhostRenderer`, `DriveFeedbackHud`).
  Tracks `_lastPlacementError` so the HUD has the actionable error to
  display when the primary placement is rejected.
- [`GarageController`](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)'s
  `EnsureBuildModeWired` instantiates and wires both new components.

### Mirror pitch — sign-flip when up reflects (§3a Bug 1, real fix)

The previous landing made `MirrorPitch` an identity function under the
assumption that mirrored side wings should pitch the same way for
"symmetric trim." The user demonstrated visually that this is wrong:
a wing on the left side tilted tip-down mirrors to a wing on the
right side tilted tip-UP. Asymmetric.

Re-derived the math: for a side-mounted wing (up=±X) under
MirrorAxis.X, the wing's local +Y (the span direction) flips to the
opposite chassis-world direction. The chord-axis pitch rotation lands
on the same world axis on both sides, so the SAME pitch sign tilts
the tip OPPOSITE ways relative to the chassis. To produce
visually-symmetric tip behaviour the mirrored pitch must be **negated**.

Fix:
- [`BlockMirror.MirrorPitch`](../../Assets/_Project/Scripts/Block/BlockMirror.cs)
  now takes `(pitch, sourceUp, axis)` and returns `-pitch` iff the
  mirror flips the source's mount-up component on the mirror axis.
  Top-mounted wings (up=+Y under either axis) keep pitch as identity.
- [`IBlueprintEntryTransform`](../../Assets/_Project/Scripts/Block/IBlueprintEntryTransform.cs)
  signature shifted to take `in Entry source` per method, so
  `MirrorTransform.TransformPitch` can read `source.EffectiveUp`
  without state-capture brittleness.
- [`BlueprintBuilder.MirrorX/Z`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs),
  [`BlockEditor`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)'s
  ghost + place paths, and [`BuildSession.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)
  all pass the source up through to `MirrorPitch`.
- Tests updated:
  - `BlueprintBuilderTests.MirrorX_NegatesPitch_WhenUpFlipsAcrossAxis`
    (renamed from `MirrorX_PreservesPitch`, asserts the new rule).
  - `BlueprintBuilderTests.MirrorX_PreservesPitch_WhenUpHasNoXComponent`
    (top-mount case — pitch preserved).
  - `BlueprintEntryTransformTests` updated for the new interface
    signature + the X/Z/identity variants of MirrorPitch.

## What this doesn't fix

The user also reported "wings cannot be placed in many places" with
no obvious pattern. Per §3.10 of the architecture review, that's the
targeting-vs-rules-disagreement problem: targeting picks a cell via
collider physics, rules ask "is this candidate legal" via grid
topology, and the two can disagree (e.g. when the raycast lands on a
sibling wing's host-cube collider instead of the central cube's).

The HUD overlay landed in this session is the doc's "cheap, immediate"
diagnosis fix — it shows the player *which* cell the targeting picked
and *why* the rule rejected it. From there:
- If the cell shown isn't where the player thought they were aiming,
  they can orbit the camera and try again (targeting hit the wrong
  collider — known bug, structural fix is the §3.10 "medium" or
  "large" rebuild).
- If the cell matches their intent, the rule reason explains why the
  placement is genuinely illegal (e.g. host is leaf — Robocraft rule).

The "medium" fix (cell-snap targeting to the nearest legal face) is
queued for a follow-up session — it's its own design pass.

## Files

**New:** `BlockGhostRenderer.cs`, `PlacementFeedbackHud.cs`.

**Modified:** `BlockMirror.cs`, `IBlueprintEntryTransform.cs`,
`BlueprintBuilder.cs`, `BlockEditor.cs`, `BuildSession.cs`,
`GarageController.cs`, plus the test files
`BlueprintBuilderTests.cs` and `BlueprintEntryTransformTests.cs`.

## Line-count snapshot

`BlockEditor.cs`: 783 → 636 lines. The doc's ~150-line target
requires further extraction (targeting raycast, CPU/stats readout,
mirror orchestration), queued for a future pass.
