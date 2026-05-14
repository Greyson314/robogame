using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Single source of truth for in-game IMGUI styling: shared dynamic
    /// font, named palette colours, and palette-coloured GUIStyle factory
    /// helpers. Lives in <c>Robogame.Core</c> so every HUD asmdef (UI,
    /// Player, Gameplay) can reference it without circular deps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this exists: the HUD grew organically across many sessions,
    /// each new overlay picking its own font / colour / size by hand.
    /// Result: SPD readouts in 18 pt bold while the scoreboard's score
    /// line is 20 pt regular; "danger" rendered as both #D93333 and
    /// #B82020 in different files; etc. This class collapses every
    /// recurring choice into a named token so a one-line edit here
    /// re-skins the whole HUD.
    /// </para>
    /// <para>
    /// Font: a dynamic OS-resolved monospace stack (Consolas → Menlo →
    /// DejaVu Sans Mono → Courier New). Monospace gives stable column
    /// alignment for changing readouts (SPD 7.3 m/s ↔ 12.6 m/s doesn't
    /// shift the suffix horizontally). The first font available on the
    /// running platform wins; Windows always has Consolas, macOS has
    /// Menlo, Linux distros typically ship DejaVu. Cached as a static
    /// because <c>Font.CreateDynamicFontFromOSFont</c> isn't free.
    /// </para>
    /// </remarks>
    public static class HudStyles
    {
        // -----------------------------------------------------------------
        // Palette — every HUD-visible colour goes through one of these.
        // -----------------------------------------------------------------

        /// <summary>Off-white body text. Reads as "system text" against any background.</summary>
        public static readonly Color TextPrimary = new(0.95f, 0.97f, 1f, 1f);

        /// <summary>Muted slate for secondary text (labels above readouts).</summary>
        public static readonly Color TextMuted = new(0.65f, 0.72f, 0.80f, 1f);

        /// <summary>Project accent — hazard orange. Player team, primary action colour.</summary>
        public static readonly Color Accent = new(0.95f, 0.55f, 0.10f, 1f);

        /// <summary>Cautious yellow — "you're taking damage but not in immediate danger."</summary>
        public static readonly Color Warning = new(0.92f, 0.74f, 0.20f, 1f);

        /// <summary>Alert red — enemy team, low HP, scrap-cap full, timer expiring.</summary>
        public static readonly Color Danger = new(0.85f, 0.20f, 0.20f, 1f);

        /// <summary>Healthy green — HP bar full, friendly side annotations.</summary>
        public static readonly Color Healthy = new(0.20f, 0.65f, 0.35f, 1f);

        /// <summary>Standard semi-opaque panel background.</summary>
        public static readonly Color PanelBg = new(0f, 0f, 0f, 0.55f);

        /// <summary>Slightly heavier panel background for prominent panels (scoreboard).</summary>
        public static readonly Color PanelBgHeavy = new(0f, 0f, 0f, 0.7f);

        /// <summary>Thin highlight line — sits at panel top edges to imply chrome.</summary>
        public static readonly Color PanelEdge = new(0.95f, 0.55f, 0.10f, 0.55f);

        // -----------------------------------------------------------------
        // Hex-string colour tags for rich-text <color=...> usage.
        // -----------------------------------------------------------------

        public const string TagAccent = "#F28C1A";
        public const string TagDanger = "#D93333";
        public const string TagMuted  = "#A5B2C0";
        public const string TagHealthy = "#34A655";

        // -----------------------------------------------------------------
        // Shared font
        // -----------------------------------------------------------------

        private static Font s_font;

        /// <summary>
        /// Monospace dynamic font shared by every HUD. Lazy-built on first
        /// access (Unity won't let you call <c>CreateDynamicFontFromOSFont</c>
        /// from a static initialiser inside the Editor's domain reload).
        /// </summary>
        public static Font Font
        {
            get
            {
                if (s_font != null) return s_font;
                string[] candidates =
                {
                    "Consolas",
                    "Menlo",
                    "DejaVu Sans Mono",
                    "Courier New",
                };
                // The string[] overload picks the first OS-available font.
                // 16 here is just a baseline — GUIStyles override fontSize
                // per-style at render time.
                s_font = Font.CreateDynamicFontFromOSFont(candidates, 16);
                return s_font;
            }
        }

        // -----------------------------------------------------------------
        // GUIStyle helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a regular HUD label style at the requested point size +
        /// colour. Uses the shared monospace font, rich-text enabled (so
        /// callers can drop <c>&lt;color=...&gt;</c> tags), and a clear
        /// alignment hint defaulting to MiddleLeft.
        /// </summary>
        public static GUIStyle Label(int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            GUIStyle s = new(GUI.skin.label)
            {
                font = Font,
                fontSize = fontSize,
                fontStyle = style,
                alignment = anchor,
                richText = true,
            };
            s.normal.textColor = color;
            return s;
        }

        /// <summary>Bold label — the project's primary readout style.</summary>
        public static GUIStyle Bold(int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
            => Label(fontSize, color, anchor, FontStyle.Bold);

        /// <summary>
        /// 1×1 white texture cached so HUDs can paint solid rects /
        /// backgrounds without each script holding its own copy. Same
        /// instance every call; consumers tint via <see cref="GUI.color"/>
        /// or <see cref="GUI.contentColor"/>.
        /// </summary>
        public static Texture2D Pixel => Texture2D.whiteTexture;
    }
}
