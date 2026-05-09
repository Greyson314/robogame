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
    }
}
