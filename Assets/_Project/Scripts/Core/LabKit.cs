using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Night-workshop tokens + runtime-baked sprites for the Laboratory
    /// screen's "evil scientist" treatment — the one sanctioned dark
    /// departure from the parchment ground used everywhere else.
    /// Colours, shapes and
    /// elevation language come from the July 2026 Laboratory design handoff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same discipline as <see cref="InkKit"/>: sprites bake once on first
    /// access, shape sprites are white for tinting, colour literals live
    /// HERE so <c>LabController</c> stays token-only (ui-direction.md's one
    /// rule). Screen-scoped on purpose — these tokens are not a third
    /// project palette; nothing outside the Laboratory should use them.
    /// </para>
    /// <para>
    /// The tiny texture helpers are duplicated from InkKit rather than
    /// widening its API: this kit needs a border-aware sprite variant
    /// (9-slice frames) that InkKit has no use for.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-laboratory]: colours, shapes, elevation.
    public static class LabKit
    {
        // -----------------------------------------------------------------
        // Statics survive domain reload; textures don't. Reset per the
        // project failure-mode list in CLAUDE.md.
        // -----------------------------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_ground = null; s_wood = null; s_plate = null; s_glow = null;
            s_circle = null; s_ring = null; s_border = null; s_brassBar = null;
            s_brassKnob = null; s_cork = null; s_ticks = null; s_fadeV = null;
            s_tubeFill = null; s_tubeOutline = null; s_miniVial = null;
            s_fogA = null; s_fogB = null;
        }

        // -----------------------------------------------------------------
        // Tokens (handoff "Design Tokens" section)
        // -----------------------------------------------------------------

        /// <summary>Galvanic accent — the screen's single glowing colour
        /// (drag highlights, hover glow). Handoff default, cyan option.</summary>
        public static readonly Color Accent = new(0.498f, 0.863f, 0.784f, 1f); // #7FDCC8

        /// <summary>Accent at glow alpha (handoff: accent + AA).</summary>
        public static readonly Color AccentGlow = new(0.498f, 0.863f, 0.784f, 0.67f);

        /// <summary>Bone text/chrome (#E8E1D2) at a given alpha — the dark
        /// screen's counterpart to ink-on-paper.</summary>
        public static Color Bone(float a = 1f) => new(0.910f, 0.882f, 0.824f, a);

        /// <summary>Black at a given alpha — wells, inset shades, shadows.</summary>
        public static Color Shade(float a) => new(0f, 0f, 0f, a);

        /// <summary>Indigo wash (#4A6E7E) at a given alpha — selected /
        /// hover state, same secondary-state semantic as the paper UI.</summary>
        public static Color IndigoWash(float a) => new(0.290f, 0.431f, 0.494f, a);

        /// <summary>Wood panel border (#4A3F2E).</summary>
        public static readonly Color WoodBorder = new(0.290f, 0.247f, 0.180f, 1f);

        /// <summary>Screw-slot / brass-shadow brown (#4A3A1C).</summary>
        public static readonly Color BrassSlot = new(0.290f, 0.227f, 0.110f, 1f);

        /// <summary>Warm brass tint (#D8B36A) at a given alpha — soot-blotch
        /// warmth, cork nubs.</summary>
        public static Color Brass(float a = 1f) => new(0.847f, 0.702f, 0.416f, a);

        // -----------------------------------------------------------------
        // Sprites
        // -----------------------------------------------------------------

        private static Sprite s_ground, s_wood, s_plate, s_glow, s_circle;
        private static Sprite s_ring, s_border, s_brassBar, s_brassKnob;
        private static Sprite s_cork, s_ticks, s_fadeV, s_tubeFill;
        private static Sprite s_tubeOutline, s_miniVial;

        /// <summary>Full-screen soot ground: radial #26211A → #1B1712 → #131009 with grain. Stretch full-bleed.</summary>
        public static Sprite Ground => s_ground ??= BakeGround();

        /// <summary>Night-workshop wood: vertical #2A241B → #262019 → #221C15 with grain streaks. Panel face.</summary>
        public static Sprite Wood => s_wood ??= BakeWood();

        /// <summary>Raised switchboard plate: top-lit vertical #2F2820 → #28221A.</summary>
        public static Sprite Plate => s_plate ??= BakeVGradient("Lab_Plate",
            new Color(0.184f, 0.157f, 0.125f), new Color(0.157f, 0.133f, 0.102f), grain: 0.012f);

        /// <summary>Soft radial white falloff — soot blotches, sheens, glows, ground shadows. Tint freely.</summary>
        public static Sprite Glow => s_glow ??= BakeGlow();

        /// <summary>Solid anti-aliased white circle — slider pips, screw slots.</summary>
        public static Sprite Circle => s_circle ??= BakeCircle(filled: true);

        /// <summary>White circle outline (~1.5px) — the vial's rising bubbles.</summary>
        public static Sprite Ring => s_ring ??= BakeCircle(filled: false);

        /// <summary>9-sliced 1px white frame — hairline borders (Close button, wells, tracks, fields).</summary>
        public static Sprite Border => s_border ??= BakeBorder();

        /// <summary>Brass slider track: vertical #4A3A1C → #6B5226 → #9C7A3E, baked colours.</summary>
        public static Sprite BrassBar => s_brassBar ??= BakeVGradient("Lab_BrassBar",
            new Color(0.290f, 0.227f, 0.110f), new Color(0.612f, 0.478f, 0.243f), grain: 0f,
            mid: new Color(0.420f, 0.322f, 0.149f), midAt: 0.4f);

        /// <summary>Brass screw head: radial #D8B36A → #8A6B33, baked. Slot is a child image.</summary>
        public static Sprite BrassKnob => s_brassKnob ??= BakeBrassKnob();

        /// <summary>Vial cork: horizontal brass gradient block with a dark rim, baked.</summary>
        public static Sprite Cork => s_cork ??= BakeCork();

        /// <summary>Tick strip — 1px white lines every 10% across the width. Tint faint bone; stretch over the track.</summary>
        public static Sprite Ticks => s_ticks ??= BakeTicks();

        /// <summary>White with vertical alpha fade (opaque top → clear bottom). Rotate for inset shades / liquid glow.</summary>
        public static Sprite FadeV => s_fadeV ??= BakeFadeV();

        /// <summary>Test-tube silhouette (straight sides, round bottom), filled white — liquid mask + glass tint.</summary>
        public static Sprite TubeFill => s_tubeFill ??= BakeTube(edgeOnly: false);

        /// <summary>Test-tube 2px outline, open top — the glass wall.</summary>
        public static Sprite TubeOutline => s_tubeOutline ??= BakeTube(edgeOnly: true);

        /// <summary>Little stoppered-vial swatch for journal rows (rounded 3/3/5/5), white.</summary>
        public static Sprite MiniVial => s_miniVial ??= BakeMiniVial();

        private static Sprite s_fogA, s_fogB;

        /// <summary>Soft white fog bank (clumped radial blobs, fully feathered
        /// edges) — tint faint and drift for the 2.5D background haze.</summary>
        public static Sprite FogA => s_fogA ??= BakeFog("Lab_FogA", seed: 5);

        /// <summary>Second fog bank with a different clump layout, so layered
        /// drift never reads as one repeating card.</summary>
        public static Sprite FogB => s_fogB ??= BakeFog("Lab_FogB", seed: 29);

        // -----------------------------------------------------------------
        // Texture helpers (see remarks re: duplication from InkKit)
        // -----------------------------------------------------------------

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Sprite ToSprite(Texture2D tex, string name, bool fullRect = false, Vector4 border = default)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0,
                fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight, border);
            s.name = name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        // -----------------------------------------------------------------
        // Bakers
        // -----------------------------------------------------------------

        private static Sprite BakeGround()
        {
            Color center = new(0.149f, 0.129f, 0.102f); // #26211A
            Color mid    = new(0.106f, 0.090f, 0.071f); // #1B1712
            Color edge   = new(0.075f, 0.063f, 0.035f); // #131009
            const int size = 256;
            var rng = new System.Random(17);
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float cx = size * 0.5f, cy = size * 0.72f; // hot spot sits high like the handoff's 50% 28%
            float maxD = size * 0.95f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx, dy = (y + 0.5f - cy) * 1.35f;
                    float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / maxD);
                    Color col = t < 0.52f ? Color.Lerp(center, mid, t / 0.52f)
                                          : Color.Lerp(mid, edge, (t - 0.52f) / 0.48f);
                    float g = 1f + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                    col.r *= g; col.g *= g; col.b *= g; col.a = 1f;
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Ground", fullRect: true);
        }

        private static Sprite BakeWood()
        {
            // Warmer and a step lighter than the handoff's near-black wood —
            // user pass: the modal ground should read solid, rich brown.
            Color top = new(0.243f, 0.188f, 0.125f); // #3E3020
            Color mid = new(0.216f, 0.165f, 0.106f); // #372A1B
            Color bot = new(0.184f, 0.137f, 0.086f); // #2F2316
            const int size = 256;
            var rng = new System.Random(41);
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            // Horizontal grain: a per-row luminance wander, softened.
            var rows = new float[size];
            float wander = 0f;
            for (int y = 0; y < size; y++)
            {
                wander = Mathf.Lerp(wander, ((float)rng.NextDouble() - 0.5f) * 0.10f, 0.35f);
                rows[y] = wander;
            }
            for (int y = 0; y < size; y++)
            {
                float t = 1f - (y + 0.5f) / size; // top → bottom
                Color baseCol = t < 0.55f ? Color.Lerp(top, mid, t / 0.55f)
                                          : Color.Lerp(mid, bot, (t - 0.55f) / 0.45f);
                for (int x = 0; x < size; x++)
                {
                    float g = 1f + rows[y] + ((float)rng.NextDouble() - 0.5f) * 0.02f;
                    Color col = baseCol;
                    col.r *= g; col.g *= g; col.b *= g; col.a = 1f;
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Wood", fullRect: true);
        }

        private static Sprite BakeVGradient(string name, Color top, Color bottom, float grain,
            Color mid = default, float midAt = -1f)
        {
            const int w = 8, h = 64;
            var rng = new System.Random(7);
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = 1f - (y + 0.5f) / h; // 0 top → 1 bottom
                Color col;
                if (midAt > 0f)
                    col = t < midAt ? Color.Lerp(top, mid, t / midAt)
                                    : Color.Lerp(mid, bottom, (t - midAt) / (1f - midAt));
                else
                    col = Color.Lerp(top, bottom, t);
                for (int x = 0; x < w; x++)
                {
                    float g = 1f + ((float)rng.NextDouble() - 0.5f) * grain * 2f;
                    Color c = col; c.r *= g; c.g *= g; c.b *= g; c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, name, fullRect: true);
        }

        private static Sprite BakeGlow()
        {
            const int size = 128;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Smooth quadratic falloff to zero at ~85% radius padding.
                    float a = Mathf.Clamp01(1f - d / 0.85f);
                    a *= a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Glow", fullRect: true);
        }

        private static Sprite BakeCircle(bool filled)
        {
            const int size = 32;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f;
            float r = c - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = filled
                        ? Mathf.Clamp01(r - d + 0.5f)
                        : Mathf.Clamp01(1.1f - Mathf.Abs(d - (r - 1f)) / 1.6f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, filled ? "Lab_Circle" : "Lab_Ring");
        }

        private static Sprite BakeBorder()
        {
            const int s = 8;
            var tex = NewTex(s, s);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x == 0 || y == 0 || x == s - 1 || y == s - 1)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Border", fullRect: true, border: new Vector4(2, 2, 2, 2));
        }

        private static Sprite BakeBrassKnob()
        {
            Color hi = new(0.847f, 0.702f, 0.416f); // #D8B36A
            Color lo = new(0.541f, 0.420f, 0.200f); // #8A6B33
            const int size = 24;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float c = size * 0.5f;
            float r = c - 1.5f;
            // Off-centre highlight like the CSS "circle at 35% 30%".
            float hx = size * 0.35f, hy = size * 0.70f;
            float span = size * 0.9f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float hdx = x + 0.5f - hx, hdy = y + 0.5f - hy;
                    float t = Mathf.Clamp01(Mathf.Sqrt(hdx * hdx + hdy * hdy) / span / 0.7f);
                    Color col = Color.Lerp(hi, lo, t);
                    col.a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_BrassKnob");
        }

        private static Sprite BakeCork()
        {
            Color l = new(0.420f, 0.322f, 0.149f); // #6B5226
            Color m = new(0.541f, 0.420f, 0.200f); // #8A6B33
            Color r = new(0.361f, 0.271f, 0.125f); // #5C4520
            Color rim = new(0.227f, 0.180f, 0.078f); // #3A2E14
            const int w = 36, h = 20;
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    float t = (x + 0.5f) / w;
                    Color col = edge ? rim
                        : t < 0.45f ? Color.Lerp(l, m, t / 0.45f)
                                    : Color.Lerp(m, r, (t - 0.45f) / 0.55f);
                    col.a = 1f;
                    px[y * w + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Cork", fullRect: true);
        }

        private static Sprite BakeTicks()
        {
            // 1px line at the left edge of each 10% band (CSS background-size: 10%).
            const int w = 200, h = 4;
            var tex = NewTex(w, h);
            tex.filterMode = FilterMode.Point;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = (x % 20 == 0)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_Ticks", fullRect: true);
        }

        private static Sprite BakeFadeV()
        {
            const int w = 8, h = 64;
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                byte a = (byte)(255f * (y + 0.5f) / h); // 0 bottom → 255 top
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_FadeV", fullRect: true);
        }

        /// <summary>Test tube: straight sides, hemispherical bottom, open flat top.
        /// Proportions match the handoff's 36px-wide, 18px-bottom-radius tube.</summary>
        private static Sprite BakeTube(bool edgeOnly)
        {
            const int w = 40, h = 220;
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            float cx = w * 0.5f;
            float halfW = w * 0.5f - 1.5f;
            float bottomCy = halfW + 1.5f; // circle centre of the round bottom (y up from 0)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    // Signed distance to the tube silhouette (negative = inside).
                    float d;
                    if (fy >= bottomCy)
                        d = Mathf.Abs(fx - cx) - halfW;                       // straight walls
                    else
                    {
                        float dx = fx - cx, dy = fy - bottomCy;
                        d = Mathf.Sqrt(dx * dx + dy * dy) - halfW;            // round bottom
                    }
                    float a;
                    if (edgeOnly)
                    {
                        a = Mathf.Clamp01(1.2f - Mathf.Abs(d + 1f) / 1.8f);   // ~2px wall
                        if (fy > h - 2f) a = 0f;                              // open top
                    }
                    else
                    {
                        a = Mathf.Clamp01(-d + 0.5f);                         // filled
                    }
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, edgeOnly ? "Lab_TubeOutline" : "Lab_TubeFill");
        }

        /// <summary>Fog bank: a dozen overlapping soft blobs summed, then a
        /// global edge feather so instances blend seamlessly while drifting.</summary>
        private static Sprite BakeFog(string name, int seed)
        {
            const int w = 192, h = 96;
            var rng = new System.Random(seed);
            const int blobs = 9;
            var bx = new float[blobs]; var by = new float[blobs];
            var br = new float[blobs]; var ba = new float[blobs];
            for (int i = 0; i < blobs; i++)
            {
                bx[i] = 0.1f + 0.8f * (float)rng.NextDouble();
                by[i] = 0.25f + 0.5f * (float)rng.NextDouble();
                // Wide, weak clumps that overlap into a continuous bank
                // instead of reading as separate bokeh dots.
                br[i] = 0.28f + 0.30f * (float)rng.NextDouble();
                ba[i] = 0.20f + 0.30f * (float)rng.NextDouble();
            }
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = (x + 0.5f) / w, fy = (y + 0.5f) / h;
                    float a = 0f;
                    for (int i = 0; i < blobs; i++)
                    {
                        // Flatten vertically (×2.2) so clumps smear into
                        // horizontal wisps rather than circles.
                        float dx = (fx - bx[i]) * (w / (float)h) * 0.75f;
                        float dy = (fy - by[i]) * 2.2f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / br[i];
                        if (d < 1f) { float f = 1f - d; a += ba[i] * f * f; }
                    }
                    // Feather to zero at the sprite edge in both axes.
                    float ex = Mathf.Clamp01(Mathf.Min(fx, 1f - fx) / 0.18f);
                    float ey = Mathf.Clamp01(Mathf.Min(fy, 1f - fy) / 0.25f);
                    a = Mathf.Clamp01(a) * ex * ex * ey * ey;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, name, fullRect: true);
        }

        private static Sprite BakeMiniVial()
        {
            const int w = 16, h = 20;
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    // Rounded rect, radius 3 top / 5 bottom (y=0 is the bottom).
                    float r = fy < h * 0.5f ? 5f : 3f;
                    float qx = Mathf.Max(Mathf.Abs(fx - w * 0.5f) - (w * 0.5f - r - 0.5f), 0f);
                    float qy = Mathf.Max(Mathf.Abs(fy - h * 0.5f) - (h * 0.5f - r - 0.5f), 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - r;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(-d + 0.5f) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return ToSprite(tex, "Lab_MiniVial");
        }
    }
}
