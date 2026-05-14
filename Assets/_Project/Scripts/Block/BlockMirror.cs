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
        /// <paramref name="axis"/>, given the source entry's
        /// <paramref name="sourceUp"/>. Negates pitch iff the mirror
        /// flips the wing's mount-up direction; preserves it otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Pitch is rotation about the foil's local +Z (chord) axis.
        /// <see cref="BlockGrid.OrientationFromUp"/> derives the foil's
        /// local frame from its mount-up. For a side-mounted wing
        /// (up=±X) under <see cref="MirrorAxis.X"/>, the up flips, the
        /// span direction flips with it, and the same pitch tilts the
        /// tip the OPPOSITE way on the mirrored side — that's the
        /// asymmetry users hit on heli blade rings + plane wings. To
        /// produce visually-symmetric tilt the mirrored pitch must be
        /// negated.
        /// </para>
        /// <para>
        /// For mounts whose up has no component on the mirror axis
        /// (e.g. a top-mounted wing with up=+Y under
        /// <see cref="MirrorAxis.X"/>), the up doesn't change and the
        /// chord-axis rotation lands on the same world direction on
        /// both sides — preserving pitch is correct there.
        /// </para>
        /// </remarks>
        public static float MirrorPitch(float pitchDeg, Vector3Int sourceUp, MirrorAxis axis)
            => MirrorUp(sourceUp, axis) == sourceUp ? pitchDeg : -pitchDeg;
    }

    /// <summary>
    /// <see cref="IBlueprintEntryTransform"/> that reflects an entry
    /// across the chosen mirror plane. The build-mode mirror tool, the
    /// <see cref="BlueprintBuilder"/> data-builder, and the editor-time
    /// scripted authoring path all compose over this so reflection logic
    /// lives in one place. Cheap to mint — allocate one per mirror call,
    /// no caching needed.
    /// </summary>
    public sealed class MirrorTransform : IBlueprintEntryTransform
    {
        public MirrorAxis Axis { get; set; }

        public MirrorTransform(MirrorAxis axis) { Axis = axis; }

        public string TransformBlockId(in ChassisBlueprint.Entry source) => source.BlockId;
        public Vector3Int TransformPosition(in ChassisBlueprint.Entry source) => BlockMirror.MirrorCell(source.Position, Axis);
        public Vector3Int TransformUp(in ChassisBlueprint.Entry source) => BlockMirror.MirrorUp(source.EffectiveUp, Axis);
        // Dims is scalar (span / thickness / chord); no axis flip applies.
        public Vector3 TransformDims(in ChassisBlueprint.Entry source) => source.Dims;
        // Pitch sign depends on whether the source's up flips under the
        // mirror — read it directly from the source entry rather than
        // capturing state across calls.
        public float TransformPitch(in ChassisBlueprint.Entry source) => BlockMirror.MirrorPitch(source.Pitch, source.EffectiveUp, Axis);
    }
}
