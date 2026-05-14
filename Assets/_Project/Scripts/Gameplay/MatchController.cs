using System;
using Robogame.Core;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Round state machine for a singleplayer / team match. Plain C# class —
    /// no MonoBehaviour, no scene dependency — so EditMode tests can drive it
    /// deterministically and a future <c>NetworkBehaviour</c> wrapper can
    /// own one without restructuring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owned and ticked by <see cref="ArenaController"/>. Listens to nothing
    /// directly — deposits, lives, and time are pushed in via the public API
    /// (<see cref="DepositScrap"/>, <see cref="NotifyPlayerLivesExhausted"/>,
    /// <see cref="Tick"/>). This keeps the netcode story simple: in MP, the
    /// server-side <c>NetworkBehaviour</c> wrapper owns the controller and
    /// pushes the same calls; clients receive the raised events as RPCs.
    /// </para>
    /// <para>
    /// Scoring: this match counts <b>team scrap deposited at depots</b>, not
    /// kills. Kills generate scrap drops (handled by <c>ScrapDropper</c>) that
    /// surviving robots pick up + carry, then bank at their team's
    /// <c>ScrapDepot</c>. The controller is told about deposits via
    /// <see cref="DepositScrap"/> and never sees the per-robot carry state.
    /// </para>
    /// <para>
    /// State transitions:
    /// <code>
    /// WarmingUp  --(timer ≥ WarmupDuration | StartMatch())-->  InProgress
    /// InProgress --(team scrap hits TargetTeamScrap)-->  RoundEnded   (ScrapLimitReached)
    /// InProgress --(round timer expires)-->              RoundEnded   (TimeExpired or Draw)
    /// InProgress --(NotifyPlayerLivesExhausted)-->       RoundEnded   (PlayerEliminated)
    /// </code>
    /// All other inputs are ignored from <c>RoundEnded</c> — that state is
    /// terminal (re-entry is a new <see cref="MatchController"/> instance).
    /// </para>
    /// </remarks>
    public sealed class MatchController
    {
        private readonly MatchConfig _config;

        private float _clock;
        private int _playerTeamScrap;
        private int _enemyTeamScrap;
        private int _playerLivesRemaining;
        // Frag counters per team. Not part of the win condition (scrap
        // deposits drive that) but the scoreboard surfaces them so the
        // player has an at-a-glance read of "am I doing damage?"
        // alongside the slower scrap economy. Incremented in
        // RegisterKill; reset to 0 on construction.
        private int _playerKills;
        private int _enemyKills;

        /// <summary>Current state of the round.</summary>
        public MatchState State { get; private set; } = MatchState.WarmingUp;

        /// <summary>Round-clock seconds remaining (only meaningful while <see cref="State"/> is <see cref="MatchState.InProgress"/>).</summary>
        public float TimeRemaining =>
            State == MatchState.InProgress
                ? UnityEngine.Mathf.Max(0f, _config.RoundDuration - _clock)
                : (State == MatchState.WarmingUp
                    ? UnityEngine.Mathf.Max(0f, _config.WarmupDuration - _clock)
                    : 0f);

        /// <summary>Lives the player has left. Decrements via <see cref="NotifyPlayerLivesExhausted"/>'s caller (<see cref="ArenaController"/>).</summary>
        public int PlayerLivesRemaining => _playerLivesRemaining;

        /// <summary>The config this match is running against. Read-only after construction.</summary>
        public MatchConfig Config => _config;

        /// <summary>Target team-scrap total that wins the round.</summary>
        public int TargetTeamScrap => UnityEngine.Mathf.Max(1, _config.TargetTeamScrap);

        // ------- events --------------------------------------------------

        /// <summary>Fires once when the warmup completes and the round begins.</summary>
        public event Action MatchStarted;

        /// <summary>
        /// Fires for every kill that occurs (during <see cref="MatchState.InProgress"/>).
        /// Kills no longer drive the score — scrap deposits do — but the
        /// notification is kept so the <c>KillAnnouncer</c> can keep its
        /// streak banner, and any future cosmetic-feedback systems
        /// (kill-cam, kill-feed) have something to hang off.
        /// </summary>
        public event Action<MatchSide /*killer*/, MatchSide /*victim*/> KillRegistered;

        /// <summary>
        /// Fires whenever a team's scrap total changes. Args: the side whose
        /// total changed, the new running total. HUDs subscribe so the
        /// counter at the top of the screen updates exactly when the value
        /// moves (no per-frame polling needed).
        /// </summary>
        public event Action<MatchSide /*side*/, int /*newTotal*/> TeamScrapChanged;

        /// <summary>Fires exactly once when the round ends, regardless of how many end conditions trigger on the same tick.</summary>
        public event Action<MatchEndedArgs> MatchEnded;

        // ------- ctor --------------------------------------------------

        /// <summary>
        /// Build a controller from a config. The config is captured by reference
        /// so live edits in the inspector during a session are picked up; if
        /// you need an immutable config, clone before constructing.
        /// </summary>
        public MatchController(MatchConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _playerLivesRemaining = UnityEngine.Mathf.Max(1, _config.PlayerLives);
        }

        // ------- per-frame tick --------------------------------------------------

        /// <summary>
        /// Advance the controller by <paramref name="deltaTime"/> seconds.
        /// Drives the warmup timer, the round timer, and the timer-expiry
        /// end conditions. Idempotent on terminal states.
        /// </summary>
        public void Tick(float deltaTime)
        {
            using var _scope = PerfMarkers.MatchControllerUpdate.Auto();

            if (State == MatchState.RoundEnded) return;
            if (deltaTime <= 0f) return;

            _clock += deltaTime;

            if (State == MatchState.WarmingUp)
            {
                if (_config.RequireManualStart) return;
                if (_clock >= _config.WarmupDuration)
                {
                    _clock = 0f;
                    State = MatchState.InProgress;
                    MatchStarted?.Invoke();
                }
                return;
            }

            // InProgress: round-timer expiry. Scrap-limit win is checked
            // inside DepositScrap at the moment the deposit lands, not
            // here.
            if (_clock >= _config.RoundDuration)
            {
                MatchEndReason reason;
                MatchSide winner;
                if (_playerTeamScrap > _enemyTeamScrap)
                {
                    reason = MatchEndReason.TimeExpired;
                    winner = MatchSide.Player;
                }
                else if (_enemyTeamScrap > _playerTeamScrap)
                {
                    reason = MatchEndReason.TimeExpired;
                    winner = MatchSide.Enemy;
                }
                else
                {
                    reason = MatchEndReason.Draw;
                    winner = MatchSide.None;
                }
                EndMatch(winner, reason);
            }
        }

        /// <summary>
        /// Fast-forward from <see cref="MatchState.WarmingUp"/> to
        /// <see cref="MatchState.InProgress"/>, raising
        /// <see cref="MatchStarted"/>. Idempotent — safe to call from a
        /// player-driven "FIGHT!" button without worrying about the warmup
        /// timer also firing the transition. After the round is in progress
        /// or ended, this is a no-op.
        /// </summary>
        public void StartMatch()
        {
            if (State != MatchState.WarmingUp) return;
            _clock = 0f;
            State = MatchState.InProgress;
            MatchStarted?.Invoke();
        }

        // ------- public mutators --------------------------------------------------

        /// <summary>
        /// Register a kill. Ignored unless <see cref="State"/> is
        /// <see cref="MatchState.InProgress"/>. Kills do not change the score
        /// — scrap deposits do — but the event is raised so kill-streak HUDs
        /// (<c>KillAnnouncer</c>) keep working.
        /// </summary>
        public void RegisterKill(MatchSide killerSide, MatchSide victimSide)
        {
            if (State != MatchState.InProgress) return;
            if (killerSide == MatchSide.None) return; // environment kills don't generate streaks
            if (killerSide == MatchSide.Player) _playerKills++;
            else if (killerSide == MatchSide.Enemy) _enemyKills++;
            KillRegistered?.Invoke(killerSide, victimSide);
        }

        /// <summary>
        /// Deposit <paramref name="amount"/> scrap into the given side's
        /// team total. Called by <c>ScrapDepot</c> when a robot of that
        /// side touches the depot trigger. Returns the side's new total
        /// after the deposit (clamped at the target if the deposit
        /// overflows).
        /// </summary>
        public int DepositScrap(MatchSide side, int amount)
        {
            if (State != MatchState.InProgress) return ScoreForSide(side);
            if (amount <= 0) return ScoreForSide(side);

            int newTotal;
            switch (side)
            {
                case MatchSide.Player:
                    _playerTeamScrap += amount;
                    newTotal = _playerTeamScrap;
                    break;
                case MatchSide.Enemy:
                    _enemyTeamScrap += amount;
                    newTotal = _enemyTeamScrap;
                    break;
                default: return 0;
            }

            TeamScrapChanged?.Invoke(side, newTotal);

            int target = TargetTeamScrap;
            if (_playerTeamScrap >= target)
            {
                EndMatch(MatchSide.Player, MatchEndReason.ScrapLimitReached);
            }
            else if (_enemyTeamScrap >= target)
            {
                EndMatch(MatchSide.Enemy, MatchEndReason.ScrapLimitReached);
            }

            return newTotal;
        }

        /// <summary>
        /// Decrement the player's life count. Returns the remaining life count
        /// after the decrement. <see cref="ArenaController"/> calls this whenever
        /// the local <c>Robot.Destroyed</c> event fires; when the result is
        /// zero, the controller would not yet have fired <see cref="MatchEnded"/>
        /// — call <see cref="NotifyPlayerLivesExhausted"/> after detecting the
        /// zero return to seal the match.
        /// </summary>
        public int DecrementPlayerLives()
        {
            if (State != MatchState.InProgress) return _playerLivesRemaining;
            _playerLivesRemaining = UnityEngine.Mathf.Max(0, _playerLivesRemaining - 1);
            return _playerLivesRemaining;
        }

        /// <summary>
        /// Force-end the match because the player has no lives left.
        /// Distinct from scrap-limit win because a player might be eliminated
        /// while leading on scrap — the outcome is still "Enemy wins by
        /// elimination" with the right end-overlay copy.
        /// </summary>
        public void NotifyPlayerLivesExhausted()
        {
            if (State != MatchState.InProgress && State != MatchState.WarmingUp) return;
            EndMatch(MatchSide.Enemy, MatchEndReason.PlayerEliminated);
        }

        // ------- queries --------------------------------------------------

        /// <summary>
        /// Running kill count for the given side this round. Bumped by
        /// every <see cref="RegisterKill"/> call where that side is the
        /// killer; not part of the win condition. Returns 0 for
        /// <see cref="MatchSide.None"/>.
        /// </summary>
        public int KillsForSide(MatchSide side)
        {
            return side switch
            {
                MatchSide.Player => _playerKills,
                MatchSide.Enemy  => _enemyKills,
                _ => 0,
            };
        }

        /// <summary>Current team-scrap total for the given side. Returns 0 for <see cref="MatchSide.None"/>.</summary>
        public int ScoreForSide(MatchSide side)
        {
            return side switch
            {
                MatchSide.Player => _playerTeamScrap,
                MatchSide.Enemy  => _enemyTeamScrap,
                _ => 0,
            };
        }

        // ------- internals --------------------------------------------------

        private void EndMatch(MatchSide winner, MatchEndReason reason)
        {
            // Idempotency: only fire once. Two end conditions can fire on the
            // same Tick; the first one wins, subsequent calls are swallowed.
            if (State == MatchState.RoundEnded) return;

            State = MatchState.RoundEnded;
            MatchEnded?.Invoke(new MatchEndedArgs(winner, reason, _playerTeamScrap, _enemyTeamScrap));
        }
    }
}
