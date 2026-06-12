using System.Text;
using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Tab-held scoreboard. While the player holds the scoreboard key
    /// (Tab by default), draws a centred overlay with one row per
    /// combatant — kills / deaths / damage dealt / scrap banked — grouped
    /// YOU-side over ENEMY-side, plus the round timer and player lives on
    /// a footer line. Per-combatant data comes from
    /// <see cref="MatchStatsTracker"/>; team aggregates from
    /// <see cref="MatchController"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auto-attached to the camera by <c>ArenaController.BindFollowCamera</c>.
    /// SP-only today; MP will feed the same tracker from a
    /// server-replicated stats list — the overlay reads rows, it doesn't
    /// care who writes them.
    /// </para>
    /// <para>
    /// The Input System binding is direct (Keyboard.current[key]) to
    /// avoid pulling another <c>InputActionAsset</c> dependency through
    /// the gameplay asmdef for a single hotkey. <see cref="StartMatchHud"/>
    /// follows the same pattern for the backtick start-match key.
    /// </para>
    /// <para>
    /// Row strings are cached against <see cref="MatchStatsTracker.Version"/>
    /// so held-Tab repaints don't allocate (invariant § 6 — OnGUI runs
    /// multiple times per frame).
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
        [SerializeField, Min(320f)] private float _panelWidth = 560f;

        [Header("Look")]
        [SerializeField, Min(8)] private int _headerFontSize = 22;
        [SerializeField, Min(8)] private int _rowFontSize = 15;

        // Fixed column widths (px). NAME takes the remainder.
        private const float ColK = 52f;
        private const float ColD = 52f;
        private const float ColDmg = 76f;
        private const float ColScrap = 76f;
        private const float PadX = 22f;
        private const float RowH = 24f;
        private const float TeamGap = 10f;

        private MatchController _match;
        private MatchStatsTracker _stats;

        private GUIStyle _headerStyle;
        private GUIStyle _colHeadStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _numStyle;
        private GUIStyle _footerStyle;
        private bool _stylesBuilt;
        private bool _wasHeld;

        // Cached per-row render strings, index-parallel to _stats.Rows.
        // Rebuilt only when the tracker's Version moves.
        private int _renderedVersion = -1;
        private readonly StringBuilder _scratch = new(32);
        private string[] _rowKills = System.Array.Empty<string>();
        private string[] _rowDeaths = System.Array.Empty<string>();
        private string[] _rowDamage = System.Array.Empty<string>();
        private string[] _rowScrap = System.Array.Empty<string>();

        // Footer (timer + lives) caches, rebuilt when the displayed second
        // or life count changes.
        private string _renderedFooter = "";
        private int _lastFooterSecs = -1;
        private int _lastFooterLives = -1;

        /// <summary>Bind both data sources. Preferred over scene scans.</summary>
        public void Bind(MatchController match, MatchStatsTracker stats)
        {
            _match = match;
            _stats = stats;
            _renderedVersion = -1;
        }

        /// <summary>Legacy bind (team aggregates only). Kept so older call sites compile; prefer <see cref="Bind"/>.</summary>
        public void BindMatch(MatchController match) => _match = match;

        private void Update()
        {
            // Open tick handled here (not OnGUI) so the cue fires once per
            // hold, not once per IMGUI event.
            Keyboard kb = Keyboard.current;
            bool held = kb != null && kb[_holdKey].isPressed;
            if (held && !_wasHeld) AudioRouter.PlayUI(AudioCue.UiHover);
            _wasHeld = held;
        }

        private void OnGUI()
        {
            if (!_wasHeld || _match == null) return;

            EnsureStyles();
            RefreshRowStrings();
            RefreshFooter();

            int rowCount = _stats != null ? _stats.Rows.Count : 0;
            float headerH = _headerFontSize + 14f;
            float colHeadH = 20f;
            float footerH = _rowFontSize + 12f;
            float panelH = headerH + colHeadH + rowCount * RowH + TeamGap + footerH + 18f;

            float x = (Screen.width - _panelWidth) * 0.5f;
            float y = (Screen.height - panelH) * 0.40f;

            Color saved = GUI.color;
            GUI.color = HudStyles.PanelBgHeavy;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, panelH), HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);
            GUI.color = saved;

            float cy = y + 6f;
            GUI.Label(new Rect(x, cy, _panelWidth, headerH - 6f), "SCOREBOARD", _headerStyle);
            cy += headerH;

            // Column headers (numbers right-aligned over their columns).
            DrawRow(x, cy, colHeadH, "", "K", "D", "DMG", "SCRAP", _colHeadStyle, _colHeadStyle);
            cy += colHeadH;

            if (_stats != null)
            {
                cy = DrawSide(MatchSide.Player, x, cy);
                cy += TeamGap;
                cy = DrawSide(MatchSide.Enemy, x, cy);
            }

            // Footer: round timer + lives, centred, muted.
            GUI.Label(new Rect(x, y + panelH - footerH - 6f, _panelWidth, footerH),
                _renderedFooter, _footerStyle);
        }

        private float DrawSide(MatchSide side, float x, float cy)
        {
            var rows = _stats.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                CombatantStats row = rows[i];
                if (row.Side != side) continue;

                // Team colour for the name; dead rows dim to muted.
                Color nameColor = !row.Alive ? HudStyles.TextMuted
                                : side == MatchSide.Player ? HudStyles.Accent
                                : HudStyles.Danger;
                _nameStyle.normal.textColor = nameColor;
                _nameStyle.fontStyle = row.IsPlayer ? FontStyle.Bold : FontStyle.Normal;
                _numStyle.normal.textColor = row.Alive ? HudStyles.TextPrimary : HudStyles.TextMuted;

                DrawRow(x, cy, RowH, row.DisplayName, _rowKills[i], _rowDeaths[i],
                    _rowDamage[i], _rowScrap[i], _nameStyle, _numStyle);
                cy += RowH;
            }
            return cy;
        }

        private void DrawRow(float x, float cy, float h, string name, string k, string d,
            string dmg, string scrap, GUIStyle nameStyle, GUIStyle numStyle)
        {
            float right = x + _panelWidth - PadX;
            float scrapX = right - ColScrap;
            float dmgX = scrapX - ColDmg;
            float deathsX = dmgX - ColD;
            float killsX = deathsX - ColK;

            GUI.Label(new Rect(x + PadX, cy, killsX - (x + PadX), h), name, nameStyle);
            GUI.Label(new Rect(killsX, cy, ColK, h), k, numStyle);
            GUI.Label(new Rect(deathsX, cy, ColD, h), d, numStyle);
            GUI.Label(new Rect(dmgX, cy, ColDmg, h), dmg, numStyle);
            GUI.Label(new Rect(scrapX, cy, ColScrap, h), scrap, numStyle);
        }

        private void RefreshRowStrings()
        {
            if (_stats == null || _stats.Version == _renderedVersion) return;
            _renderedVersion = _stats.Version;

            var rows = _stats.Rows;
            if (_rowKills.Length < rows.Count)
            {
                _rowKills = new string[rows.Count];
                _rowDeaths = new string[rows.Count];
                _rowDamage = new string[rows.Count];
                _rowScrap = new string[rows.Count];
            }

            for (int i = 0; i < rows.Count; i++)
            {
                CombatantStats row = rows[i];
                _rowKills[i] = FormatInt(row.Kills);
                _rowDeaths[i] = FormatInt(row.Deaths);
                _rowDamage[i] = FormatInt(Mathf.RoundToInt(row.DamageDealt));
                _rowScrap[i] = FormatInt(row.ScrapDeposited);
            }
        }

        private void RefreshFooter()
        {
            int secs = Mathf.Max(0, Mathf.CeilToInt(_match.TimeRemaining));
            int lives = _match.PlayerLivesRemaining;
            if (secs == _lastFooterSecs && lives == _lastFooterLives) return;
            _lastFooterSecs = secs;
            _lastFooterLives = lives;

            _scratch.Clear();
            _scratch.Append("TIME  ").Append(secs / 60).Append(':');
            int ss = secs % 60;
            if (ss < 10) _scratch.Append('0');
            _scratch.Append(ss).Append("      LIVES  ").Append(lives);
            _renderedFooter = _scratch.ToString();
        }

        private string FormatInt(int value)
        {
            _scratch.Clear();
            _scratch.Append(value);
            return _scratch.ToString();
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;
            _headerStyle = HudStyles.Bold(_headerFontSize, HudStyles.Accent, TextAnchor.MiddleCenter);
            _colHeadStyle = HudStyles.Label(12, HudStyles.TextMuted, TextAnchor.MiddleRight, FontStyle.Bold);
            _nameStyle = HudStyles.Label(_rowFontSize, HudStyles.TextPrimary, TextAnchor.MiddleLeft);
            _numStyle = HudStyles.Label(_rowFontSize, HudStyles.TextPrimary, TextAnchor.MiddleRight);
            _footerStyle = HudStyles.Label(_rowFontSize, HudStyles.TextMuted, TextAnchor.MiddleCenter);
        }
    }
}
