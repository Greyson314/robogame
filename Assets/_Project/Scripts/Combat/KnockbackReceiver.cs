using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-chassis sink for weapon-hit knockback impulses. Lazily added
    /// to a <see cref="Robot"/> the first time it takes a knockback hit,
    /// so a never-hit bot never carries this component (zero baseline
    /// cost — invariant #5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both application paths apply the impulse at the chassis centre of
    /// mass, making knockback pure translation: a graze on a wing tip
    /// can't barrel-roll a light bot. Rotational impact stays the
    /// momentum-damage system's concern; this layer is displacement only.
    /// All forces go to the single chassis Rigidbody (invariant #4).
    /// </para>
    /// <list type="bullet">
    /// <item><b>Immediate</b> (cannon / mortar / explosion) — the impulse
    ///       lands this physics step. Punchy stagger / pop.</item>
    /// <item><b>Smoothed</b> (rapid-fire SMG) — the impulse accumulates
    ///       into a debt vector that bleeds out over
    ///       <see cref="DebtTimeConstant"/>. A 12 Hz pellet stream becomes
    ///       one bounded push instead of per-frame jitter.</item>
    /// </list>
    /// <para>
    /// Every impulse is clamped to a delta-v ceiling scaled by chassis
    /// mass, so no weapon can launch a skeleton-framed light bot to orbit
    /// — combat stays readable regardless of how little a target weighs.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class KnockbackReceiver : MonoBehaviour
    {
        // Seconds for the smoothed debt to bleed to ~37% of its value
        // (one time constant). The SMG fires every ~0.083 s, so under
        // sustained fire debt accumulates faster than it drains → a
        // steady push; it relaxes within ~2 τ once fire stops.
        private const float DebtTimeConstant = 0.7f;

        // Per-immediate-hit delta-v ceiling (m/s). A single cannon shot
        // changes chassis velocity by at most this, independent of bot
        // mass — the cap that stops light bots from being launched.
        private const float MaxImmediateDeltaV = 3.0f;

        // Ceiling on the accumulated smoothed debt, expressed as the
        // delta-v it would impart. Bounds sustained rapid fire.
        private const float MaxDebtDeltaV = 4.0f;

        private Rigidbody _rb;
        private Vector3 _debt; // world-space impulse waiting to bleed out

        private void Awake()
        {
            Robot robot = GetComponent<Robot>();
            _rb = robot != null ? robot.Rigidbody : GetComponent<Rigidbody>();
        }

        /// <summary>Apply an impulse this physics step — punchy single hits.</summary>
        public void ApplyImmediate(Vector3 worldImpulse)
        {
            if (_rb == null) return;
            worldImpulse = ClampToDeltaV(worldImpulse, MaxImmediateDeltaV);
            _rb.AddForceAtPosition(worldImpulse, _rb.worldCenterOfMass, ForceMode.Impulse);
        }

        /// <summary>Accumulate an impulse to bleed out over time — rapid fire.</summary>
        public void AddSmoothed(Vector3 worldImpulse)
        {
            _debt = ClampToDeltaV(_debt + worldImpulse, MaxDebtDeltaV);
        }

        private void FixedUpdate()
        {
            if (_rb == null || _debt.sqrMagnitude < 1e-6f) return;
            // Exponential bleed: chunk = debt · (1 − e^(−dt/τ)). The sum
            // of every chunk converges to the full debt, so total momentum
            // imparted equals one impulse of `debt` — just spread in time.
            float k = 1f - Mathf.Exp(-Time.fixedDeltaTime / DebtTimeConstant);
            Vector3 chunk = _debt * k;
            _rb.AddForceAtPosition(chunk, _rb.worldCenterOfMass, ForceMode.Impulse);
            _debt -= chunk;
        }

        // Scale an impulse down so it imparts at most `maxDeltaV` to this
        // chassis (impulse = mass · Δv). Below the ceiling it passes
        // through untouched.
        private Vector3 ClampToDeltaV(Vector3 impulse, float maxDeltaV)
        {
            float maxImpulse = _rb.mass * maxDeltaV;
            float m = impulse.magnitude;
            if (m > maxImpulse && m > 1e-5f) impulse *= maxImpulse / m;
            return impulse;
        }
    }
}
