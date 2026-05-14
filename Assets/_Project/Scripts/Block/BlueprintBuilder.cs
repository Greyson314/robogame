using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure-data builder for <see cref="ChassisBlueprint.Entry"/> arrays
    /// — appends entries to a list and returns a <see cref="BlueprintPlan"/>.
    /// Does NOT route through the rules engine and does NOT trigger
    /// auto-companion cascades. Use cases: hand-authored validator test
    /// inputs, ASCII-snapshot helpers, anywhere a "raw entry layout" is
    /// the unit of work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For default chassis presets and any "what would the player produce"
    /// authoring, use <c>Robogame.Tools.Editor.ScriptedChassisBuilder</c>
    /// instead — it drives <see cref="Gameplay.BuildSession.TryPlace"/>
    /// against a real <see cref="BlockGrid"/>, so the rules engine and
    /// auto-companion logic run on every placement. Anything authored
    /// here is one step away from "user could have built this" — handy
    /// for validator unit tests that need a contrived bad-shape, but the
    /// wrong choice for shipping default robots.
    /// </para>
    /// <para>
    /// The builder does not allocate a <see cref="ChassisBlueprint"/>
    /// SO — it produces a data-only <see cref="BlueprintPlan"/>. Tests
    /// can materialise via <see cref="BlueprintPlan.ToBlueprint"/>.
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

        /// <summary>
        /// Place a single block with explicit mount up + per-instance dims
        /// (foil span/thickness/chord, rope segment count, ...). Used by
        /// tests that exercise the swept-volume occupancy rules.
        /// </summary>
        public BlueprintBuilder Block(string blockId, Vector3Int position, Vector3Int up, Vector3 dims)
        {
            _entries.Add(new ChassisBlueprint.Entry(blockId, position, up, dims));
            return this;
        }

        /// <summary>
        /// Place a single block with explicit mount up, dims, and pitch
        /// (foil incidence / rotor collective in degrees). Used by the
        /// new-paradigm plane / heli blueprints that need built-in foil
        /// pitch without hand-tilting the mount.
        /// </summary>
        public BlueprintBuilder Block(string blockId, Vector3Int position, Vector3Int up, Vector3 dims, float pitchDeg)
        {
            _entries.Add(new ChassisBlueprint.Entry(blockId, position, up, dims, pitchDeg));
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
        public BlueprintBuilder MirrorX(Action<BlueprintBuilder> action) => Mirror(MirrorAxis.X, action);

        /// <summary>Same as <see cref="MirrorX"/> but across the Z=0 plane.</summary>
        public BlueprintBuilder MirrorZ(Action<BlueprintBuilder> action) => Mirror(MirrorAxis.Z, action);

        // Single mirror implementation, composed over the
        // IBlueprintEntryTransform contract. Adding a new field to
        // ChassisBlueprint.Entry forces MirrorTransform to handle it
        // explicitly — no more silent drops on the mirrored side.
        private BlueprintBuilder Mirror(MirrorAxis axis, Action<BlueprintBuilder> action)
        {
            int countBefore = _entries.Count;
            action(this);
            int countAfter = _entries.Count;
            MirrorTransform transform = new MirrorTransform(axis);
            for (int i = countBefore; i < countAfter; i++)
            {
                ChassisBlueprint.Entry e = _entries[i];
                if (BlockMirror.IsOnPlane(e.Position, axis)) continue;
                _entries.Add(BlueprintEntryTransform.Apply(transform, e));
            }
            return this;
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
