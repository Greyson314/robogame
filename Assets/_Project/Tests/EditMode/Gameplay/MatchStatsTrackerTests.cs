// =============================================================================
// MatchStatsTrackerTests — EditMode
//
// What this suite covers
// -----------------------
// Per-combatant scoreboard bookkeeping: damage accumulation, last-damager
// kill attribution inside/outside the credit window, friendly-fire credit
// exclusion, death counting, respawn row identity (same name → same row,
// marked alive again), and scrap-deposit accumulation. These rules ARE the
// scoreboard's honesty contract — a kill credited to the wrong combatant
// or a stat line lost on respawn is a player-visible bug, which is why
// each rule gets its own test rather than one happy-path sweep.
// =============================================================================

using NUnit.Framework;
using Robogame.Gameplay;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class MatchStatsTrackerTests
    {
        private const float Window = MatchStatsTracker.DefaultKillCreditWindowSeconds;

        // -----------------------------------------------------------------
        // Row identity
        // -----------------------------------------------------------------

        [Test]
        public void GetOrCreate_SameName_ReturnsSameRow()
        {
            var tracker = new MatchStatsTracker();
            var a = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);
            var b = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);
            Assert.AreSame(a, b, "Stable name must map to one persistent row — respawned bots keep their stat line.");
            Assert.AreEqual(1, tracker.Rows.Count);
        }

        [Test]
        public void GetOrCreate_AfterDeath_MarksRowAliveAgain()
        {
            var tracker = new MatchStatsTracker();
            var row = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);
            tracker.RecordDeath(row, now: 10f);
            Assert.IsFalse(row.Alive, "Death must mark the row dead (scoreboard dims it).");

            tracker.GetOrCreate("BOT 1", MatchSide.Enemy); // respawn re-bind
            Assert.IsTrue(row.Alive, "Respawn re-fetch must revive the row without a separate call.");
            Assert.AreEqual(1, row.Deaths, "Revival must not erase accumulated deaths.");
        }

        // -----------------------------------------------------------------
        // Damage + kill attribution
        // -----------------------------------------------------------------

        [Test]
        public void RecordDamage_AccumulatesOnAttacker()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(you, bot, 12f, now: 1f);
            tracker.RecordDamage(you, bot, 8f, now: 2f);

            Assert.AreEqual(20f, you.DamageDealt, 0.001f);
            Assert.AreEqual(0f, bot.DamageDealt, 0.001f, "Victim's dealt-damage must not move on being hit.");
        }

        [Test]
        public void RecordDeath_WithinWindow_CreditsLastAttacker()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(you, bot, 30f, now: 5f);
            var credited = tracker.RecordDeath(bot, now: 5f + Window - 0.1f);

            Assert.AreSame(you, credited);
            Assert.AreEqual(1, you.Kills);
            Assert.AreEqual(1, bot.Deaths);
        }

        [Test]
        public void RecordDeath_OutsideWindow_NoCredit()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(you, bot, 30f, now: 5f);
            var credited = tracker.RecordDeath(bot, now: 5f + Window + 1f);

            Assert.IsNull(credited, "Stale damage must not claim a kill — a bot that rams a wall a minute after a firefight died to the wall.");
            Assert.AreEqual(0, you.Kills);
            Assert.AreEqual(1, bot.Deaths, "The death itself still counts.");
        }

        [Test]
        public void RecordDeath_LastAttackerWins_NotFirst()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var ally = tracker.GetOrCreate("ALLY", MatchSide.Player);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(you, bot, 50f, now: 1f);
            tracker.RecordDamage(ally, bot, 5f, now: 2f);
            var credited = tracker.RecordDeath(bot, now: 3f);

            Assert.AreSame(ally, credited, "Most recent opposing damager takes the credit (standard FPS last-hit rule).");
        }

        [Test]
        public void RecordDamage_SameSide_NeverArmsKillCredit()
        {
            var tracker = new MatchStatsTracker();
            var bot1 = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);
            var bot2 = tracker.GetOrCreate("BOT 2", MatchSide.Enemy);

            // Friendly splash still counts as damage dealt…
            tracker.RecordDamage(bot1, bot2, 25f, now: 1f);
            Assert.AreEqual(25f, bot1.DamageDealt, 0.001f);

            // …but must never claim the kill.
            var credited = tracker.RecordDeath(bot2, now: 2f);
            Assert.IsNull(credited, "A teammate must never be credited with an enemy's kill.");
            Assert.AreEqual(0, bot1.Kills);
        }

        [Test]
        public void RecordDeath_ClearsAttributionForNextLife()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(you, bot, 30f, now: 1f);
            tracker.RecordDeath(bot, now: 2f);
            tracker.GetOrCreate("BOT 1", MatchSide.Enemy); // respawn

            // Untouched second life dies to the environment immediately:
            // the first life's damage must not carry credit across lives.
            var credited = tracker.RecordDeath(bot, now: 3f);
            Assert.IsNull(credited, "Kill attribution must reset between lives.");
            Assert.AreEqual(1, you.Kills, "First-life kill stands; no double credit.");
        }

        [Test]
        public void RecordDamage_NullAttackerOrSelf_Ignored()
        {
            var tracker = new MatchStatsTracker();
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);

            tracker.RecordDamage(null, bot, 40f, now: 1f); // environment
            tracker.RecordDamage(bot, bot, 40f, now: 1f);  // self

            Assert.AreEqual(0f, bot.DamageDealt, 0.001f);
            var credited = tracker.RecordDeath(bot, now: 2f);
            Assert.IsNull(credited, "Environment/self damage must never arm kill credit.");
        }

        // -----------------------------------------------------------------
        // Scrap
        // -----------------------------------------------------------------

        [Test]
        public void RecordScrapDeposit_Accumulates()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);

            tracker.RecordScrapDeposit(you, 5);
            tracker.RecordScrapDeposit(you, 3);
            tracker.RecordScrapDeposit(you, 0);  // no-op
            tracker.RecordScrapDeposit(null, 4); // no-op

            Assert.AreEqual(8, you.ScrapDeposited);
        }

        // -----------------------------------------------------------------
        // HUD dirty-flag contract
        // -----------------------------------------------------------------

        [Test]
        public void Version_BumpsOnMutation_StableOtherwise()
        {
            var tracker = new MatchStatsTracker();
            var you = tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            var bot = tracker.GetOrCreate("BOT 1", MatchSide.Enemy);
            int v = tracker.Version;

            // Reads + no-op writes must not dirty the HUD cache…
            _ = tracker.Rows;
            tracker.RecordDamage(null, bot, 10f, now: 1f);
            tracker.GetOrCreate("YOU", MatchSide.Player, isPlayer: true);
            Assert.AreEqual(v, tracker.Version, "No-ops must not force the scoreboard to re-format strings.");

            // …real mutations must.
            tracker.RecordDamage(you, bot, 10f, now: 1f);
            Assert.Greater(tracker.Version, v, "Mutations must dirty the HUD cache or the scoreboard renders stale numbers.");
        }
    }
}
