using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Per-block-id dispatch for the aero family's shape constants.
    /// Foils (<see cref="BlockIds.Aero"/> / <see cref="BlockIds.AeroFin"/>)
    /// read <see cref="FoilDefaults"/>, the Wing reads
    /// <see cref="WingDefaults"/>. One resolver so occupancy, the
    /// variant panel, the ghost factory and the placed-mesh rig all
    /// agree on what "default dims" means for a given id.
    /// </summary>
    public static class AeroShape
    {
        /// <summary>Every id that rides the AeroSurfaceBlock span/thickness/chord dims scheme.</summary>
        public static bool IsAeroId(string blockId) =>
            blockId == BlockIds.Aero || blockId == BlockIds.AeroFin || blockId == BlockIds.Wing;

        /// <summary>Block-default (span, thickness, chord) in metres for the id.</summary>
        public static void Defaults(string blockId, out float span, out float thickness, out float chord)
        {
            if (blockId == BlockIds.Wing)
            {
                span      = WingDefaults.DefaultSpan;
                thickness = WingDefaults.DefaultThickness;
                chord     = WingDefaults.DefaultChord;
            }
            else
            {
                span      = FoilDefaults.DefaultSpan;
                thickness = FoilDefaults.DefaultThickness;
                chord     = FoilDefaults.DefaultChord;
            }
        }

        /// <summary>
        /// Resolve raw per-entry Dims (zero component → the id's default)
        /// into the effective (span, thickness, chord) triplet. Id-aware
        /// twin of <c>AeroSurfaceBlock.ResolveDims</c>, which stays
        /// foil-only for source-compat with shipped call sites.
        /// </summary>
        public static void ResolveDims(string blockId, Vector3 raw,
            out float span, out float thickness, out float chord)
        {
            Defaults(blockId, out float ds, out float dt, out float dc);
            span      = raw.x > 0f ? raw.x : ds;
            thickness = raw.y > 0f ? raw.y : dt;
            chord     = raw.z > 0f ? raw.z : dc;
        }
    }
}
