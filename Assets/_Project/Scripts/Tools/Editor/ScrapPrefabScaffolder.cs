using System.IO;
using Robogame.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// One-shot editor utility that builds the runtime <c>ScrapPickup</c>
    /// prefab from the Kenney <c>coin-bronze</c> FBX and stores it under
    /// <c>Assets/_Project/Resources/Prefabs/</c> so
    /// <see cref="ScrapPickup.Spawn"/> can <see cref="Resources.Load"/>
    /// it at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime spawner falls back to a procedural cube if this
    /// asset is missing, so the gameplay loop works without running
    /// this scaffolder. Run it (Robogame &gt; Scaffold &gt; Build Scrap
    /// Prefab) once after pulling the session-35 changes to upgrade the
    /// scrap visual to the proper coin model.
    /// </para>
    /// <para>
    /// Idempotent — re-running overwrites the existing prefab in place.
    /// </para>
    /// </remarks>
    public static class ScrapPrefabScaffolder
    {
        public const string PrefabFolder = "Assets/_Project/Resources/Prefabs";
        public const string PrefabPath   = PrefabFolder + "/ScrapPickup.prefab";

        // Coin-bronze is the platformer-kit FBX we're wrapping. Falls back
        // to silver / gold variants if bronze isn't present (e.g. partial
        // kit install) so the scaffolder doesn't no-op silently.
        private static readonly string[] s_coinFallbackOrder = { "coin-bronze", "coin-silver", "coin-gold" };

        [MenuItem("Robogame/Scaffold/Build Scrap Prefab")]
        public static void Build()
        {
            // Make sure Kenney FBX import settings are correct (cm → m
            // scale, single shared colormap). Cheap idempotent call.
            KenneyKit.EnsureImportSettings();

            GameObject coinFbx = ResolveCoinFbx();
            if (coinFbx == null)
            {
                Debug.LogError(
                    "[Robogame] ScrapPrefabScaffolder: no coin FBX found in the Kenney " +
                    "platformer kit at " + KenneyKit.PlatformerRoot + ". Check the kit's " +
                    "extraction state.");
                return;
            }

            EnsureFolder(PrefabFolder);

            // Build the runtime hierarchy in a hidden temporary scene
            // root, save it as a prefab, then destroy the temp.
            GameObject root = new GameObject("ScrapPickup");
            try
            {
                // Visual: instantiate the coin FBX as a child so the prefab
                // owns a copy of the model rather than a reference. Bumped
                // scale so the coin reads from a few metres away — Kenney's
                // authored size is ~0.4 m, which disappears in the arena.
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(coinFbx);
                if (visual == null)
                {
                    Debug.LogError("[Robogame] ScrapPrefabScaffolder: failed to instantiate coin FBX.");
                    Object.DestroyImmediate(root);
                    return;
                }
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, worldPositionStays: false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // coin lays flat by default
                visual.transform.localScale = Vector3.one * 1.5f;

                // Strip any colliders embedded in the FBX import so they
                // don't fight the trigger volume below.
                Collider[] visualColliders = visual.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int i = 0; i < visualColliders.Length; i++)
                {
                    if (visualColliders[i] != null) Object.DestroyImmediate(visualColliders[i]);
                }

                // Trigger collider on the root — sized for forgiving pickup.
                SphereCollider trig = root.AddComponent<SphereCollider>();
                trig.isTrigger = true;
                trig.radius = 1.6f;
                trig.center = Vector3.zero;

                // Pickup behaviour. Default-config'd; runtime ScrapPickup.Spawn
                // calls Configure(value) after Instantiate to set per-drop value.
                root.AddComponent<ScrapPickup>();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Robogame] Scrap prefab saved to {PrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject ResolveCoinFbx()
        {
            for (int i = 0; i < s_coinFallbackOrder.Length; i++)
            {
                GameObject candidate = KenneyKit.Platformer(s_coinFallbackOrder[i]);
                if (candidate != null) return candidate;
            }
            return null;
        }

        // AssetDatabase.CreateFolder needs a parent that exists. Walk down
        // the path creating intermediate folders idempotently.
        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf   = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
