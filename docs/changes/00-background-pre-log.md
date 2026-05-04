# Background — initial refactor pass (pre-log)

The repo had already gone through one large hygiene pass before this
log started. Highlights, in case future contributors trip over the
patterns:

1. **Centralised block IDs.** All canonical IDs live as
   `public const string` in [BlockIds.cs](../../Assets/_Project/Scripts/Block/BlockIds.cs).
   Don't use string literals; they break on rename.
2. **`BlockBinder` base class** at
   [BlockBinder.cs](../../Assets/_Project/Scripts/Block/BlockBinder.cs).
   Subclasses (Wheel/Aero/Weapon) override only `ShouldBind` + `Bind`.
3. **`BlockVisuals` rig helpers** at
   [BlockVisuals.cs](../../Assets/_Project/Scripts/Block/BlockVisuals.cs):
   `HideHostMesh`, `GetOrCreateChild`, `GetOrCreatePrimitiveChild`.
   Used by every block type with a mesh rig.
4. **Editor scaffolder split.** [SceneScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
   is just menu commands now. Tuning SO load-or-create lives in
   [TuningAssets.cs](../../Assets/_Project/Scripts/Tools/Editor/TuningAssets.cs).
   Editor utilities live in
   [ScaffoldHelpers.cs](../../Assets/_Project/Scripts/Tools/Editor/ScaffoldHelpers.cs).
   Block layouts live in
   [RobotLayouts.cs](../../Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs).
5. **Tuning ScriptableObjects** under
   [Movement/Tuning/](../../Assets/_Project/Scripts/Movement/Tuning/).
   Pattern: optional `[SerializeField] private XxxTuning _tuning`;
   resolved-property reads `_tuning != null ? _tuning.X : _x`.
   Note: superseded for the *exposed* knobs by the new Tweakables
   registry. Inline + SO defaults still resolve when a Tweakables key
   isn't registered.
6. **Pooled tracers + `Physics.RaycastNonAlloc`** everywhere on hot
   paths. See [HitscanGun.cs](../../Assets/_Project/Scripts/Combat/HitscanGun.cs),
   [WheelBlock.cs](../../Assets/_Project/Scripts/Movement/WheelBlock.cs),
   [RobotDrive.cs](../../Assets/_Project/Scripts/Movement/RobotDrive.cs),
   [WeaponMount.cs](../../Assets/_Project/Scripts/Combat/WeaponMount.cs).
7. **Event-driven wheel cache** in [GroundDriveSubsystem.cs](../../Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs):
   `BlockGrid.BlockPlaced`/`BlockRemoving` rather than periodic
   `GetComponentsInChildren`.
