using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Shared diminishing-returns curves for stacked same-type
    /// contributors (pogo feet today; gyro torque, aura modules, or any
    /// future "N of the same block" system tomorrow). One primitive:
    /// <see cref="PowerLaw"/> = count^exponent. Exponent 1 = linear
    /// (physical stacking, no DR), 0.5 = square-root returns, 0 = flat
    /// (count buys nothing). Each system owns its exponent as a
    /// schema-side constant next to its other tuning (e.g.
    /// <see cref="PogoDefaults.StackHeightExponent"/>) so the curve
    /// shape stays server-canonical and single-sourced (invariant #1) —
    /// this class is deliberately just the math.
    /// </summary>
    public static class StackingCurves
    {
        /// <summary>count^exponent, with count floored at 1.</summary>
        public static float PowerLaw(int count, float exponent)
            => Mathf.Pow(Mathf.Max(1, count), exponent);
    }
}
