using Robogame.Core;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Bridge between combat events (bombs, explosions) and the dig-zone
    /// system. Static so callers in <c>Robogame.Combat</c> don't need to
    /// resolve a DigZone reference — they pass world-space coordinates,
    /// this helper finds the right zone via <see cref="DigField.ZoneAt"/>
    /// and dispatches a <see cref="BrushKind.SphereSubtract"/> brush.
    /// </summary>
    public static class TerrainCratering
    {
        /// <summary>
        /// If <paramref name="worldPoint"/> falls inside a registered dig
        /// zone, emit a <see cref="BrushKind.SphereSubtract"/> there with
        /// the given radius and apply it. No-op if no zone contains the
        /// point or the radius is non-positive. Returns the SDF
        /// changed-cell count (0 if no zone matched).
        /// </summary>
        public static int OnBombDetonation(Vector3 worldPoint, float radiusMeters)
        {
            if (radiusMeters <= 0f) return 0;
            IDigZone zone = DigField.ZoneAt(worldPoint);
            if (zone == null) return 0;

            BrushOp op = new BrushOp
            {
                kind = BrushKind.SphereSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(worldPoint),
                p1 = Vector3Fixed.FromVector3(worldPoint),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(radiusMeters * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };
            return zone.ApplyBrush(op);
        }
    }
}
