using System.Collections.Generic;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// World-space "carried-scrap" indicator that floats above every
    /// robot whose <see cref="Robot.ScrapHeld"/> > 0. Renders via IMGUI
    /// (no UI canvas needed — matches the rest of the in-game HUD).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives on the main camera, scans <see cref="Robot"/> instances each
    /// frame, projects their world position to screen space, and draws a
    /// tinted label. Player robots get the project's orange accent; enemy
    /// robots get red. Lets the player prioritise targets carrying juicy
    /// scrap loads (high reward for the kill) vs respawn-fresh enemies
    /// (no carried scrap → only the base death drop).
    /// </para>
    /// <para>
    /// Robot enumeration: a small static list maintained by
    /// <see cref="Robot.OnEnable"/> / <see cref="Robot.OnDisable"/>
    /// would be ideal, but to avoid touching the Robot lifecycle for a
    /// HUD, we use a per-frame cache populated via
    /// <see cref="Object.FindObjectsByType{T}"/> on a 0.5 s cadence.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScrapCarriedIndicator : MonoBehaviour
    {
        [Tooltip("World-space vertical offset above each chassis (m).")]
        [SerializeField, Min(0f)] private float _yOffset = 2.0f;

        [Tooltip("Robots closer than this (m) to the camera are skipped — no need to overlay the player's own indicator on their own chassis.")]
        [SerializeField, Min(0f)] private float _hideWithin = 4.5f;

        [Tooltip("Refresh interval (s) for the robot list. Doesn't drive label updates — those are per-frame — only the FindObjectsByType cost.")]
        [SerializeField, Min(0.05f)] private float _refreshInterval = 0.5f;

        [Tooltip("Font size for the world-space label.")]
        [SerializeField, Min(8)] private int _fontSize = 16;

        private readonly List<Robot> _robotCache = new();
        private float _nextRefresh;
        private GUIStyle _labelStyle;
        private GUIStyle _shadowStyle;

        // Cached side-lookup so the indicator can colour by team without
        // re-scanning every frame. Built once per robot via the bound
        // ArenaController.
        private System.Func<Robot, MatchSide> _sideLookup;

        public void BindSideLookup(System.Func<Robot, MatchSide> lookup)
        {
            _sideLookup = lookup;
        }

        private void Update()
        {
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + _refreshInterval;
            RefreshCache();
        }

        private void RefreshCache()
        {
            _robotCache.Clear();
#if UNITY_2023_1_OR_NEWER
            Robot[] found = Object.FindObjectsByType<Robot>(FindObjectsSortMode.None);
#else
            Robot[] found = Object.FindObjectsOfType<Robot>();
#endif
            for (int i = 0; i < found.Length; i++)
            {
                Robot r = found[i];
                if (r != null && !r.IsDestroyed) _robotCache.Add(r);
            }
        }

        private void OnGUI()
        {
            Camera cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            EnsureStyle();

            for (int i = 0; i < _robotCache.Count; i++)
            {
                Robot r = _robotCache[i];
                if (r == null || r.IsDestroyed) continue;
                int scrap = r.ScrapHeld;
                if (scrap <= 0) continue;

                Vector3 worldPos = r.transform.position + Vector3.up * _yOffset;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                if (screenPos.z <= 0f) continue; // behind camera

                // Hide labels too close to the camera so the local player's
                // own indicator doesn't clutter the viewport.
                if (screenPos.z < _hideWithin) continue;

                MatchSide side = _sideLookup != null ? _sideLookup(r) : MatchSide.None;
                Color col = side switch
                {
                    MatchSide.Player => new Color(0.95f, 0.55f, 0.10f, 1f),
                    MatchSide.Enemy  => new Color(0.95f, 0.25f, 0.20f, 1f),
                    _                => new Color(0.8f, 0.8f, 0.8f, 0.85f),
                };

                // GUI's y axis is flipped relative to screen-space y.
                float guiY = Screen.height - screenPos.y;
                // Approximate text width for centring.
                string text = $"⛁ {scrap}";
                float w = 80f;
                float h = _fontSize + 6f;
                Rect r2 = new Rect(screenPos.x - w * 0.5f, guiY - h * 0.5f, w, h);

                // Shadow first (1px offset, dark) so the label reads on
                // any background.
                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(r2.x + 1f, r2.y + 1f, r2.width, r2.height), text, _shadowStyle);
                GUI.color = col;
                GUI.Label(r2, text, _labelStyle);
                GUI.color = prev;
            }
        }

        private void EnsureStyle()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _shadowStyle = new GUIStyle(_labelStyle);
        }
    }
}
