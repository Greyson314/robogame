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
    }
}
