using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor-only safety net that re-bakes <c>Mesh_ArenaHills.asset</c> on
    /// domain reload if it doesn't match the current
    /// <see cref="HillsSettings"/>. Saves the user from having to click the
    /// "Rebake hills mesh" button (or re-run Build Arena Pass A) every time
    /// the settings asset is edited externally.
    /// </summary>
    /// <remarks>
    /// Stale-mesh detection is a vertex-count comparison: if the asset's
    /// vertex count doesn't equal <c>resolution²</c>, we know the bake is
    /// out of date with the settings and rebuild. Cheap, deterministic, and
    /// covers the only failure mode that's actually bitten — settings YAML
    /// edits done outside Unity (e.g. by tools) that don't fire OnValidate.
    /// </remarks>
    [InitializeOnLoad]
    internal static class HillsAutoRebake
    {
        static HillsAutoRebake()
        {
            // Defer until the asset database is ready — InitializeOnLoad
            // fires before AssetDatabase calls are safe.
            EditorApplication.delayCall += CheckOnce;
        }

        private static void CheckOnce()
        {
            HillsSettings settings = AssetDatabase.LoadAssetAtPath<HillsSettings>(HillsGround.SettingsPath);
            if (settings == null) return;

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HillsGround.MeshPath);
            int expected = settings.resolution * settings.resolution;
            if (mesh != null && mesh.vertexCount == expected) return;

            HillsGround.RebakeMesh();
        }
    }
}
