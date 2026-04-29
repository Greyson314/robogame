using System.Collections.Generic;
using System.Linq;
using Robogame.Block;
using Robogame.Robots;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Play-mode utilities for poking at robots without needing real weapons.
    /// </summary>
    public static class DamageTestTools
    {
        // Standard ring-falloff: direct hit, 50% to neighbours, 20% two steps away.
        private static readonly float[] s_defaultSplash = { 200f, 100f, 40f };

        [MenuItem("Robogame/Test/Damage Random Block %#d", priority = 200)]
        public static void DamageRandomBlock()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Robogame] Damage test only works in Play mode.");
                return;
            }

            Robot robot = FindActiveRobot();
            if (robot == null)
            {
                Debug.LogWarning("[Robogame] No Robot found in the open scene(s).");
                return;
            }

            BlockGrid grid = robot.Grid;
            if (grid == null || grid.Count == 0)
            {
                Debug.LogWarning("[Robogame] Robot has no blocks to damage.", robot);
                return;
            }

            List<Vector3Int> alive = grid.Blocks
                .Where(kvp => kvp.Value != null && kvp.Value.IsAlive)
                .Select(kvp => kvp.Key)
                .ToList();

            if (alive.Count == 0)
            {
                Debug.LogWarning("[Robogame] No living blocks left.", robot);
                return;
            }

            Vector3Int target = alive[Random.Range(0, alive.Count)];
            grid.ApplySplashDamage(target, s_defaultSplash);
            Debug.Log($"[Robogame] Splash {s_defaultSplash[0]}/{s_defaultSplash[1]}/{s_defaultSplash[2]} at {target}.", robot);
        }

        [MenuItem("Robogame/Test/Destroy CPU Block", priority = 201)]
        public static void DestroyCpuBlock()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Robogame] Damage test only works in Play mode.");
                return;
            }

            Robot robot = FindActiveRobot();
            if (robot == null || robot.CpuBlock == null)
            {
                Debug.LogWarning("[Robogame] No active Robot/CPU found.");
                return;
            }
            robot.CpuBlock.TakeDamage(float.MaxValue);
        }

        [MenuItem("Robogame/Test/Rebuild Test Robot %#r", priority = 220)]
        public static void RebuildTestRobot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Robogame] Rebuild only works in Play mode. Use Robogame/Scaffold/Build Test Robot in edit mode.");
                return;
            }

            DestroyByName("Robot");
            GameObject go = new GameObject("Robot");
            SceneScaffolder.PopulateTestRobot(go);
            Debug.Log("[Robogame] Rebuilt Test Robot in play mode.");
        }

        [MenuItem("Robogame/Test/Rebuild Combat Dummy", priority = 221)]
        public static void RebuildCombatDummy()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Robogame] Rebuild only works in Play mode.");
                return;
            }

            DestroyByName("CombatDummy");
            GameObject go = new GameObject("CombatDummy");
            SceneScaffolder.PopulateCombatDummy(go);
            Debug.Log("[Robogame] Rebuilt Combat Dummy in play mode.");
        }

        private static void DestroyByName(string n)
        {
            GameObject existing = GameObject.Find(n);
            if (existing != null) Object.Destroy(existing);
        }

        private static Robot FindActiveRobot()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<Robot>();
#else
            return Object.FindObjectOfType<Robot>();
#endif
        }
    }
}
