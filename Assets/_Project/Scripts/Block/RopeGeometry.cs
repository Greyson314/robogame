using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Cell-grid geometry of a rope: chain length (in chassis cells) and
    /// the tip cell where a Hook / Mace must be placed. Lives in
    /// <see cref="Robogame.Block"/> so the placement rules + BFS +
    /// blueprint validator can reason about ropes without referencing
    /// <c>Robogame.Movement</c> (which would be a circular asmdef).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why cells, not metres.</b> The rope's tip block lives at
    /// <c>rope.cell + ChainCellCount × rope.up</c>; that cell must be
    /// derivable purely from per-block blueprint data (the entry's
    /// <see cref="ChassisBlueprint.Entry.Dims"/>.x). Using a Tweakable
    /// would put a placement-relevant value behind a setting and break
    /// the netcode contract (CLAUDE.md §1 — Tweakables don't affect
    /// gameplay outcomes).
    /// </para>
    /// <para>
    /// The <c>Robogame.Movement.RopeBlock</c> class still owns the
    /// verlet-sim sub-segment density (driven by the
    /// <c>Tweakables.RopeSegmentLength</c> knob), but length-in-cells is
    /// authoritative here.
    /// </para>
    /// </remarks>
    public static class RopeGeometry
    {
        /// <summary>Block-default rope length in chassis grid cells when
        /// the entry's Dims.x is 0 (matches the legacy default of 8
        /// segments × 0.5 m = 4 cells before the cell-semantic rename).</summary>
        public const int DefaultLengthCells = 4;
        /// <summary>Min / max range for the variant-config slider.</summary>
        public const int MinLengthCells = 1;
        public const int MaxLengthCells = 16;

        /// <summary>Resolve a rope's length-in-cells from a live block.</summary>
        public static int ChainCellCount(BlockBehaviour rope)
        {
            if (rope == null) return DefaultLengthCells;
            return Resolve(Mathf.RoundToInt(rope.Dims.x));
        }

        /// <summary>Entry-flavoured variant for the blueprint validator
        /// (pre-instantiation, no live BlockBehaviour).</summary>
        public static int ChainCellCount(ChassisBlueprint.Entry ropeEntry)
        {
            return Resolve(Mathf.RoundToInt(ropeEntry.Dims.x));
        }

        /// <summary>Cell where this rope's tip block (Hook / Mace) is
        /// placed: <c>rope.GridPosition + ChainCellCount × rope.Up</c>.</summary>
        public static Vector3Int TipCell(BlockBehaviour rope)
        {
            if (rope == null) return Vector3Int.zero;
            return rope.GridPosition + rope.Up * ChainCellCount(rope);
        }

        /// <summary>Entry-flavoured <see cref="TipCell(BlockBehaviour)"/>.</summary>
        public static Vector3Int TipCell(ChassisBlueprint.Entry ropeEntry)
        {
            return ropeEntry.Position + ropeEntry.EffectiveUp * ChainCellCount(ropeEntry);
        }

        private static int Resolve(int authored)
        {
            int raw = authored > 0 ? authored : DefaultLengthCells;
            return Mathf.Clamp(raw, MinLengthCells, MaxLengthCells);
        }
    }
}
