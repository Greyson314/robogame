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
    /// Font: Yuji Syuku via <see cref="InkKit.Display"/> — the single UI
    /// face of the inventor + painter direction (labels, numerals,
    /// hotkeys). Cached as a static; the null-check tolerates domain
    /// reload because Unity's fake-null reports destroyed fonts as null.
    /// </para>
    /// </remarks>
    public static class HudStyles
    {
        // -----------------------------------------------------------------
        // Palette — every HUD-visible colour goes through one of these.
        // -----------------------------------------------------------------

        // TRACE[DOC:research/ui-design-handoff]: "inventor + painter" palette
        // — ink on paper, indigo wash, rationed vermilion. Values are the
        // handoff's design tokens verbatim.

        /// <summary>Primary text — near-black ink (#2E2820). HUD panels supply a paper backing.</summary>
        public static readonly Color TextPrimary = new(0.180f, 0.157f, 0.125f, 1f);

        /// <summary>Faded ink (#6E6350) for annotations / secondary labels.</summary>
        public static readonly Color TextMuted = new(0.431f, 0.388f, 0.314f, 1f);

        /// <summary>Project accent — indigo wash (#4A6E7E). Player team, secondary state colour.</summary>
        public static readonly Color Accent = new(0.290f, 0.431f, 0.494f, 1f);

        /// <summary>Cautious burnt ochre — "you're taking damage but not in immediate danger."</summary>
        public static readonly Color Warning = new(0.659f, 0.455f, 0.165f, 1f);

        /// <summary>Vermilion (#C33D1F) — enemy team, low HP, timer expiring. STRICTLY RATIONED: small marks only, never large fills.</summary>
        public static readonly Color Danger = new(0.765f, 0.239f, 0.122f, 1f);

        /// <summary>Healthy moss-ink green — HP bar full, friendly side annotations.</summary>
        public static readonly Color Healthy = new(0.290f, 0.431f, 0.322f, 1f);

        /// <summary>Standard panel backing — translucent paper, not black. "Never a solid box."</summary>
        public static readonly Color PanelBg = new(0.965f, 0.941f, 0.878f, 0.72f);

        /// <summary>Heavier paper backing for prominent panels (scoreboard).</summary>
        public static readonly Color PanelBgHeavy = new(0.945f, 0.914f, 0.831f, 0.90f);

        /// <summary>Thin rule at panel edges — frame-line ink, rgba(46,40,32,0.5).</summary>
        public static readonly Color PanelEdge = new(0.180f, 0.157f, 0.125f, 0.5f);

        /// <summary>Solid ink (#26211A) — brush fills, primary buttons.</summary>
        public static readonly Color Ink = new(0.149f, 0.129f, 0.102f, 1f);

        /// <summary>Cream text (#F1E9D4) — the only text colour used on ink surfaces.</summary>
        public static readonly Color CreamText = new(0.945f, 0.914f, 0.831f, 1f);

        // -----------------------------------------------------------------
        // Hex-string colour tags for rich-text <color=...> usage.
        // -----------------------------------------------------------------

        public const string TagAccent = "#4A6E7E";
        public const string TagDanger = "#C33D1F";
        public const string TagMuted  = "#6E6350";
        public const string TagHealthy = "#4A6E52";

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
                // TRACE[DOC:research/ui-design-handoff]: Yuji Syuku is THE UI
                // face (labels, numerals, hotkeys). The old OS-monospace stack
                // traded style for stable readout columns; the design accepts
                // proportional digits. InkKit falls back to LegacyRuntime.ttf
                // if the TTF is missing, so this never returns null.
                s_font = InkKit.Display;
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
