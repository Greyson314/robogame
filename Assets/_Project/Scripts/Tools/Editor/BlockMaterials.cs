using System.IO;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Per-category block materials. One asset per <see cref="BlockCategory"/>
    /// (and a couple of special-case sub-materials), all backed by MK Toon
    /// shaders so blocks pick up cel shading automatically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Hero" categories (CPU, Weapon, Movement-thrusters) use MK Toon's
    /// <c>+ Outline</c> variant so they get a black ink line that pops at
    /// distance. The bulk-volume Structure cubes deliberately do <em>not</em>
    /// outline — they're the canvas, not the focal point.
    /// </para>
    /// <para>
    /// Materials live at <see cref="Folder"/> as project assets and are
    /// referenced from each <see cref="BlockDefinition"/> via the
    /// <c>_material</c> serialised field. <see cref="BlockGrid.PlaceBlock"/>
    /// applies them via <c>sharedMaterial</c> — no <c>Renderer.material</c>
    /// instantiation, so we don't churn per-block material copies.
    /// </para>
    /// </remarks>
    public static class BlockMaterials
    {
        public const string Folder = "Assets/_Project/Materials/Blocks";

        // MK Toon shader names — both variants live in the same package.
        private const string ToonShaderName        = "MK/Toon/URP/Standard/Physically Based";
        private const string ToonOutlineShaderName = "MK/Toon/URP/Standard/Physically Based + Outline";
        private const string LitShaderName         = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Build (or refresh) every category material. Idempotent — safe to
        /// call from a scaffolder or wizard. Returns the dictionary so
        /// callers can wire references onto definitions in one pass.
        /// </summary>
        public static void BuildAll()
        {
            EnsureFolder(Folder);

            // Phase 2.5: pattern textures from the Kenney Pattern Pack.
            // Force their import settings to Repeat / sRGB / uncompressed
            // so they tile cleanly and stay crisp on hero blocks.
            BlockTextures.EnsureImportSettings();

            // Structure: light slate workhorse with a fat black outline so
            // the bulk-volume cubes read as comic-panel cells. This is the
            // single biggest readability lever — Structure is most of what
            // the eye sees, so giving it the obvious ink line lands the
            // toon look immediately.
            //
            // Note on outlineSize units: in HullClip mode (the cube-friendly
            // mode) the shader divides by _ScreenParams.xy, so values are
            // effectively pixels-of-stroke at reference resolution. 5 ≈ 1px,
            // which is why the previous attempts looked invisible. 60 reads
            // as a confident comic-panel ink line at 1080p.
            Build("BlockMat_Structure", WorldPalette.SlateLight, metallic: 0.0f, smoothness: 0.20f, outline: true,
                  outlineSize: 95f, textureIndex: BlockTextures.Picks.Structure);

            // Cpu: cyan, outline-on, low base emission. Beacon adds the loud emission.
            Build("BlockMat_Cpu",       WorldPalette.Cyan,       metallic: 0.5f, smoothness: 0.55f, outline: true,
                  emission: WorldPalette.Cyan * 0.6f, outlineSize: 85f, textureIndex: BlockTextures.Picks.Cpu);

            // Wheel & hub.
            Build("BlockMat_WheelTire", new Color(0.12f, 0.13f, 0.15f), metallic: 0.0f, smoothness: 0.05f, outline: false,
                  textureIndex: BlockTextures.Picks.WheelTire);
            Build("BlockMat_WheelHub",  WorldPalette.SlateLight, metallic: 0.6f, smoothness: 0.55f, outline: false,
                  textureIndex: BlockTextures.Picks.WheelHub);

            // Thruster: hazard-orange body, outline on (hero).
            Build("BlockMat_Thruster",  WorldPalette.Hazard,     metallic: 0.3f, smoothness: 0.40f, outline: true,
                  emission: WorldPalette.Hazard * 0.4f, outlineSize: 85f, textureIndex: BlockTextures.Picks.Thruster);

            // Aero / wing: pale slate, no outline.
            Build("BlockMat_Aero",      WorldPalette.SlateLight, metallic: 0.0f, smoothness: 0.30f, outline: false,
                  textureIndex: BlockTextures.Picks.Aero);

            // Weapon: alert red, outline on.
            Build("BlockMat_Weapon",    WorldPalette.Alert,      metallic: 0.4f, smoothness: 0.55f, outline: true,
                  outlineSize: 85f, textureIndex: BlockTextures.Picks.Weapon);

            // Bomb bay: dark slate body with the same hazard pattern as
            // the thruster so the silhouette reads as "this thing carries
            // ordnance". Outlined like the rest of the hero blocks.
            Build("BlockMat_BombBay",   new Color(0.18f, 0.18f, 0.20f), metallic: 0.35f, smoothness: 0.45f, outline: true,
                  emission: WorldPalette.Hazard * 0.25f, outlineSize: 85f, textureIndex: BlockTextures.Picks.Thruster);

            // Rope anchor: dark matte slate, no outline, no pattern. The
            // anchor cube is hidden at runtime (RopeBlock.HideHostMesh),
            // so the only time this material is seen is in the build
            // ghost / garage preview. Match the rope segment colour so
            // the preview reads as "rope" without extra wiring.
            Build("BlockMat_Rope",      new Color(0.18f, 0.20f, 0.22f), metallic: 0.0f, smoothness: 0.10f, outline: false);

            // Rotor anchor: same story — the host cube is hidden at
            // runtime by RotorBlock so this material only appears in the
            // build ghost. Lean cyan-tinted slate so the preview reads
            // as "this thing has tech / will spin" before the player
            // even places it.
            Build("BlockMat_Rotor",     new Color(0.22f, 0.30f, 0.36f), metallic: 0.2f, smoothness: 0.40f, outline: false);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Resolve the right material for a given category. Used by the
        /// wizard to populate the <c>_material</c> field on each definition.
        /// </summary>
        public static Material ForBlockId(string blockId, BlockCategory category)
        {
            // The wheel/hub split is the only one that doesn't fall out of
            // category alone — every other category maps 1:1.
            if (blockId == BlockIds.Wheel || blockId == BlockIds.WheelSteer)
                return Load("BlockMat_WheelTire");
            if (blockId == BlockIds.Rope)
                return Load("BlockMat_Rope");
            if (blockId == BlockIds.Rotor)
                return Load("BlockMat_Rotor");

            switch (category)
            {
                case BlockCategory.Cpu:        return Load("BlockMat_Cpu");
                case BlockCategory.Weapon:
                    if (blockId == BlockIds.BombBay) return Load("BlockMat_BombBay");
                    return Load("BlockMat_Weapon");
                case BlockCategory.Movement:
                    if (blockId == BlockIds.Thruster) return Load("BlockMat_Thruster");
                    if (blockId == BlockIds.Aero)     return Load("BlockMat_Aero");
                    if (blockId == BlockIds.AeroFin)  return Load("BlockMat_Aero");
                    if (blockId == BlockIds.Rudder)   return Load("BlockMat_Aero");
                    return Load("BlockMat_WheelTire");
                case BlockCategory.Structure:
                case BlockCategory.Module:
                case BlockCategory.Cosmetic:
                default:
                    return Load("BlockMat_Structure");
            }
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private static Material Build(string assetName, Color baseColor, float metallic, float smoothness,
                                      bool outline, Color? emission = null, float outlineSize = 85f,
                                      int textureIndex = 0)
        {
            string path = $"{Folder}/{assetName}.mat";
            string shaderName = outline ? ToonOutlineShaderName : ToonShaderName;
            Shader shader = Shader.Find(shaderName)
                            ?? Shader.Find(ToonShaderName)
                            ?? Shader.Find(LitShaderName)
                            ?? Shader.Find("Standard");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Multi-shader property probe — same trick used in WorldPalette.
            if (mat.HasProperty("_AlbedoColor")) mat.SetColor("_AlbedoColor", baseColor);
            if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor",   baseColor);
            if (mat.HasProperty("_Color"))       mat.SetColor("_Color",       baseColor);
            if (mat.HasProperty("_Metallic"))    mat.SetFloat("_Metallic",    metallic);
            if (mat.HasProperty("_Smoothness"))  mat.SetFloat("_Smoothness",  smoothness);
            if (mat.HasProperty("_Glossiness"))  mat.SetFloat("_Glossiness",  smoothness);

            if (emission.HasValue && mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", emission.Value);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            // Pattern texture (Kenney Pattern Pack). Goes in MK Toon's
            // _AlbedoMap slot, multiplied against _AlbedoColor — black
            // pattern lines etch into the surface, white passes the
            // palette colour through. Tile so the pattern repeats a few
            // times per cube face. Intensity stops the pattern from
            // overpowering the palette tint. Setting _MainTex too keeps
            // the URP/Lit fallback shader path looking right.
            if (textureIndex > 0)
            {
                Texture2D pattern = BlockTextures.LoadPattern(textureIndex);
                if (pattern != null)
                {
                    Vector2 tile   = new Vector2(BlockTextures.DefaultTiling, BlockTextures.DefaultTiling);
                    Vector2 offset = Vector2.zero;

                    if (mat.HasProperty("_AlbedoMap"))
                    {
                        mat.SetTexture("_AlbedoMap", pattern);
                        mat.SetTextureScale("_AlbedoMap", tile);
                        mat.SetTextureOffset("_AlbedoMap", offset);
                    }
                    if (mat.HasProperty("_AlbedoMapIntensity"))
                    {
                        mat.SetFloat("_AlbedoMapIntensity", BlockTextures.DefaultIntensity);
                    }
                    if (mat.HasProperty("_BaseMap"))     // URP/Lit fallback
                    {
                        mat.SetTexture("_BaseMap", pattern);
                        mat.SetTextureScale("_BaseMap", tile);
                    }
                    if (mat.HasProperty("_MainTex"))     // Standard / hidden alias
                    {
                        mat.SetTexture("_MainTex", pattern);
                        mat.SetTextureScale("_MainTex", tile);
                    }
                }
                else
                {
                    Debug.LogWarning($"[Robogame] BlockMaterials: pattern_{textureIndex:00}.png not found for {assetName}.");
                }
            }

            // Outline tuning — MK Toon's outline lives on a second pass
            // (LightMode = MKToonOutline) and exposes _OutlineColor /
            // _OutlineSize / _Outline (hull mode enum). We just set the
            // properties; the keyword wiring happens in
            // RunMKToonValidation below, which calls MK Toon's own
            // ValidateMaterial and matches what its inspector does on
            // every property change.
            if (outline)
            {
                if (mat.HasProperty("_OutlineColor"))        mat.SetColor("_OutlineColor", Color.black);
                if (mat.HasProperty("_OutlineSize"))         mat.SetFloat("_OutlineSize", outlineSize);
                if (mat.HasProperty("_OutlineWidth"))        mat.SetFloat("_OutlineWidth", outlineSize);
                if (mat.HasProperty("_OutlineConstantSize")) mat.SetFloat("_OutlineConstantSize", 0f);
                if (mat.HasProperty("_OutlineNoise"))        mat.SetFloat("_OutlineNoise", 0f);
                // _Outline enum: 1=HullObject, 2=HullOrigin, 3=HullClip.
                // HullClip is the most reliable for cube blocks (clip-space
                // expansion gives a near-uniform stroke from any angle).
                if (mat.HasProperty("_Outline"))             mat.SetFloat("_Outline", 3f);
            }

            // Force MK Toon to re-derive every keyword from the property
            // values we just set. Without this the outline pass compiles
            // to the `__` (no-displacement) variant and renders nothing,
            // and several body-pass keywords (workflow, blend, etc.) stay
            // at their pre-import defaults. ValidateMaterial is exactly
            // what the inspector calls on every property change.
            RunMKToonValidation(mat);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material Load(string assetName)
            => AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{assetName}.mat");

        // -----------------------------------------------------------------
        // MK Toon keyword sync
        // -----------------------------------------------------------------

        // Resolved lazily once. The MK Toon editor classes are `internal`
        // so we have to go through reflection — but we only do it once per
        // domain reload, and we cache failure as null so we don't keep
        // probing if MK Toon isn't installed.
        private static System.Reflection.MethodInfo s_validateMethod;
        private static object s_editorInstance;
        private static bool   s_validateResolved;

        /// <summary>
        /// Invoke MK Toon's own <c>ValidateMaterial</c> on the given
        /// material. This is the same method the inspector calls on every
        /// property change — it walks every property and calls
        /// <c>EditorHelper.SetKeyword</c> for the matching shader feature
        /// (workflow, outline hull mode, surface type, blend, …). Without
        /// this, properties we set in code don't translate into shader
        /// keywords and the variant compiler picks the `__` no-op branch.
        /// </summary>
        private static void RunMKToonValidation(Material mat)
        {
            if (mat == null || mat.shader == null) return;

            // First call: try to find the editor type that the shader
            // declares as its CustomEditor and the ValidateMaterial method.
            if (!s_validateResolved)
            {
                s_validateResolved = true;

                // The PBS+Outline shader uses CustomEditor
                // "MK.Toon.Editor.URP.StandardPBSEditor". Find by name
                // across all loaded assemblies — Editor asmdef is
                // loaded in the editor domain so this resolves cleanly.
                System.Type editorType = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    editorType = asm.GetType("MK.Toon.Editor.URP.StandardPBSEditor", throwOnError: false);
                    if (editorType != null) break;
                }

                if (editorType == null)
                {
                    Debug.LogWarning("[Robogame] BlockMaterials: MK.Toon.Editor.URP.StandardPBSEditor not found. " +
                                     "Outline keywords will not be auto-synced.");
                    return;
                }

                try
                {
                    s_editorInstance = System.Activator.CreateInstance(editorType, nonPublic: true);
                }
                catch
                {
                    s_editorInstance = System.Runtime.Serialization.FormatterServices
                        .GetUninitializedObject(editorType);
                }

                // ValidateMaterial(Material) is declared on UnlitEditorBase
                // as virtual public (Unity 2021.2+). Walk up the hierarchy.
                var t = editorType;
                while (t != null && s_validateMethod == null)
                {
                    s_validateMethod = t.GetMethod(
                        "ValidateMaterial",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(Material) },
                        modifiers: null);
                    t = t.BaseType;
                }

                if (s_validateMethod == null)
                {
                    Debug.LogWarning("[Robogame] BlockMaterials: ValidateMaterial(Material) not found on MK Toon editor.");
                }
            }

            if (s_validateMethod == null || s_editorInstance == null) return;

            try
            {
                s_validateMethod.Invoke(s_editorInstance, new object[] { mat });
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Robogame] BlockMaterials: MK Toon ValidateMaterial threw on {mat.name}: {ex.GetBaseException().Message}");
            }
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
