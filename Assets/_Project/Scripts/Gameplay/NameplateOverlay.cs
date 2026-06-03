using System.Collections.Generic;
using Robogame.Core;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// World-space chassis nameplates — name label + HP bar floating above
    /// every non-local Robot in the arena. One camera-scoped overlay
    /// instead of a per-chassis MonoBehaviour: one OnGUI walks the cached
    /// Robot list and projects each anchor through the camera. Periodic
    /// FindObjectsByType refresh (every <see cref="_refreshInterval"/> s)
    /// catches spawn / respawn / destroy without per-event plumbing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auto-attached to the camera by
    /// <see cref="ArenaController.ConfigureCamera"/> alongside the other
    /// HUDs. SP-only today; the future NGO Phase 7 sibling will populate
    /// display names from <c>OwnerClientId</c> instead of the chassis
    /// GameObject name. The HUD itself is unchanged either way — it
    /// reads from each Robot's BlockCount / InitialBlockCount which are
    /// already replicated by Phase 4.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NameplateOverlay : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Vertical world-space offset above each chassis where the nameplate sits, in metres.")]
        [SerializeField, Min(0f)] private float _verticalOffset = 2.2f;

        [Tooltip("Width of each nameplate pill in screen pixels.")]
        [SerializeField, Min(40f)] private float _width = 120f;

        [Tooltip("Total height of each nameplate (label + HP bar combined).")]
        [SerializeField, Min(14f)] private float _height = 24f;

        [Tooltip("HP bar height inside the nameplate.")]
        [SerializeField, Min(2f)] private float _hpBarHeight = 4f;

        [Header("Lifecycle")]
        [Tooltip("Seconds between FindObjectsByType refreshes. Lower = catches new spawns " +
                 "faster, higher = less editor overhead in giant arenas.")]
        [SerializeField, Min(0.1f)] private float _refreshInterval = 1.0f;

        [Tooltip("Maximum world-space distance from camera at which nameplates render. " +
                 "Past this, the chassis is far enough that the plate is unreadable anyway.")]
        [SerializeField, Min(5f)] private float _maxDistance = 120f;

        [Header("Look")]
        [SerializeField, Min(8)] private int _fontSize = 12;

        private FollowCamera _follow;
        private Camera _camera;
        private GUIStyle _labelStyle;
        // Refreshed periodically; nulled-out entries (Unity-fake-null after
        // destroy) are filtered at render time.
        private readonly List<Robot> _robots = new(16);
        // Display name per robot, index-parallel to _robots. Computed once
        // per refresh instead of per-OnGUI (OnGUI fires 2–6×/frame and
        // FormatName allocates a substring on the "(Clone)" trim path).
        private readonly List<string> _names = new(16);
        private float _nextRefreshAt;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefreshAt)
            {
                _nextRefreshAt = Time.unscaledTime + _refreshInterval;
                RefreshRobotList();
            }
        }

        private void RefreshRobotList()
        {
            _robots.Clear();
            _names.Clear();
            Robot[] all = Object.FindObjectsByType<Robot>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Robot r = all[i];
                if (r == null || r.IsDestroyed) continue;
                _robots.Add(r);
                _names.Add(FormatName(r));
            }
        }

        private void OnGUI()
        {
            if (_camera == null || _robots.Count == 0) return;
            EnsureStyle();

            Robot localRobot = ResolveLocalRobot();
            Vector3 camPos = _camera.transform.position;

            for (int i = 0; i < _robots.Count; i++)
            {
                Robot r = _robots[i];
                // Skip Unity-fake-null + the local chassis (player doesn't
                // need their own nameplate; it would just stack on the
                // crosshair).
                if (r == null || r.IsDestroyed) continue;
                if (r == localRobot) continue;

                Vector3 anchor = r.transform.position + Vector3.up * _verticalOffset;
                float distSqr = (camPos - anchor).sqrMagnitude;
                if (distSqr > _maxDistance * _maxDistance) continue;

                Vector3 screen = _camera.WorldToScreenPoint(anchor);
                if (screen.z <= 0f) continue;

                float x = screen.x - _width * 0.5f;
                float y = Screen.height - screen.y - _height * 0.5f;
                Rect bg = new Rect(x, y, _width, _height);

                float hpFrac = r.InitialBlockCount > 0
                    ? Mathf.Clamp01((float)r.BlockCount / r.InitialBlockCount)
                    : 0f;

                Color saved = GUI.color;

                // Background pill.
                GUI.color = HudStyles.PanelBg;
                GUI.DrawTexture(bg, HudStyles.Pixel);

                // Name label (top portion).
                _labelStyle.normal.textColor = HudStyles.TextPrimary;
                GUI.color = Color.white;
                Rect labelRect = new Rect(x + 4f, y + 1f, _width - 8f, _height - _hpBarHeight - 4f);
                GUI.Label(labelRect, _names[i], _labelStyle);

                // HP bar (bottom strip).
                float barY = y + _height - _hpBarHeight - 2f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(x + 4f, barY, _width - 8f, _hpBarHeight), HudStyles.Pixel);
                GUI.color = HpColor(hpFrac);
                GUI.DrawTexture(new Rect(x + 4f, barY, (_width - 8f) * hpFrac, _hpBarHeight), HudStyles.Pixel);

                GUI.color = saved;
            }
        }

        private Robot ResolveLocalRobot()
        {
            if (_follow == null) return null;
            Transform t = _follow.Target;
            return t != null ? t.GetComponentInParent<Robot>() : null;
        }

        private void EnsureStyle()
        {
            if (_labelStyle != null) return;
            _labelStyle = HudStyles.Bold(_fontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
        }

        private static string FormatName(Robot r)
        {
            // In SP we use the GameObject name; arena spawns name them
            // sensibly ("Bot 1" / "Bot 2"). MP swaps this for the player's
            // display name when the networked sibling lands.
            string n = r.gameObject != null ? r.gameObject.name : "Chassis";
            // Trim Unity's "(Clone)" suffix so respawns render cleanly.
            const string clone = "(Clone)";
            if (n.EndsWith(clone)) n = n.Substring(0, n.Length - clone.Length).TrimEnd();
            return n;
        }

        private static Color HpColor(float frac)
        {
            if (frac < 0.30f) return HudStyles.Danger;
            if (frac < 0.60f) return HudStyles.Warning;
            return HudStyles.Healthy;
        }
    }
}
