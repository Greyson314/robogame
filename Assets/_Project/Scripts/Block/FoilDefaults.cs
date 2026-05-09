namespace Robogame.Block
{
    /// <summary>
    /// Authoritative foil shape constants. Single source of truth for
    /// span / thickness / chord defaults + slider ranges; both
    /// <see cref="BlockOccupancy"/> (chassis-local AABB math) and
    /// <c>Robogame.Movement.AeroSurfaceBlock</c> (mesh + lift) read from
    /// here so the build-mode swept bounds and the placed-block visual
    /// can never drift apart.
    /// </summary>
    /// <remarks>
    /// Lives in <see cref="Robogame.Block"/> deliberately:
    /// <see cref="Robogame.Movement"/> references Block, not the other
    /// way round. This is the precedent the §3.5 diagnosis flagged —
    /// per-block stats belong on the schema-side library, not on the
    /// runtime-side component.
    /// </remarks>
    public static class FoilDefaults
    {
        /// <summary>Span (m) at default Dims.</summary>
        public const float DefaultSpan      = 1.00f;
        /// <summary>Thickness (m) at default Dims.</summary>
        public const float DefaultThickness = 0.08f;
        /// <summary>Chord (m) at default Dims.</summary>
        public const float DefaultChord     = 0.90f;

        /// <summary>Build-mode slider range for span.</summary>
        public const float MinSpan      = 0.30f, MaxSpan      = 3.00f;
        /// <summary>Build-mode slider range for thickness.</summary>
        public const float MinThickness = 0.02f, MaxThickness = 0.40f;
        /// <summary>Build-mode slider range for chord.</summary>
        public const float MinChord     = 0.20f, MaxChord     = 2.50f;
    }
}
