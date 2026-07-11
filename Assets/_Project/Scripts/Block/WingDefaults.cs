namespace Robogame.Block
{
    /// <summary>
    /// Authoritative Wing shape constants — the bat-wing aero part
    /// (session 140), sibling of <see cref="FoilDefaults"/>. Single
    /// source of truth for span / thickness / chord defaults + slider
    /// ranges plus the flap-sweep envelope; <see cref="BlockOccupancy"/>
    /// (swept placement bounds), <c>Robogame.Movement.AeroSurfaceBlock</c>
    /// (mesh + lift) and the build-mode ghost all read from here so the
    /// placement footprint, the animation range and the rendered mesh
    /// can never drift apart.
    /// </summary>
    /// <remarks>
    /// The default dims are MEASURED, not chosen: <c>artgen/inv_export.py
    /// export_wing_anim()</c> prints the recentred rest-pose bounding box
    /// of the rigged mesh at export ("WingDefaults (Unity): …"). Re-run
    /// the export and update these constants whenever <c>inv_wing.py</c>
    /// reshapes the wing. Values below are from the session-140 export.
    /// </remarks>
    public static class WingDefaults
    {
        /// <summary>Span (m) at default Dims — authored mesh extent.</summary>
        public const float DefaultSpan      = 1.828f;
        /// <summary>Thickness (m) at default Dims (rest pose, camber arch included).</summary>
        public const float DefaultThickness = 0.195f;
        /// <summary>Chord (m) at default Dims.</summary>
        public const float DefaultChord     = 1.004f;

        /// <summary>Build-mode slider range for span.</summary>
        public const float MinSpan      = 0.90f, MaxSpan      = 3.60f;
        /// <summary>Build-mode slider range for thickness.</summary>
        public const float MinThickness = 0.10f, MaxThickness = 0.40f;
        /// <summary>Build-mode slider range for chord.</summary>
        public const float MinChord     = 0.50f, MaxChord     = 2.00f;

        /// <summary>
        /// Flap-sweep half-extent along the camber (thickness) axis, per
        /// metre of span. The flap is an angular motion, so the swept
        /// distance scales linearly with span. Measured at export: the
        /// posed mesh's camber extent over the full cycle was
        /// [-0.595, +0.533] m at span 1.828 → 0.595 / 1.828 = 0.326,
        /// rounded up and symmetrised (conservative — placement reserves
        /// slightly more than the flap ever visits).
        /// </summary>
        public const float SweepHalfExtentPerSpan = 0.33f;
    }
}
