using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Procedural runtime materials. Centralises the URP transparent-material
    /// setup so every translucent runtime visual (shield dome, cloak ghost, …)
    /// renders the same correct way.
    /// </summary>
    /// <remarks>
    /// The load-bearing detail is <c>EnableKeyword("_SURFACE_TYPE_TRANSPARENT")</c>
    /// plus <c>_ZWrite = 0</c> and the <c>RenderType</c> override tag: setting
    /// the <c>_Surface</c> float alone (what a naive <c>new Material(litShader)</c>
    /// does) leaves the shader on its opaque pass at runtime, which is why the
    /// first cut of the shield + cloak rendered bright and opaque regardless of
    /// alpha. Mirrors <c>BlockGhostRenderer.MakeMat</c>, the proven pattern.
    /// Unlit (not Lit) so a low-alpha tint reads as a faint film, not a
    /// lit/emissive glow.
    /// </remarks>
    public static class RuntimeMaterials
    {
        /// <summary>
        /// An alpha-blended unlit material at <paramref name="color"/> (use the
        /// alpha channel to set translucency). Caller owns the returned
        /// instance unless it caches + reuses it.
        /// </summary>
        public static Material UnlitTransparent(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "Mat_UnlitTransparent" };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // 1 = Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);     // 0 = Alpha
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000;
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        // -------------------------------------------------------------
        // MaterialPropertyBlock tinting
        //
        // One implementation for the "tint this renderer" idiom that was
        // hand-copied across ~11 block visual rigs. Sets every color
        // property family the project's shaders use: the MK Toon block
        // shader (_AlbedoColor), URP Lit/Unlit (_BaseColor), and legacy
        // (_Color). Properties a shader lacks are ignored, so writing all
        // three is safe — and omitting one (as a drifted copy did with
        // _AlbedoColor) silently no-ops on the toon shader.
        // -------------------------------------------------------------

        private static readonly int s_albedoColorId   = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_baseColorId     = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId   = Shader.PropertyToID("_Color");
        private static readonly int s_emissionColorId = Shader.PropertyToID("_EmissionColor");

        // Plain C# object (not a UnityEngine.Object): survives domain
        // reload harmlessly — GetPropertyBlock overwrites it every call.
        private static MaterialPropertyBlock s_tintMpb;

        /// <summary>Tint a renderer via MaterialPropertyBlock (all shader families).</summary>
        public static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = s_tintMpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_albedoColorId, color);
            mpb.SetColor(s_baseColorId,   color);
            mpb.SetColor(s_legacyColorId, color);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Tint plus emission. Emission is written only when it is
        /// non-black, so unlit rigs don't pick up a stray property.
        /// </summary>
        public static void Tint(Renderer r, Color color, Color emission)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = s_tintMpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_albedoColorId, color);
            mpb.SetColor(s_baseColorId,   color);
            mpb.SetColor(s_legacyColorId, color);
            if (emission.maxColorComponent > 0f)
                mpb.SetColor(s_emissionColorId, emission);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Convenience: tint the Renderer on <paramref name="t"/>, if any.</summary>
        public static void Tint(Transform t, Color color)
        {
            if (t == null) return;
            Tint(t.GetComponent<Renderer>(), color);
        }
    }
}
