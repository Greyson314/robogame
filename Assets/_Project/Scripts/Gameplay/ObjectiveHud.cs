using System.Text;
using Robogame.Core;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Top-centre scoreboard HUD: team header (YOU vs ENEMY) over the
    /// scrap-bar, with per-team frag count, a round-timer pill, and an
    /// HP rail beneath. Drives the player's read on "am I winning the
    /// round + do I need to back off."
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout: three rows. Row 1: large team-vs-team scrap line in
    /// accent / danger colours, with the round timer centred between
    /// them. Row 2: small FRAGS line (per-team kill counts since
    /// session 59 — MatchController tracks them). Row 3: HP bar (player
    /// chassis only). Background panel uses
    /// <see cref="HudStyles.PanelBgHeavy"/> + an accent top-edge highlight
    /// so it reads as scoreboard chrome, not stat overlay.
    /// </para>
    /// <para>
    /// Lives in <c>Robogame.Gameplay</c> rather than <c>Robogame.Player</c>
    /// because it pulls match state from <see cref="MatchController"/>;
    /// <c>Robogame.Player</c> sits at a lower asmdef tier (BEST_PRACTICES § 2.2).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHud : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Pixels from the top edge to the panel.")]
        [SerializeField, Min(4f)] private float _topMargin = 18f;

        [Tooltip("Panel width in pixels. Wider than the legacy 360 px so the team labels + score + timer have room to breathe.")]
        [SerializeField, Min(240f)] private float _panelWidth = 520f;

        [Tooltip("Panel height in pixels.")]
        [SerializeField, Min(60f)] private float _panelHeight = 124f;

        [Header("HP bar")]
        [Tooltip("Pixels of HP-bar height inside the panel.")]
        [SerializeField, Min(4f)] private float _hpBarHeight = 10f;

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
        private GUIStyle _headerStyle;     // "YOU" / "ENEMY"
        private GUIStyle _scoreStyle;      // 12 / 20
        private GUIStyle _timerStyle;      // 1:24
        private GUIStyle _fragsStyle;      // FRAGS 3 — 1
        private GUIStyle _targetStyle;     // / 20
        private bool _stylesBuilt;

        private readonly StringBuilder _scratch = new(32);
        private string _renderedTimer = "—:—";
        private string _renderedFrags = "";
        private int _lastTimerSecs = -1;
        private int _lastPlayerKills = -1;
        private int _lastEnemyKills = -1;

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>Bind the HUD to a controller. Preferred over scene scans.</summary>
        public void BindMatch(MatchController match) => _match = match;

        /// <summary>Diagnostic accessor — returns the player score the HUD is currently displaying.</summary>
        public int DisplayedPlayerScore => _match != null ? _match.ScoreForSide(MatchSide.Player) : 0;

        /// <summary>Diagnostic accessor — returns the enemy score the HUD is currently displaying.</summary>
        public int DisplayedEnemyScore => _match != null ? _match.ScoreForSide(MatchSide.Enemy) : 0;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            if (_match == null)
            {
#if UNITY_2023_1_OR_NEWER
                ArenaController arena = Object.FindFirstObjectByType<ArenaController>();
#else
                ArenaController arena = Object.FindObjectOfType<ArenaController>();
#endif
                if (arena != null) _match = arena.Match;
            }

            Transform t = _follow != null ? _follow.Target : null;
            if (t != _boundChassis)
            {
                _boundChassis = t;
                _robot = _boundChassis != null ? _boundChassis.GetComponent<Robot>() : null;
            }

            if (_match == null) return;

            int secs = Mathf.CeilToInt(_match.TimeRemaining);
            if (secs != _lastTimerSecs)
            {
                _lastTimerSecs = secs;
                _scratch.Clear();
                int mm = Mathf.Max(0, secs / 60);
                int ss = Mathf.Max(0, secs % 60);
                _scratch.Append(mm).Append(':');
                if (ss < 10) _scratch.Append('0');
                _scratch.Append(ss);
                _renderedTimer = _scratch.ToString();
            }

            int pk = _match.KillsForSide(MatchSide.Player);
            int ek = _match.KillsForSide(MatchSide.Enemy);
            if (pk != _lastPlayerKills || ek != _lastEnemyKills)
            {
                _lastPlayerKills = pk;
                _lastEnemyKills = ek;
                _scratch.Clear();
                _scratch
                    .Append("FRAGS  ")
                    .Append("<color=").Append(HudStyles.TagAccent).Append('>').Append(pk).Append("</color>")
                    .Append("  —  ")
                    .Append("<color=").Append(HudStyles.TagDanger).Append('>').Append(ek).Append("</color>");
                _renderedFrags = _scratch.ToString();
            }
        }

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (_match == null) return;
            EnsureStyles();

            float x = (Screen.width - _panelWidth) * 0.5f;
            float y = _topMargin;

            // Panel background + accent top edge.
            Color prev = GUI.color;
            GUI.color = HudStyles.PanelBgHeavy;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, _panelHeight), HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);
            GUI.color = prev;

            float padX = 18f;
            int playerScrap = _match.ScoreForSide(MatchSide.Player);
            int enemyScrap = _match.ScoreForSide(MatchSide.Enemy);
            int target = _match.TargetTeamScrap;

            // Row 1: team labels — "YOU" on left, "ENEMY" on right.
            float headerY = y + 6f;
            float headerH = 18f;
            GUI.Label(new Rect(x + padX, headerY, _panelWidth * 0.5f - padX, headerH),
                "YOU", _headerStyle);
            // Manually right-align "ENEMY" because the header style has a
            // separate alignment instance reused for "YOU".
            GUIStyle right = new GUIStyle(_headerStyle) { alignment = TextAnchor.MiddleRight };
            right.normal.textColor = HudStyles.Danger;
            GUI.Label(new Rect(x + _panelWidth * 0.5f, headerY, _panelWidth * 0.5f - padX, headerH),
                "ENEMY", right);

            // Row 2: scrap totals (big), timer centred between them.
            float scoreY = headerY + headerH + 4f;
            float scoreH = 36f;
            // Player scrap — left half, right-aligned to the centreline so
            // both team numbers visually flank the timer.
            GUIStyle leftScore = new GUIStyle(_scoreStyle) { alignment = TextAnchor.MiddleRight };
            leftScore.normal.textColor = HudStyles.Accent;
            float halfW = _panelWidth * 0.5f;
            GUI.Label(new Rect(x + padX, scoreY, halfW - padX - 60f, scoreH),
                playerScrap.ToString(), leftScore);

            // Timer centred in a fixed-width pill between the two scores.
            float timerSecsRemaining = _match.TimeRemaining;
            bool timerAlert = _match.State == MatchState.InProgress
                              && timerSecsRemaining > 0f
                              && timerSecsRemaining < _timerLowSeconds;
            Color timerColor = timerAlert ? HudStyles.Danger : HudStyles.TextPrimary;
            GUIStyle timer = new GUIStyle(_timerStyle) { alignment = TextAnchor.MiddleCenter };
            timer.normal.textColor = timerColor;
            const float timerW = 120f;
            GUI.Label(new Rect(x + halfW - timerW * 0.5f, scoreY, timerW, scoreH),
                _renderedTimer, timer);

            // Enemy scrap — right half, left-aligned to the centreline.
            GUIStyle rightScore = new GUIStyle(_scoreStyle) { alignment = TextAnchor.MiddleLeft };
            rightScore.normal.textColor = HudStyles.Danger;
            GUI.Label(new Rect(x + halfW + 60f, scoreY, halfW - padX - 60f, scoreH),
                enemyScrap.ToString(), rightScore);

            // Sub-text: "/ target" tucked under each score in muted text.
            float targetY = scoreY + scoreH - 4f;
            float targetH = 14f;
            GUIStyle leftTarget = new GUIStyle(_targetStyle) { alignment = TextAnchor.MiddleRight };
            leftTarget.normal.textColor = HudStyles.TextMuted;
            GUI.Label(new Rect(x + padX, targetY, halfW - padX - 60f, targetH),
                "/ " + target, leftTarget);
            GUIStyle rightTarget = new GUIStyle(_targetStyle) { alignment = TextAnchor.MiddleLeft };
            rightTarget.normal.textColor = HudStyles.TextMuted;
            GUI.Label(new Rect(x + halfW + 60f, targetY, halfW - padX - 60f, targetH),
                "/ " + target, rightTarget);

            // Row 3: frags + HP bar share the bottom strip.
            float fragsY = targetY + targetH + 4f;
            float fragsH = 16f;
            GUI.Label(new Rect(x, fragsY, _panelWidth, fragsH), _renderedFrags, _fragsStyle);

            // HP rail under the frags line.
            float hpY = fragsY + fragsH + 4f;
            float hpInset = padX;
            float hpFullW = _panelWidth - hpInset * 2f;
            float hpFraction = ResolveHpFraction();

            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(x + hpInset, hpY, hpFullW, _hpBarHeight), HudStyles.Pixel);
            GUI.color = ResolveHpColor(hpFraction);
            GUI.DrawTexture(new Rect(x + hpInset, hpY, hpFullW * Mathf.Clamp01(hpFraction), _hpBarHeight),
                HudStyles.Pixel);
            GUI.color = prev;
        }

        private float ResolveHpFraction()
        {
            if (_robot == null || _robot.InitialBlockCount <= 0) return 0f;
            return Mathf.Clamp01((float)_robot.BlockCount / _robot.InitialBlockCount);
        }

        private Color ResolveHpColor(float fraction)
        {
            if (fraction < _hpAlertThreshold) return HudStyles.Danger;
            if (fraction < _hpHurtThreshold) return HudStyles.Warning;
            return HudStyles.Healthy;
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;
            _headerStyle = HudStyles.Bold(13, HudStyles.Accent, TextAnchor.MiddleLeft);
            _scoreStyle = HudStyles.Bold(32, HudStyles.Accent, TextAnchor.MiddleRight);
            _timerStyle = HudStyles.Bold(24, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
            _fragsStyle = HudStyles.Label(13, HudStyles.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            _targetStyle = HudStyles.Label(12, HudStyles.TextMuted, TextAnchor.MiddleRight);
        }
    }
}
