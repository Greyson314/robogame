// =============================================================================
// MatchControllerTests — EditMode
//
// What this suite covers
// -----------------------
// Pure state-machine logic for MatchController. No Unity scene, no
// MonoBehaviours, no physics. MatchController must be constructible and
// drivable from code via a Tick(float deltaTime) method so these tests can
// advance time without a running scene.
//
// Invariants exercised
// --------------------
// • State machine starts in WarmingUp, transitions to InProgress after the
//   warmup period.
// • KillRegistered increments the correct side's score.
// • KillRegistered raises MatchEnded when the kill count hits the target frag.
// • Round timer expiry with no winner → Draw.
// • Round timer expiry with one side ahead → that side wins.
// • Player elimination (all lives exhausted) → MatchEnded(Enemy, PlayerEliminated).
// • Idempotency: MatchEnded fires exactly once even if multiple end conditions
//   trigger on the same Tick.
// • NETCODE_PLAN readiness: the state machine is a plain C# object with no
//   UnityEngine dependencies beyond config data, so it can be deterministically
//   replayed on a server without a scene.
//
// Requested production APIs (must land before these tests compile)
// ---------------------------------------------------------------
// • MatchController(MatchConfig config)  — constructor, no MonoBehaviour
// • MatchController.Tick(float deltaTime) — advances the clock and fires events
// • MatchController.State — MatchState enum { WarmingUp, InProgress, RoundEnded }
// • MatchController.ScoreForSide(MatchSide side) — int, returns current kill count
// • MatchController.RegisterKill(MatchSide killerSide, MatchSide victimSide)
// • MatchController.NotifyPlayerLivesExhausted() — triggers PlayerEliminated path
// • Events: MatchController.MatchStarted, MatchController.KillRegistered,
//            MatchController.MatchEnded(MatchEndedArgs { WinnerSide, Reason })
// • MatchConfig — ScriptableObject or plain class with fields:
//     float WarmupDuration, float RoundDuration, int TargetFragCount, int PlayerLives
// • MatchSide enum { Player, Enemy, None }
// • MatchEndReason enum { FragLimitReached, TimeExpired, PlayerEliminated, Draw }
// • MatchEndedArgs struct { MatchSide WinnerSide; MatchEndReason Reason; }
// =============================================================================

using System;
using NUnit.Framework;

// API in flight — using the planned namespace for the feature under development.
// Tests will fail to compile until MatchController, MatchConfig, etc. exist.
// That is intentional: these test failures are the implementation checklist.
using Robogame.Gameplay;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class MatchControllerTests
    {
        // -----------------------------------------------------------------
        // Factory helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Mint a MatchConfig with sensible defaults. Override individual
        /// fields per test to isolate the condition under test.
        /// </summary>
        private static MatchConfig MakeConfig(
            float warmupDuration    = 3f,
            float roundDuration     = 120f,
            int   targetFragCount   = 5,
            int   playerLives       = 1)
        {
            // API in flight: MatchConfig constructor. If MatchConfig is a
            // ScriptableObject, use ScriptableObject.CreateInstance<MatchConfig>()
            // and assign fields; if it's a plain class, use new MatchConfig().
            // The field names below reflect the planned API.
            var cfg = new MatchConfig
            {
                // Tests exercise the warmup-timer auto-transition path, not
                // the production default which is manual-start (FIGHT! button).
                // Opting out keeps these tests focused on the timer logic.
                RequireManualStart = false,
                WarmupDuration  = warmupDuration,
                RoundDuration   = roundDuration,
                TargetFragCount = targetFragCount,
                PlayerLives     = playerLives,
            };
            return cfg;
        }

        /// <summary>
        /// Advance the controller past the warmup phase by ticking exactly
        /// <paramref name="seconds"/> worth of warmup time, then one more small
        /// step so the transition fires.
        /// </summary>
        private static void TickThroughWarmup(MatchController mc, float warmupDuration)
        {
            // API in flight: MatchController.Tick(float)
            mc.Tick(warmupDuration + 0.01f);
        }

        // -----------------------------------------------------------------
        // State machine entry
        // -----------------------------------------------------------------

        [Test]
        public void MatchController_InitialState_IsWarmingUp()
        {
            // API in flight; update assertion when MatchController.ctor lands.
            MatchController mc = new MatchController(MakeConfig());

            Assert.AreEqual(MatchState.WarmingUp, mc.State,
                "MatchController must start in WarmingUp before any Tick.");
        }

        [Test]
        public void Tick_AfterWarmupDuration_TransitionsToInProgress()
        {
            float warmup = 3f;
            MatchController mc = new MatchController(MakeConfig(warmupDuration: warmup));

            bool startedFired = false;
            // API in flight: MatchController.MatchStarted event.
            mc.MatchStarted += () => startedFired = true;

            TickThroughWarmup(mc, warmup);

            Assert.AreEqual(MatchState.InProgress, mc.State,
                "State must be InProgress once warmup time elapses.");
            Assert.IsTrue(startedFired,
                "MatchStarted event must fire on warmup-to-in-progress transition.");
        }

        [Test]
        public void Tick_BeforeWarmupExpires_RemainsInWarmingUp()
        {
            float warmup = 5f;
            MatchController mc = new MatchController(MakeConfig(warmupDuration: warmup));

            mc.Tick(warmup * 0.5f); // half the warmup; should not transition

            Assert.AreEqual(MatchState.WarmingUp, mc.State,
                "State must remain WarmingUp until the full warmup duration elapses.");
        }

        // -----------------------------------------------------------------
        // Kill registration and scoring
        // -----------------------------------------------------------------

        [Test]
        public void RegisterKill_PlayerKillsEnemy_IncrementsPlayerScore()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 99));
            TickThroughWarmup(mc, 3f);

            // API in flight: MatchController.RegisterKill(killerSide, victimSide)
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            // API in flight: MatchController.ScoreForSide(side)
            Assert.AreEqual(1, mc.ScoreForSide(MatchSide.Player),
                "Player score must increment by 1 after killing an enemy.");
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Enemy),
                "Enemy score must remain 0 when the player is the killer.");
        }

        [Test]
        public void RegisterKill_EnemyKillsPlayer_IncrementsEnemyScore()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 99));
            TickThroughWarmup(mc, 3f);

            mc.RegisterKill(MatchSide.Enemy, MatchSide.Player);

            Assert.AreEqual(1, mc.ScoreForSide(MatchSide.Enemy),
                "Enemy score must increment by 1 after killing the player.");
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Player),
                "Player score must remain 0 when the enemy is the killer.");
        }

        [Test]
        public void KillRegistered_Event_IncludesCorrectSides()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 99));
            TickThroughWarmup(mc, 3f);

            MatchSide firedKiller = MatchSide.None;
            MatchSide firedVictim = MatchSide.None;
            // API in flight: event KillRegistered(killerSide, victimSide)
            mc.KillRegistered += (killer, victim) =>
            {
                firedKiller = killer;
                firedVictim = victim;
            };

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.AreEqual(MatchSide.Player, firedKiller,
                "KillRegistered event must pass the correct killer side.");
            Assert.AreEqual(MatchSide.Enemy, firedVictim,
                "KillRegistered event must pass the correct victim side.");
        }

        // -----------------------------------------------------------------
        // Frag limit win
        // -----------------------------------------------------------------

        [Test]
        public void RegisterKill_HitsTargetFrag_RaisesMatchEndedWithCorrectWinner()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 1));
            TickThroughWarmup(mc, 3f);

            MatchEndedArgs? endArgs = null;
            // API in flight: event MatchEnded(MatchEndedArgs)
            mc.MatchEnded += args => endArgs = args;

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.IsNotNull(endArgs,
                "MatchEnded must fire when kill count reaches targetFragCount.");
            Assert.AreEqual(MatchSide.Player, endArgs!.Value.WinnerSide,
                "WinnerSide must be Player when the player hits frag limit first.");
            Assert.AreEqual(MatchEndReason.FragLimitReached, endArgs.Value.Reason,
                "Reason must be FragLimitReached on frag-limit win.");
            Assert.AreEqual(MatchState.RoundEnded, mc.State,
                "State must be RoundEnded after MatchEnded fires.");
        }

        [Test]
        public void RegisterKill_Enemy_HitsTargetFrag_RaisesMatchEndedEnemyWinner()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 1));
            TickThroughWarmup(mc, 3f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.RegisterKill(MatchSide.Enemy, MatchSide.Player);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Enemy, endArgs!.Value.WinnerSide,
                "WinnerSide must be Enemy when the enemy hits frag limit.");
        }

        // -----------------------------------------------------------------
        // Timer expiry
        // -----------------------------------------------------------------

        [Test]
        public void Tick_RoundTimerExpires_NoLeader_RaisesMatchEndedDraw()
        {
            float roundDuration = 10f;
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: roundDuration, targetFragCount: 99));

            TickThroughWarmup(mc, 0f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.Tick(roundDuration + 0.1f);

            Assert.IsNotNull(endArgs,
                "MatchEnded must fire when round timer expires.");
            Assert.AreEqual(MatchSide.None, endArgs!.Value.WinnerSide,
                "WinnerSide must be None on a draw.");
            Assert.AreEqual(MatchEndReason.Draw, endArgs.Value.Reason,
                "Reason must be Draw when neither side leads at timer expiry.");
        }

        [Test]
        public void Tick_RoundTimerExpires_PlayerLeading_RaisesMatchEndedPlayerWins()
        {
            float roundDuration = 10f;
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: roundDuration, targetFragCount: 99));

            TickThroughWarmup(mc, 0f);
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy); // player leads 1-0

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.Tick(roundDuration + 0.1f);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Player, endArgs!.Value.WinnerSide,
                "Player must win by score when the round timer expires with a lead.");
            Assert.AreEqual(MatchEndReason.TimeExpired, endArgs.Value.Reason,
                "Reason must be TimeExpired (not FragLimitReached) on timer win.");
        }

        [Test]
        public void Tick_RoundTimerExpires_EnemyLeading_RaisesMatchEndedEnemyWins()
        {
            float roundDuration = 10f;
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: roundDuration, targetFragCount: 99));

            TickThroughWarmup(mc, 0f);
            mc.RegisterKill(MatchSide.Enemy, MatchSide.Player); // enemy leads 1-0

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.Tick(roundDuration + 0.1f);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Enemy, endArgs!.Value.WinnerSide,
                "Enemy must win by score when the round timer expires with a lead.");
        }

        // -----------------------------------------------------------------
        // Player elimination
        // -----------------------------------------------------------------

        [Test]
        public void NotifyPlayerLivesExhausted_RaisesMatchEndedPlayerEliminated()
        {
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, playerLives: 1));
            TickThroughWarmup(mc, 0f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            // API in flight: MatchController.NotifyPlayerLivesExhausted()
            mc.NotifyPlayerLivesExhausted();

            Assert.IsNotNull(endArgs,
                "MatchEnded must fire immediately when the player has no lives left.");
            Assert.AreEqual(MatchSide.Enemy, endArgs!.Value.WinnerSide,
                "Enemy wins when the player is eliminated.");
            Assert.AreEqual(MatchEndReason.PlayerEliminated, endArgs.Value.Reason,
                "Reason must be PlayerEliminated, not a timer/frag path.");
        }

        // -----------------------------------------------------------------
        // Idempotency — MatchEnded fires exactly once
        // -----------------------------------------------------------------

        [Test]
        public void MatchEnded_FiresExactlyOnce_WhenMultipleEndConditionsTriggerSameTick()
        {
            // Contrived scenario: frag limit = 1 AND round duration = 0 so both
            // conditions fire on the same Tick call. MatchEnded must still fire
            // exactly once.
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: 0.001f, targetFragCount: 1));
            TickThroughWarmup(mc, 0f);

            int endCount = 0;
            mc.MatchEnded += _ => endCount++;

            // Single kill that hits frag limit; the timer is also about to expire.
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);
            mc.Tick(1f); // well past the round timer

            Assert.AreEqual(1, endCount,
                "MatchEnded must fire exactly once regardless of how many end conditions trigger. " +
                "Duplicate fires would corrupt score boards and double-show the end overlay.");
        }

        [Test]
        public void RegisterKill_AfterRoundEnded_IsIgnored()
        {
            MatchController mc = new MatchController(MakeConfig(targetFragCount: 1));
            TickThroughWarmup(mc, 3f);

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy); // ends the round

            Assert.AreEqual(MatchState.RoundEnded, mc.State, "Precondition: round must be over.");

            int scoreBeforeSpam = mc.ScoreForSide(MatchSide.Player);
            int endCount = 0;
            mc.MatchEnded += _ => endCount++;

            // Spam kills after the round is over — these must be swallowed.
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.AreEqual(scoreBeforeSpam, mc.ScoreForSide(MatchSide.Player),
                "Score must not increase after RoundEnded state.");
            Assert.AreEqual(0, endCount,
                "MatchEnded must not fire again after the round has already ended.");
        }

        // -----------------------------------------------------------------
        // RegisterKill ignored before InProgress
        // -----------------------------------------------------------------

        [Test]
        public void RegisterKill_DuringWarmup_IsIgnored()
        {
            // Kills during warmup (e.g. stray projectile from previous round or
            // spawn damage) must not count toward the score or trigger a win.
            MatchController mc = new MatchController(MakeConfig(warmupDuration: 5f, targetFragCount: 1));

            Assert.AreEqual(MatchState.WarmingUp, mc.State, "Precondition: still warming up.");

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.IsNull(endArgs,
                "MatchEnded must not fire for kills registered during WarmingUp.");
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Player),
                "Score must not increment during WarmingUp.");
        }
    }
}
