using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Turns the pilot's rotational demands into a control-surface
    /// deflection for ONE aero surface, from nothing but where that surface
    /// sits relative to the centre of mass and which way its lift points.
    /// Pure math, no Unity state — the same call serves every foil and the
    /// EditMode sign tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A unit of extra lift along <c>liftAxis</c> applied at <c>r</c> (both in
    /// chassis space, <c>r</c> measured from the CoM) produces a moment along
    /// <c>m = r × liftAxis</c>. The components of the unit vector <c>m̂</c>
    /// say how much of that moment lands on each chassis axis and with which
    /// sign, so the deflection a surface should take is simply the demand
    /// dotted with its own moment direction (with the axis sign conventions
    /// of <see cref="DriveIntent"/>: +pitch = −X rotation, +roll = −Z
    /// rotation, +yaw = +Y rotation).
    /// </para>
    /// <para>
    /// Consequences, all emergent rather than coded: a tail elevator and a
    /// canard deflect opposite ways for the same pitch demand; left and right
    /// ailerons oppose; a fin behind the CoM yaws the nose the right way; a
    /// surface sitting ON the CoM contributes nothing to rotation; and losing
    /// a wing changes <c>r</c> for every survivor because the CoM moves.
    /// That geometry dependence is the point — see ADR-0009 / invariant #11.
    /// </para>
    /// </remarks>
    // TRACE[ADR-0009]: authority from geometry, not from a chassis torque.
    // TRACE[INV-11]: size + position of parts must matter; this is where
    // control authority picks up the lever arm.
    public static class AeroControl
    {
        /// <summary>Below this moment-arm magnitude (m) a surface is treated as sitting on the CoM.</summary>
        public const float MinMomentArm = 0.05f;

        /// <summary>
        /// Deflection in radians for a surface at <paramref name="rFromComLocal"/>
        /// (chassis-local, CoM-relative) whose positive lift points along
        /// <paramref name="liftAxisLocal"/> (chassis-local unit vector), given
        /// <paramref name="intent"/>. Magnitude saturates at
        /// <paramref name="maxRad"/>; the sign is whatever makes extra lift on
        /// this surface push the chassis toward the demand.
        /// </summary>
        public static float Deflection(in DriveIntent intent, Vector3 rFromComLocal, Vector3 liftAxisLocal, float maxRad)
        {
            if (maxRad <= 0f || !intent.HasRotation) return 0f;
            Vector3 m = Vector3.Cross(rFromComLocal, liftAxisLocal);
            float mag = m.magnitude;
            if (mag < MinMomentArm) return 0f;
            m /= mag;
            // +pitch demand = nose up = negative X rotation, so a surface whose
            // extra lift rotates about +X (m.x > 0, nose DOWN) must deflect
            // negative. Same reasoning for roll (+roll = −Z) and yaw (+yaw = +Y).
            float cmd = intent.Pitch * -m.x
                      + intent.Roll  * -m.z
                      + intent.Yaw   *  m.y;
            return Mathf.Clamp(cmd, -1f, 1f) * maxRad;
        }
    }
}
