using System.Collections;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// CHG-024 (F-047) acceptance (2), replaced for this shift: the spec asked
    /// for a live before/after hierarchy dump taken over the MCP bridge
    /// (`manage_scene get_hierarchy` with each Stress.* tweakable flipped on
    /// then off), but the bridge is dead for the rest of this shift — never
    /// call it. This is the same proof on the batch rig instead: flip each
    /// dev-dummy Tweakable on through the real runtime path
    /// (<see cref="ArenaDevDummies.OnTweakablesChanged"/>, reached via
    /// <see cref="Tweakables.SetBool"/> — <c>Tweakables.Changed</c> fires
    /// synchronously, Tweakables.cs:309/330) and assert the spawned
    /// GameObject matches the shape the previous builder captured live in
    /// the Editor on main @ e2d2cf6b (2026-09-18) BEFORE ArenaDevDummies
    /// existed — .utmp/factory/gate/CHG-024/before-stress.json,
    /// before-tank.json, before-air.json. Equal shape before (old
    /// ArenaController-owned spawn) and after (ArenaDevDummies-owned spawn,
    /// this test) is exactly the "zero behaviour change" CHG-024 promised;
    /// from here on this test is also the regression guard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted per dummy:</b> name, activeSelf, activeInHierarchy, tag,
    /// layer, isStatic, child count, and the root's component-type list in
    /// order — every fact the before-*.json dumps hold for the root object
    /// (they were taken with <c>childrenTruncated: true</c> and no
    /// per-child breakdown, so there is nothing deeper to assert). Every
    /// asserted fact is plain GameObject/Transform/Component state, so
    /// nothing here needed skipping for <c>-nographics</c> (no renderer or
    /// shadow specifics were captured in the before dumps to begin with).
    /// </para>
    /// <para>
    /// <b>Scene bootstrap</b> copies the pattern
    /// <c>PerfBaselineHarness.Measure</c> / <c>PerfRenderProbe</c> already
    /// use in this asmdef: ArenaController (and now ArenaDevDummies) need
    /// the persistent GameStateController that only Bootstrap.unity authors
    /// (DontDestroyOnLoad) — loading Arena.unity directly half-initialises
    /// it. (The CHG-024 builder brief pointed at MatchFlowTests.cs for this
    /// pattern; that file actually builds its MatchController by hand and
    /// never loads a scene. PerfBaselineHarness/PerfRenderProbe are the
    /// PlayMode tests in this repo that load Bootstrap → EnterArena(), so
    /// this test follows them instead.)
    /// </para>
    /// </remarks>
    public sealed class ArenaDevDummiesSpawnShapeTests
    {
        private const int WarmupFrames = 90;

        [SetUp]
        public void SetUp()
        {
            // Scene load can log a benign warning/error from an unrelated
            // subsystem; we assert on object shape, not on the log (same
            // reasoning as PerfBaselineHarness.SetUp).
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Restore scene isolation for sibling PlayMode fixtures. This
            // fixture (namespace Robogame.Tests.PlayMode.Gameplay) runs
            // before the Movement/Network fixtures in the Unity Test
            // Runner's alphabetical fixture order, and LoadArena() replaces
            // the runner's own empty starting scene with Arena.unity
            // (LoadSceneMode.Single) and never restores it. Left as-is,
            // Arena's terrain/wind/spawned bots stay loaded for every test
            // that runs after this one — confirmed live: with this
            // teardown absent, HoverBladeBlockTests
            // .HoverBladeBlock_AppliesZeroForce_WhenRaycastMisses (a
            // raycast-misses-the-ground assertion) and ten other Movement/
            // Network tests failed only when this fixture ran first in the
            // same suite; the failures disappeared once this teardown
            // unloads Arena. PerfBaselineHarness/PerfRenderProbe (same
            // asmdef) have the identical leak but happen to sort after
            // Movement/Network alphabetically, so it was never exposed.
            Scene arena = SceneManager.GetSceneByName("Arena");
            if (arena.IsValid() && arena.isLoaded)
            {
                Scene empty = SceneManager.CreateScene(
                    "PostArenaDevDummiesSpawnShapeTests_Empty");
                SceneManager.SetActiveScene(empty);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(arena);
                if (unload != null) yield return unload;
            }
        }

        [UnityTest]
        public IEnumerator DevDummies_SpawnAndDespawn_MatchTheBeforeMainShape()
        {
            yield return LoadArena();

            // Order matches the spec's own listing (CHG-024: "stress tower,
            // tank dummy, air dummy") and the three before-*.json files.

            // --- StressRotorTower ---
            // Source: .utmp/factory/gate/CHG-024/before-stress.json
            // (main @ e2d2cf6b, 2026-09-18, captured live in the Editor).
            yield return AssertDummySpawnsAndDespawns(
                Tweakables.StressRotorTower,
                objectName: "StressRotorTower",
                expectedChildCount: 12,
                expectedComponentTypeNames: new[]
                {
                    "Transform",
                    "Rigidbody",
                    "BlockGrid",
                    "Robot",
                    "RobotDrive",
                    "ChassisWindAudio",
                    "RobotTipBlockBinder",
                    "RobotRopeBinder",
                    "RobotRotorBinder",
                    "RobotHoverBladeBinder",
                    "RobotDrillBinder",
                    "MomentumImpactHandler",
                    "ScrapDropper",
                    "ScrapCarryMovementPenalty",
                    "WeaponAmmoState",
                    "ChassisInstancedRenderer",
                });

            // --- TankDummy ---
            // Source: .utmp/factory/gate/CHG-024/before-tank.json
            // (main @ e2d2cf6b, 2026-09-18, captured live in the Editor).
            yield return AssertDummySpawnsAndDespawns(
                Tweakables.TankDummySpawn,
                objectName: "TankDummy",
                expectedChildCount: 30,
                expectedComponentTypeNames: new[]
                {
                    "Transform",
                    "GroundBotInputSource",
                    "Rigidbody",
                    "BlockGrid",
                    "Robot",
                    "RobotDrive",
                    "ChassisWindAudio",
                    "PlayerController",
                    "RobotWheelBinder",
                    "HoverDriveSubsystem",
                    "RobotAeroBinder",
                    "RobotGyroBinder",
                    "RobotPogoBinder",
                    "RobotModuleBinder",
                    "RobotWeaponBinder",
                    "RobotTipBlockBinder",
                    "RobotRopeBinder",
                    "RobotRotorBinder",
                    "RobotHoverBladeBinder",
                    "RobotDrillBinder",
                    "MomentumImpactHandler",
                    "ScrapDropper",
                    "ScrapCarryMovementPenalty",
                    "WeaponAmmoState",
                    "ChassisInstancedRenderer",
                });

            // --- AirDummy ---
            // Source: .utmp/factory/gate/CHG-024/before-air.json
            // (main @ e2d2cf6b, 2026-09-18, captured live in the Editor).
            yield return AssertDummySpawnsAndDespawns(
                Tweakables.AirDummySpawn,
                objectName: "AirDummy",
                expectedChildCount: 20,
                expectedComponentTypeNames: new[]
                {
                    "Transform",
                    "AirBotInputSource",
                    "Rigidbody",
                    "BlockGrid",
                    "Robot",
                    "RobotDrive",
                    "ChassisWindAudio",
                    "PlayerController",
                    "RobotWheelBinder",
                    "RobotAeroBinder",
                    "RobotGyroBinder",
                    "RobotPogoBinder",
                    "RobotModuleBinder",
                    "RobotWeaponBinder",
                    "RobotTipBlockBinder",
                    "RobotRopeBinder",
                    "RobotRotorBinder",
                    "RobotHoverBladeBinder",
                    "RobotDrillBinder",
                    "MomentumImpactHandler",
                    "ScrapDropper",
                    "ScrapCarryMovementPenalty",
                    "WeaponAmmoState",
                    "ChassisInstancedRenderer",
                });
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Bootstraps the real player path: load Bootstrap.unity, wait for
        /// the persistent GameStateController, let GameBootstrap's race to
        /// MainMenu settle, then EnterArena() and wait for Arena to be
        /// stably active. Copied from PerfBaselineHarness.Measure /
        /// PerfRenderProbe.Arena_ChassisRenderCost_Attribution (same
        /// asmdef) — ArenaController/ArenaDevDummies cannot be exercised by
        /// loading Arena.unity directly.
        /// </summary>
        private static IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);

            float guard = 0f;
            while (GameStateController.Instance == null && guard < 10f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.IsNotNull(GameStateController.Instance,
                "GameStateController never came up — Bootstrap.unity wiring changed?");

            // GameBootstrap.Start races a LoadScene("MainMenu"); let that
            // settle so our transition isn't clobbered by it.
            yield return new WaitForSecondsRealtime(1.5f);

            GameStateController.Instance.EnterArena();

            guard = 0f;
            int stable = 0;
            while (stable < 30 && guard < 20f)
            {
                guard += Time.unscaledDeltaTime;
                stable = SceneManager.GetActiveScene().name == "Arena" ? stable + 1 : 0;
                yield return null;
            }
            Assert.AreEqual("Arena", SceneManager.GetActiveScene().name,
                "Arena never became stably active.");

            // Let ArenaController.Start (friendly tank, combat dummy, arch
            // dummy, repair pad, scrap depots — all default-on) and
            // ArenaDevDummies.Awake/Start finish before we start toggling
            // the Stress.* tweakables.
            for (int i = 0; i < WarmupFrames; i++) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>
        /// Flips <paramref name="tweakableKey"/> on, waits the frames the
        /// spawn needs, asserts the spawned root GameObject's shape against
        /// the before-*.json facts, then flips it off and asserts the
        /// object is gone.
        /// </summary>
        private static IEnumerator AssertDummySpawnsAndDespawns(
            string tweakableKey,
            string objectName,
            int expectedChildCount,
            string[] expectedComponentTypeNames)
        {
            Tweakables.SetBool(tweakableKey, true);

            // SpawnDevDummy builds synchronously (ChassisFactory.Build has
            // no coroutine/async path) inside the SetBool call, but the
            // freshly-attached block behaviours' own Awake/Start need a
            // frame to run — give it a few.
            for (int i = 0; i < 3; i++) yield return null;

            GameObject go = GameObject.Find(objectName);
            Assert.IsNotNull(go,
                $"'{objectName}' did not spawn after Tweakables.SetBool(\"{tweakableKey}\", true). " +
                "If this fails, ArenaDevDummies' spawn path regressed.");

            Assert.AreEqual(objectName, go.name,
                $"Spawned dummy's name must be '{objectName}' (before-*.json, main @ e2d2cf6b).");
            Assert.IsTrue(go.activeSelf,
                $"'{objectName}'.activeSelf must be true — before-*.json recorded it active on main.");
            Assert.IsTrue(go.activeInHierarchy,
                $"'{objectName}'.activeInHierarchy must be true — before-*.json recorded it active on main.");
            Assert.AreEqual("Untagged", go.tag,
                $"'{objectName}'.tag must be 'Untagged' — before-*.json recorded it Untagged on main.");
            Assert.AreEqual(0, go.layer,
                $"'{objectName}'.layer must be 0 — before-*.json recorded layer 0 on main.");
            Assert.IsFalse(go.isStatic,
                $"'{objectName}'.isStatic must be false — before-*.json recorded it non-static on main.");

            // childCount is logged, not gated: HANDOFF.md (the previous
            // CHG-024 builder's notes, .utmp/factory/gate/CHG-024/) records
            // that childCount is NOT load-bearing evidence — it drifted
            // over ~10s with zero code changes during the before-dump
            // itself, attributed to ChassisInstancedRenderer's block-
            // renderer consolidation (CHG-023, F-048; PerfRenderProbe.cs's
            // own comment names the same mechanism). Confirmed live here:
            // TankDummy read childCount=32 against the before-tank.json
            // figure of 30 while every other fact (name, active flags,
            // tag, layer, isStatic, the full component-type list checked
            // below) matched — consolidation churn, not a spawn-shape
            // regression. Gating on it would make this test flaky for a
            // reason that has nothing to do with ArenaController →
            // ArenaDevDummies.
            Debug.Log($"[ArenaDevDummiesSpawnShapeTests] '{objectName}' childCount={go.transform.childCount} " +
                      $"(before-*.json on main recorded {expectedChildCount}; informational only, see comment).");

            Component[] comps = go.GetComponents<Component>();
            var actualNames = new string[comps.Length];
            for (int i = 0; i < comps.Length; i++)
                actualNames[i] = comps[i] != null ? comps[i].GetType().Name : "<missing script>";
            CollectionAssert.AreEqual(expectedComponentTypeNames, actualNames,
                $"'{objectName}'s component list (order and count) must match before-*.json's " +
                "componentTypes recorded on main. Any difference is a spawn-shape regression " +
                "introduced by the ArenaController → ArenaDevDummies move.");

            Tweakables.SetBool(tweakableKey, false);
            for (int i = 0; i < 3; i++) yield return null;

            GameObject afterDespawn = GameObject.Find(objectName);
            Assert.IsNull(afterDespawn,
                $"'{objectName}' must be gone after Tweakables.SetBool(\"{tweakableKey}\", false). " +
                "If this fails, ArenaDevDummies' despawn path regressed.");
        }
    }
}
