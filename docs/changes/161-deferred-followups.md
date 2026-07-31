# 161 — Deferred follow-ups from the spring-cleaning arc

Works through the follow-up list parked at the end of session 160.

## Shipped

1. **Flip + hook-release verbs on IInputSource (MP-blocking).**
   `FlipPressed` / `HookReleasePressed` added to the interface.
   - `PlayerInputHandler`: H (flip) and R (release — deliberately still
     shared with reload; that pairing is the pre-existing behaviour).
     Both get the HUD-pointer suppression.
   - `FlipController` / `RobotHookReleaseInput` consume the verbs via
     `GetComponentInParent<IInputSource>` (late-resolve, LOG-132 class)
     — no more local `Keyboard.current` polls. FlipController's
     `_flipKey` field is gone (runtime-added component, nothing
     serialized it).
   - `InputCommand` wire: two new serialized bools; owner send path
     (`NetworkRobotMovement`) packs them. Both bots stub false.
2. **WeaponAmmoState local-player check MP-ready.** New
   `IInputSourceWrapper` (Input assembly) with `InnerSource`;
   `NetworkInputSource` implements it (null on the server copy —
   correctly "not local"). `IsLocalPlayerChassis` unwraps through
   wrappers before the `PlayerInputHandler` type check. No Combat →
   Network asmdef edge needed.
3. **Bot-AI shared base.** New `BotInputSourceBase` owns the
   IInputSource plumbing (outputs, player-verb stubs, cached
   `_drive`/`_robot`, HealthFraction + test override, Update →
   abstract `UpdateBrain`, `ZeroOutputs`, `ComputeFacingDot`) plus the
   pure steering statics (`ComputeSteer`, `ComputeSteerForHeading` —
   moved from GroundBotInputSource; existing
   `GroundBotInputSource.ComputeSteer` call sites resolve through the
   derived type, so nothing else changed). Ground/Air keep their own
   state enums — different vocabularies, a shared enum would put dead
   states on every bot. ~120 duplicated lines deleted.
4. **BlockGhostFactory per-block rig recipes.** The id-switch (the last
   hardcoded id list of its kind post-ADR-0008) is now a
   `Dictionary<string, GhostRecipe>` registry with a `GhostContext`
   param struct; unknown ids keep the cube fallback. One registration
   line per new block. TRACE[ADR-0008] added.

## Skipped — needs design sign-off

5. **VariantConfigPanel declarative tune schema.** The panel is ~1,600
   lines of hand-anchored UGUI across 8 block-family sections. A
   schema rework is a rebuild of working, test-uncovered UI whose
   verification is visual (live screenshots), so it needs a plan
   review first. Proposed shape, for when it's picked up:
   - A `TuneField` descriptor: kind (slider / int-slider / chooser),
     label, min/max/default, snap step, cache target (DimsX/DimsY/
     DimsZ/Pitch/Teeter/Config/ConcoctionId), tip text, readout hook.
   - Per-block-id `TuneSchema` = list of TuneFields + optional presets
     + title. Registry keyed by id (ghost-recipe pattern).
   - One generic section builder consumes a schema; bespoke bits
     (foil advanced expander, concoction list) stay hand-built until
     proven schema-expressible.

## Verification

- `run-tests.sh`: EditMode 495/496 (pre-existing inconclusive),
  PlayMode 122/123, 0 failed.
- One PlayMode flake observed mid-session (MPTK `fluid_voice` NRE,
  Tuba-D1 sample, during `Garage_Idle_Baseline`) — audio plugin, not
  reproducible on re-run, unrelated to these changes. Known MPTK
  soundfont class; noting for the flake tally.
- Unity console: 0 errors after recompile.
