using System;
using Robogame.Core;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Round state machine for a singleplayer match. Plain C# class — no
    /// MonoBehaviour, no scene dependency — so EditMode tests can drive it
    /// deterministically and a future <c>NetworkBehaviour</c> wrapper can
    /// own one without restructuring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owned and ticked by <see cref="ArenaController"/>. Listens to nothing
    /// directly — kills, lives, and time are pushed in via the public API
    /// (<see cref="RegisterKill"/>, <see cref="NotifyPlayerLivesExhausted"/>,
    /// <see cref="Tick"/>). This keeps the netcode story simple: in MP, the
    /// server-side <c>NetworkBehaviour</c> wrapper owns the controller and
    /// pushes the same calls; clients receive the raised events as RPCs.
    /// </para>
    /// <para>
    /// State transitions:
    /// <code>
    /// WarmingUp  --(timer ≥ WarmupDuration)-->  InProgress
    /// InProgress --(score hits TargetFragCount)-->  RoundEnded   (FragLimitReached)
    /// InProgress --(round timer expires)-->         RoundEnded   (TimeExpired or Draw)
    /// InProgress --(NotifyPlayerLivesExhausted)-->  RoundEnded   (PlayerEliminated)
    /// </code>
    /// All other inputs are ignored from <c>RoundEnded</c> — that state is
    /// terminal (re-entry is a new <see cref="MatchController"/> instance).
    /// </para>
    /// </remarks>
    public sealed class MatchController
    {
        private readonly MatchConfig _config;

        private float _clock;
        private int _playerScore;
        private int _enemyScore;
        private int _playerLivesRemaining;

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

        // ------- events --------------------------------------------------

        /// <summary>Fires once when the warmup completes and the round begins.</summary>
        public event Action MatchStarted;

        /// <summary>Fires for every kill that counts toward the score (i.e. while <see cref="State"/> is <see cref="MatchState.InProgress"/>).</summary>
        public event Action<MatchSide /*killer*/, MatchSide /*victim*/> KillRegistered;

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
                // Manual-start mode: warmup is indefinite. Only StartMatch
                // (called from the FIGHT! button) leaves WarmingUp. The
                // warmup timer still runs (TimeRemaining reflects it) but
                // doesn't trigger the transition.
                if (_config.RequireManualStart) return;
                if (_clock >= _config.WarmupDuration)
                {
                    // Reset the clock so InProgress measures from the start
                    // of the round, not the start of the warmup.
                    _clock = 0f;
                    State = MatchState.InProgress;
                    MatchStarted?.Invoke();
                }
                return;
            }

            // InProgress: check round-timer expiry. Frag-limit is checked
            // inside RegisterKill at the moment the kill lands, not here.
            if (_clock >= _config.RoundDuration)
            {
                MatchEndReason reason;
                MatchSide winner;
                if (_playerScore > _enemyScore)
                {
                    reason = MatchEndReason.TimeExpired;
                    winner = MatchSide.Player;
                }
                else if (_enemyScore > _playerScore)
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
        /// Register a kill. Ignored unless <see cref="State"/> is <see cref="MatchState.InProgress"/>.
        /// Increments the killer's score, raises <see cref="KillRegistered"/>, and
        /// triggers <see cref="MatchEnded"/> if the killer's score reaches
        /// <see cref="MatchConfig.TargetFragCount"/>.
        /// </summary>
        public void RegisterKill(MatchSide killerSide, MatchSide victimSide)
        {
            // Pre-round + post-round kills are dropped silently — spawning
            // a chassis can fire a Robot.Destroyed during teardown of the
            // previous round, and a stray projectile mid-warmup shouldn't
            // count.
            if (State != MatchState.InProgress) return;

            switch (killerSide)
            {
                case MatchSide.Player: _playerScore++; break;
                case MatchSide.Enemy:  _enemyScore++;  break;
                default: return; // None / unknown — ignore (environment kill)
            }

            KillRegistered?.Invoke(killerSide, victimSide);

            int target = UnityEngine.Mathf.Max(1, _config.TargetFragCount);
            if (_playerScore >= target)
            {
                EndMatch(MatchSide.Player, MatchEndReason.FragLimitReached);
            }
            else if (_enemyScore >= target)
            {
                EndMatch(MatchSide.Enemy, MatchEndReason.FragLimitReached);
            }
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
        /// Distinct from frag-limit win because a player might be eliminated
        /// while leading on score — the outcome is still "Enemy wins by
        /// elimination" with the right end-overlay copy.
        /// </summary>
        public void NotifyPlayerLivesExhausted()
        {
            if (State != MatchState.InProgress && State != MatchState.WarmingUp) return;
            EndMatch(MatchSide.Enemy, MatchEndReason.PlayerEliminated);
        }

        // ------- queries --------------------------------------------------

        /// <summary>Current kill count for the given side. Returns 0 for <see cref="MatchSide.None"/>.</summary>
        public int ScoreForSide(MatchSide side)
        {
            return side switch
            {
                MatchSide.Player => _playerScore,
                MatchSide.Enemy  => _enemyScore,
                _ => 0,
            };
        }

        // ------- internals --------------------------------------------------

        private void EndMatch(MatchSide winner, MatchEndReason reason)
        {
            // Idempotency: only fire once. Two end conditions can fire on the
            // same Tick (e.g. timer expires the frame the frag limit is hit);
            // the first one wins, subsequent calls are swallowed.
            if (State == MatchState.RoundEnded) return;

            State = MatchState.RoundEnded;
            MatchEnded?.Invoke(new MatchEndedArgs(winner, reason, _playerScore, _enemyScore));
        }
    }
}
