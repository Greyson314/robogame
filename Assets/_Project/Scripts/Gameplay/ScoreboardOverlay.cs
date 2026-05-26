using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Tab-held scoreboard. While the player holds the scoreboard key
    /// (Tab by default), draws a centred overlay listing per-side match
    /// state: scrap totals, frag counts, player lives remaining, time
    /// remaining. Reads <see cref="MatchController"/> directly — no
    /// caching, since the overlay only renders while held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auto-attached to the camera by
    /// <see cref="ArenaController.ConfigureCamera"/>. SP-only today;
    /// MP will replace the per-side aggregates with a per-player
    /// <c>NetworkList&lt;ScoreboardEntry&gt;</c> driven by a future
    /// <c>NetworkScoreboard</c> sibling. The overlay itself is unchanged
    /// — it reads from <see cref="MatchController"/>, which the networked
    /// sibling will write through.
    /// </para>
    /// <para>
    /// The Input System binding is direct (Keyboard.current.tabKey) to
    /// avoid pulling another <c>InputActionAsset</c> dependency through
    /// the gameplay asmdef for a single hotkey. <see cref="StartMatchHud"/>
    /// follows the same pattern for the backtick start-match key.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScoreboardOverlay : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Key the player holds to show the scoreboard. Tab by default; " +
                 "configurable per project preference.")]
        [SerializeField] private Key _holdKey = Key.Tab;

        [Header("Layout")]
        [Tooltip("Panel width in pixels.")]
        [SerializeField, Min(240f)] private float _panelWidth = 520f;

        [Tooltip("Panel height in pixels.")]
        [SerializeField, Min(120f)] private float _panelHeight = 240f;

        [Header("Look")]
        [SerializeField, Min(8)] private int _headerFontSize = 22;
        [SerializeField, Min(8)] private int _rowFontSize = 16;

        private MatchController _match;
        private GUIStyle _headerStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _rowRightStyle;
        private bool _stylesBuilt;

        public void BindMatch(MatchController match) => _match = match;

        private void OnGUI()
        {
            if (_match == null) return;
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb[_holdKey].isPressed) return;

            EnsureStyles();

            float x = (Screen.width - _panelWidth) * 0.5f;
            float y = (Screen.height - _panelHeight) * 0.5f;
            Rect panel = new Rect(x, y, _panelWidth, _panelHeight);

            Color saved = GUI.color;
            GUI.color = HudStyles.PanelBgHeavy;
            GUI.DrawTexture(panel, HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);
            GUI.color = saved;

            // Header.
            Rect headerRect = new Rect(x, y + 6f, _panelWidth, _headerFontSize + 8f);
            GUI.Label(headerRect, "MATCH STATUS", _headerStyle);

            // Two-column body: YOU left, ENEMY right. Each shows
            // SCRAP / FRAGS / LIVES (player only).
            float colTop = y + 6f + _headerFontSize + 16f;
            float rowH = _rowFontSize + 8f;
            float colW = _panelWidth * 0.5f - 30f;

            float playerScrap = _match.ScoreForSide(MatchSide.Player);
            float enemyScrap = _match.ScoreForSide(MatchSide.Enemy);
            int playerKills = _match.KillsForSide(MatchSide.Player);
            int enemyKills = _match.KillsForSide(MatchSide.Enemy);
            int target = _match.TargetTeamScrap;
            int lives = _match.PlayerLivesRemaining;
            float secs = _match.TimeRemaining;

            // YOU column.
            float yL = colTop;
            DrawRow(x + 24f, yL, colW, "YOU", "", _rowStyle, _rowRightStyle); yL += rowH;
            DrawRow(x + 24f, yL, colW, "SCRAP",  $"{playerScrap} / {target}", _rowStyle, _rowRightStyle); yL += rowH;
            DrawRow(x + 24f, yL, colW, "FRAGS",  playerKills.ToString(),       _rowStyle, _rowRightStyle); yL += rowH;
            DrawRow(x + 24f, yL, colW, "LIVES",  lives.ToString(),             _rowStyle, _rowRightStyle);

            // ENEMY column.
            float yR = colTop;
            float rx = x + _panelWidth * 0.5f + 6f;
            DrawRow(rx, yR, colW, "ENEMY", "", _rowStyle, _rowRightStyle); yR += rowH;
            DrawRow(rx, yR, colW, "SCRAP", $"{enemyScrap} / {target}", _rowStyle, _rowRightStyle); yR += rowH;
            DrawRow(rx, yR, colW, "FRAGS", enemyKills.ToString(),       _rowStyle, _rowRightStyle); yR += rowH;
            DrawRow(rx, yR, colW, "LIVES", "—",                          _rowStyle, _rowRightStyle);

            // Timer row spans the full width at the bottom.
            int mm = Mathf.Max(0, Mathf.CeilToInt(secs) / 60);
            int ss = Mathf.Max(0, Mathf.CeilToInt(secs) % 60);
            string timer = $"TIME REMAINING  {mm}:{(ss < 10 ? "0" : "")}{ss}";
            Rect timerRect = new Rect(x, y + _panelHeight - rowH - 8f, _panelWidth, rowH);
            GUIStyle centred = new GUIStyle(_rowStyle) { alignment = TextAnchor.MiddleCenter };
            centred.normal.textColor = HudStyles.TextMuted;
            GUI.Label(timerRect, timer, centred);
        }

        private static void DrawRow(float x, float y, float w, string label, string value, GUIStyle leftStyle, GUIStyle rightStyle)
        {
            float h = leftStyle.fontSize + 8f;
            Rect labelRect = new Rect(x, y, w * 0.5f, h);
            Rect valueRect = new Rect(x + w * 0.5f, y, w * 0.5f, h);
            GUI.Label(labelRect, label, leftStyle);
            GUI.Label(valueRect, value, rightStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;
            _headerStyle = HudStyles.Bold(_headerFontSize, HudStyles.Accent, TextAnchor.MiddleCenter);
            _rowStyle = HudStyles.Bold(_rowFontSize, HudStyles.TextPrimary, TextAnchor.MiddleLeft);
            _rowRightStyle = new GUIStyle(_rowStyle) { alignment = TextAnchor.MiddleRight };
            _rowRightStyle.normal.textColor = HudStyles.TextPrimary;
        }
    }
}
