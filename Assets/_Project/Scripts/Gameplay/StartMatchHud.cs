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
        [SerializeField] private Color _bgColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _textColor = new Color(0.95f, 0.97f, 1f, 0.95f);
        [SerializeField] private Color _accentColor = new Color(0.95f, 0.55f, 0.10f, 1f); // hazard orange (key name)
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
            _content = null; // force GUIContent rebuild on next OnGUI
        }

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsVisible) return;
            EnsureStyles();

            // Measure once per content change, not per IMGUI event.
            if (_content == null) _content = new GUIContent(_renderedText);
            Vector2 size = _labelStyle.CalcSize(_content);
            float w = size.x + _padding * 2f;
            float h = size.y + _padding * 1.2f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - _bottomMargin - h;

            Color prev = GUI.color;
            // Pill background.
            GUI.color = _bgColor;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = prev;

            // Label, with the key name accented. Easiest path is two passes:
            // base text in normal colour, then re-draw with rich-text
            // colouring. We use a single rich-text label to keep it cheap.
            string rich = _renderedText.Replace($"[{_keyName}]",
                $"<b><color=#{ColorUtility.ToHtmlStringRGB(_accentColor)}>[{_keyName}]</color></b>");
            GUI.Label(new Rect(x + _padding, y + _padding * 0.6f, size.x, size.y), rich, _labelStyle);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _labelStyle.normal.textColor = _textColor;
        }
    }
}
