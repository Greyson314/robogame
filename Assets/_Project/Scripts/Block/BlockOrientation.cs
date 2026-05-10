using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure functions for converting between "world-intent" pitch (the
    /// value the player dials in: positive = leading edge tilts toward
    /// world +Y / sky) and the local-frame pitch the foil's lift formula
    /// + visual rotation expect (positive = rotation around the foil's
    /// chord axis by +θ, which tilts the tip toward foil-local -X).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why two frames: <see cref="BlockGrid.OrientationFromUp"/> derives
    /// the foil's local frame from its mount-up. For lateral mounts
    /// (up=±X / ±Z), the chord axis lands on the same chassis-world
    /// direction on both sides of the chassis, but local-X (the rotation
    /// reference) flips its world-Y sign between the two sides. Without
    /// a per-up sign correction, the same hand-set local pitch produces
    /// inconsistent world-frame tilts (tip-up on one side, tip-down on
    /// the opposite) — exactly the symptom the user hit when placing
    /// foils on opposite faces of a rotor mechanism.
    /// </para>
    /// <para>
    /// The conversion is involutive (sign flip), so world → local and
    /// local → world share the same function given the same up. That
    /// also means the build-mode mirror reduces to "normalize the world
    /// intent for each side's up independently" — no separate mirror-axis
    /// negation rule needed.
    /// </para>
    /// </remarks>
    public static class BlockOrientation
    {
        /// <summary>
        /// Convert a world-intent pitch into the local-frame pitch for
        /// a foil mounted with <paramref name="up"/>. Negates iff the
        /// foil's local-X axis (the right vector from
        /// <see cref="BlockGrid.OrientationFromUp"/>) has a positive
        /// world-Y component — that's the geometric definition of "the
        /// chord-axis rotation by positive angle would tilt the tip
        /// toward world -Y instead of world +Y."
        /// </summary>
        public static float NormalizePitchForUp(float pitchDeg, Vector3Int up)
        {
            if (up == Vector3Int.zero) up = Vector3Int.up;
            Vector3 u = ((Vector3)up).normalized;
            Vector3 fwdSeed = Mathf.Abs(Vector3.Dot(u, Vector3.forward)) < 0.99f
                ? Vector3.forward : Vector3.right;
            Vector3 right = Vector3.Cross(u, fwdSeed).normalized;
            return right.y > 0f ? -pitchDeg : pitchDeg;
        }

        /// <summary>
        /// True iff the block id participates in the world-intent pitch
        /// scheme. Foils have a chord axis whose world direction
        /// depends on mount-up; rotors use pitch as collective (a
        /// post-adoption local-frame value applied uniformly to every
        /// blade), so they bypass the conversion.
        /// </summary>
        public static bool UsesWorldIntentPitch(BlockDefinition def)
        {
            if (def == null) return false;
            return def.Id == BlockIds.Aero || def.Id == BlockIds.AeroFin;
        }

        /// <summary>
        /// Apply <see cref="NormalizePitchForUp"/> only to blocks that
        /// use the world-intent pitch scheme. Convenience wrapper for
        /// placement code that already has the BlockDefinition in hand.
        /// </summary>
        public static float NormalizePitchForUp(BlockDefinition def, float worldPitch, Vector3Int up)
        {
            return UsesWorldIntentPitch(def) ? NormalizePitchForUp(worldPitch, up) : worldPitch;
        }
    }
}
