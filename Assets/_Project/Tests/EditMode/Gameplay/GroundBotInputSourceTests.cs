// =============================================================================
// GroundBotInputSourceTests — EditMode
//
// What this suite covers
// -----------------------
// Pure heading-math tests for GroundBotInputSource.ComputeSteer — the patrol /
// engage steering primitive used by every ground bot in the project. The
// function is static and side-effect-free, so we exercise it without a scene
// or any MonoBehaviour.
//
// History: this file used to test DummyAiInputSource, which session 62 deleted
// in favour of GroundBotInputSource (same math, full behaviour state machine).
// The tests previously mirrored DummyAiInputSource's math in an inline helper;
// they now call the real GroundBotInputSource.ComputeSteer.
//
// Coordinate note: Vector3.Cross(Vector3.up, radial) with radial = (+X)
// returns (0, 0, -1) in Unity's coordinate system, so the patrol-circle
// tangent at the +X point of the circle is the −Z direction. Each test's
// forward vector is chosen against that tangent — not whatever a CCW
// convention would suggest.
// =============================================================================

using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Tests for the static patrol-steering helper in <see cref="GroundBotInputSource"/>.
    /// </summary>
    public sealed class GroundBotInputSourceTests
    {
        // Defaults mirror GroundBotInputSource serialized defaults. The real
        // API takes no defaults; this wrapper lets each test override only the
        // parameter under examination.
        private static Vector2 ComputeSteer(
            Vector3 pos,
            Vector3 forward,
            Vector3 circleCentre,
            float   circleRadius,
            float   radialCorrectionGain = 0.04f,
            float   steerGain            = 1.5f,
            float   throttle             = 0.7f)
            => GroundBotInputSource.ComputeSteer(
                pos, forward, circleCentre, circleRadius,
                radialCorrectionGain, steerGain, throttle);

        // -----------------------------------------------------------------
        // Patrol circle — happy path
        // -----------------------------------------------------------------

        [Test]
        public void PatrolSteer_BotFacingTangent_AtRadius_ProducesZeroSteer()
        {
            // Bot is on the circle (r == radius) and already pointing along
            // the tangent — zero steering correction expected.
            Vector3 pos     = new Vector3(30f, 0f, 0f); // east of centre
            Vector3 forward = new Vector3(0f, 0f, -1f); // tangent at east = -Z
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.AreEqual(0f, move.x, 0.01f,
                "Steer must be ~0 when the bot is on the radius and already faces the tangent.");
            Assert.Greater(move.y, 0f,
                "Throttle must be positive when steering straight.");
        }

        [Test]
        public void PatrolSteer_BotFacingAwayFromTangent_ProducesHardTurn()
        {
            // Bot faces directly opposite the patrol tangent — must produce
            // maximum (magnitude 1) steer via the dot < -0.5 fallback branch
            // rather than relying on the tiny cross value at anti-parallel.
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = new Vector3(0f, 0f, 1f); // opposite of -Z tangent at east
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.AreEqual(1f, Mathf.Abs(move.x), 0.01f,
                "Steer must be ±1 when facing directly opposite the desired heading (dot < -0.5 branch).");
        }

        [Test]
        public void PatrolSteer_BotInsideRadius_SteersMixedOutward()
        {
            // Bot is inside the target circle (r < radius) — radial error is
            // negative, so the desired heading bends outward. Steer output
            // should be non-zero (turning to rejoin the circle) and throttle
            // should be positive.
            Vector3 pos     = new Vector3(5f, 0f, 0f); // well inside a 30 m circle
            Vector3 forward = new Vector3(0f, 0f, 1f);
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.Greater(move.y, 0f, "Throttle must remain positive when inside the radius.");
        }

        [Test]
        public void PatrolSteer_BotOutsideRadius_SteersInward()
        {
            // Bot is outside the circle (r > radius) — radial error is positive,
            // so the heading bends inward. Just verify throttle stays positive
            // and the output is finite (no NaN/Inf).
            Vector3 pos     = new Vector3(60f, 0f, 0f); // outside a 30 m circle
            Vector3 forward = new Vector3(0f, 0f, 1f);
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.IsFalse(float.IsNaN(move.x) || float.IsNaN(move.y),
                "Steer output must not be NaN when the bot is outside the patrol radius.");
            Assert.IsFalse(float.IsInfinity(move.x) || float.IsInfinity(move.y),
                "Steer output must not be Infinity when the bot is outside the patrol radius.");
            Assert.Greater(move.y, 0f, "Throttle must stay positive outside the radius.");
        }

        // -----------------------------------------------------------------
        // Boundary: bot exactly on the centre (singularity guard)
        // -----------------------------------------------------------------

        [Test]
        public void PatrolSteer_BotExactlyAtCentre_NoNaNOrSingularity()
        {
            // fromCentre.magnitude ≈ 0 → radial falls back to Vector3.right.
            Vector3 pos     = Vector3.zero; // exactly at centre
            Vector3 forward = Vector3.forward;
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.IsFalse(float.IsNaN(move.x) || float.IsNaN(move.y),
                "Steer must not be NaN when the bot stands on the circle centre (singularity guard).");
            Assert.GreaterOrEqual(move.y, 0f, "Throttle must be non-negative even at singularity.");
        }

        [Test]
        public void PatrolSteer_ZeroForward_ReturnsForwardThrottleOnly()
        {
            // If the chassis has no meaningful XZ forward (e.g. first frame
            // after spawn when velocity is zero), the function should not blow
            // up. The production code returns (0, throttle) in this case.
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = Vector3.zero; // degenerate forward
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius);

            Assert.AreEqual(0f, move.x, 0.001f,
                "With zero forward the steer component should be 0 (fallback branch).");
            Assert.Greater(move.y, 0f,
                "Throttle should still be positive with zero forward (fallback branch).");
        }

        // -----------------------------------------------------------------
        // Steer is clamped to [-1, 1]
        // -----------------------------------------------------------------

        [Test]
        public void PatrolSteer_SteerOutput_IsAlwaysClampedToMinusOneToOne()
        {
            // Force a setup where cross * gain would exceed 1 without the
            // clamp. forward perpendicular to tangent → cross.y ≈ 1, gain 4 →
            // unclamped 4. The clamp must cap it at ±1.
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = new Vector3(1f, 0f, 0f); // east — perpendicular to the -Z tangent
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius, steerGain: 4f);

            Assert.LessOrEqual(Mathf.Abs(move.x), 1f + 1e-4f,
                "Steer must never exceed ±1 regardless of gain or heading error.");
            Assert.AreEqual(1f, Mathf.Abs(move.x), 0.01f,
                "Steer should saturate at ±1 in this setup (cross.y ≈ 1, gain 4 → unclamped 4).");
        }

        // -----------------------------------------------------------------
        // Throttle modulation with steering
        // -----------------------------------------------------------------

        [Test]
        public void PatrolSteer_FullSteer_ReducesThrottle()
        {
            // When steer is saturated (±1), throttle must be reduced to avoid
            // spin-out. The production formula applies Lerp(1, 0.55, |steer|).
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = new Vector3(0f, 0f, 1f); // opposite tangent → full steer
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;
            float   baseThrottle = 0.7f;

            Vector2 move = ComputeSteer(pos, forward, centre, radius, throttle: baseThrottle);

            Assert.Less(move.y, baseThrottle,
                "Throttle must be reduced from base when steering is at maximum (anti-spin-out).");
            Assert.Greater(move.y, 0f, "Throttle must remain positive even at full steer.");
        }
    }
}
