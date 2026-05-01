using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Authors per-scene <see cref="VolumeProfile"/> assets so the URP
    /// post stack matches <c>docs/ART_DIRECTION.md</c> without a human
    /// having to right-click and tweak sliders. Idempotent: re-running
    /// overwrites the profile in place.
    /// </summary>
    /// <remarks>
    /// Profiles are saved under <see cref="Folder"/>. The matching
    /// <see cref="Volume"/> component is dropped into each scene by
    /// <see cref="EnvironmentBuilder"/>, which loads the profile by
    /// path right before assignment (same fake-null avoidance pattern
    /// documented in <c>CHANGES.md</c>).
    /// </remarks>
    internal static class PostProcessingBuilder
    {
        public const string Folder = "Assets/_Project/Rendering/PostProcessing";
        public const string GarageProfilePath = Folder + "/PostProfile_Garage.asset";
        public const string ArenaProfilePath  = Folder + "/PostProfile_Arena.asset";

        public static void BuildAll()
        {
            EnsureFolder(Folder);
            BuildGarageProfile();
            BuildArenaProfile();
            AssetDatabase.SaveAssets();
        }

        // -----------------------------------------------------------------
        // Garage — workshop dusk: warmer, slightly contrasty, vignette on.
        // Numbers come from ART_DIRECTION.md "Post-Processing Rules".
        // -----------------------------------------------------------------
        private static VolumeProfile BuildGarageProfile()
        {
            VolumeProfile p = LoadOrCreate(GarageProfilePath);
            p.components.Clear();

            Bloom bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.5f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.95f, 0.85f));

            Tonemapping tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = p.Add<ColorAdjustments>(true);
            color.contrast.Override(10f);
            color.saturation.Override(15f);
            color.colorFilter.Override(new Color(1.02f, 0.98f, 0.92f));

            Vignette vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.25f);
            vig.smoothness.Override(0.4f);
            vig.color.Override(new Color(0.05f, 0.05f, 0.08f));

            EditorUtility.SetDirty(p);
            return p;
        }

        // -----------------------------------------------------------------
        // Arena — bright, exposed, neutral. Bloom does the heavy lifting
        // for the cyan CPU beacon.
        // -----------------------------------------------------------------
        private static VolumeProfile BuildArenaProfile()
        {
            VolumeProfile p = LoadOrCreate(ArenaProfilePath);
            p.components.Clear();

            Bloom bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.4f);
            bloom.scatter.Override(0.75f);

            Tonemapping tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = p.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(15f);

            EditorUtility.SetDirty(p);
            return p;
        }

        private static VolumeProfile LoadOrCreate(string path)
        {
            VolumeProfile p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p != null) return p;
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            return p;
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
