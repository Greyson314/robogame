using System.Text;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Top-centre objective HUD: HP bar, kill counter, round timer. Reads
    /// directly from <see cref="MatchController"/> for score / timer / state
    /// and from the local <see cref="Robot"/> (via the sibling
    /// <see cref="FollowCamera"/>) for HP. IMGUI to match
    /// <see cref="VehicleStatsHud"/> / <see cref="HitMarkerOverlay"/>; UI
    /// Toolkit is the longer-term destination for all in-game HUD.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in <c>Robogame.Gameplay</c> rather than <c>Robogame.Player</c>
    /// because it pulls match state from <see cref="MatchController"/>;
    /// <c>Robogame.Player</c> sits at a lower asmdef tier than
    /// <c>Robogame.Gameplay</c> (see BEST_PRACTICES § 2.2), so the
    /// dependency has to flow through the higher-tier asmdef.
    /// </para>
    /// <para>
    /// Bind via <see cref="BindMatch"/> (preferred — explicit dependency,
    /// zero scene scans on the hot path). Falls back to a one-shot
    /// <c>FindFirstObjectByType&lt;ArenaController&gt;</c> in <see cref="Update"/>
    /// if nothing has bound yet, so the auto-add path in
    /// <c>ArenaController.BindLocalChassisHud</c> stays robust against
    /// component-add ordering surprises.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHud : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Pixels from the top edge to the panel.")]
        [SerializeField, Min(4f)] private float _topMargin = 18f;

        [Tooltip("Panel width in pixels.")]
        [SerializeField, Min(160f)] private float _panelWidth = 360f;

        [Tooltip("Panel height in pixels.")]
        [SerializeField, Min(40f)] private float _panelHeight = 72f;

        [Header("Look")]
        [SerializeField] private Color _bgColor = new Color(0f, 0f, 0f, 0.45f);
        [SerializeField] private Color _textColor = new Color(0.95f, 0.97f, 1f, 1f);
        [SerializeField] private Color _hpHealthyColor = new Color(0.20f, 0.65f, 0.35f, 1f);
        [SerializeField] private Color _hpHurtColor    = new Color(0.92f, 0.74f, 0.20f, 1f);
        [SerializeField] private Color _hpDangerColor  = new Color(0.75f, 0.25f, 0.20f, 1f);
        [SerializeField] private Color _timerNormalColor = new Color(0.95f, 0.97f, 1f, 1f);
        [SerializeField] private Color _timerLowColor    = new Color(0.92f, 0.30f, 0.20f, 1f);
        [SerializeField, Min(8)] private int _fontSize = 16;

        [Header("HP bar")]
        [Tooltip("Pixels of HP-bar height inside the panel.")]
        [SerializeField, Min(4f)] private float _hpBarHeight = 12f;

        [Tooltip("Health fraction below which the HP bar tint flips to alert.")]
        [SerializeField, Range(0f, 1f)] private float _hpAlertThreshold = 0.3f;

        [Tooltip("Health fraction below which the HP bar reads as 'hurt' (yellow tint).")]
        [SerializeField, Range(0f, 1f)] private float _hpHurtThreshold = 0.6f;

        [Tooltip("Seconds-remaining threshold below which the timer flips to alert red.")]
        [SerializeField, Min(0f)] private float _timerLowSeconds = 30f;

        // -----------------------------------------------------------------
        // Cached refs
        // -----------------------------------------------------------------

        private FollowCamera _follow;
        private Robot _robot;
        private Transform _boundChassis;
        private MatchController _match;

        // Reuse styles + buffers so OnGUI doesn't allocate per draw.
        private GUIStyle _labelStyle;
        private GUIStyle _bigLabelStyle;
        private readonly StringBuilder _scoreText = new StringBuilder(32);
        private readonly StringBuilder _timerText = new StringBuilder(16);
        private string _renderedScore = "0  —  0";
        private string _renderedTimer = "—:—";
        private int _lastPlayerScore = -1;
        private int _lastEnemyScore = -1;
        private int _lastTimerSecs = -1;

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>Bind the HUD to a controller. Preferred over scene scans.</summary>
        public void BindMatch(MatchController match) => _match = match;

        /// <summary>Diagnostic accessor — returns the player score the HUD is currently displaying.</summary>
        public int DisplayedPlayerScore => _lastPlayerScore < 0 ? 0 : _lastPlayerScore;

        /// <summary>Diagnostic accessor — returns the enemy score the HUD is currently displaying.</summary>
        public int DisplayedEnemyScore => _lastEnemyScore < 0 ? 0 : _lastEnemyScore;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            // Lazy-resolve the MatchController on the first frame nothing
            // has bound it. ArenaController.BindLocalChassisHud is the
            // canonical bind site; this fallback handles the edge case
            // where component-add ordering puts ObjectiveHud first.
            if (_match == null)
            {
#if UNITY_2023_1_OR_NEWER
                ArenaController arena = Object.FindFirstObjectByType<ArenaController>();
#else
                ArenaController arena = Object.FindObjectOfType<ArenaController>();
#endif
                if (arena != null) _match = arena.Match;
            }

            // Rebind chassis on respawn (Target identity changes).
            Transform t = _follow != null ? _follow.Target : null;
            if (t != _boundChassis)
            {
                _boundChassis = t;
                _robot = _boundChassis != null ? _boundChassis.GetComponent<Robot>() : null;
            }

            // Cache string builds: only rebuild when the displayed values
            // change. Avoids per-frame allocation on a steady-state tick.
            if (_match != null)
            {
                int p = _match.ScoreForSide(MatchSide.Player);
                int e = _match.ScoreForSide(MatchSide.Enemy);
                if (p != _lastPlayerScore || e != _lastEnemyScore)
                {
                    _lastPlayerScore = p;
                    _lastEnemyScore = e;
                    _scoreText.Clear();
                    _scoreText.Append(p).Append("  —  ").Append(e);
                    _renderedScore = _scoreText.ToString();
                }
                int secs = Mathf.CeilToInt(_match.TimeRemaining);
                if (secs != _lastTimerSecs)
                {
                    _lastTimerSecs = secs;
                    _timerText.Clear();
                    int mm = Mathf.Max(0, secs / 60);
                    int ss = Mathf.Max(0, secs % 60);
                    _timerText.Append(mm).Append(':');
                    if (ss < 10) _timerText.Append('0');
                    _timerText.Append(ss);
                    _renderedTimer = _timerText.ToString();
                }
            }
        }

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (_match == null) return;

            EnsureStyles();

            float hpFraction = ResolveHpFraction();

            float x = (Screen.width - _panelWidth) * 0.5f;
            float y = _topMargin;

            // Panel background.
            Color prev = GUI.color;
            GUI.color = _bgColor;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, _panelHeight), Texture2D.whiteTexture);
            GUI.color = prev;

            // HP bar — full width, top of panel.
            float hpBarMargin = 8f;
            float hpBarTop = y + 6f;
            float hpBarFullWidth = _panelWidth - hpBarMargin * 2f;
            // Background rail.
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(x + hpBarMargin, hpBarTop, hpBarFullWidth, _hpBarHeight), Texture2D.whiteTexture);
            // Fill.
            GUI.color = ResolveHpColor(hpFraction);
            GUI.DrawTexture(new Rect(x + hpBarMargin, hpBarTop, hpBarFullWidth * Mathf.Clamp01(hpFraction), _hpBarHeight), Texture2D.whiteTexture);
            GUI.color = prev;

            // Score line — centred under the HP bar.
            Rect scoreRect = new Rect(x, hpBarTop + _hpBarHeight + 6f, _panelWidth, _fontSize + 8f);
            GUI.Label(scoreRect, _renderedScore, _bigLabelStyle);

            // Timer line — under the score, tinted alert when under threshold.
            float timerSecsRemaining = _match.TimeRemaining;
            bool timerAlert = _match.State == MatchState.InProgress && timerSecsRemaining > 0f && timerSecsRemaining < _timerLowSeconds;
            GUI.color = timerAlert ? _timerLowColor : _timerNormalColor;
            Rect timerRect = new Rect(x, scoreRect.y + scoreRect.height + 2f, _panelWidth, _fontSize + 4f);
            GUI.Label(timerRect, _renderedTimer, _labelStyle);
            GUI.color = prev;
        }

        private float ResolveHpFraction()
        {
            if (_robot == null || _robot.InitialBlockCount <= 0) return 0f;
            return Mathf.Clamp01((float)_robot.BlockCount / _robot.InitialBlockCount);
        }

        private Color ResolveHpColor(float fraction)
        {
            if (fraction < _hpAlertThreshold) return _hpDangerColor;
            if (fraction < _hpHurtThreshold) return _hpHurtColor;
            return _hpHealthyColor;
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _labelStyle.normal.textColor = _textColor;

            _bigLabelStyle = new GUIStyle(_labelStyle)
            {
                fontSize = _fontSize + 4,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
