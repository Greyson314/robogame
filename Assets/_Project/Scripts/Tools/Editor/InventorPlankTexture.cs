using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Generates the inventor-aesthetic oak-plank albedo for the
    /// structure cube and applies it to <c>BlockMat_Structure</c>. The
    /// texture carries the full colour (base tint goes white so damage
    /// MPB darkening still multiplies correctly); geometry stays the
    /// batched primitive cube — this is the "planks as albedo, not as
    /// 49 objects" route from the session-132 cube decision.
    /// </summary>
    // TRACE[LOG-132]: structure-cube direction — continuous oak planks.
    public static class InventorPlankTexture
    {
        public const string TexturePath = "Assets/_Project/Art/Textures/PlankOak_Inv.png";
        private const string MaterialPath = BlockMaterials.Folder + "/BlockMat_Structure.mat";

        private const int Size = 256;
        private const int Courses = 4;

        // sRGB bytes of the linear oak/walnut tones in artgen/inventorlib.py.
        private static readonly Color32 OakA = new Color32(133, 101, 67, 255);
        private static readonly Color32 OakB = new Color32(124, 93, 61, 255);
        private static readonly Color32 Seam = new Color32(70, 47, 30, 255);

        // Butt-joint x positions per course (fractions), staggered — same
        // table family as artgen/inv_cube.py.
        private static readonly float[] Joints = { 0.34f, 0.68f, 0.47f, 0.76f };

        [MenuItem("Robogame/Art/Generate Plank Texture (Structure Cube)")]
        public static void GenerateAndApply()
        {
            Generate();
            ApplyIfPresent();
        }

        private static void Generate()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            int courseH = Size / Courses;

            for (int y = 0; y < Size; y++)
            {
                int course = y / courseH;
                int yIn = y % courseH;
                int jointX = Mathf.RoundToInt(Joints[course % Joints.Length] * Size);

                for (int x = 0; x < Size; x++)
                {
                    // Course seams (horizontal) — 2px at the course base so
                    // the texture tiles cleanly across cube faces.
                    if (yIn < 2) { tex.SetPixel(x, y, Seam); continue; }
                    // Butt joint (vertical) within this course — 2px.
                    int dj = Mathf.Abs(x - jointX);
                    if (dj < 2) { tex.SetPixel(x, y, Seam); continue; }

                    int board = x < jointX ? 0 : 1;
                    Color32 tone32 = (course + board) % 2 == 0 ? OakA : OakB;
                    Color tone = tone32;

                    // Grain: streaks stretched along X, banded per course so
                    // planks don't visibly share grain across seams.
                    float n = Mathf.PerlinNoise(x * 0.021f + course * 13.7f,
                                                y * 0.55f);
                    float streak = Mathf.PerlinNoise(x * 0.15f + course * 31.1f,
                                                     y * 0.05f);
                    float v = 0.94f + 0.10f * n + 0.05f * (streak - 0.5f);

                    // Soft top-lit shading inside each course: lighter at the
                    // top edge, a touch darker at the bottom — a painted
                    // chamfer that keeps the flat mesh reading bevelled.
                    float e = yIn / (float)courseH;
                    v *= 1.0f + 0.05f * (e - 0.35f);

                    tex.SetPixel(x, y, new Color(tone.r * v, tone.g * v, tone.b * v));
                }
            }

            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[InventorPlankTexture] Generated {TexturePath}");
        }

        /// <summary>
        /// Applies the plank albedo to BlockMat_Structure when the texture
        /// exists. Called from BlockMaterials after its rebuild so a Build
        /// Everything run can't silently revert the cube to slate.
        /// </summary>
        public static void ApplyIfPresent()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null) return;   // texture not generated yet — no-op
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Debug.LogWarning($"[InventorPlankTexture] Missing {MaterialPath} — run Build Everything first.");
                return;
            }

            var tile = Vector2.one;
            if (mat.HasProperty("_AlbedoMap"))
            {
                mat.SetTexture("_AlbedoMap", tex);
                mat.SetTextureScale("_AlbedoMap", tile);
            }
            if (mat.HasProperty("_AlbedoMapIntensity")) mat.SetFloat("_AlbedoMapIntensity", 1f);
            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tile); }
            if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", tile); }
            // Texture carries the colour; tint goes white so the damage
            // MPB darkening keeps multiplying correctly.
            if (mat.HasProperty("_AlbedoColor")) mat.SetColor("_AlbedoColor", Color.white);
            if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor",   Color.white);
            if (mat.HasProperty("_Color"))       mat.SetColor("_Color",       Color.white);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[InventorPlankTexture] Applied plank albedo to BlockMat_Structure.");
        }
    }
}
