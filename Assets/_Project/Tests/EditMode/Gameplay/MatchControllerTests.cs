// =============================================================================
// MatchControllerTests — EditMode
//
// What this suite covers
// -----------------------
// Pure state-machine logic for MatchController. Scrap-based scoring: kills
// fire informational events for HUD streak banners but don't change the
// score; deposits at team depots do. First side to TargetTeamScrap wins.
// =============================================================================

using NUnit.Framework;
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
            float warmupDuration   = 3f,
            float roundDuration    = 120f,
            int   targetTeamScrap  = 20,
            int   playerLives      = 1)
        {
            var cfg = new MatchConfig
            {
                RequireManualStart = false,
                WarmupDuration  = warmupDuration,
                RoundDuration   = roundDuration,
                TargetTeamScrap = targetTeamScrap,
                PlayerLives     = playerLives,
            };
            return cfg;
        }

        private static void TickThroughWarmup(MatchController mc, float warmupDuration)
        {
            mc.Tick(warmupDuration + 0.01f);
        }

        // -----------------------------------------------------------------
        // State machine entry
        // -----------------------------------------------------------------

        [Test]
        public void MatchController_InitialState_IsWarmingUp()
        {
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
            mc.Tick(warmup * 0.5f);
            Assert.AreEqual(MatchState.WarmingUp, mc.State);
        }

        // -----------------------------------------------------------------
        // Scrap deposits and scoring
        // -----------------------------------------------------------------

        [Test]
        public void DepositScrap_IncrementsTeamTotal()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 99));
            TickThroughWarmup(mc, 3f);

            int newTotal = mc.DepositScrap(MatchSide.Player, 5);

            Assert.AreEqual(5, newTotal, "DepositScrap must return the post-deposit total.");
            Assert.AreEqual(5, mc.ScoreForSide(MatchSide.Player),
                "Player team total must reflect the deposited scrap.");
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Enemy),
                "Enemy team total must remain 0 when only the player deposited.");
        }

        [Test]
        public void DepositScrap_FiresTeamScrapChanged()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 99));
            TickThroughWarmup(mc, 3f);

            MatchSide firedSide = MatchSide.None;
            int firedTotal = -1;
            mc.TeamScrapChanged += (side, total) => { firedSide = side; firedTotal = total; };

            mc.DepositScrap(MatchSide.Enemy, 3);

            Assert.AreEqual(MatchSide.Enemy, firedSide);
            Assert.AreEqual(3, firedTotal,
                "TeamScrapChanged must carry the new running total, not the delta.");
        }

        [Test]
        public void DepositScrap_HitsTarget_RaisesMatchEndedWithCorrectWinner()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 5));
            TickThroughWarmup(mc, 3f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.DepositScrap(MatchSide.Player, 5);

            Assert.IsNotNull(endArgs,
                "MatchEnded must fire when team scrap hits TargetTeamScrap.");
            Assert.AreEqual(MatchSide.Player, endArgs!.Value.WinnerSide);
            Assert.AreEqual(MatchEndReason.ScrapLimitReached, endArgs.Value.Reason);
            Assert.AreEqual(MatchState.RoundEnded, mc.State);
            Assert.AreEqual(5, endArgs.Value.PlayerScore,
                "MatchEndedArgs.PlayerScore must carry the final team-scrap total.");
        }

        [Test]
        public void DepositScrap_EnemyHitsTarget_EnemyWinner()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 4));
            TickThroughWarmup(mc, 3f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.DepositScrap(MatchSide.Enemy, 4);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Enemy, endArgs!.Value.WinnerSide);
        }

        [Test]
        public void DepositScrap_NegativeOrZero_NoOp()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 99));
            TickThroughWarmup(mc, 3f);
            mc.DepositScrap(MatchSide.Player, 5);

            mc.DepositScrap(MatchSide.Player, 0);
            mc.DepositScrap(MatchSide.Player, -3);

            Assert.AreEqual(5, mc.ScoreForSide(MatchSide.Player),
                "Zero / negative deposits must be ignored — spend paths get their own API later.");
        }

        // -----------------------------------------------------------------
        // RegisterKill is now informational only
        // -----------------------------------------------------------------

        [Test]
        public void RegisterKill_DoesNotChangeScore()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 99));
            TickThroughWarmup(mc, 3f);

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Player),
                "Kills must NOT increment the team-scrap total — only deposits do.");
        }

        [Test]
        public void RegisterKill_FiresKillRegisteredEvent_ForHudFeedback()
        {
            MatchController mc = new MatchController(MakeConfig());
            TickThroughWarmup(mc, 3f);

            MatchSide firedKiller = MatchSide.None;
            MatchSide firedVictim = MatchSide.None;
            mc.KillRegistered += (k, v) => { firedKiller = k; firedVictim = v; };

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);

            Assert.AreEqual(MatchSide.Player, firedKiller,
                "KillRegistered event must still fire for KillAnnouncer streak banner.");
            Assert.AreEqual(MatchSide.Enemy, firedVictim);
        }

        [Test]
        public void RegisterKill_IncrementsKillsForSide_ForScoreboard()
        {
            // ObjectiveHud's "FRAGS" row reads KillsForSide each side per
            // frame — guarantee the counter actually moves on RegisterKill
            // so the scoreboard isn't stuck on 0 forever even though
            // KillRegistered events fire.
            MatchController mc = new MatchController(MakeConfig());
            TickThroughWarmup(mc, 3f);

            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);
            mc.RegisterKill(MatchSide.Player, MatchSide.Enemy);
            mc.RegisterKill(MatchSide.Enemy,  MatchSide.Player);

            Assert.AreEqual(2, mc.KillsForSide(MatchSide.Player),
                "Player frag count must match RegisterKill(Player, _) calls.");
            Assert.AreEqual(1, mc.KillsForSide(MatchSide.Enemy),
                "Enemy frag count must match RegisterKill(Enemy, _) calls.");
            Assert.AreEqual(0, mc.KillsForSide(MatchSide.None),
                "Neutral side must always report zero frags.");
        }

        // -----------------------------------------------------------------
        // Timer expiry
        // -----------------------------------------------------------------

        [Test]
        public void Tick_RoundTimerExpires_NoLeader_RaisesMatchEndedDraw()
        {
            float roundDuration = 10f;
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: roundDuration, targetTeamScrap: 99));
            TickThroughWarmup(mc, 0f);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.Tick(roundDuration + 0.1f);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.None, endArgs!.Value.WinnerSide);
            Assert.AreEqual(MatchEndReason.Draw, endArgs.Value.Reason);
        }

        [Test]
        public void Tick_RoundTimerExpires_PlayerLeading_RaisesMatchEndedPlayerWins()
        {
            float roundDuration = 10f;
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: roundDuration, targetTeamScrap: 99));
            TickThroughWarmup(mc, 0f);
            mc.DepositScrap(MatchSide.Player, 1); // player leads 1-0 on team scrap

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.Tick(roundDuration + 0.1f);

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Player, endArgs!.Value.WinnerSide);
            Assert.AreEqual(MatchEndReason.TimeExpired, endArgs.Value.Reason);
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

            mc.NotifyPlayerLivesExhausted();

            Assert.IsNotNull(endArgs);
            Assert.AreEqual(MatchSide.Enemy, endArgs!.Value.WinnerSide);
            Assert.AreEqual(MatchEndReason.PlayerEliminated, endArgs.Value.Reason);
        }

        // -----------------------------------------------------------------
        // Idempotency — MatchEnded fires exactly once
        // -----------------------------------------------------------------

        [Test]
        public void MatchEnded_FiresExactlyOnce_WhenMultipleEndConditionsTriggerSameTick()
        {
            MatchController mc = new MatchController(
                MakeConfig(warmupDuration: 0f, roundDuration: 0.001f, targetTeamScrap: 1));
            TickThroughWarmup(mc, 0f);

            int endCount = 0;
            mc.MatchEnded += _ => endCount++;

            mc.DepositScrap(MatchSide.Player, 1); // hits scrap limit
            mc.Tick(1f); // well past the round timer — should NOT re-fire

            Assert.AreEqual(1, endCount);
        }

        [Test]
        public void DepositScrap_AfterRoundEnded_IsIgnored()
        {
            MatchController mc = new MatchController(MakeConfig(targetTeamScrap: 1));
            TickThroughWarmup(mc, 3f);
            mc.DepositScrap(MatchSide.Player, 1); // ends the round

            Assert.AreEqual(MatchState.RoundEnded, mc.State);

            int totalBefore = mc.ScoreForSide(MatchSide.Player);
            int endCount = 0;
            mc.MatchEnded += _ => endCount++;

            mc.DepositScrap(MatchSide.Player, 5);

            Assert.AreEqual(totalBefore, mc.ScoreForSide(MatchSide.Player));
            Assert.AreEqual(0, endCount);
        }

        // -----------------------------------------------------------------
        // RegisterKill ignored before InProgress
        // -----------------------------------------------------------------

        [Test]
        public void DepositScrap_DuringWarmup_IsIgnored()
        {
            MatchController mc = new MatchController(MakeConfig(warmupDuration: 5f, targetTeamScrap: 1));
            Assert.AreEqual(MatchState.WarmingUp, mc.State);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.DepositScrap(MatchSide.Player, 1);

            Assert.IsNull(endArgs);
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Player),
                "Deposits during WarmingUp must not count.");
        }
    }
}
