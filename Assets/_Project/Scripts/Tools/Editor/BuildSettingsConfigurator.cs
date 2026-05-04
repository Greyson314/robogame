using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Keeps <c>EditorBuildSettings.scenes</c> in sync with the canonical
    /// scene order defined in code, so we never have to use the Build Profiles
    /// dialog manually.
    /// </summary>
    public static class BuildSettingsConfigurator
    {
        private static readonly string[] CanonicalSceneOrder = new[]
        {
            ScaffoldUtils.BootstrapScene,
            ScaffoldUtils.GarageScene,
            ScaffoldUtils.ArenaScene,
            ScaffoldUtils.WaterArenaScene,
            ScaffoldUtils.PlanetArenaScene,
        };

        public static void SyncSceneList()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (string path in CanonicalSceneOrder)
            {
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogWarning($"[Robogame] Scene missing, skipping: {path}");
                    continue;
                }
                scenes.Add(new EditorBuildSettingsScene(path, enabled: true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Robogame] Build scene list synced ({scenes.Count} scenes).");
        }
    }
}
