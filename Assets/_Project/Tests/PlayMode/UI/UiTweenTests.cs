// =============================================================================
// UiTweenTests — PlayMode
//
// First test coverage for the ink-motion-kit tween driver (docs/research/
// ui-design-handoff-motion.md). UiTween's entire public surface (Alpha, Fill,
// Scale, Move, Tint, RotZ, Cancel, Complete, CompleteAll, ActiveCount) is
// `public static`, so unlike most PlayMode suites in this project no
// reflection is needed anywhere below — the tests compile straight against
// the production API.
//
// INVARIANTS COVERED
//   • Retarget, never restart — a second tween on the same (target, channel)
//     re-aims from the CURRENT value; the value never snaps backward.
//   • CompleteAll() jumps every live tween — including one still inside a
//     delay hold — to its exact end value, and ActiveCount drops to 0.
//   • Stale handles (a slot since reused by a newer tween) are inert on both
//     Cancel and Complete — they must not disturb the newer occupant.
//   • delay > 0 holds the from-state exactly until the delay elapses.
//   • A target destroyed mid-tween releases its slot on a later tick with no
//     exception escaping Update().
//   • RotZ always takes the shortest angular path.
//   • Natural completion lands on the exact requested target value (Alpha,
//     Fill) — no easing residue.
//   • Move / Scale / Tint each reach their target exactly (API-surface
//     coverage for the three channels the required-behaviors list doesn't
//     otherwise touch).
//   • The 160-slot pool never drops a request at capacity — it evicts the
//     tween nearest completion instead (Claim()'s documented trade-off).
//
// POOL RESET
//   UiTween is a DontDestroyOnLoad singleton; its slot pool is NOT reset
//   between tests by the Unity Test Framework. SetUp/TearDown both call the
//   public CompleteAll() to guarantee ActiveCount == 0 at the start of every
//   test, rather than adding a test-only reflection seam to production code.
//   (This does mean a broken CompleteAll() would cascade into every other
//   test in this file failing too — an accepted trade for not touching
//   UiTween.cs.)
//
// TIMING
//   Waits are driven by accumulating Time.unscaledDeltaTime — the same clock
//   UiTween.Update reads — via the AdvanceUnscaled() helper below, not a
//   fixed frame count. This stays correct regardless of the test runner's
//   actual frame rate. Durations are kept in the 0.05–0.2 s range per the
//   test-drafter brief so the whole suite runs fast.
//
// NOT COVERED (see summary for the full list)
//   Retargeting across two DIFFERENT channels on the same target in the same
//   frame, and visual/render-side correctness (Canvas/CanvasRenderer output)
//   are out of scope — this suite is data-plane only, matching how the rest
//   of the project's PlayMode tests treat UI.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.UI
{
    public sealed class UiTweenTests
    {
        // Heterogeneous UI objects (CanvasGroup / Image / bare RectTransform)
        // get created per test, unlike the single-chassis-root pattern used
        // by the Movement/Combat suites — track them generically instead of
        // one named field per target.
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Defensive: flush anything left active by a prior test that threw
            // before its own TearDown ran (see POOL RESET note above).
            UiTween.CompleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            UiTween.CompleteAll();
            foreach (GameObject go in _spawned)
                if (go != null) UnityEngine.Object.Destroy(go);
            _spawned.Clear();
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private CanvasGroup NewCanvasGroup(string name)
        {
            var go = new GameObject(name, typeof(CanvasGroup));
            _spawned.Add(go);
            return go.GetComponent<CanvasGroup>();
        }

        private Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(Image)); // RequireComponent pulls in RectTransform + CanvasRenderer
            _spawned.Add(go);
            return go.GetComponent<Image>();
        }

        private RectTransform NewRectTransform(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _spawned.Add(go);
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Advances frames until at least <paramref name="seconds"/> of
        /// Time.unscaledDeltaTime has accumulated — the same clock
        /// UiTween.Update reads — so waits stay correct regardless of the
        /// test runner's actual frame rate. <paramref name="onFrame"/>, if
        /// given, runs after every accumulated frame (used for per-frame
        /// assertions during a wait). Bounded by a frame-count safety net so
        /// a stalled driver fails the test fast instead of hanging it.
        /// </summary>
        private static IEnumerator AdvanceUnscaled(float seconds, Action onFrame = null)
        {
            float elapsed = 0f;
            int frames = 0;
            const int safetyFrameCap = 5000;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                onFrame?.Invoke();
                if (++frames > safetyFrameCap)
                {
                    Assert.Fail($"AdvanceUnscaled({seconds}s) had not elapsed after {safetyFrameCap} frames " +
                                $"(only {elapsed:F4}s accumulated) — the driver may be stalled.");
                }
            }
        }

        // -----------------------------------------------------------------
        // 1. Retarget, never restart
        // -----------------------------------------------------------------

        /// <summary>
        /// Starting a second Alpha tween on the same CanvasGroup mid-flight
        /// must re-aim from whatever value the FIRST tween had already
        /// reached — never snap to a different anchor first. This is the
        /// hover-off-mid-animation case from the motion handoff: CSS-
        /// transition semantics, not a hard restart.
        /// </summary>
        [UnityTest]
        public IEnumerator Alpha_RetargetedMidFlight_ReAimsFromCurrentValue_NeverSnaps()
        {
            CanvasGroup cg = NewCanvasGroup("RetargetTarget");
            cg.alpha = 0f;

            const float duration = 0.12f;
            // Linear so the "still mid-flight" probe below has a predictable value.
            UiTween.Alpha(cg, 1f, duration, UiMotion.Ease.Linear);

            yield return AdvanceUnscaled(duration * 0.5f);
            float midAlpha = cg.alpha;
            Assert.Greater(midAlpha, 0.05f, "Test setup: expected the entrance to be visibly underway by the halfway point.");
            Assert.Less(midAlpha, 0.95f, "Test setup: expected the entrance to still be underway (not already complete).");

            // Retarget: hover-off fires a new tween on the same (target, channel).
            UiTween.Alpha(cg, 0f, duration, UiMotion.Ease.Settle);

            // Scheduling the retarget must not itself write a value — only
            // the next Update tick does that.
            Assert.AreEqual(midAlpha, cg.alpha, 0f,
                "Scheduling a retarget must not itself change the value — a snap here would mean " +
                "the driver reset From to something other than the live current value.");

            // Sample every frame through the retarget's decay: alpha must move
            // smoothly DOWN from midAlpha toward 0, never back up (which a
            // "restart from the original from-value" bug would produce) and
            // never past midAlpha (no re-approach of the old target).
            float prev = midAlpha;
            const float epsilon = 1e-4f;
            yield return AdvanceUnscaled(duration + 0.05f, () =>
            {
                float now = cg.alpha;
                Assert.LessOrEqual(now, prev + epsilon,
                    $"alpha rose from {prev} to {now} during the retargeted decay — a snap/restart, not a smooth turn-around.");
                Assert.LessOrEqual(now, midAlpha + epsilon,
                    $"alpha ({now}) exceeded the value at the moment of retarget ({midAlpha}) — it must never re-approach the old target.");
                prev = now;
            });

            Assert.AreEqual(0f, cg.alpha, 1e-5f,
                "The retargeted tween must still land exactly on its new target (0) once its own duration elapses.");
        }

        // -----------------------------------------------------------------
        // 2. CompleteAll
        // -----------------------------------------------------------------

        /// <summary>
        /// CompleteAll() must jump every live tween to its exact end value —
        /// including one still inside a delay hold, since a staggered
        /// entrance's later children haven't started animating yet but must
        /// still be skippable by "any input" — and ActiveCount must drop to 0.
        /// </summary>
        [UnityTest]
        public IEnumerator CompleteAll_JumpsEveryLiveTween_ToExactEndValue_AndActiveCountDropsToZero()
        {
            CanvasGroup cgA = NewCanvasGroup("CompleteAllA");
            CanvasGroup cgB = NewCanvasGroup("CompleteAllB");
            Image img = NewImage("CompleteAllFill");
            img.type = Image.Type.Filled;
            CanvasGroup cgDelayed = NewCanvasGroup("CompleteAllDelayed");

            cgA.alpha = 0f;
            cgB.alpha = 1f;
            img.fillAmount = 0f;
            cgDelayed.alpha = 0f;

            UiTween.Alpha(cgA, 1f, 0.15f, UiMotion.Ease.Settle);
            UiTween.Alpha(cgB, 0.25f, 0.15f, UiMotion.Ease.Draw);
            UiTween.Fill(img, 0.8f, 0.15f, UiMotion.Ease.Page);
            // Delay long enough that it could never naturally elapse within
            // this test — CompleteAll must still snap it, proving it isn't
            // gated by the delay hold.
            UiTween.Alpha(cgDelayed, 0.6f, 0.15f, UiMotion.Ease.Settle, delay: 10f);

            Assert.AreEqual(4, UiTween.ActiveCount, "Test setup: all four tweens should be live before CompleteAll.");

            UiTween.CompleteAll();

            Assert.AreEqual(1f, cgA.alpha, 0f, "CompleteAll must snap alpha exactly to its target.");
            Assert.AreEqual(0.25f, cgB.alpha, 1e-5f, "CompleteAll must snap alpha exactly to its target.");
            Assert.AreEqual(0.8f, img.fillAmount, 1e-5f, "CompleteAll must snap fillAmount exactly to its target.");
            Assert.AreEqual(0.6f, cgDelayed.alpha, 1e-5f,
                "CompleteAll must snap a tween still inside its delay hold too — 'any input skips the " +
                "entrance' includes staggered children that haven't started animating yet.");

            Assert.AreEqual(0, UiTween.ActiveCount, "CompleteAll must leave zero live tweens.");

            // No further drift: one more tick must not move anything (slots were released).
            yield return null;
            Assert.AreEqual(1f, cgA.alpha, 0f, "A tick after CompleteAll must not move a released slot's target.");
            Assert.AreEqual(0, UiTween.ActiveCount);
        }

        // -----------------------------------------------------------------
        // 3. Stale handles are inert
        // -----------------------------------------------------------------

        /// <summary>
        /// A handle stays valid only for the specific tween instance it was
        /// issued for. Once that slot is reused by a NEWER tween (the
        /// original completed naturally and the pool recycled the index —
        /// Claim() scans free slots from index 0, so a second tween on the
        /// same target deterministically reclaims the same index), calling
        /// Cancel on the OLD handle must be a no-op: it must not touch the
        /// new occupant.
        /// </summary>
        [UnityTest]
        public IEnumerator Cancel_OnStaleHandle_DoesNotDisturbNewerTweenInTheSameSlot()
        {
            CanvasGroup cg = NewCanvasGroup("StaleCancelTarget");
            cg.alpha = 0f;

            const float shortDuration = 0.05f;
            UiTweenHandle staleHandle = UiTween.Alpha(cg, 1f, shortDuration, UiMotion.Ease.Linear);
            yield return AdvanceUnscaled(shortDuration + 0.05f);
            Assert.AreEqual(1f, cg.alpha, 0f, "Test setup: the first tween must have completed naturally.");
            Assert.AreEqual(0, UiTween.ActiveCount, "Test setup: the slot must be free before the second tween claims it.");

            const float newDuration = 0.2f;
            UiTween.Alpha(cg, 0f, newDuration, UiMotion.Ease.Linear);

            yield return AdvanceUnscaled(newDuration * 0.5f);
            float midValue = cg.alpha;
            int countBeforeStaleCall = UiTween.ActiveCount;
            Assert.AreEqual(1, countBeforeStaleCall, "Test setup: the newer tween must be live.");

            UiTween.Cancel(staleHandle);

            Assert.AreEqual(midValue, cg.alpha, 0f,
                "Cancel on a stale handle changed the value of a newer, unrelated tween occupying the same slot.");
            Assert.AreEqual(countBeforeStaleCall, UiTween.ActiveCount,
                "Cancel on a stale handle must not release a slot it no longer owns.");

            // The newer tween must still be free to run to completion untouched.
            yield return AdvanceUnscaled(newDuration * 0.5f + 0.05f);
            Assert.AreEqual(0f, cg.alpha, 1e-5f,
                "The newer tween must complete normally — the stale Cancel call must not have canceled it.");
        }

        /// <summary>
        /// Same contract as the Cancel case above, exercised through
        /// Complete instead — Complete on a stale handle must not
        /// force-finish a newer tween that happens to share its old slot.
        /// </summary>
        [UnityTest]
        public IEnumerator Complete_OnStaleHandle_DoesNotDisturbNewerTweenInTheSameSlot()
        {
            CanvasGroup cg = NewCanvasGroup("StaleCompleteTarget");
            cg.alpha = 0f;

            const float shortDuration = 0.05f;
            UiTweenHandle staleHandle = UiTween.Alpha(cg, 1f, shortDuration, UiMotion.Ease.Linear);
            yield return AdvanceUnscaled(shortDuration + 0.05f);
            Assert.AreEqual(1f, cg.alpha, 0f, "Test setup: the first tween must have completed naturally.");

            const float newDuration = 0.2f;
            UiTween.Alpha(cg, 0f, newDuration, UiMotion.Ease.Linear);

            yield return AdvanceUnscaled(newDuration * 0.5f);
            float midValue = cg.alpha;
            Assert.Greater(midValue, 0.05f, "Test setup: the newer tween should still be mid-flight.");
            Assert.Less(midValue, 0.95f, "Test setup: the newer tween should still be mid-flight.");

            UiTween.Complete(staleHandle);

            Assert.AreEqual(midValue, cg.alpha, 0f,
                "Complete on a stale handle jumped a newer, unrelated tween to ITS end value — the stale handle must be inert.");
            Assert.AreEqual(1, UiTween.ActiveCount,
                "Complete on a stale handle must not finish/free a slot it no longer owns — the newer tween is still live.");
        }

        // -----------------------------------------------------------------
        // 4. delay > 0 holds the from-state
        // -----------------------------------------------------------------

        /// <summary>
        /// While a delayed tween's delay hasn't elapsed, the target's value
        /// must be untouched (still exactly the from-state) — Update() must
        /// hold, not merely clamp near-zero progress.
        /// </summary>
        [UnityTest]
        public IEnumerator Alpha_WithDelay_HoldsFromValue_UntilDelayElapses()
        {
            CanvasGroup cg = NewCanvasGroup("DelayTarget");
            cg.alpha = 0f;

            const float delay = 0.12f;
            const float duration = 0.1f;
            UiTween.Alpha(cg, 1f, duration, UiMotion.Ease.Settle, delay);

            // Comfortably inside the delay window on every sampled frame.
            yield return AdvanceUnscaled(delay * 0.5f, () =>
            {
                Assert.AreEqual(0f, cg.alpha, 0f,
                    "alpha changed before the delay elapsed — a delayed tween must hold the from-state exactly.");
            });

            // Past delay + duration with margin: the tween must have completed.
            yield return AdvanceUnscaled(delay * 0.5f + duration + 0.05f);
            Assert.AreEqual(1f, cg.alpha, 1e-5f,
                "After the delay elapses and the tween runs its full duration, alpha must reach the target.");
        }

        // -----------------------------------------------------------------
        // 5. Destroyed target mid-tween
        // -----------------------------------------------------------------

        /// <summary>
        /// If the tween's target GameObject is destroyed mid-flight, the
        /// driver must release the slot on a later tick with no exception —
        /// a hover-off racing a panel teardown must not leave a permanently
        /// stuck slot or crash the Update loop for every other live tween
        /// that frame. (An unhandled exception inside UiTween.Update would
        /// propagate out of this coroutine and fail the test on its own —
        /// no explicit try/catch needed here.)
        /// </summary>
        [UnityTest]
        public IEnumerator Alpha_TargetDestroyedMidTween_ReleasesSlotWithoutException()
        {
            CanvasGroup cg = NewCanvasGroup("DestroyedMidTweenTarget");
            cg.alpha = 0f;
            UiTween.Alpha(cg, 1f, 0.2f, UiMotion.Ease.Settle);

            yield return AdvanceUnscaled(0.05f);
            Assert.AreEqual(1, UiTween.ActiveCount, "Test setup: the tween must be live before its target is destroyed.");

            UnityEngine.Object.Destroy(cg.gameObject);
            _spawned.Remove(cg.gameObject); // destroyed here; TearDown must not double-Destroy it

            // Object.Destroy is deferred to end-of-frame — poll a bounded
            // number of frames for the slot to release.
            const int maxFrames = 30;
            int frames = 0;
            while (UiTween.ActiveCount > 0 && frames < maxFrames)
            {
                yield return null;
                frames++;
            }

            Assert.AreEqual(0, UiTween.ActiveCount,
                $"The slot was not released within {maxFrames} frames of its target being destroyed.");
        }

        // -----------------------------------------------------------------
        // 6. RotZ shortest path
        // -----------------------------------------------------------------

        /// <summary>
        /// RotZ must take the shortest angular path. A face resting at
        /// -0.7° (which Unity reports back as ~359.3° via localEulerAngles)
        /// tweened to 0° must sweep the short 0.7° arc forward through
        /// 360°/0°, never the long way backward through ~180° — the exact
        /// artifact a naive Lerp(359.3, 0, t) would produce (it would cross
        /// ~179.65° at the halfway point).
        /// </summary>
        [UnityTest]
        public IEnumerator RotZ_FromNegativePointSevenDegrees_ToZero_NeverSwingsThroughOneEighty()
        {
            RectTransform rt = NewRectTransform("RotZShortestPath");
            Vector3 euler = rt.localEulerAngles;
            euler.z = -0.7f;
            rt.localEulerAngles = euler;
            float startZ = rt.localEulerAngles.z;
            Assert.AreEqual(359.3f, startZ, 0.1f,
                "Test setup: Unity should report -0.7° back as ~359.3° — the exact scenario the shortest-path guard exists for.");

            const float duration = 0.1f;
            UiTween.RotZ(rt, 0f, duration, UiMotion.Ease.Settle);

            // True max deviation from either endpoint is 0.7°; a naive-lerp
            // bug would peak near 179.65° at the midpoint, so 5° is a wide
            // margin above the correct path and a tight one below the bug.
            const float maxDeviationDegrees = 5f;
            yield return AdvanceUnscaled(duration + 0.05f, () =>
            {
                float z = rt.localEulerAngles.z;
                float deviationFromZero = Mathf.Abs(Mathf.DeltaAngle(0f, z));
                Assert.Less(deviationFromZero, maxDeviationDegrees,
                    $"z={z:F2}° is {deviationFromZero:F2}° from the 0° endpoint mid-tween — RotZ swung " +
                    "the long way around instead of taking the 0.7° short path.");
            });

            float finalZ = rt.localEulerAngles.z;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, finalZ)), 0.1f,
                $"RotZ must land on 0° (got {finalZ:F4}°).");
        }

        // -----------------------------------------------------------------
        // 7. Exact end values
        // -----------------------------------------------------------------

        /// <summary>
        /// After natural completion (not via CompleteAll/Complete), alpha
        /// must equal the requested target exactly — no easing residue.
        /// From is deliberately 0 here so the arithmetic (0 + (to-0)*1) is
        /// bit-exact, the strictest form of this claim.
        /// </summary>
        [UnityTest]
        public IEnumerator Alpha_NaturalCompletion_LandsExactlyOnTarget()
        {
            CanvasGroup cg = NewCanvasGroup("ExactAlphaTarget");
            cg.alpha = 0f;
            const float duration = 0.08f;
            UiTween.Alpha(cg, 1f, duration, UiMotion.Ease.Draw);

            yield return AdvanceUnscaled(duration + 0.05f);

            Assert.AreEqual(1f, cg.alpha, 0f, "Natural completion must leave alpha exactly at the requested target, no easing residue.");
            Assert.AreEqual(0, UiTween.ActiveCount, "The slot must be released once the tween completes naturally.");
        }

        /// <summary>Same claim as above, on the Fill channel.</summary>
        [UnityTest]
        public IEnumerator Fill_NaturalCompletion_LandsExactlyOnTarget()
        {
            Image img = NewImage("ExactFillTarget");
            img.type = Image.Type.Filled;
            img.fillAmount = 0f;
            const float duration = 0.08f;
            UiTween.Fill(img, 1f, duration, UiMotion.Ease.Page);

            yield return AdvanceUnscaled(duration + 0.05f);

            Assert.AreEqual(1f, img.fillAmount, 0f, "Natural completion must leave fillAmount exactly at the requested target, no easing residue.");
        }

        // -----------------------------------------------------------------
        // Channel API coverage — Move / Scale / Tint happy paths
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator Move_NaturalCompletion_AnchoredPositionReachesTarget()
        {
            RectTransform rt = NewRectTransform("MoveTarget");
            rt.anchoredPosition = Vector2.zero;
            var target = new Vector2(120f, -40f);
            UiTween.Move(rt, target, 0.08f, UiMotion.Ease.Settle);

            yield return AdvanceUnscaled(0.13f);

            Assert.AreEqual(target.x, rt.anchoredPosition.x, 1e-4f, "Move must land exactly on the target X.");
            Assert.AreEqual(target.y, rt.anchoredPosition.y, 1e-4f, "Move must land exactly on the target Y.");
        }

        [UnityTest]
        public IEnumerator Scale_NaturalCompletion_UniformScaleReachesTarget()
        {
            RectTransform rt = NewRectTransform("ScaleTarget");
            rt.localScale = Vector3.one;
            UiTween.Scale(rt, 1.5f, 0.08f, UiMotion.Ease.Settle);

            yield return AdvanceUnscaled(0.13f);

            Assert.AreEqual(1.5f, rt.localScale.x, 1e-4f, "Scale must land exactly on the target for X.");
            Assert.AreEqual(1.5f, rt.localScale.y, 1e-4f, "Scale must land exactly on the target for Y.");
            Assert.AreEqual(1f, rt.localScale.z, 1e-4f, "Scale is uniform-XY only (2D UI) — Z must stay 1.");
        }

        [UnityTest]
        public IEnumerator Tint_NaturalCompletion_ColorReachesTarget()
        {
            Image img = NewImage("TintTarget");
            img.color = Color.white;
            var target = new Color(0.2f, 0.4f, 0.6f, 1f);
            UiTween.Tint(img, target, 0.08f, UiMotion.Ease.Settle);

            yield return AdvanceUnscaled(0.13f);

            Assert.AreEqual(target.r, img.color.r, 1e-4f, "Tint must land exactly on the target red channel.");
            Assert.AreEqual(target.g, img.color.g, 1e-4f, "Tint must land exactly on the target green channel.");
            Assert.AreEqual(target.b, img.color.b, 1e-4f, "Tint must land exactly on the target blue channel.");
        }

        // -----------------------------------------------------------------
        // Boundary — fixed capacity (160)
        // -----------------------------------------------------------------

        /// <summary>
        /// At full capacity (160 live tweens), starting one more on a
        /// brand-new target must NOT be dropped — the pool evicts the slot
        /// nearest completion (force-finishing it in place) and hands the
        /// new request that slot. This is the documented trade-off in
        /// UiTween.Claim(): "never drop the new request."
        /// </summary>
        [UnityTest]
        public IEnumerator Alpha_AtFullCapacity_EvictsNearestCompletionInsteadOfDroppingTheNewRequest()
        {
            const int capacity = 160;
            var fillers = new List<CanvasGroup>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                CanvasGroup cg = NewCanvasGroup($"CapacityFiller{i}");
                cg.alpha = 0f;
                // Long duration: none of these may finish on their own during this test.
                UiTween.Alpha(cg, 1f, 10f, UiMotion.Ease.Linear);
                fillers.Add(cg);
            }
            Assert.AreEqual(capacity, UiTween.ActiveCount, "Test setup: the pool must be completely full.");

            CanvasGroup overflowTarget = NewCanvasGroup("CapacityOverflow");
            overflowTarget.alpha = 0f;
            UiTweenHandle handle = UiTween.Alpha(overflowTarget, 1f, 0.08f, UiMotion.Ease.Linear);

            Assert.IsTrue(handle.IsValid,
                "A tween requested while the pool is full must still return a valid handle — the request must never be silently dropped.");
            Assert.AreEqual(capacity, UiTween.ActiveCount,
                "Evicting one slot to admit the new request must keep the pool at its capacity ceiling, not grow past it.");

            // The evicted victim was force-completed in place (WriteValue at
            // tNorm=1) before being handed to the new request — exactly one
            // of the 160 fillers must now read alpha == 1 despite its 10s
            // duration never elapsing, and the rest must be untouched.
            int completedFillers = 0;
            int untouchedFillers = 0;
            foreach (CanvasGroup g in fillers)
            {
                if (g == null) continue;
                if (Mathf.Approximately(g.alpha, 1f)) completedFillers++;
                else if (Mathf.Approximately(g.alpha, 0f)) untouchedFillers++;
            }
            Assert.AreEqual(1, completedFillers, "Exactly one filler should have been force-completed to free the slot the overflow request needed.");
            Assert.AreEqual(capacity - 1, untouchedFillers, "All fillers except the evicted one must be untouched by the overflow request.");

            // The overflow tween itself must still be free to run normally afterward.
            yield return AdvanceUnscaled(0.13f);
            Assert.AreEqual(1f, overflowTarget.alpha, 1e-4f, "The overflow tween must complete normally in its own new slot.");
        }
    }
}
