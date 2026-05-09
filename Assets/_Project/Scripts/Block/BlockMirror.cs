using UnityEngine;

namespace Robogame.Block
{
    /// <summary>Which chassis-local plane the build-mode mirror reflects across.</summary>
    public enum MirrorAxis
    {
        /// <summary>Reflect across the chassis x = 0 plane (left/right symmetry — the Robocraft default).</summary>
        X,
        /// <summary>Reflect across the chassis z = 0 plane (front/back symmetry).</summary>
        Z,
    }

    /// <summary>
    /// Pure-data helpers for the build-mode mirror feature: take a
    /// chassis-local cell + mount-up and produce the corresponding values
    /// on the opposite side of the mirror plane. Used by
    /// <c>Robogame.Gameplay.BlockEditor</c> for placement / removal / ghost
    /// preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-block dims (foil span/thickness/chord, rope segment count) are
    /// scalar — they don't need mirroring. The mount-up vector reflects so
    /// a wing on a +X face mirrors to a wing on a -X face with up=-X. This
    /// matches what the player would manually re-author on the opposite
    /// side and keeps swept-bounds geometry symmetric.
    /// </para>
    /// </remarks>
    public static class BlockMirror
    {
        public static Vector3Int MirrorCell(Vector3Int cell, MirrorAxis axis)
        {
            switch (axis)
            {
                case MirrorAxis.X: return new Vector3Int(-cell.x, cell.y, cell.z);
                case MirrorAxis.Z: return new Vector3Int(cell.x, cell.y, -cell.z);
                default:           return cell;
            }
        }

        public static Vector3Int MirrorUp(Vector3Int up, MirrorAxis axis)
        {
            switch (axis)
            {
                case MirrorAxis.X: return new Vector3Int(-up.x, up.y, up.z);
                case MirrorAxis.Z: return new Vector3Int(up.x, up.y, -up.z);
                default:           return up;
            }
        }

        /// <summary>
        /// True if the cell lies on the mirror plane. Such cells should
        /// only be placed once — placing a mirror copy at the same cell
        /// would always fail (cell is already occupied).
        /// </summary>
        public static bool IsOnPlane(Vector3Int cell, MirrorAxis axis)
        {
            switch (axis)
            {
                case MirrorAxis.X: return cell.x == 0;
                case MirrorAxis.Z: return cell.z == 0;
                default:           return false;
            }
        }

        /// <summary>
        /// Reflect a per-block pitch (incidence in degrees) under
        /// <paramref name="axis"/>. Centralised here so the mirror rule
        /// for pitch lives next to the rules for cell + up — a future
        /// schema addition that changes "pitch is scalar" can be revisited
        /// in one place rather than chased through every consumer.
        /// </summary>
        /// <remarks>
        /// Pitch is rotation about the foil's local +Z (chord) axis.
        /// <see cref="BlockGrid.OrientationFromUp"/> derives the foil's
        /// local frame from its mount-up; for both
        /// <see cref="MirrorAxis.X"/> and <see cref="MirrorAxis.Z"/> the
        /// chord axis lands at the same chassis-world direction on both
        /// sides of the mirror, so pitch passes through unchanged.
        /// Symmetric main wings rely on this: a +2° pitch on each wing
        /// produces lift in the same chassis-world sense on both sides.
        /// </remarks>
        public static float MirrorPitch(float pitchDeg, MirrorAxis axis) => pitchDeg;
    }

    /// <summary>
    /// <see cref="IBlueprintEntryTransform"/> that reflects an entry
    /// across the chosen mirror plane. The build-mode mirror tool and
    /// the <see cref="BlueprintBuilder.MirrorX"/> /
    /// <see cref="BlueprintBuilder.MirrorZ"/> authoring helpers compose
    /// over this so reflection logic lives in one place. Cheap to mint —
    /// allocate one per mirror call, no caching needed.
    /// </summary>
    public sealed class MirrorTransform : IBlueprintEntryTransform
    {
        public MirrorAxis Axis { get; set; }

        public MirrorTransform(MirrorAxis axis) { Axis = axis; }

        public string TransformBlockId(string id) => id;
        public Vector3Int TransformPosition(Vector3Int p) => BlockMirror.MirrorCell(p, Axis);
        public Vector3Int TransformUp(Vector3Int u) => BlockMirror.MirrorUp(u, Axis);
        // Dims is scalar (span / thickness / chord); no axis flip applies.
        public Vector3 TransformDims(Vector3 d) => d;
        public float TransformPitch(float p) => BlockMirror.MirrorPitch(p, Axis);
    }
}
