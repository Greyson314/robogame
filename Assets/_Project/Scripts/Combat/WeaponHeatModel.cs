using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Pure, deterministic spin-up + overheat state machine for sustained-
    /// fire weapons. No MonoBehaviour, no clock reads — the owner ticks it
    /// with explicit deltas, so it is headless-testable and (later)
    /// replayable under CSP.
    /// </summary>
    /// <remarks>
    /// Design intent: spin-up rewards commitment (rate lerps min→max over
    /// the ramp), overheat punishes unbroken sustain (lockout), and
    /// feathering the trigger is the skill expression (heat cools at the
    /// same rate it accumulates, so a ~50% duty cycle never trips).
    /// State transitions happen at tick granularity: a tick that crosses
    /// the overheat threshold trips lockout in that same tick, and the
    /// tick in which the cooldown expires resets heat to zero without
    /// re-accumulating from any leftover delta.
    /// </remarks>
    public sealed class WeaponHeatModel
    {
        private readonly float _spinUpSeconds;
        private readonly float _spinDownSeconds;
        private readonly float _overheatSeconds;
        private readonly float _overheatCooldownSeconds;
        private readonly float _minFireRate;
        private readonly float _maxFireRate;

        private float _cooldownRemaining;

        public WeaponHeatModel(
            float spinUpSeconds,
            float spinDownSeconds,
            float overheatSeconds,
            float overheatCooldownSeconds,
            float minFireRate,
            float maxFireRate)
        {
            _spinUpSeconds           = Mathf.Max(1e-4f, spinUpSeconds);
            _spinDownSeconds         = Mathf.Max(1e-4f, spinDownSeconds);
            _overheatSeconds         = Mathf.Max(1e-4f, overheatSeconds);
            _overheatCooldownSeconds = Mathf.Max(0f, overheatCooldownSeconds);
            _minFireRate             = minFireRate;
            _maxFireRate             = maxFireRate;
        }

        /// <summary>0 = cold start (min rate), 1 = fully spun up (max rate).</summary>
        public float SpinUp01 { get; private set; }

        /// <summary>0 = cool, 1 = tripping into lockout.</summary>
        public float Heat01 { get; private set; }

        /// <summary>True during the post-trip lockout window. No firing, no heat accumulation.</summary>
        public bool IsOverheated { get; private set; }

        /// <summary>Current shots-per-second given the spin-up state.</summary>
        public float CurrentFireRate => Mathf.Lerp(_minFireRate, _maxFireRate, SpinUp01);

        /// <summary>Advance the state machine by <paramref name="dt"/> seconds.</summary>
        public void Tick(bool triggerHeld, float dt)
        {
            if (dt <= 0f) return;

            if (IsOverheated)
            {
                // Lockout runs on its own clock: the trigger can neither
                // extend nor shorten it, and holding it does not bank heat.
                _cooldownRemaining -= dt;
                SpinUp01 = Mathf.Max(0f, SpinUp01 - dt / _spinDownSeconds);
                if (_cooldownRemaining <= 0f)
                {
                    _cooldownRemaining = 0f;
                    IsOverheated = false;
                    Heat01 = 0f;
                }
                return;
            }

            if (triggerHeld)
            {
                SpinUp01 = Mathf.Min(1f, SpinUp01 + dt / _spinUpSeconds);
                Heat01 += dt / _overheatSeconds;
                if (Heat01 >= 1f)
                {
                    Heat01 = 1f;
                    IsOverheated = true;
                    _cooldownRemaining = _overheatCooldownSeconds;
                    if (_cooldownRemaining <= 0f)
                    {
                        // Degenerate config: zero cooldown clears instantly.
                        IsOverheated = false;
                        Heat01 = 0f;
                    }
                }
            }
            else
            {
                SpinUp01 = Mathf.Max(0f, SpinUp01 - dt / _spinDownSeconds);
                Heat01 = Mathf.Max(0f, Heat01 - dt / _overheatSeconds);
            }
        }
    }
}
