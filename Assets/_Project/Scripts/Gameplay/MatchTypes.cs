using System;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Which "team" a chassis (or kill event) belongs to. Two-sided today; a
    /// future team-vs-team mode would extend this enum, but the
    /// <see cref="MatchController"/> contract is structured so that adding a
    /// new value here doesn't break existing call sites — score is per-side
    /// indexed.
    /// </summary>
    public enum MatchSide
    {
        /// <summary>Default for events that don't have a meaningful side (warmup-time deaths, environment damage).</summary>
        None = 0,
        /// <summary>The local human player and any future co-op companions.</summary>
        Player = 1,
        /// <summary>AI-controlled bots opposing the player.</summary>
        Enemy = 2,
    }

    /// <summary>
    /// Round state. <see cref="WarmingUp"/> covers the pre-fight grace period
    /// (no kills count, no timer); <see cref="InProgress"/> is the live round
    /// (kills count, timer ticks, win conditions evaluate);
    /// <see cref="RoundEnded"/> is terminal — no further state changes.
    /// </summary>
    public enum MatchState
    {
        WarmingUp = 0,
        InProgress = 1,
        RoundEnded = 2,
    }

    /// <summary>
    /// Why <see cref="MatchController.MatchEnded"/> fired. Distinguishing the
    /// reason lets the end overlay show the right copy ("Defeated by scrap
    /// limit" vs "Out of lives" vs "Time up — draw").
    /// </summary>
    public enum MatchEndReason
    {
        /// <summary>One side deposited at least <see cref="MatchConfig.TargetTeamScrap"/>.</summary>
        ScrapLimitReached = 0,
        /// <summary>Round timer expired with one side leading on team scrap.</summary>
        TimeExpired = 1,
        /// <summary>Player ran out of lives.</summary>
        PlayerEliminated = 2,
        /// <summary>Round timer expired with both sides tied.</summary>
        Draw = 3,
    }

    /// <summary>
    /// Payload for <see cref="MatchController.MatchEnded"/>. Struct so the
    /// event invocation has zero GC cost and the values are immutable across
    /// the listener fan-out.
    /// </summary>
    public readonly struct MatchEndedArgs
    {
        public readonly MatchSide WinnerSide;
        public readonly MatchEndReason Reason;
        public readonly int PlayerScore;
        public readonly int EnemyScore;

        public MatchEndedArgs(MatchSide winner, MatchEndReason reason, int playerScore, int enemyScore)
        {
            WinnerSide = winner;
            Reason = reason;
            PlayerScore = playerScore;
            EnemyScore = enemyScore;
        }
    }

    /// <summary>
    /// Designer-facing tuning for a single match. Plain <see cref="SerializableAttribute"/>
    /// class so it can be embedded as a <c>[SerializeField]</c> on
    /// <see cref="ArenaController"/> AND constructed inline by EditMode tests
    /// (<c>new MatchConfig { ... }</c>) without needing
    /// <see cref="ScriptableObject.CreateInstance{T}"/> in test code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value here is gameplay-observable — frag limits, round duration,
    /// AI fire range, lives — so per the
    /// <see href="https://github.com/anthropics/robogame/blob/main/docs/PHYSICS_PLAN.md#15">PHYSICS_PLAN
    /// § 1.5</see> rule these MUST NOT live in <c>Tweakables</c>. Multiplayer
    /// will read these from a server-canonical config; for now they're set in
    /// the inspector on the arena scene's <see cref="ArenaController"/>.
    /// </para>
    /// <para>
    /// Public fields (not auto-properties) so Unity serialises them AND so
    /// the EditMode tests' object-initializer syntax compiles.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class MatchConfig
    {
        [Header("Round shape")]
        [Tooltip("If true, the warmup never auto-ends — the round begins only when MatchController.StartMatch is called (e.g. by clicking the StartMatchHud's FIGHT! button). If false, WarmupDuration governs auto-start.")]
        public bool RequireManualStart = true;

        [Tooltip("Seconds of warmup before the round auto-starts (no kills count, timer doesn't tick). Ignored when RequireManualStart is true.")]
        [Min(0f)] public float WarmupDuration = 3f;

        [Tooltip("Total round length in seconds. Match ends in a Draw if no side leads at expiry.")]
        [Min(10f)] public float RoundDuration = 300f;

        [Tooltip("Team scrap required to win. First side to deposit this much scrap at their depot wins immediately.")]
        [Min(1)] public int TargetTeamScrap = 20;

        [Tooltip("Flat scrap dropped when a chassis dies, on top of whatever scrap the victim was carrying.")]
        [Min(0)] public int BaseDeathDrop = 3;

        [Tooltip("Maximum scrap a single robot can carry. Past this, scrap pickups remain in the world. Encourages depositing.")]
        [Min(1)] public int ScrapCarryCapacity = 8;

        [Tooltip("Lives the player has before the match ends in PlayerEliminated. Bots respawn until frag limit.")]
        [Min(1)] public int PlayerLives = 3;

        [Tooltip("Seconds before a destroyed bot respawns. <= 0 = bots do not respawn.")]
        [Min(0f)] public float BotRespawnDelay = 4f;

        [Tooltip("Seconds before the destroyed player can respawn. The DeathOverlay covers this window.")]
        [Min(0f)] public float PlayerRespawnDelay = 2.5f;

        [Header("Bot roster")]
        [Tooltip("One entry per ground bot to spawn at match start. Spawn position is provided by ArenaController.")]
        public BotEntry[] GroundBots;

        [Tooltip("One entry per air bot to spawn at match start. Spawn position is provided by ArenaController.")]
        public BotEntry[] AirBots;

        /// <summary>
        /// One ground- or air-bot configuration line. The blueprint is the
        /// chassis the bot uses; everything else (target, patrol radius,
        /// fire range) is left at the AI input source's defaults so designer
        /// tuning happens via the SerializeFields on those classes — not
        /// duplicated here.
        /// </summary>
        [Serializable]
        public struct BotEntry
        {
            [Tooltip("Chassis the bot drives. Must match the bot type (ground entries should use ground-kind blueprints, etc).")]
            public ChassisBlueprint Blueprint;

            [Tooltip("Optional explicit spawn position. Vector3.zero falls back to ArenaController's per-side default spawn.")]
            public Vector3 SpawnPositionOverride;

            [Tooltip("Optional patrol-circle centre. Vector3.zero falls back to the world origin (ground bot).")]
            public Vector3 PatrolCentreOverride;
        }
    }
}
