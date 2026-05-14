using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Short-lived banner stinger for player kill streaks.
    /// "FIRST BLOOD!" on the first player-side kill of a round,
    /// "DOUBLE KILL!" / "TRIPLE KILL!" / etc. for kills landed within
    /// a tight time window. Subscribes to
    /// <see cref="MatchController.KillRegistered"/> and renders an
    /// IMGUI overlay above the centre of the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the structure of <see cref="MatchEndOverlay"/> /
    /// <see cref="StartMatchHud"/>: IMGUI on the main camera, bound by
    /// <see cref="ArenaController"/> at scene load. Allocation-free
    /// hot path — the banner string is pre-formatted on each kill,
    /// not per OnGUI repaint.
    /// </para>
    /// <para>
    /// Sticks to player kills. Enemy-side kills don't fire the
    /// announcer — a player getting bodied doesn't deserve the
    /// "DOMINATING!" treatment.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class KillAnnouncer : MonoBehaviour
    {
        [Tooltip("Seconds the banner stays on screen.")]
        [SerializeField, Min(0.3f)] private float _displaySeconds = 1.6f;

        [Tooltip("Within this many seconds of the previous player kill, count as a streak. " +
                 "Past this window the streak resets to 1.")]
        [SerializeField, Min(0.5f)] private float _streakWindow = 4.0f;

        [SerializeField] private int _bannerFontSize = 56;

        // Palette tokens. First three feed off HudStyles for consistency
        // with the scoreboard / stats overlays; plasma stays local —
        // it's the rampage-only colour and not used elsewhere.
        private static readonly Color s_hazard  = HudStyles.Accent;
        private static readonly Color s_caution = HudStyles.Warning;
        private static readonly Color s_alert   = HudStyles.Danger;
        private static readonly Color s_plasma  = new Color(0.63f, 0.33f, 0.95f, 1f);

        private MatchController _match;
        private GUIStyle _style;
        private GUIContent _content;

        private string _banner;
        private float _bannerExpireAt;
        private Color _bannerColor;
        private int _streak;
        private float _lastKillTime = -100f;
        private bool _firstKillFired;

        public void BindMatch(MatchController match)
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
            _match = match;
            if (_match != null) _match.KillRegistered += HandleKill;
            // Reset state on bind — fresh match, fresh streak.
            _streak = 0;
            _firstKillFired = false;
            _banner = null;
        }

        private void OnDisable()
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
        }

        private void HandleKill(MatchSide killerSide, MatchSide victimSide)
        {
            if (killerSide != MatchSide.Player) return;

            float now = Time.unscaledTime;
            // Streak window: in-window kills extend the streak; out-of-
            // window kills reset to 1.
            if (now - _lastKillTime <= _streakWindow) _streak++;
            else _streak = 1;
            _lastKillTime = now;

            // First-blood overrides streak banner on the first kill.
            if (!_firstKillFired)
            {
                _firstKillFired = true;
                _banner = "FIRST BLOOD!";
                _bannerColor = s_alert;
            }
            else
            {
                _banner = StreakName(_streak);
                _bannerColor = StreakColor(_streak);
            }
            _bannerExpireAt = now + _displaySeconds;
            // GUIContent reused per OnGUI — re-stamp the text now so
            // OnGUI doesn't allocate.
            if (_content == null) _content = new GUIContent();
            _content.text = _banner;

            // Audio ping — solo cue so a quick double-kill cuts the
            // single-kill ping rather than overlapping two stings.
            Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.KillBanner);
        }

        private static string StreakName(int streak) => streak switch
        {
            <= 1 => "KILL!",
            2    => "DOUBLE KILL!",
            3    => "TRIPLE KILL!",
            4    => "QUAD KILL!",
            _    => "RAMPAGE!",
        };

        private static Color StreakColor(int streak) => streak switch
        {
            <= 1 => s_hazard,
            2    => s_caution,
            3    => s_hazard,
            4    => s_alert,
            _    => s_plasma,
        };

        private void OnGUI()
        {
            if (_banner == null) return;
            float now = Time.unscaledTime;
            if (now >= _bannerExpireAt) { _banner = null; return; }

            EnsureStyle();

            // Fade in/out: 15 % attack, 70 % hold, 15 % decay.
            float t = 1f - (_bannerExpireAt - now) / Mathf.Max(0.01f, _displaySeconds);
            float alpha = t < 0.15f ? Mathf.Clamp01(t / 0.15f)
                        : t > 0.85f ? Mathf.Clamp01((1f - t) / 0.15f)
                                    : 1f;

            Color savedColor = GUI.color;
            Color c = _bannerColor;
            c.a *= alpha;
            GUI.color = c;
            Vector2 size = _style.CalcSize(_content);
            Rect r = new Rect(
                (Screen.width  - size.x) * 0.5f,
                Screen.height * 0.20f,
                size.x, size.y);
            GUI.Label(r, _content, _style);
            GUI.color = savedColor;
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            // Tinted via GUI.color so the streak palette flows through.
            _style = HudStyles.Bold(_bannerFontSize, Color.white, TextAnchor.MiddleCenter);
        }
    }
}
