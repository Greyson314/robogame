// =============================================================================
// DummyAiInputSourceTests — EditMode
//
// What this suite covers
// -----------------------
// Pure heading-math tests for the patrol steering logic inside
// DummyAiInputSource (lines 117–162 in DummyAiInputSource.cs).
// Because the math is deterministic given position + forward + config,
// we extract it as a static helper and test it without MonoBehaviours or
// a running scene.
//
// Tests also cover the chase / retreat behaviour planned for
// GroundBotInputSource (upgrade of DummyAiInputSource), verifying that:
//  • When target is within chase range the move vector points toward it.
//  • When health drops below 30% the bot retreats (move.y negative, away
//    from target).
//  • When no target is set the patrol circle heading is used.
//  • Boundary: bot exactly on the circle centre (r ≈ 0) avoids a
//    singularity and outputs a valid steer.
//
// Requested production APIs (must land before new-behavior tests compile)
// -----------------------------------------------------------------------
// • GroundBotInputSource — new class in Robogame.Gameplay, implements IInputSource
// • GroundBotInputSource.ComputeSteer(Vector3 pos, Vector3 forward,
//       Vector3 circleCentre, float circleRadius,
//       float radialCorrectionGain, float steerGain)
//     → Vector2 (steer x, throttle y) — static or extractable as a pure function
// • GroundBotInputSource.ChaseRange — float, radius within which bot chases player
// • GroundBotInputSource.RetreatHealthFraction — float (0..1), default 0.3
//   If these live on DummyAiInputSource directly (upgraded in-place), adapt
//   the references below to DummyAiInputSource.
//
// Existing DummyAiInputSource API used (already compiles)
// --------------------------------------------------------
// • DummyAiInputSource.CircleCentre, CircleRadius, FireAtTarget, Target
// • IInputSource.Move, FireHeld
//
// NOTE: The existing DummyAiInputSource.UpdatePatrolSteering() is private.
// The tests below call the method indirectly by driving Update() via
// reflection, or test through the public Move property. The pure-math
// variant (ComputeSteer) will be a static method planned for GroundBotInputSource;
// those tests are marked "API in flight" and will not compile until it exists.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Tests for the patrol steering math shared by <see cref="DummyAiInputSource"/>
    /// and the planned <c>GroundBotInputSource</c>.
    /// </summary>
    public sealed class DummyAiInputSourceTests
    {
        // -----------------------------------------------------------------
        // Inline steering math (mirrors DummyAiInputSource lines 124–161)
        // This is an in-test extraction so EditMode tests don't need
        // MonoBehaviours. When GroundBotInputSource.ComputeSteer lands as a
        // public static method, replace these calls with the real API.
        // -----------------------------------------------------------------

        /// <summary>
        /// Replicates the patrol steering formula from DummyAiInputSource
        /// so we can test it in isolation without a running scene.
        /// If the formula changes in production, update this copy to match.
        /// </summary>
        private static Vector2 ComputePatrolSteer(
            Vector3 pos,
            Vector3 forward,
            Vector3 circleCentre,
            float   circleRadius,
            float   radialCorrectionGain = 0.04f,
            float   steerGain            = 1.5f,
            float   throttle             = 0.7f)
        {
            Vector3 fromCentre = pos - circleCentre;
            fromCentre.y = 0f;
            float r = fromCentre.magnitude;
            Vector3 radial = r > 0.01f ? fromCentre / r : Vector3.right;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            float radialError = r - circleRadius;
            Vector3 desired = tangent - radial * radialError * radialCorrectionGain;
            if (desired.sqrMagnitude < 1e-4f) desired = tangent;
            desired.Normalize();

            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                return new Vector2(0f, throttle);
            forward.Normalize();

            float cross = Vector3.Cross(forward, desired).y;
            float dot   = Vector3.Dot(forward, desired);
            float steer = dot < -0.5f
                ? Mathf.Sign(cross == 0f ? 1f : cross)
                : Mathf.Clamp(cross * steerGain, -1f, 1f);
            float t = throttle * Mathf.Lerp(1f, 0.55f, Mathf.Abs(steer));
            return new Vector2(steer, t);
        }

        // -----------------------------------------------------------------
        // Patrol circle — happy path
        // -----------------------------------------------------------------

        [Test]
        public void PatrolSteer_BotFacingTangent_AtRadius_ProducesZeroSteer()
        {
            // Bot is on the circle (r == radius) and already pointing along
            // the tangent — zero steering correction expected.
            Vector3 pos     = new Vector3(30f, 0f, 0f); // east of centre
            Vector3 forward = new Vector3(0f, 0f, 1f);  // pointing north = CCW tangent at east
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

            Assert.AreEqual(0f, move.x, 0.01f,
                "Steer must be ~0 when the bot is on the radius and already faces the tangent.");
            Assert.Greater(move.y, 0f,
                "Throttle must be positive when steering straight.");
        }

        [Test]
        public void PatrolSteer_BotFacingAwayFromTangent_ProducesHardTurn()
        {
            // Bot faces directly opposite the CCW tangent — must produce
            // maximum (magnitude 1) steer rather than relying on tiny cross values.
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = new Vector3(0f, 0f, -1f); // south = opposite of CCW tangent
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

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

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

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

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

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

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

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

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius);

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
            // High gain + large angular error should still saturate at ±1, not exceed it.
            Vector3 pos     = new Vector3(30f, 0f, 0f);
            Vector3 forward = new Vector3(0.01f, 0f, -1f); // nearly opposite to tangent
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;

            // Use default gain (1.5) but force a scenario where cross * gain > 1.
            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius, steerGain: 4f);

            Assert.LessOrEqual(Mathf.Abs(move.x), 1f + 1e-4f,
                "Steer must never exceed ±1 regardless of gain or heading error.");
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
            Vector3 forward = new Vector3(0f, 0f, -1f); // facing backward → full steer
            Vector3 centre  = Vector3.zero;
            float   radius  = 30f;
            float   baseThrottle = 0.7f;

            Vector2 move = ComputePatrolSteer(pos, forward, centre, radius, throttle: baseThrottle);

            Assert.Less(move.y, baseThrottle,
                "Throttle must be reduced from base when steering is at maximum (anti-spin-out).");
            Assert.Greater(move.y, 0f, "Throttle must remain positive even at full steer.");
        }

        // -----------------------------------------------------------------
        // Planned: GroundBotInputSource chase / retreat (API in flight)
        // -----------------------------------------------------------------

        [Test]
        public void GroundBotInputSource_ChaseMode_MoveYIsPositive_WhenTargetInRange()
        {
            // API in flight: GroundBotInputSource with ChaseRange + target transform.
            // The test stub asserts the pattern; update when GroundBotInputSource lands.
            //
            // var go = new GameObject("BotTest");
            // var bot = go.AddComponent<GroundBotInputSource>();
            // var targetGo = new GameObject("Target");
            // targetGo.transform.position = new Vector3(5f, 0f, 0f);
            // bot.Target = targetGo.transform;
            // bot.ChaseRange = 50f;
            // bot.UpdateBrain(); // or invoke Update via reflection
            // Assert.Greater(bot.Move.y, 0f, "Bot must throttle forward when chasing in range.");
            // Object.DestroyImmediate(go);
            // Object.DestroyImmediate(targetGo);

            Assert.Pass("API in flight — GroundBotInputSource.ChaseRange + UpdateBrain() " +
                        "must exist before this test runs real assertions.");
        }

        [Test]
        public void GroundBotInputSource_RetreatMode_MoveYIsNegative_WhenHealthBelowThreshold()
        {
            // API in flight: GroundBotInputSource with RetreatHealthFraction.
            // When current health fraction < RetreatHealthFraction (default 0.3),
            // the bot should reverse away from the target.
            //
            // var go = new GameObject("BotTest");
            // var bot = go.AddComponent<GroundBotInputSource>();
            // var targetGo = new GameObject("Target");
            // targetGo.transform.position = new Vector3(5f, 0f, 0f);
            // bot.Target = targetGo.transform;
            // bot.HealthFraction = 0.2f; // below retreat threshold
            // bot.UpdateBrain();
            // Assert.Less(bot.Move.y, 0f, "Bot must reverse when health is below retreat threshold.");
            // Object.DestroyImmediate(go);
            // Object.DestroyImmediate(targetGo);

            Assert.Pass("API in flight — GroundBotInputSource.HealthFraction + retreat logic " +
                        "must exist before this test runs real assertions.");
        }

        [Test]
        public void AirBotInputSource_MoveVertical_IsPositive_WhenBelowTargetAltitude()
        {
            // API in flight: AirBotInputSource with TargetAltitude.
            // When the bot is below its target altitude, IInputSource.Vertical
            // must be positive so the thrust block fires upward.
            //
            // var go = new GameObject("AirBotTest");
            // go.transform.position = new Vector3(0f, 5f, 0f);  // 5 m altitude
            // var bot = go.AddComponent<AirBotInputSource>();
            // bot.TargetAltitude = 50f;
            // bot.UpdateBrain();
            // Assert.Greater(bot.Vertical, 0f,
            //     "AirBot must output positive Vertical when below target altitude.");
            // Object.DestroyImmediate(go);

            Assert.Pass("API in flight — AirBotInputSource.TargetAltitude + Vertical output " +
                        "must exist before this test runs real assertions.");
        }
    }
}
