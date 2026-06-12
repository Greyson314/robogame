using System;
using Robogame.Robots;

namespace Robogame.Combat
{
    /// <summary>
    /// Static fan-in point for "who damaged whom" reports. Every damage
    /// source (projectiles, ram impacts, future hazards) calls
    /// <see cref="Report"/> with the attacking and victim <see cref="Robot"/>s;
    /// match-level consumers (the per-combatant stats tracker on
    /// <c>ArenaController</c>) subscribe to <see cref="Reported"/> once and
    /// never need to know how many damage sources exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The amounts reported are <b>nominal</b> (the headline damage of the
    /// hit, e.g. <c>ProjectileSpec.Damage</c> or splash ring 0) — not the
    /// exact per-block HP drained after falloff. The consumer is a
    /// scoreboard stat, not a damage ledger; nominal keeps every report
    /// site O(1) with no per-block accounting.
    /// </para>
    /// <para>
    /// Server-authoritative by construction: all report sites already run
    /// only where damage is applied (offline or server). A future netcode
    /// phase replicates the aggregated stats, not these events.
    /// </para>
    /// </remarks>
    public static class DamageAttribution
    {
        /// <summary>
        /// Raised on every damaging robot-vs-robot hit.
        /// Args: attacker (may be null for environment damage), victim
        /// (never null), nominal damage amount (always &gt; 0).
        /// </summary>
        public static event Action<Robot, Robot, float> Reported;

        /// <summary>
        /// Report a damaging hit. No-ops when <paramref name="victim"/> is
        /// null or <paramref name="amount"/> is non-positive, so call sites
        /// don't need their own guards.
        /// </summary>
        public static void Report(Robot attacker, Robot victim, float amount)
        {
            if (victim == null || amount <= 0f) return;
            Reported?.Invoke(attacker, victim, amount);
        }

        // TRACE[DOC:CLAUDE.md§Known failure modes]: statics survive domain
        // reload — a stale subscriber from the previous play session would
        // keep a destroyed ArenaController alive and double-count.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reported = null;
    }
}
