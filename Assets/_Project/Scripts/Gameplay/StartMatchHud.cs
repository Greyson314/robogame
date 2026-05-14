using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Non-blocking warmup prompt. While <see cref="MatchState.WarmingUp"/>
    /// is active, draws a small "Press [KEY] to begin combat" label at the
    /// bottom-centre of the screen. The player can fly / drive freely the
    /// whole time — bots are passive, the cursor stays locked, and there's
    /// no modal overlay to click through. The actual key handling lives on
    /// <see cref="ArenaController"/> (matching the <c>_respawnKey</c>
    /// pattern); this component only displays the prompt.
    /// </summary>
    /// <remarks>
    /// IMGUI to match the rest of the in-arena overlays
    /// (<c>VehicleStatsHud</c>, <c>HitMarkerOverlay</c>, etc). Hides itself
    /// the moment the match transitions to InProgress; cost when hidden is
    /// one bool check per IMGUI event.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StartMatchHud : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Pill background — defaults match HudStyles.PanelBg.")]
        [SerializeField] private Color _bgColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField, Min(8)] private int _fontSize = 18;

        [Header("Layout")]
        [Tooltip("Pixels from the bottom edge of the screen to the prompt.")]
        [SerializeField, Min(8f)] private float _bottomMargin = 80f;

        [Tooltip("Padding inside the prompt's background pill.")]
        [SerializeField, Min(4f)] private float _padding = 12f;

        // -----------------------------------------------------------------
        // Cached
        // -----------------------------------------------------------------

        private MatchController _match;
        private string _keyName = "SPACE";
        private GUIStyle _labelStyle;
        private string _renderedText = "Press [SPACE] to begin combat";
        private string _renderedRich  = "Press [SPACE] to begin combat";
        private GUIContent _content;

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>True while the prompt is visible.</summary>
        public bool IsVisible => _match != null && _match.State == MatchState.WarmingUp;

        /// <summary>Bind to a controller. Idempotent.</summary>
        public void BindMatch(MatchController match)
        {
            _match = match;
        }

        /// <summary>
        /// Update the displayed key name (called by <see cref="ArenaController"/>
        /// at bind time so the prompt always matches the actual hotkey
        /// configured on the controller).
        /// </summary>
        public void SetKeyName(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) keyName = "SPACE";
            if (_keyName == keyName) return;
            _keyName = keyName;
            _renderedText = $"Press [{_keyName}] to begin combat";
            // Pre-bake the rich-text variant so CalcSize measures what
            // we actually draw — not the plain string. Without this the
            // pill is sized to the unbolded text, then drawn with the
            // wider <b>…</b> wrap and overruns the right edge.
            _renderedRich = _renderedText.Replace(
                $"[{_keyName}]",
                $"<b><color={HudStyles.TagAccent}>[{_keyName}]</color></b>");
            _content = null; // force GUIContent rebuild on next OnGUI
        }

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsVisible) return;
            EnsureStyles();

            // Measure once per content change. Measure the rich-text
            // string (the one we draw), not the raw — bold + colour
            // tags expand under the bold style and a plain-text CalcSize
            // sizes the pill too tight, clipping the right side.
            if (_content == null) _content = new GUIContent(_renderedRich);
            Vector2 size = _labelStyle.CalcSize(_content);
            // Generous horizontal padding — the monospace font's CalcSize
            // occasionally underestimates dynamic glyph widths the first
            // few frames after the font is built. 2.4× pad on x keeps
            // the prompt comfortably inside the pill in every case.
            float w = size.x + _padding * 2.4f;
            float h = size.y + _padding * 1.4f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - _bottomMargin - h;

            Color prev = GUI.color;
            // Pill background + accent edge so it reads as a sibling of
            // the scoreboard / stats panels.
            GUI.color = _bgColor;
            GUI.DrawTexture(new Rect(x, y, w, h), HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, w, 2f), HudStyles.Pixel);
            GUI.color = prev;

            // Label fills the pill horizontally (centred via the style's
            // MiddleCenter anchor) so the centre-of-pill stays the
            // centre-of-text even if CalcSize rounds.
            GUI.Label(new Rect(x, y, w, h), _renderedRich, _labelStyle);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = HudStyles.Bold(_fontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
        }
    }
}
