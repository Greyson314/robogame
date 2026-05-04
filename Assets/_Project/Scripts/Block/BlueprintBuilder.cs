using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Fluent helper for assembling <see cref="ChassisBlueprint.Entry"/> lists
    /// without a wall of <c>list.Add(new Entry(id, new Vector3Int(...)))</c>
    /// calls. Returns a <see cref="BlueprintPlan"/> the editor scaffolder can
    /// write into a real ScriptableObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in <see cref="Robogame.Block"/> so the API is reachable both
    /// at edit-time (scaffolders authoring presets) and at runtime (the
    /// in-game garage editor when player builds reach the same shape).
    /// </para>
    /// <para>
    /// The builder does not allocate the blueprint asset — it produces a
    /// data-only <see cref="BlueprintPlan"/>. The editor scaffolder is the
    /// thing that writes ScriptableObjects to disk; runtime callers can
    /// turn a plan into an in-memory blueprint via
    /// <see cref="BlueprintPlan.ToBlueprint"/>.
    /// </para>
    /// </remarks>
    public sealed class BlueprintBuilder
    {
        private string _displayName;
        private ChassisKind _kind;
        private bool _rotorsGenerateLift;
        private readonly List<ChassisBlueprint.Entry> _entries = new List<ChassisBlueprint.Entry>(64);

        /// <summary>Start a new builder. Always go through this — the
        /// constructor is private so a missing display name can't slip past.</summary>
        public static BlueprintBuilder Create(string displayName, ChassisKind kind)
            => new BlueprintBuilder(displayName, kind);

        private BlueprintBuilder(string displayName, ChassisKind kind)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("displayName must not be empty", nameof(displayName));
            _displayName = displayName;
            _kind = kind;
        }

        // -----------------------------------------------------------------
        // Single placements
        // -----------------------------------------------------------------

        /// <summary>Place a single block. Equivalent to <c>Block(blockId, new Vector3Int(x,y,z))</c>.</summary>
        public BlueprintBuilder Block(string blockId, int x, int y, int z)
            => Block(blockId, new Vector3Int(x, y, z));

        /// <summary>Place a single block with default upright (+Y) orientation.</summary>
        public BlueprintBuilder Block(string blockId, Vector3Int position)
        {
            _entries.Add(new ChassisBlueprint.Entry(blockId, position));
            return this;
        }

        /// <summary>Place a single block with an explicit mount-up direction.</summary>
        public BlueprintBuilder Block(string blockId, Vector3Int position, Vector3Int up)
        {
            _entries.Add(new ChassisBlueprint.Entry(blockId, position, up));
            return this;
        }

        // -----------------------------------------------------------------
        // Linear and rectangular fills
        // -----------------------------------------------------------------

        /// <summary>
        /// Fill an axis-aligned line of cells from <paramref name="from"/> to
        /// <paramref name="to"/> (inclusive). Throws if the endpoints aren't
        /// axis-aligned (two of the three axes must agree).
        /// </summary>
        public BlueprintBuilder Row(string blockId, Vector3Int from, Vector3Int to)
        {
            int axesChanging = (from.x != to.x ? 1 : 0)
                             + (from.y != to.y ? 1 : 0)
                             + (from.z != to.z ? 1 : 0);
            if (axesChanging > 1)
                throw new ArgumentException(
                    $"Row from {from} to {to} is not axis-aligned ({axesChanging} axes change).");
            if (axesChanging == 0) return Block(blockId, from);

            Vector3Int delta = to - from;
            Vector3Int step = new Vector3Int(
                delta.x == 0 ? 0 : delta.x / Mathf.Abs(delta.x),
                delta.y == 0 ? 0 : delta.y / Mathf.Abs(delta.y),
                delta.z == 0 ? 0 : delta.z / Mathf.Abs(delta.z));
            int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));
            for (int i = 0; i <= steps; i++)
                Block(blockId, from + step * i);
            return this;
        }

        /// <summary>
        /// Fill a 3D rectangular block (inclusive bounds) with the given
        /// block id. Useful for hulls, fortress dummies, and the like.
        /// </summary>
        public BlueprintBuilder Box(string blockId, Vector3Int from, Vector3Int to)
        {
            Vector3Int min = Vector3Int.Min(from, to);
            Vector3Int max = Vector3Int.Max(from, to);
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                        Block(blockId, new Vector3Int(x, y, z));
            return this;
        }

        // -----------------------------------------------------------------
        // Symmetry
        // -----------------------------------------------------------------

        /// <summary>
        /// Apply <paramref name="action"/> and then mirror every cell it
        /// produced across the X=0 plane. Cells with x==0 are NOT
        /// duplicated. The mount-up vector is mirrored too: a block with
        /// up=(1,0,0) on the +X side becomes up=(-1,0,0) on the -X side.
        /// </summary>
        public BlueprintBuilder MirrorX(Action<BlueprintBuilder> action)
        {
            int countBefore = _entries.Count;
            action(this);
            int countAfter = _entries.Count;
            for (int i = countBefore; i < countAfter; i++)
            {
                ChassisBlueprint.Entry e = _entries[i];
                if (e.Position.x == 0) continue;
                _entries.Add(new ChassisBlueprint.Entry(
                    e.BlockId,
                    new Vector3Int(-e.Position.x, e.Position.y, e.Position.z),
                    new Vector3Int(-e.EffectiveUp.x, e.EffectiveUp.y, e.EffectiveUp.z)));
            }
            return this;
        }

        /// <summary>Same as <see cref="MirrorX"/> but across the Z=0 plane.</summary>
        public BlueprintBuilder MirrorZ(Action<BlueprintBuilder> action)
        {
            int countBefore = _entries.Count;
            action(this);
            int countAfter = _entries.Count;
            for (int i = countBefore; i < countAfter; i++)
            {
                ChassisBlueprint.Entry e = _entries[i];
                if (e.Position.z == 0) continue;
                _entries.Add(new ChassisBlueprint.Entry(
                    e.BlockId,
                    new Vector3Int(e.Position.x, e.Position.y, -e.Position.z),
                    new Vector3Int(e.EffectiveUp.x, e.EffectiveUp.y, -e.EffectiveUp.z)));
            }
            return this;
        }

        // -----------------------------------------------------------------
        // Rotor + foil ring
        // -----------------------------------------------------------------

        /// <summary>
        /// Place a rotor at <paramref name="cell"/> and ring four foils
        /// around the mechanism cell (one cell along the spin axis from
        /// <paramref name="cell"/>). Also drops an invisible structural
        /// cube at the mechanism cell — that cell anchors the foils to
        /// the chassis grid for connectivity, and the rotor block hides
        /// its renderer at runtime so the visual reads as one continuous
        /// "stem + mechanism" unit.
        /// </summary>
        public BlueprintBuilder RotorWithFoils(Vector3Int cell, Vector3Int spinAxis = default)
        {
            if (spinAxis == default) spinAxis = Vector3Int.up;
            Block(BlockIds.Rotor, cell, spinAxis);
            Vector3Int mechanism = cell + spinAxis;
            // Mechanism cap: invisible cube. Provides connectivity for the
            // four foil cells; rotor's BuildBlockVisual hides the host
            // mesh at this cell so the rotor's mast + disc + bars read
            // as the single "rotor head" silhouette.
            Block(BlockIds.Cube, mechanism);
            // Four foils ringed around the mechanism cell, perpendicular
            // to the spin axis.
            Vector3Int a, b;
            LateralAxes(spinAxis, out a, out b);
            Block(BlockIds.Aero, mechanism + a);
            Block(BlockIds.Aero, mechanism - a);
            Block(BlockIds.Aero, mechanism + b);
            Block(BlockIds.Aero, mechanism - b);
            return this;
        }

        /// <summary>
        /// Place a bare rotor (no mechanism cap, no foils) — cosmetic spin
        /// only. Used when the player wants the rotor visual without
        /// committing to lift production (e.g. tail rotors, chassis decoration).
        /// </summary>
        public BlueprintBuilder RotorBare(Vector3Int cell, Vector3Int spinAxis = default)
        {
            if (spinAxis == default) spinAxis = Vector3Int.up;
            Block(BlockIds.Rotor, cell, spinAxis);
            return this;
        }

        // -----------------------------------------------------------------
        // Rope with adopted tip
        // -----------------------------------------------------------------

        /// <summary>
        /// Place a rope at <paramref name="ropeCell"/> with a Hook block
        /// directly below it (so <see cref="Movement.RopeBlock"/> adopts
        /// the hook as its tip at game-start). Both cells go in the
        /// chassis grid; visual swap happens at runtime.
        /// </summary>
        public BlueprintBuilder RopeWithHook(Vector3Int ropeCell)
        {
            Block(BlockIds.Rope, ropeCell);
            Block(BlockIds.Hook, ropeCell + Vector3Int.down);
            return this;
        }

        /// <summary>
        /// Place a rope at <paramref name="ropeCell"/> with a Mace block
        /// directly below it. Heavier than a hook (default 2.0 kg vs 0.5
        /// kg) so the chain swings with more momentum and hits harder.
        /// </summary>
        public BlueprintBuilder RopeWithMace(Vector3Int ropeCell)
        {
            Block(BlockIds.Rope, ropeCell);
            Block(BlockIds.Mace, ropeCell + Vector3Int.down);
            return this;
        }

        // Pick two unit Vector3Ints perpendicular to `axis` (each axis-aligned).
        private static void LateralAxes(Vector3Int axis, out Vector3Int a, out Vector3Int b)
        {
            if (Mathf.Abs(axis.x) > 0) { a = new Vector3Int(0, 1, 0); b = new Vector3Int(0, 0, 1); }
            else if (Mathf.Abs(axis.y) > 0) { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 0, 1); }
            else { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 1, 0); }
        }

        // -----------------------------------------------------------------
        // Flags
        // -----------------------------------------------------------------

        public BlueprintBuilder RotorsGenerateLift(bool value = true)
        {
            _rotorsGenerateLift = value;
            return this;
        }

        public BlueprintBuilder DisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("displayName must not be empty", nameof(name));
            _displayName = name;
            return this;
        }

        // -----------------------------------------------------------------
        // Output
        // -----------------------------------------------------------------

        /// <summary>Materialise the accumulated state into a <see cref="BlueprintPlan"/>.</summary>
        public BlueprintPlan Build()
            => new BlueprintPlan(_displayName, _kind, _entries.ToArray(), _rotorsGenerateLift);

        /// <summary>
        /// Convenience: build, validate, and throw on errors. Use this in
        /// scaffolders so a typo can never produce a broken asset on disk.
        /// </summary>
        public BlueprintPlan BuildValidated(BlockDefinitionLibrary library = null)
        {
            BlueprintPlan plan = Build();
            BlueprintValidationResult result = BlueprintValidator.Validate(plan, library);
            if (!result.IsValid)
                throw new InvalidOperationException(
                    $"Blueprint '{_displayName}' failed validation:\n{result}");
            return plan;
        }
    }

    /// <summary>
    /// Immutable data tuple describing a chassis layout. Either the
    /// editor scaffolder writes this into a <see cref="ChassisBlueprint"/>
    /// asset on disk, or runtime code calls <see cref="ToBlueprint"/> to
    /// get an in-memory ScriptableObject instance for spawning.
    /// </summary>
    public readonly struct BlueprintPlan
    {
        public readonly string DisplayName;
        public readonly ChassisKind Kind;
        public readonly ChassisBlueprint.Entry[] Entries;
        public readonly bool RotorsGenerateLift;

        public BlueprintPlan(string displayName, ChassisKind kind,
            ChassisBlueprint.Entry[] entries, bool rotorsGenerateLift)
        {
            DisplayName = displayName;
            Kind = kind;
            Entries = entries ?? Array.Empty<ChassisBlueprint.Entry>();
            RotorsGenerateLift = rotorsGenerateLift;
        }

        /// <summary>
        /// Materialise this plan into an in-memory <see cref="ChassisBlueprint"/>.
        /// The result is NOT persisted to disk — call this only when you
        /// want a runtime instance (e.g. test scaffolds, garage previews).
        /// </summary>
        public ChassisBlueprint ToBlueprint()
        {
            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = DisplayName;
            bp.Kind = Kind;
            bp.SetEntries(Entries);
            bp.RotorsGenerateLift = RotorsGenerateLift;
            return bp;
        }
    }
}
