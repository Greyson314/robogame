using System;
using Robogame.Robots;

namespace Robogame.Combat
{
    /// <summary>
    /// Static fan-in for "a projectile hurt a robot" reports carrying
    /// the projectile's <see cref="ProjectileKind"/> — the musical
    /// damage-feedback layer's feed (ADR-0006). Sibling of
    /// <see cref="DamageAttribution"/>, kept separate because the
    /// scoreboard consumer doesn't care about kinds and the music
    /// consumer doesn't care about environment damage; neither event
    /// signature has to grow for the other.
    /// </summary>
    /// <remarks>
    /// Cosmetic-only by contract: subscribers schedule presentation
    /// (stingers), never gameplay. Reports fire where damage is applied
    /// (offline or server today); post-netcode the client-side
    /// projectile visuals layer reports locally — musical feedback is
    /// allowed to be client-local (INV-3 untouched).
    /// </remarks>
    public static class MusicalHits
    {
        /// <summary>
        /// Raised on every damaging projectile hit against a robot.
        /// Args: attacker (may be null), victim (never null), the
        /// projectile kind, nominal damage of the hit.
        /// </summary>
        public static event Action<Robot, Robot, ProjectileKind, float> Reported;

        /// <summary>Report a damaging projectile hit. Guards match
        /// <see cref="DamageAttribution.Report"/>.</summary>
        public static void Report(Robot attacker, Robot victim, ProjectileKind kind, float amount)
        {
            if (victim == null || amount <= 0f) return;
            Reported?.Invoke(attacker, victim, kind, amount);
        }

        // TRACE[DOC:CLAUDE.md§Known failure modes]: statics survive domain
        // reload — stale subscribers double-play.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reported = null;
    }
}
