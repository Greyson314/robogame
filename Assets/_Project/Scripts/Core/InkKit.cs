using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Runtime-generated sprite kit + fonts for the "inventor + painter" UI
    /// direction: brush blobs, wash fills, underline swipes, splats, wax
    /// seals, dashed lines, paper grounds, drafting grids, registration
    /// marks. Shape language and
    /// token values come from the July 2026 design handoff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why generated instead of PNG assets: the whole UI is procedural C#
    /// (see <c>docs/subsystems/ui-direction.md</c>) — keeping the brush
    /// shapes in code means no binary assets, no import settings, and the
    /// organic wobble stays tweakable next to its consumers. Everything is
    /// baked once on first access (allocation at init, not per frame).
    /// </para>
    /// <para>
    /// All shape sprites are white — consumers tint via
    /// <see cref="UnityEngine.UI.Image.color"/> with <see cref="UguiPalette"/>
    /// tokens. The one exception is <see cref="WaxSeal"/>, whose three-stop
    /// vermilion gradient is baked (it is never used in another colour).
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff]: shape language + token values.
    public static class InkKit
    {
        // -----------------------------------------------------------------
        // Statics survive domain reload; textures don't. Reset per the
        // project failure-mode list in CLAUDE.md.
        // -----------------------------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_display = null; s_annotation = null;
            s_blob = null; s_barFill = null; s_washFill = null;
            s_underline = null; s_splat = null; s_waxSeal = null;
            s_dashTile = null; s_gridTile = null; s_paper = null;
            s_regMark = null; s_wipeBrush = null; s_arrowTip = null;
        }

        // -----------------------------------------------------------------
        // Fonts (Resources/Fonts/*.ttf, OFL — Google Fonts)
        // -----------------------------------------------------------------

        private static Font s_display;
        private static Font s_annotation;

        /// <summary>Averia Libre — display + all UI text, labels, numerals, hotkeys.
        /// (User pick, supersedes the handoff's Yuji Syuku.)</summary>
        public static Font Display
        {
            get
            {
                if (s_display == null)
                    s_display = Resources.Load<Font>("Fonts/AveriaLibre-Regular");
                // Fallback keeps the UI alive if the TTF ever goes missing.
                if (s_display == null)
                    s_display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_display;
            }
        }

        /// <summary>Space Mono — annotations, part numbers, flavor lines, readouts.
        /// (User pick, supersedes the handoff's Cardo Italic; call sites keep
        /// FontStyle.Italic for the annotation voice.)</summary>
        public static Font Annotation
        {
            get
            {
                if (s_annotation == null)
                    s_annotation = Resources.Load<Font>("Fonts/SpaceMono-Regular");
                if (s_annotation == null)
                    s_annotation = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_annotation;
            }
        }

        // -----------------------------------------------------------------
        // Sprites
        // -----------------------------------------------------------------

        private static Sprite s_blob, s_barFill, s_washFill, s_underline;
        private static Sprite s_splat, s_waxSeal, s_dashTile, s_gridTile;
        private static Sprite s_paper, s_regMark, s_wipeBrush, s_arrowTip;

        /// <summary>Organic button blob (irregular corners, white). Tint ink for primary buttons.</summary>
        public static Sprite BrushBlob => s_blob ??= BakeSuperellipse(
            "Ink_BrushBlob", 256, 96, exponent: 3.2f, wobbleAmp: 0.045f, wobbleFreq: 5, seed: 71, alphaTail: 0f);

        /// <summary>Solid bar fill with brushy edges — hull/ink fills.</summary>
        public static Sprite BarFill => s_barFill ??= BakeSuperellipse(
            "Ink_BarFill", 256, 40, exponent: 5f, wobbleAmp: 0.05f, wobbleFreq: 7, seed: 12, alphaTail: 0f);

        /// <summary>Bar fill whose alpha fades left→right (0.8 → 0.4 → 0.08) — indigo wash fills.</summary>
        public static Sprite WashFill => s_washFill ??= BakeSuperellipse(
            "Ink_WashFill", 256, 40, exponent: 5f, wobbleAmp: 0.05f, wobbleFreq: 7, seed: 34, alphaTail: 1f);

        /// <summary>Thin underline swipe with tapered ends.</summary>
        public static Sprite Underline => s_underline ??= BakeSuperellipse(
            "Ink_Underline", 256, 24, exponent: 1.8f, wobbleAmp: 0.10f, wobbleFreq: 6, seed: 55, alphaTail: 0f);

        /// <summary>Lumpy splat / ammo pip blob.</summary>
        public static Sprite Splat => s_splat ??= BakeSplat("Ink_Splat", 64, seed: 9);

        /// <summary>Wax seal — baked vermilion radial (#E06843 → #C33D1F → #8F2812). Do not tint.</summary>
        public static Sprite WaxSeal => s_waxSeal ??= BakeWaxSeal("Ink_WaxSeal", 64, seed: 23);

        /// <summary>12×2 dash tile (7 on / 5 off). Use Image.Type.Tiled for dashed rules/borders.</summary>
        public static Sprite DashTile => s_dashTile ??= BakeDashTile();

        /// <summary>28×28 drafting-grid tile (1px lines on two edges). Tile full-surface, tint ink at ~3% alpha.</summary>
        public static Sprite GridTile => s_gridTile ??= BakeGridTile();

        /// <summary>Paper ground: baked radial #F6F0E0 → #EDE4CC → #E1D5B6 with faint grain. Stretch full-bleed.</summary>
        public static Sprite Paper => s_paper ??= BakePaper("Ink_Paper", 256, seed: 3);

        /// <summary>Registration cross (+) for screen corners. Tint ink @ ~45% alpha.</summary>
        public static Sprite RegMark => s_regMark ??= BakeRegMark();

        /// <summary>
        /// Vertical brush edge for the full-screen page wipe: solid on the
        /// left, jagged dry-brush alpha on the right. Stretch to screen
        /// height at the leading edge of the ink cover (mirror for the
        /// trailing edge), tint ink.
        /// </summary>
        // TRACE[DOC:research/ui-design-handoff-motion]: ink-wipe transition.
        public static Sprite WipeBrush => s_wipeBrush ??= BakeWipeBrush("Ink_WipeBrush", 128, 512, seed: 41);

        /// <summary>Small triangle arrowhead (points +X) for dimension lines and leaders. Tint ink/faded.</summary>
        public static Sprite ArrowTip => s_arrowTip ??= BakeArrowTip("Ink_ArrowTip", 32);

        // -----------------------------------------------------------------
        // Bakers
        // -----------------------------------------------------------------

        private static Texture2D NewTex(int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return t;
        }

        private static Sprite ToSprite(Texture2D tex, string name, bool fullRect = false)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0,
                fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight);
            s.name = name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        /// <summary>
        /// Superellipse silhouette (|x/a|^n + |y/b|^n = 1) with a low-frequency
        /// angular wobble so nothing reads as a perfect rectangle. White fill,
        /// anti-aliased edge. <paramref name="alphaTail"/> = 1 bakes the
        /// left→right wash gradient (0.8 → 0.4 → 0.08).
        /// </summary>
        private static Sprite BakeSuperellipse(string name, int w, int h,
            float exponent, float wobbleAmp, int wobbleFreq, int seed, float alphaTail)
        {
            var rng = new System.Random(seed);
            float p0 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float p1 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            float cx = w * 0.5f, cy = h * 0.5f;
            // Inset so the wobble never clips the texture edge.
            float a = cx * (1f - wobbleAmp - 0.03f);
            float b = cy * (1f - wobbleAmp - 0.03f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float ang = Mathf.Atan2(dy, dx);
                    // Two sine bands at different frequencies = organic, not scalloped.
                    float wob = 1f + wobbleAmp * (Mathf.Sin(ang * wobbleFreq + p0) * 0.65f
                                                + Mathf.Sin(ang * (wobbleFreq * 2 + 1) + p1) * 0.35f);
                    float sx = Mathf.Abs(dx) / (a * wob);
                    float sy = Mathf.Abs(dy) / (b * wob);
                    float d = Mathf.Pow(Mathf.Pow(sx, exponent) + Mathf.Pow(sy, exponent), 1f / exponent);
                    // ~1.5px anti-aliased edge in normalized units.
                    float edge = 1.5f / Mathf.Min(a, b);
                    float alpha = Mathf.Clamp01((1f - d) / edge);
                    if (alphaTail > 0f && alpha > 0f)
                    {
                        float t = (x + 0.5f) / w; // 0 left → 1 right
                        float wash = t < 0.5f ? Mathf.Lerp(0.8f, 0.4f, t * 2f)
                                              : Mathf.Lerp(0.4f, 0.08f, (t - 0.5f) * 2f);
                        alpha *= Mathf.Lerp(1f, wash, alphaTail);
                    }
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return ToSprite(tex, name);
        }

        /// <summary>Radial blob with lumpy boundary — kill-feed splats, ammo pips.</summary>
        private static Sprite BakeSplat(string name, int size, int seed)
        {
            var rng = new System.Random(seed);
            float p0 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float p1 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f;
            float baseR = c * 0.78f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float ang = Mathf.Atan2(dy, dx);
                    float r = baseR * (1f + 0.16f * Mathf.Sin(ang * 3f + p0)
                                          + 0.09f * Mathf.Sin(ang * 7f + p1));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((r - dist) / 1.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, name);
        }

        /// <summary>Wax seal: three-stop vermilion radial with an irregular rim. Colours baked.</summary>
        private static Sprite BakeWaxSeal(string name, int size, int seed)
        {
            Color inner = new(0.878f, 0.408f, 0.263f); // #E06843
            Color mid   = new(0.765f, 0.239f, 0.122f); // #C33D1F
            Color outer = new(0.561f, 0.157f, 0.071f); // #8F2812
            var rng = new System.Random(seed);
            float p0 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f;
            float baseR = c * 0.82f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float ang = Mathf.Atan2(dy, dx);
                    float rim = baseR * (1f + 0.10f * Mathf.Sin(ang * 5f + p0));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(dist / rim);
                    Color col = t < 0.45f ? Color.Lerp(inner, mid, t / 0.45f)
                                          : Color.Lerp(mid, outer, (t - 0.45f) / 0.55f);
                    col.a = Mathf.Clamp01((rim - dist) / 1.5f);
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, name);
        }

        private static Sprite BakeDashTile()
        {
            const int w = 12, h = 2;
            var tex = NewTex(w, h, TextureWrapMode.Repeat);
            tex.filterMode = FilterMode.Point; // crisp dashes
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = x < 7 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Ink_DashTile", fullRect: true);
        }

        private static Sprite BakeGridTile()
        {
            const int s = 28;
            var tex = NewTex(s, s, TextureWrapMode.Repeat);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x == 0 || y == 0)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Ink_GridTile", fullRect: true);
        }

        private static Sprite BakePaper(string name, int size, int seed)
        {
            Color center = new(0.965f, 0.941f, 0.878f); // #F6F0E0
            Color mid    = new(0.929f, 0.894f, 0.800f); // #EDE4CC
            Color edge   = new(0.882f, 0.835f, 0.714f); // #E1D5B6
            var rng = new System.Random(seed);
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float c = size * 0.5f;
            float maxD = Mathf.Sqrt(2f) * c;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float t = Mathf.Sqrt(dx * dx + dy * dy) / maxD;
                    Color col = t < 0.55f ? Color.Lerp(center, mid, t / 0.55f)
                                          : Color.Lerp(mid, edge, (t - 0.55f) / 0.45f);
                    // ±1.2% luminance grain so the ground doesn't band.
                    float g = 1f + ((float)rng.NextDouble() - 0.5f) * 0.024f;
                    col.r *= g; col.g *= g; col.b *= g; col.a = 1f;
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, name, fullRect: true);
        }

        /// <summary>
        /// Brush edge strip: alpha 1 at x=0 fading through a two-band sine
        /// wobble edge — the dry side of a loaded brush stroke.
        /// </summary>
        private static Sprite BakeWipeBrush(string name, int w, int h, int seed)
        {
            var rng = new System.Random(seed);
            float p0 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float p1 = (float)(rng.NextDouble() * Mathf.PI * 2f);
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                // Edge position 0.30–0.85 of width, organic two-band wobble.
                float edge = w * (0.55f + 0.22f * Mathf.Sin(v * 9.3f * Mathf.PI + p0)
                                        + 0.08f * Mathf.Sin(v * 23.7f * Mathf.PI + p1));
                for (int x = 0; x < w; x++)
                {
                    // 3px soft edge; occasional dry-brush streak past it.
                    float a = Mathf.Clamp01((edge - x) / 3f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, name, fullRect: true);
        }

        /// <summary>Anti-aliased triangle pointing +X, drawn with a slightly concave back edge.</summary>
        private static Sprite BakeArrowTip(string name, int s)
        {
            var tex = NewTex(s, s);
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    // Triangle: tip at (s-2, s/2), base at x=2 spanning full height.
                    float t = (x - 2f) / (s - 4f);                    // 0 base → 1 tip
                    float halfHeight = Mathf.Lerp(s * 0.42f, 0.8f, t); // taper
                    float dy = Mathf.Abs(y + 0.5f - s * 0.5f);
                    float a = Mathf.Clamp01((halfHeight - dy) / 1.4f) * Mathf.Clamp01((s - 2f - x) / 1.4f) * Mathf.Clamp01((x - 1f) / 1.4f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, name);
        }

        private static Sprite BakeRegMark()
        {
            const int s = 24;
            var tex = NewTex(s, s);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[s * s];
            int mid0 = s / 2 - 1, mid1 = s / 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x == mid0 || x == mid1 || y == mid0 || y == mid1)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Ink_RegMark");
        }
    }
}
