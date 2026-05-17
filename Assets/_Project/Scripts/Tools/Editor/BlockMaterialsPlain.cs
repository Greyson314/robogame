using System.Collections.Generic;
using System.IO;
using Robogame.Player;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Builds the plain (no-outline) counterpart of every outlined block
    /// material and writes the runtime <see cref="OutlineMaterialRegistry"/>
    /// that maps outline → plain. Run as part of the block-material
    /// scaffold (right after <see cref="BlockMaterials.BuildAll"/>).
    /// </summary>
    /// <remarks>
    /// The plain variant is a property copy of the authored outline
    /// material with its shader switched to the base
    /// <c>MK/Toon/URP/Standard/Physically Based</c> (no <c>+ Outline</c>).
    /// Because the base shader has no MKToonOutline pass at all, the
    /// outline draw is gone regardless of keyword state — that's the
    /// whole point: a non-relevant chassis renders these and skips the
    /// per-renderer outline pass. Idempotent.
    /// </remarks>
    public static class BlockMaterialsPlain
    {
        private const string ToonShaderName = "MK/Toon/URP/Standard/Physically Based";
        private const string LitShaderName  = "Universal Render Pipeline/Lit";
        private const string ResourcesFolder = "Assets/_Project/Resources";
        private const string RegistryPath    = ResourcesFolder + "/OutlineMaterialRegistry.asset";

        // The outline:true categories in BlockMaterials.BuildAll.
        private static readonly string[] OutlineAssetNames =
        {
            "BlockMat_Structure",
            "BlockMat_Cpu",
            "BlockMat_Thruster",
            "BlockMat_Weapon",
            "BlockMat_BombBay",
        };

        public static void BuildAll()
        {
            Shader baseShader = Shader.Find(ToonShaderName)
                                ?? Shader.Find(LitShaderName)
                                ?? Shader.Find("Standard");

            var pairs = new List<OutlineMaterialRegistry.Pair>(OutlineAssetNames.Length);

            foreach (string name in OutlineAssetNames)
            {
                string srcPath = $"{BlockMaterials.Folder}/{name}.mat";
                Material outline = AssetDatabase.LoadAssetAtPath<Material>(srcPath);
                if (outline == null)
                {
                    Debug.LogWarning($"[Robogame] BlockMaterialsPlain: source material not found at {srcPath} — run BlockMaterials.BuildAll first.");
                    continue;
                }

                string plainPath = $"{BlockMaterials.Folder}/{name}_Plain.mat";
                Material plain = AssetDatabase.LoadAssetAtPath<Material>(plainPath);
                if (plain == null)
                {
                    plain = new Material(outline) { name = name + "_Plain" };
                    AssetDatabase.CreateAsset(plain, plainPath);
                }
                else
                {
                    // Idempotent refresh: re-copy from the (possibly
                    // re-tinted) outline source.
                    plain.CopyPropertiesFromMaterial(outline);
                }

                if (baseShader != null && plain.shader != baseShader)
                    plain.shader = baseShader;
                if (plain.HasProperty("_Outline"))
                    plain.SetFloat("_Outline", 0f);

                EditorUtility.SetDirty(plain);
                pairs.Add(new OutlineMaterialRegistry.Pair { Outline = outline, Plain = plain });
            }

            EnsureFolder(ResourcesFolder);
            OutlineMaterialRegistry reg = AssetDatabase.LoadAssetAtPath<OutlineMaterialRegistry>(RegistryPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<OutlineMaterialRegistry>();
                AssetDatabase.CreateAsset(reg, RegistryPath);
            }
            reg.EditorSetPairs(pairs);
            EditorUtility.SetDirty(reg);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] BlockMaterialsPlain: built {pairs.Count} plain material(s) + registry.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
