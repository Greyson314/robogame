using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Full-screen end-of-round overlay. Shows VICTORY / DEFEAT / DRAW headline,
    /// the final score, and a "Return to Garage" button. Mirrors the
    /// IMGUI pattern in <c>Robogame.Player.DeathOverlay</c> so it can be
    /// added to the main camera at scene-bind time without prefab authoring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IMGUI (not UGUI) on purpose: the in-arena cursor is locked, and IMGUI
    /// buttons fire on raw mouse events without the EventSystem/Canvas
    /// dance. <see cref="ArenaController"/> unlocks the cursor when
    /// <see cref="MatchController.MatchEnded"/> fires so the player can
    /// click through.
    /// </para>
    /// <para>
    /// Bind via <see cref="BindMatch"/>. The overlay self-suppresses while
    /// the match is in any state other than <see cref="MatchState.RoundEnded"/>,
    /// so it costs ~one bool check per IMGUI event during a live round.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MatchEndOverlay : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private Color _dimColor = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField, Min(8)] private int _headlineFontSize = 72;
        [SerializeField, Min(8)] private int _scoreFontSize = 26;
        [SerializeField, Min(8)] private int _reasonFontSize = 16;
        [SerializeField, Min(8)] private int _buttonFontSize = 22;

        [Header("Layout")]
        [SerializeField] private Vector2 _buttonSize = new Vector2(260f, 56f);

        // Text strings exposed so designers / localisers can override later.
        [Header("Copy")]
        [SerializeField] private string _victoryHeadline = "VICTORY";
        [SerializeField] private string _defeatHeadline  = "DEFEAT";
        [SerializeField] private string _drawHeadline    = "DRAW";
        [SerializeField] private string _returnButton    = "Return to Garage";

        // -----------------------------------------------------------------
        // Cached
        // -----------------------------------------------------------------

        private MatchController _match;
        private MatchEndedArgs _lastArgs;
        private bool _hasArgs;
        private GUIStyle _headlineStyle;
        private GUIStyle _scoreStyle;
        private GUIStyle _reasonStyle;
        private GUIStyle _buttonStyle;

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>True while the overlay is visible (i.e. match has ended and the user hasn't clicked through yet).</summary>
        public bool IsVisible => _hasArgs && _match != null && _match.State == MatchState.RoundEnded;

        /// <summary>Bind to a controller. Subscribes to <see cref="MatchController.MatchEnded"/>.</summary>
        public void BindMatch(MatchController match)
        {
            // Defensive: unsubscribe from any previous controller before
            // rebinding (rare but cheap, and avoids leaks when the same
            // overlay is rebound after a respawn / round restart).
            if (_match != null) _match.MatchEnded -= HandleMatchEnded;
            _match = match;
            if (_match != null) _match.MatchEnded += HandleMatchEnded;
        }

        // -----------------------------------------------------------------
        // Event hookup
        // -----------------------------------------------------------------

        private void HandleMatchEnded(MatchEndedArgs args)
        {
            _lastArgs = args;
            _hasArgs = true;
            // Free the cursor so the player can click "Return to Garage".
            // ArenaController also flips this — both paths set the same
            // state, so being defensive is harmless.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable()
        {
            if (_match != null) _match.MatchEnded -= HandleMatchEnded;
        }

        // -----------------------------------------------------------------
        // Render
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsVisible) return;
            EnsureStyles();

            // Dim background.
            Color prev = GUI.color;
            GUI.color = _dimColor;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            // Headline.
            string headline;
            Color headlineColor;
            switch (_lastArgs.WinnerSide)
            {
                case MatchSide.Player:
                    headline = _victoryHeadline; headlineColor = HudStyles.Accent; break;
                case MatchSide.Enemy:
                    headline = _defeatHeadline;  headlineColor = HudStyles.Danger; break;
                default:
                    headline = _drawHeadline;    headlineColor = HudStyles.TextMuted; break;
            }

            Rect headlineRect = new Rect(0, Screen.height * 0.22f, Screen.width, _headlineFontSize + 16);
            GUI.color = headlineColor;
            GUI.Label(headlineRect, headline, _headlineStyle);

            // Score line — team scrap totals. Rich-text colour per team
            // so the player can see at a glance which side carried.
            Rect scoreRect = new Rect(0, Screen.height * 0.36f, Screen.width, _scoreFontSize + 8);
            GUI.color = HudStyles.TextPrimary;
            string scoreLine = $"SCRAP   <color={HudStyles.TagAccent}>{_lastArgs.PlayerScore}</color>  —  <color={HudStyles.TagDanger}>{_lastArgs.EnemyScore}</color>";
            GUI.Label(scoreRect, scoreLine, _scoreStyle);

            // Reason line.
            Rect reasonRect = new Rect(0, Screen.height * 0.42f, Screen.width, _reasonFontSize + 8);
            GUI.Label(reasonRect, ResolveReasonCopy(_lastArgs.Reason), _reasonStyle);

            GUI.color = prev;

            // Return button.
            float bx = (Screen.width - _buttonSize.x) * 0.5f;
            float by = Screen.height * 0.55f;
            Rect buttonRect = new Rect(bx, by, _buttonSize.x, _buttonSize.y);
            if (GUI.Button(buttonRect, _returnButton, _buttonStyle))
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiBack);
                ReturnToGarage();
            }
        }

        private static string ResolveReasonCopy(MatchEndReason reason) => reason switch
        {
            MatchEndReason.ScrapLimitReached => "Scrap quota reached",
            MatchEndReason.TimeExpired       => "Time up",
            MatchEndReason.PlayerEliminated  => "Out of lives",
            MatchEndReason.Draw              => "Time up — no winner",
            _                                 => "Match ended",
        };

        private void ReturnToGarage()
        {
            // Hide ourselves first so the next scene's GUI doesn't see a
            // stale "VICTORY" overlay during the load frame.
            _hasArgs = false;
            GameStateController state = GameStateController.Instance;
            if (state != null) state.EnterGarage();
        }

        private void EnsureStyles()
        {
            if (_headlineStyle != null) return;
            _headlineStyle = HudStyles.Bold(_headlineFontSize, Color.white, TextAnchor.MiddleCenter);
            _scoreStyle = HudStyles.Bold(_scoreFontSize, Color.white, TextAnchor.MiddleCenter);
            _reasonStyle = HudStyles.Label(_reasonFontSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Italic);

            // Buttons inherit GUI.skin.button (so the engine's pressed /
            // hover states still fire); we only override font + sizing.
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = HudStyles.Font,
                fontSize = _buttonFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
