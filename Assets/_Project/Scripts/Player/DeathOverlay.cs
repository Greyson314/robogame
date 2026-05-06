using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Full-screen "you died" overlay shown when the local chassis loses
    /// its CPU (block count reaches 0 or the chassis transform is gone).
    /// Mirrors the SettingsHud / HitMarkerOverlay IMGUI pattern so it
    /// can be added to the camera at scene-bind time without prefab
    /// authoring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Polls <see cref="FollowCamera.Target"/> + the resolved
    /// <see cref="Robot"/>'s block count each frame; cheap. When the
    /// chassis is dead, paints a dim vignette + "DESTROYED" headline +
    /// "Press K to respawn" hint. <see cref="Gameplay.ArenaController"/>
    /// already binds K → RespawnPlayer so the prompt is actionable.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DeathOverlay : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private Color _dimColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _headlineColor = new Color(0.95f, 0.30f, 0.20f, 1f);
        [SerializeField] private Color _hintColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField, Min(8)] private int _headlineFontSize = 64;
        [SerializeField, Min(8)] private int _hintFontSize = 22;
        [SerializeField] private string _headline = "DESTROYED";
        [SerializeField] private string _hint = "press K to respawn";

        private FollowCamera _follow;
        private Robot _robot;
        private bool _shouldShow;
        private GUIStyle _headlineStyle;
        private GUIStyle _hintStyle;

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            // Re-resolve the chassis on bind change. FollowCamera respawn
            // rebinds Target to a fresh Robot each respawn; cache only
            // while the reference holds.
            Transform t = _follow != null ? _follow.Target : null;
            if (t == null)
            {
                _robot = null;
                _shouldShow = true; // no chassis at all = dead
                return;
            }
            if (_robot == null || _robot.transform != t)
            {
                _robot = t.GetComponent<Robot>() ?? t.GetComponentInParent<Robot>();
            }

            // BlockCount drops to 0 when the chassis disintegrates after
            // CPU loss. That's the tell.
            _shouldShow = _robot == null || _robot.BlockCount == 0;
        }

        private void OnGUI()
        {
            if (!_shouldShow) return;

            EnsureStyles();

            // Dim background.
            Color prev = GUI.color;
            GUI.color = _dimColor;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            // Headline.
            Rect headlineRect = new Rect(0, Screen.height * 0.35f, Screen.width, _headlineFontSize + 16);
            GUI.color = _headlineColor;
            GUI.Label(headlineRect, _headline, _headlineStyle);

            // Hint.
            Rect hintRect = new Rect(0, Screen.height * 0.50f, Screen.width, _hintFontSize + 16);
            GUI.color = _hintColor;
            GUI.Label(hintRect, _hint, _hintStyle);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_headlineStyle != null) return;
            _headlineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _headlineFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _headlineStyle.normal.textColor = Color.white; // GUI.color tints
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _hintFontSize,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
            };
            _hintStyle.normal.textColor = Color.white;
        }
    }
}
