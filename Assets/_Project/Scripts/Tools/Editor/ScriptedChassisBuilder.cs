using System;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor-time chassis authoring that runs through the SAME verbs the
    /// player uses in the garage: <see cref="BuildSession.TryPlace"/>
    /// against a real <see cref="BlockGrid"/>, with the rules engine and
    /// auto-companion cascade enforced. The output blueprint is what the
    /// player would have produced by clicking these placements in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Defaults previously authored entries as raw
    /// <c>Entry[]</c> arrays via <see cref="BlueprintBuilder"/>. That path
    /// duplicates runtime behaviour (rotor mechanism cube, mirror axis
    /// pitch normalization, rope-bridge connectivity) in two places and
    /// silently drifts when one side gains a feature. With this harness,
    /// the default presets and the player's user-built bots are the same
    /// thing — different inputs to the same verb.
    /// </para>
    /// <para>
    /// <b>Edit-mode safety.</b> The temp parent GameObject is held
    /// inactive while placements happen, so prefab children's
    /// <c>Awake</c>/<c>OnEnable</c> don't fire during scaffolding. We
    /// only need the data side (grid + blueprint sync), not live physics
    /// or audio. <c>BlockBehaviour.Initialize</c> is called explicitly
    /// by <see cref="BlockGrid.PlaceBlock"/>, which sets the fields the
    /// blueprint reads back. After <see cref="Build"/> snapshots the
    /// blueprint, the temp hierarchy is destroyed.
    /// </para>
    /// <para>
    /// <b>Hard-fails.</b> Every <see cref="Place"/> throws if the rules
    /// engine rejects the candidate. A scripted build that wouldn't pass
    /// player-side placement rules is a bug in the script, not a default
    /// the scaffolder should silently downgrade. Pairs with the strict
    /// validation in <see cref="GameplayScaffolder"/>.
    /// </para>
    /// </remarks>
    public sealed class ScriptedChassisBuilder : IDisposable
    {
        private readonly GameObject _root;
        private readonly BlockGrid _grid;
        private readonly BuildSession _session;
        private readonly ChassisBlueprint _bp;
        private readonly BlockDefinitionLibrary _library;
        private readonly string _displayName;
        private readonly ChassisKind _kind;
        private bool _disposed;

        public static ScriptedChassisBuilder Create(string displayName, ChassisKind kind, BlockDefinitionLibrary library)
            => new ScriptedChassisBuilder(displayName, kind, library);

        private ScriptedChassisBuilder(string displayName, ChassisKind kind, BlockDefinitionLibrary library)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("displayName must not be empty", nameof(displayName));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _displayName = displayName;
            _kind = kind;

            // Hold the root inactive so prefab Awake/OnEnable doesn't run.
            // BlockGrid.PlaceBlock's Instantiate calls land in an inactive
            // hierarchy; we only need the data fields set by
            // BlockBehaviour.Initialize (called explicitly by PlaceBlock).
            _root = new GameObject("ScriptedChassisBuilder_Temp");
            _root.SetActive(false);
            _root.hideFlags = HideFlags.HideAndDontSave;
            _grid = _root.AddComponent<BlockGrid>();

            _bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            _bp.hideFlags = HideFlags.HideAndDontSave;
            _bp.DisplayName = displayName;
            _bp.Kind = kind;

            _session = new BuildSession();
            _session.Bind(_grid, _bp, library);
        }

        // -----------------------------------------------------------------
        // Single-placement verbs — same arguments TryPlace takes.
        // -----------------------------------------------------------------

        public ScriptedChassisBuilder Place(string blockId, int x, int y, int z)
            => Place(blockId, new Vector3Int(x, y, z), Vector3Int.up, Vector3.zero, 0f);

        public ScriptedChassisBuilder Place(string blockId, Vector3Int cell)
            => Place(blockId, cell, Vector3Int.up, Vector3.zero, 0f);

        public ScriptedChassisBuilder Place(string blockId, Vector3Int cell, Vector3Int up)
            => Place(blockId, cell, up, Vector3.zero, 0f);

        public ScriptedChassisBuilder Place(string blockId, Vector3Int cell, Vector3Int up, Vector3 dims)
            => Place(blockId, cell, up, dims, 0f);

        /// <summary>
        /// Place a single block at <paramref name="cell"/>. World-intent
        /// pitch is normalized to the block's local frame by the session
        /// (positive = "tilt tip toward sky" on every mount face). Throws
        /// if the rules engine rejects.
        /// </summary>
        public ScriptedChassisBuilder Place(string blockId, Vector3Int cell, Vector3Int up, Vector3 dims, float worldPitch)
        {
            BlockDefinition def = _library.Get(blockId);
            if (def == null)
                throw new InvalidOperationException($"ScriptedChassisBuilder '{_displayName}': unknown block id '{blockId}' (not in BlockDefinitionLibrary).");
            if (up == Vector3Int.zero) up = Vector3Int.up;

            BuildSession.PlaceOutcome outcome = _session.TryPlace(def, cell, up, dims, worldPitch);
            if (!outcome.PrimarySucceeded)
            {
                throw new InvalidOperationException(
                    $"ScriptedChassisBuilder '{_displayName}': TryPlace({blockId} @ {cell} up={up}) " +
                    $"rejected with {outcome.Primary}. " +
                    "A scripted build that can't pass the rules engine is a script bug — " +
                    "fix the placement order / host topology to match what the player would do.");
            }
            return this;
        }

        // -----------------------------------------------------------------
        // Mirror — pushes mirror state onto the session for the duration
        // of the action. TryPlace itself handles the mirrored side via
        // BuildSession.MirrorEnabled, so this is just a state toggle.
        // -----------------------------------------------------------------

        public ScriptedChassisBuilder MirrorX(Action<ScriptedChassisBuilder> action) => Mirror(MirrorAxis.X, action);
        public ScriptedChassisBuilder MirrorZ(Action<ScriptedChassisBuilder> action) => Mirror(MirrorAxis.Z, action);

        private ScriptedChassisBuilder Mirror(MirrorAxis axis, Action<ScriptedChassisBuilder> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            bool prevEnabled = _session.MirrorEnabled;
            MirrorAxis prevAxis = _session.MirrorAxis;
            _session.SetMirrorAxis(axis);
            _session.SetMirrorEnabled(true);
            try { action(this); }
            finally
            {
                _session.SetMirrorEnabled(prevEnabled);
                _session.SetMirrorAxis(prevAxis);
            }
            return this;
        }

        // -----------------------------------------------------------------
        // Linear / rectangular fills — pure convenience over Place, kept
        // because hand-placing a 35-cell hull is painful. Each cell still
        // routes through TryPlace individually so rules still gate.
        // -----------------------------------------------------------------

        /// <summary>
        /// Fill an axis-aligned line of cells from <paramref name="from"/> to
        /// <paramref name="to"/> inclusive. Throws if the endpoints aren't
        /// axis-aligned.
        /// </summary>
        public ScriptedChassisBuilder Row(string blockId, Vector3Int from, Vector3Int to)
        {
            int axesChanging = (from.x != to.x ? 1 : 0)
                             + (from.y != to.y ? 1 : 0)
                             + (from.z != to.z ? 1 : 0);
            if (axesChanging > 1)
                throw new ArgumentException($"Row from {from} to {to} is not axis-aligned ({axesChanging} axes change).");
            if (axesChanging == 0) return Place(blockId, from);

            Vector3Int delta = to - from;
            Vector3Int step = new Vector3Int(
                delta.x == 0 ? 0 : delta.x / Mathf.Abs(delta.x),
                delta.y == 0 ? 0 : delta.y / Mathf.Abs(delta.y),
                delta.z == 0 ? 0 : delta.z / Mathf.Abs(delta.z));
            int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));
            for (int i = 0; i <= steps; i++)
                Place(blockId, from + step * i);
            return this;
        }

        /// <summary>Fill a 3D rectangular block (inclusive bounds) with the given block id.</summary>
        public ScriptedChassisBuilder Box(string blockId, Vector3Int from, Vector3Int to)
        {
            Vector3Int min = Vector3Int.Min(from, to);
            Vector3Int max = Vector3Int.Max(from, to);
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                        Place(blockId, new Vector3Int(x, y, z));
            return this;
        }

        // -----------------------------------------------------------------
        // Skip / fill helpers — sometimes you want "fill this region but
        // skip the CPU cell." Replaces the per-loop "if (x==0 && z==0) continue".
        // -----------------------------------------------------------------

        /// <summary>
        /// Fill a 3D rectangular block (inclusive) with <paramref name="blockId"/>
        /// but SKIP <paramref name="skip"/>. Use for the common "floor of
        /// cubes with a CPU at centre" pattern.
        /// </summary>
        public ScriptedChassisBuilder BoxSkip(string blockId, Vector3Int from, Vector3Int to, Vector3Int skip)
        {
            Vector3Int min = Vector3Int.Min(from, to);
            Vector3Int max = Vector3Int.Max(from, to);
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                    {
                        Vector3Int cell = new Vector3Int(x, y, z);
                        if (cell == skip) continue;
                        Place(blockId, cell);
                    }
            return this;
        }

        // -----------------------------------------------------------------
        // High-level helpers — same outcome as the player clicking the
        // equivalent sequence in the garage. Each helper bottoms out in
        // Place(), so every step still passes through BuildSession.TryPlace
        // and the rules engine. If the rules reject any step, the whole
        // helper throws — exactly what the player would see as a red
        // ghost in the editor.
        // -----------------------------------------------------------------

        /// <summary>
        /// Place a rotor at <paramref name="cell"/> and ring four foils
        /// around the auto-companion mechanism cube on its spin-axis face.
        /// The rotor placement triggers the cube cascade in
        /// <see cref="BuildSession.TryPlace"/>; the foils then mount on
        /// the cube's four lateral faces.
        /// </summary>
        public ScriptedChassisBuilder RotorWithFoils(Vector3Int cell, Vector3Int spinAxis = default)
        {
            if (spinAxis == default) spinAxis = Vector3Int.up;
            // Rotor's up == its spin axis. The auto-companion mechanism
            // cube lands at cell + spinAxis automatically.
            Place(BlockIds.Rotor, cell, spinAxis);
            Vector3Int mechanism = cell + spinAxis;
            LateralAxes(spinAxis, out Vector3Int a, out Vector3Int b);
            Place(BlockIds.Aero, mechanism + a, a);
            Place(BlockIds.Aero, mechanism - a, -a);
            Place(BlockIds.Aero, mechanism + b, b);
            Place(BlockIds.Aero, mechanism - b, -b);
            return this;
        }

        /// <summary>
        /// Place a bare rotor (cosmetic / cube companion only, no foils).
        /// The auto-companion mechanism cube still lands on the spin-axis
        /// face — that's what makes the rotor structurally legal regardless
        /// of whether blades follow.
        /// </summary>
        public ScriptedChassisBuilder RotorBare(Vector3Int cell, Vector3Int spinAxis = default)
        {
            if (spinAxis == default) spinAxis = Vector3Int.up;
            Place(BlockIds.Rotor, cell, spinAxis);
            return this;
        }

        /// <summary>
        /// Place a rope with a Hook block at the chain's free end. The rope
        /// authors its length-in-cells into Dims.x; the hook lands at
        /// <c>ropeCell + lengthCells * up</c>, matching the rope-bridge
        /// connectivity edge in <see cref="PlacementRules"/>.
        /// </summary>
        public ScriptedChassisBuilder RopeWithHook(Vector3Int ropeCell, Vector3Int up, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithTip(BlockIds.Hook, ropeCell, up, lengthCells);

        public ScriptedChassisBuilder RopeWithHook(Vector3Int ropeCell, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithHook(ropeCell, Vector3Int.up, lengthCells);

        /// <summary>Same as <see cref="RopeWithHook(Vector3Int,Vector3Int,int)"/> but with a Mace tip.</summary>
        public ScriptedChassisBuilder RopeWithMace(Vector3Int ropeCell, Vector3Int up, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithTip(BlockIds.Mace, ropeCell, up, lengthCells);

        public ScriptedChassisBuilder RopeWithMace(Vector3Int ropeCell, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithMace(ropeCell, Vector3Int.up, lengthCells);

        /// <summary>Same as <see cref="RopeWithHook(Vector3Int,Vector3Int,int)"/> but with a Magnet tip (pull-field tool).</summary>
        public ScriptedChassisBuilder RopeWithMagnet(Vector3Int ropeCell, Vector3Int up, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithTip(BlockIds.Magnet, ropeCell, up, lengthCells);

        public ScriptedChassisBuilder RopeWithMagnet(Vector3Int ropeCell, int lengthCells = RopeGeometry.DefaultLengthCells)
            => RopeWithMagnet(ropeCell, Vector3Int.up, lengthCells);

        private ScriptedChassisBuilder RopeWithTip(string tipBlockId, Vector3Int ropeCell, Vector3Int up, int lengthCells)
        {
            if (up == Vector3Int.zero) up = Vector3Int.up;
            int len = Mathf.Clamp(lengthCells, RopeGeometry.MinLengthCells, RopeGeometry.MaxLengthCells);
            Place(BlockIds.Rope, ropeCell, up, new Vector3(len, 0f, 0f));
            Place(tipBlockId, ropeCell + up * len, up);
            return this;
        }

        // Pick two axis-aligned Vector3Ints perpendicular to `axis`.
        private static void LateralAxes(Vector3Int axis, out Vector3Int a, out Vector3Int b)
        {
            if (Mathf.Abs(axis.x) > 0) { a = new Vector3Int(0, 1, 0); b = new Vector3Int(0, 0, 1); }
            else if (Mathf.Abs(axis.y) > 0) { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 0, 1); }
            else { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 1, 0); }
        }

        // -----------------------------------------------------------------
        // Flags
        // -----------------------------------------------------------------

        public ScriptedChassisBuilder RotorsGenerateLift(bool value = true)
        {
            _bp.RotorsGenerateLift = value;
            return this;
        }

        // -----------------------------------------------------------------
        // Output
        // -----------------------------------------------------------------

        public ChassisKind Kind => _kind;
        public string DisplayName => _displayName;
        public int BlockCount => _grid != null ? _grid.Count : 0;

        /// <summary>
        /// Snapshot the resulting blueprint. The script's effect is the
        /// post-sync entry list (which includes auto-companion cubes,
        /// canonical sorting, and any auto-derived flags). Calling
        /// <see cref="Dispose"/> after this is mandatory; cleanest pattern
        /// is <c>using (var b = ScriptedChassisBuilder.Create(...)) { ... return b.Build(); }</c>.
        /// </summary>
        public BlueprintPlan Build()
        {
            // Ensure the blueprint reflects every placement (TryPlace
            // already calls SyncBlueprint, but a defensive re-sync costs
            // nothing and guards against external callers that bypassed
            // the verb).
            _session.SyncBlueprint();

            ChassisBlueprint.Entry[] src = _bp.Entries;
            var copy = new ChassisBlueprint.Entry[src.Length];
            Array.Copy(src, copy, src.Length);
            return new BlueprintPlan(_displayName, _kind, copy, _bp.RotorsGenerateLift);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Destroy the in-memory blueprint SO first so its OnDisable
            // can't fire after the GameObject tree is gone (defensive —
            // ChassisBlueprint has no OnDisable today).
            if (_bp != null) UnityEngine.Object.DestroyImmediate(_bp);
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }
    }
}
