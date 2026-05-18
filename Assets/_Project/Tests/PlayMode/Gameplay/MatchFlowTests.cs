// =============================================================================
// MatchFlowTests — PlayMode
//
// What this suite covers
// -----------------------
// Integration-level tests for the Pillar 1 singleplayer game loop. These
// tests need a running scene (MonoBehaviours, events, multi-frame timing)
// so they live in PlayMode.
//
// Tests exercise the following flows:
//  1. Scrap-deposit win — deposit enough scrap for TargetTeamScrap=1, assert
//     MatchEnded fires with Player as winner and state is RoundEnded.
//  2. Player death during a round does NOT increment the enemy score when the
//     player still has lives remaining; the game continues.
//  3. Bot spawn — genuinely untestable in isolation: requires GameStateController
//     + ChassisFactory + a full-scene stack. [Ignore] with documented blocker.
//  4. RoundEnded terminal-state — MatchController stays in RoundEnded on
//     additional Tick calls; the state is final.
//  5. MatchEndOverlay becomes visible once MatchController raises MatchEnded.
//  6. ObjectiveHud.DisplayedPlayerScore reflects MatchController.ScoreForSide
//     after a DepositScrap call.
//
// Naming follows the project convention:
//   {ClassName}Tests.{Feature}_{Scenario}_{Expected}
//
// Assertion philosophy (CLAUDE.md Rule 9): every assertion encodes WHY the
// behaviour matters. A test that can pass with a no-op implementation is wrong.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Robogame.Gameplay;

namespace Robogame.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// Integration tests for the Pillar-1 match game loop.
    /// Each test builds a minimal in-scene rig by hand — no scene files, no
    /// prefabs that must exist on disk. SetUp creates fresh GameObjects;
    /// TearDown destroys them.
    /// </summary>
    public class MatchFlowTests
    {
        // Root objects destroyed in TearDown.
        private readonly List<GameObject> _roots = new();

        // -----------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            _roots.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _roots)
                if (go != null) Object.Destroy(go);
            _roots.Clear();
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>Create a tracked GameObject. Destroyed automatically in TearDown.</summary>
        private GameObject MakeGo(string name)
        {
            var go = new GameObject(name);
            _roots.Add(go);
            return go;
        }

        /// <summary>
        /// Build a MatchController with zero warmup so tests can go straight to
        /// InProgress via a single Tick. Matches the pattern in
        /// MatchControllerTests (EditMode counterpart).
        /// </summary>
        private static MatchController MakeStartedController(int targetTeamScrap = 5, float roundDuration = 300f, int playerLives = 3)
        {
            var cfg = new MatchConfig
            {
                RequireManualStart = false,
                WarmupDuration     = 0f,
                RoundDuration      = roundDuration,
                TargetTeamScrap    = targetTeamScrap,
                PlayerLives        = playerLives,
            };
            var mc = new MatchController(cfg);
            mc.Tick(0.01f); // advance past zero-duration warmup → InProgress
            Assert.AreEqual(MatchState.InProgress, mc.State,
                "Helper precondition: controller must be InProgress before test body runs.");
            return mc;
        }

        // -----------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------

        /// <summary>
        /// Scrap-deposit win: deposit exactly TargetTeamScrap=1, assert MatchEnded
        /// fires with Player as winner and the state transitions to RoundEnded.
        ///
        /// WHY: this is the primary singleplayer win condition. If DepositScrap
        /// doesn't raise MatchEnded, the round never ends regardless of how much
        /// scrap the player banks. If WinnerSide is wrong, the end overlay shows
        /// "DEFEAT" for a win. Both are regression-critical for the netcode
        /// wrapper, which will drive MatchEnded as an RPC.
        /// </summary>
        [UnityTest]
        public IEnumerator RegisterKill_FragLimitOf1_PlayerKills_MatchEndedFires()
        {
            // The test was originally scaffolded around a TargetFragCount win
            // condition that was never implemented. The real win condition is
            // scrap-deposit-based (MatchController.DepositScrap hitting
            // TargetTeamScrap). This test now covers that path; the method name
            // is preserved so the test runner history stays continuous.
            MatchController mc = MakeStartedController(targetTeamScrap: 1);

            MatchEndedArgs? endArgs = null;
            mc.MatchEnded += args => endArgs = args;

            mc.DepositScrap(MatchSide.Player, 1);

            // MatchEnded must fire synchronously on DepositScrap — no frame wait
            // needed because MatchController is a plain C# class. Yield once to
            // match [UnityTest] contract and to surface any deferred callbacks
            // if the implementation adds them.
            yield return null;

            Assert.IsNotNull(endArgs,
                "MatchEnded must fire after player deposits enough scrap to hit TargetTeamScrap=1. " +
                "If this fails, DepositScrap's win-condition check is broken.");
            Assert.AreEqual(MatchSide.Player, endArgs!.Value.WinnerSide,
                "WinnerSide must be Player when the player side hits the scrap limit. " +
                "A wrong WinnerSide renders the wrong end-overlay (DEFEAT on a win).");
            Assert.AreEqual(MatchEndReason.ScrapLimitReached, endArgs.Value.Reason,
                "MatchEndReason must be ScrapLimitReached. The netcode wrapper uses Reason " +
                "to pick the correct RPC payload for the end-of-round overlay.");
            Assert.AreEqual(MatchState.RoundEnded, mc.State,
                "State must be RoundEnded after MatchEnded fires — the state machine must " +
                "be terminal. Additional Tick calls or deposits must not re-fire MatchEnded.");
        }

        /// <summary>
        /// Player death with lives remaining: DecrementPlayerLives when 3 lives
        /// are configured must leave 2 lives, must NOT change the enemy score,
        /// and must leave the match InProgress.
        ///
        /// WHY: kills are informational (KillRegistered fires for streak banners)
        /// but do NOT score points — only scrap deposits do. A player death with
        /// lives remaining is a respawn trigger, not a match-ending event.
        /// If enemy score increments here, the ScrapDepot UI shows a phantom
        /// point the enemy never earned. If State moves to RoundEnded, the match
        /// ends when the player dies with lives remaining — a critical regression
        /// that would make all multi-life configs unplayable.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerDeath_WithLivesRemaining_DoesNotIncrementEnemyScore()
        {
            MatchController mc = MakeStartedController(targetTeamScrap: 99, playerLives: 3);

            // Simulate a player death: register the kill (fires KillRegistered for
            // HUD banners) then decrement lives (what ArenaController.HandleRobotDestroyed
            // does when victimSide == Player).
            mc.RegisterKill(MatchSide.Enemy, MatchSide.Player);
            int livesLeft = mc.DecrementPlayerLives();

            yield return null;

            Assert.AreEqual(2, livesLeft,
                "DecrementPlayerLives must return 2 after the first death with PlayerLives=3. " +
                "If it returns wrong, ArenaController may call NotifyPlayerLivesExhausted early " +
                "and end the match incorrectly.");
            Assert.AreEqual(2, mc.PlayerLivesRemaining,
                "PlayerLivesRemaining must match the DecrementPlayerLives return value. " +
                "Both paths are used by different callers (ArenaController checks the return; " +
                "HUD reads the property).");
            Assert.AreEqual(0, mc.ScoreForSide(MatchSide.Enemy),
                "Enemy scrap score must NOT change when the player dies with lives remaining. " +
                "Kills are informational only — scrap deposits drive the score. " +
                "A regression here would show phantom enemy scrap in the ObjectiveHud.");
            Assert.AreEqual(MatchState.InProgress, mc.State,
                "Match must remain InProgress while the player still has 2 lives left. " +
                "RoundEnded here would skip the respawn coroutine and lock the arena.");
        }

        /// <summary>
        /// Bot spawn: requires GameStateController.Instance, ChassisFactory.Build,
        /// a library of BlockDefinitions, and a ChassisBlueprint asset — none of
        /// which can be constructed in isolation in a PlayMode test without the
        /// full Bootstrap scene.
        ///
        /// WHY this is ignored: the bot spawn path is exercised end-to-end in
        /// manual play-testing and will be covered by a scene-load integration
        /// test once the netcode session wires ArenaController to a
        /// NetworkManager. Unblocking it here would require either:
        ///   (a) a fake GameStateController + fake Library — high maintenance,
        ///   (b) a scene load via SceneManager — fragile in CI (no Bootstrap scene
        ///       asset in the test runner context).
        /// The plain-C# MatchController tests cover the state-machine half;
        /// this test is the one gap that genuinely waits on the scene stack.
        /// </summary>
        [UnityTest]
        [Ignore("SpawnBot requires GameStateController.Instance + ChassisFactory + BlockDefinition library. " +
                "Cannot construct in isolation without the Bootstrap scene. " +
                "Re-enable when a test-scene asset (Assets/_Project/Tests/Scenes/MinimalArena.unity) exists " +
                "and loads a trimmed GameStateController with a minimal one-block library.")]
        public IEnumerator SpawnBot_ResultingGameObject_HasResolvedIInputSource()
        {
            yield return null;
            Assert.Fail("This test body should never run while [Ignore] is active.");
        }

        /// <summary>
        /// RoundEnded is a terminal state: additional Tick calls after MatchEnded
        /// fires must not change State or re-fire MatchEnded.
        ///
        /// WHY: the netcode wrapper will call MatchController.Tick every server
        /// frame. If RoundEnded is not truly terminal, a late tick can re-fire
        /// MatchEnded, sending a duplicate end-of-round RPC to all clients. That
        /// produces double overlays and corrupts the session score log.
        /// </summary>
        [UnityTest]
        public IEnumerator MatchController_AfterMatchEnded_StateRemainsRoundEnded()
        {
            // Use a very short round so the timer-expiry path ends the match, not
            // a deposit. That exercises the Tick→EndMatch path rather than the
            // DepositScrap path already covered above.
            MatchController mc = MakeStartedController(targetTeamScrap: 99, roundDuration: 0.001f);

            int endCount = 0;
            mc.MatchEnded += _ => endCount++;

            // Tick well past the round timer to ensure EndMatch fires.
            mc.Tick(1f);

            yield return null;

            Assert.AreEqual(MatchState.RoundEnded, mc.State,
                "State must be RoundEnded once the round timer expires. " +
                "If this fails, the timer-expiry path in Tick is not calling EndMatch.");
            Assert.AreEqual(1, endCount,
                "MatchEnded must have fired exactly once. " +
                "Firing zero times means the timer check is broken; " +
                "firing more than once would send duplicate RPCs.");

            // Spam additional ticks — state and fire count must not change.
            mc.Tick(100f);
            mc.Tick(100f);

            yield return null;

            Assert.AreEqual(MatchState.RoundEnded, mc.State,
                "State must remain RoundEnded across additional Tick calls. " +
                "RoundEnded is a terminal state — re-entry is only via a new " +
                "MatchController instance (a new round).");
            Assert.AreEqual(1, endCount,
                "MatchEnded must still have fired exactly once after extra ticks. " +
                "A second fire would mean the idempotency guard in EndMatch is broken.");
        }

        /// <summary>
        /// MatchEndOverlay.IsVisible must become true when MatchController raises
        /// MatchEnded, and must remain false before that event fires.
        ///
        /// WHY: MatchEndOverlay.IsVisible gates the OnGUI draw path. If it stays
        /// false after MatchEnded, the player sees no end-of-round overlay and
        /// cannot click "Return to Garage" — the session is soft-locked. If it
        /// becomes true before MatchEnded, the overlay stacks over the gameplay
        /// HUD during a live round.
        ///
        /// MatchEndOverlay.IsVisible is defined as:
        ///   _hasArgs && _match != null && _match.State == MatchState.RoundEnded
        /// which means it reads live MatchController state — this test also
        /// verifies that BindMatch wires the subscription correctly.
        /// </summary>
        [UnityTest]
        public IEnumerator MatchEndOverlay_BecomesVisible_WhenMatchControllerEnds()
        {
            MatchController mc = MakeStartedController(targetTeamScrap: 1);

            // MatchEndOverlay is a MonoBehaviour — must live on a GameObject.
            GameObject overlayGo = MakeGo("MatchEndOverlay");
            MatchEndOverlay overlay = overlayGo.AddComponent<MatchEndOverlay>();
            overlay.BindMatch(mc);

            // Let Awake run (it runs synchronously via AddComponent, but yield
            // one frame to match real scene behaviour and flush any deferred paths).
            yield return null;

            // Pre-condition: overlay must be invisible before the match ends.
            Assert.IsFalse(overlay.IsVisible,
                "MatchEndOverlay must NOT be visible before MatchEnded fires. " +
                "A visible overlay during a live round obscures the gameplay HUD.");

            // End the match by hitting the scrap limit.
            mc.DepositScrap(MatchSide.Player, 1);

            yield return null;

            Assert.IsTrue(overlay.IsVisible,
                "MatchEndOverlay must become visible once MatchController raises MatchEnded. " +
                "IsVisible := _hasArgs && match.State == RoundEnded. " +
                "If this fails, BindMatch did not subscribe to MatchEnded, or " +
                "_hasArgs is not set in HandleMatchEnded.");
        }

        /// <summary>
        /// ObjectiveHud.DisplayedPlayerScore must reflect the current player scrap
        /// total from MatchController.ScoreForSide(Player).
        ///
        /// WHY: ObjectiveHud.DisplayedPlayerScore is defined as
        ///   _match != null ? _match.ScoreForSide(MatchSide.Player) : 0
        /// which means it reads directly from the MatchController rather than
        /// maintaining a separate counter. If BindMatch is broken (null _match),
        /// DisplayedPlayerScore will always return 0 regardless of deposits —
        /// the scoreboard shows 0/20 forever and the player has no round-state
        /// feedback. This test pins the binding contract.
        ///
        /// Note: the stub tested a non-existent 'DisplayedPlayerKills' property
        /// tied to a frag-count win condition. The real property is
        /// 'DisplayedPlayerScore' (team scrap). Test updated to match reality.
        /// </summary>
        [UnityTest]
        public IEnumerator ObjectiveHud_KillCount_ReflectsMatchControllerScore()
        {
            MatchController mc = MakeStartedController(targetTeamScrap: 99);

            // ObjectiveHud is a MonoBehaviour that needs a camera component chain
            // in production (FollowCamera for chassis binding). For this unit test
            // we only care about the score binding, which is independent of the
            // camera / chassis chain.
            GameObject hudGo = MakeGo("ObjectiveHud");
            ObjectiveHud hud = hudGo.AddComponent<ObjectiveHud>();
            hud.BindMatch(mc);

            // Pre-condition: no deposit yet → both scores must read 0.
            Assert.AreEqual(0, hud.DisplayedPlayerScore,
                "DisplayedPlayerScore must be 0 before any deposit. " +
                "If non-zero, BindMatch is wiring to the wrong controller instance.");
            Assert.AreEqual(0, hud.DisplayedEnemyScore,
                "DisplayedEnemyScore must be 0 before any deposit.");

            mc.DepositScrap(MatchSide.Player, 3);

            // DisplayedPlayerScore reads _match.ScoreForSide synchronously —
            // no frame wait needed for the value itself. Yield once to match
            // [UnityTest] contract and catch any deferred-update bugs.
            yield return null;

            Assert.AreEqual(3, hud.DisplayedPlayerScore,
                "DisplayedPlayerScore must return 3 after DepositScrap(Player, 3). " +
                "If 0, BindMatch did not assign _match (subscription was skipped or " +
                "the DisplayedPlayerScore property reads from a stale reference).");
            Assert.AreEqual(0, hud.DisplayedEnemyScore,
                "DisplayedEnemyScore must remain 0 — only the player deposited. " +
                "If non-zero, ScoreForSide is returning the wrong side's total.");
        }
    }
}
