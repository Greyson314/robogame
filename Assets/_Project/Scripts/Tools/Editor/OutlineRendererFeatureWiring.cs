// OutlineRendererFeatureWiring.cs
// Idempotently registers MK Toon's `MKToonPerObjectOutlines`
// ScriptableRendererFeature on the URP renderer assets. MK Toon's outline
// pass uses a custom URP LightMode tag ("MKToonOutline") which only
// renders if that feature is in the renderer's m_RendererFeatures list.
// Without it the outline material data is dead weight.
//
// Implemented via reflection so this Editor asmdef does NOT need to take
// a hard reference on MK.Toon.URP. If the MK Toon URP package is absent
// (or its MK_URP define is off because URP is missing) we just log and
// skip — the rest of Pass A still runs.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Robogame.Tools.Editor
{
    internal static class OutlineRendererFeatureWiring
    {
        private const string OutlineFeatureTypeName = "MK.Toon.URP.MKToonPerObjectOutlines";

        // Renderer assets we care about. PC is the active one for desktop play;
        // Mobile is included so the scaffolder stays consistent across targets.
        private static readonly string[] RendererAssetPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        public static void EnsureOutlineFeatureOnRenderers()
        {
            Type featureType = ResolveOutlineFeatureType();
            if (featureType == null)
            {
                Debug.LogWarning(
                    "[Robogame] OutlineRendererFeatureWiring: type " +
                    OutlineFeatureTypeName +
                    " not found. MK Toon URP package missing or MK_URP define not set. " +
                    "Skipping outline feature registration.");
                return;
            }

            foreach (string path in RendererAssetPaths)
            {
                EnsureOnRenderer(path, featureType);
            }
        }

        private static Type ResolveOutlineFeatureType()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(OutlineFeatureTypeName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureOnRenderer(string assetPath, Type featureType)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(assetPath);
            if (rendererData == null)
            {
                Debug.LogWarning($"[Robogame] OutlineRendererFeatureWiring: renderer asset not found at {assetPath}.");
                return;
            }

            // Already present? Bail.
            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing != null && featureType.IsInstanceOfType(existing))
                {
                    return;
                }
            }

            // Create the feature instance, parent it to the renderer asset,
            // and append to the m_RendererFeatures + m_RendererFeatureMap
            // serialized lists. URP's RendererData uses both: the list of
            // sub-asset references AND a parallel list of stable GUIDs used
            // by the inspector for reordering.
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            feature.name = featureType.Name;
            feature.hideFlags = HideFlags.HideInHierarchy;

            AssetDatabase.AddObjectToAsset(feature, rendererData);

            var so = new SerializedObject(rendererData);
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var mapProp      = so.FindProperty("m_RendererFeatureMap");

            int idx = featuresProp.arraySize;
            featuresProp.InsertArrayElementAtIndex(idx);
            featuresProp.GetArrayElementAtIndex(idx).objectReferenceValue = feature;

            if (mapProp != null)
            {
                mapProp.InsertArrayElementAtIndex(mapProp.arraySize);
                // 8-byte stable id; URP itself just needs uniqueness inside the asset.
                mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue =
                    Guid.NewGuid().GetHashCode() ^ ((long)Guid.NewGuid().GetHashCode() << 32);
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(rendererData);

            Debug.Log($"[Robogame] OutlineRendererFeatureWiring: added {featureType.Name} to {assetPath}.");
        }
    }
}
