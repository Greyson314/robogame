using System.Collections.Generic;

namespace Robogame.Gameplay
{
    /// <summary>
    /// One combatant's running stat line for the current round. Identity is
    /// the <see cref="DisplayName"/> — rows survive chassis respawns (the
    /// fresh Robot re-binds to the same row via
    /// <see cref="MatchStatsTracker.GetOrCreate"/>), so K/D/DMG accumulate
    /// across lives the way a player expects a scoreboard to behave.
    /// </summary>
    public sealed class CombatantStats
    {
        public string DisplayName { get; }
        public MatchSide Side { get; }
        /// <summary>True for the local player's row — the scoreboard highlights it.</summary>
        public bool IsPlayer { get; }

        public int Kills { get; internal set; }
        public int Deaths { get; internal set; }
        /// <summary>Nominal damage dealt (headline hit values, not per-block HP). See <c>DamageAttribution</c>.</summary>
        public float DamageDealt { get; internal set; }
        public int ScrapDeposited { get; internal set; }
        /// <summary>False between death and respawn. The scoreboard dims dead rows.</summary>
        public bool Alive { get; internal set; } = true;

        // Last enemy that damaged this combatant + when — drives
        // kill-credit attribution in MatchStatsTracker.RecordDeath.
        internal CombatantStats LastAttacker;
        internal float LastAttackedAt = float.NegativeInfinity;

        internal CombatantStats(string displayName, MatchSide side, bool isPlayer)
        {
            DisplayName = displayName;
            Side = side;
            IsPlayer = isPlayer;
        }
    }

    /// <summary>
    /// Per-combatant kill / death / damage / scrap tracker for one round.
    /// Plain C# (no UnityEngine) so EditMode tests drive it deterministically
    /// — same construction rationale as <see cref="MatchController"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kill attribution: the killer is the last <i>opposing-side</i>
    /// combatant to damage the victim within the credit window before the
    /// death. This fixes the "killer is always the other side" inference in
    /// <c>ArenaController.HandleRobotDestroyed</c> for multi-bot rounds:
    /// a victim that rams a wall 30 s after last being shot dies
    /// uncredited, while a victim finished by terrain seconds after a
    /// firefight credits the shooter — standard FPS bookkeeping.
    /// </para>
    /// <para>
    /// The tracker never touches Robot / MonoBehaviour types. The caller
    /// (<c>ArenaController</c>) owns the Robot→row mapping and pushes
    /// events in with its own clock, which is what makes the credit-window
    /// logic testable without a scene.
    /// </para>
    /// </remarks>
    public sealed class MatchStatsTracker
    {
        public const float DefaultKillCreditWindowSeconds = 8f;

        private readonly Dictionary<string, CombatantStats> _byName = new(8);
        private readonly List<CombatantStats> _rows = new(8);
        private readonly float _killCreditWindow;

        /// <summary>
        /// Bumped on every stat mutation. HUDs compare against a cached copy
        /// to rebuild rendered strings only when something actually changed
        /// (no per-frame formatting allocs).
        /// </summary>
        public int Version { get; private set; }

        /// <summary>All rows in registration order (player first by convention of the caller).</summary>
        public IReadOnlyList<CombatantStats> Rows => _rows;

        public MatchStatsTracker(float killCreditWindowSeconds = DefaultKillCreditWindowSeconds)
        {
            _killCreditWindow = killCreditWindowSeconds;
        }

        /// <summary>
        /// Fetch the row for <paramref name="displayName"/>, creating it on
        /// first sight. Re-fetching an existing row (bot respawn) marks it
        /// alive again — the caller doesn't need a separate respawn call.
        /// </summary>
        public CombatantStats GetOrCreate(string displayName, MatchSide side, bool isPlayer = false)
        {
            if (string.IsNullOrEmpty(displayName)) displayName = "?";
            if (_byName.TryGetValue(displayName, out CombatantStats existing))
            {
                if (!existing.Alive)
                {
                    existing.Alive = true;
                    Version++;
                }
                return existing;
            }

            var row = new CombatantStats(displayName, side, isPlayer);
            _byName[displayName] = row;
            _rows.Add(row);
            Version++;
            return row;
        }

        /// <summary>
        /// Record a damaging hit. Null attacker (environment) still updates
        /// nothing but is accepted so call sites stay guard-free; null victim
        /// (damage to an unregistered chassis, e.g. the warmup dummy) is
        /// ignored. Self-damage counts toward nobody.
        /// </summary>
        public void RecordDamage(CombatantStats attacker, CombatantStats victim, float amount, float now)
        {
            if (victim == null || amount <= 0f) return;
            if (attacker == null || attacker == victim) return;

            attacker.DamageDealt += amount;
            Version++;

            // Only opposing-side damage arms kill credit — a teammate's
            // splash overlap must never claim an enemy's kill.
            if (attacker.Side != victim.Side)
            {
                victim.LastAttacker = attacker;
                victim.LastAttackedAt = now;
            }
        }

        /// <summary>
        /// Record a death. Bumps the victim's death count, marks it dead,
        /// and credits a kill to the last opposing attacker within the
        /// credit window. Returns the credited row, or null when the death
        /// was unattributed (environment / stale damage).
        /// </summary>
        public CombatantStats RecordDeath(CombatantStats victim, float now)
        {
            if (victim == null) return null;

            victim.Deaths++;
            victim.Alive = false;

            CombatantStats credited = null;
            if (victim.LastAttacker != null
                && now - victim.LastAttackedAt <= _killCreditWindow)
            {
                credited = victim.LastAttacker;
                credited.Kills++;
            }

            // A respawned victim starts with a clean attribution slate.
            victim.LastAttacker = null;
            victim.LastAttackedAt = float.NegativeInfinity;
            Version++;
            return credited;
        }

        /// <summary>Record scrap banked at a depot by this combatant.</summary>
        public void RecordScrapDeposit(CombatantStats depositor, int amount)
        {
            if (depositor == null || amount <= 0) return;
            depositor.ScrapDeposited += amount;
            Version++;
        }
    }
}
