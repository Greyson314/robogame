using Robogame.Combat;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Overlay drawn at screen-centre that flashes a small orange X for
    /// ~150 ms whenever the local player's projectile damages a target.
    /// Tracks the firing chassis via <see cref="FollowCamera.Target"/>: a
    /// hit is "ours" iff its owner robot matches that target's
    /// <see cref="Robot"/> component. Subscribes to
    /// <see cref="Projectile.Hit"/>; nothing else needed for routing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitMarkerOverlay : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Half-length of each X arm (pixels).")]
        [SerializeField, Min(2f)] private float _armLength = 10f;

        [Tooltip("Stroke thickness (pixels).")]
        [SerializeField, Min(1f)] private float _thickness = 2f;

        [Tooltip("Gap between the screen-centre dot and the X arms (pixels).")]
        [SerializeField, Min(0f)] private float _gap = 4f;

        [Tooltip("How long the marker stays visible after a confirmed hit (seconds).")]
        [SerializeField, Min(0.05f)] private float _duration = 0.15f;

        [Tooltip("Marker colour. Default is the palette Hazard orange to read against any background.")]
        [SerializeField] private Color _color = new Color(0.95f, 0.55f, 0.10f, 0.95f);

        [Tooltip("Outline colour drawn one pixel around the X arms for legibility on bright backgrounds.")]
        [SerializeField] private Color _outlineColor = new Color(0f, 0f, 0f, 0.65f);

        [Tooltip("Outline thickness in pixels.")]
        [SerializeField, Min(0f)] private float _outline = 1f;

        // Use UNSCALED time so a Time.timeScale=0 pause still hides the
        // marker after _duration seconds. realtimeSinceStartup is monotonic
        // and unaffected by Time.timeScale.
        private float _hitAt = float.NegativeInfinity;
        private FollowCamera _follow;

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void OnEnable()
        {
            Projectile.Hit += HandleProjectileHit;
        }

        private void OnDisable()
        {
            Projectile.Hit -= HandleProjectileHit;
        }

        private void HandleProjectileHit(Robot owner, Vector3 worldPoint)
        {
            // Filter: only flash when the local chassis (FollowCamera target)
            // owns the projectile. Everything else (AI bots, other players
            // post-netcode) is somebody else's hit and should be silent.
            if (_follow == null) _follow = GetComponent<FollowCamera>();
            if (_follow == null || _follow.Target == null) return;
            Robot localRobot = _follow.Target.GetComponent<Robot>()
                ?? _follow.Target.GetComponentInParent<Robot>();
            if (owner == null || owner != localRobot) return;
            _hitAt = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (Time.unscaledTime - _hitAt > _duration) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float arm = _armLength;
            float th = _thickness;
            float gap = _gap;

            // X arms: four diagonals using rotated rectangles. GUI doesn't
            // do rotated draws cleanly, so we use thin rectangles aligned
            // along ±45° via GUIUtility.RotateAroundPivot.
            DrawArm(cx, cy, +1f, +1f, gap, arm, th);
            DrawArm(cx, cy, -1f, -1f, gap, arm, th);
            DrawArm(cx, cy, +1f, -1f, gap, arm, th);
            DrawArm(cx, cy, -1f, +1f, gap, arm, th);
        }

        private void DrawArm(float cx, float cy, float dx, float dy, float gap, float length, float th)
        {
            // Direction unit vector (scaled by 1/sqrt(2) so length is in
            // pixels along the diagonal).
            const float invSqrt2 = 0.70710678f;
            float ux = dx * invSqrt2;
            float uy = dy * invSqrt2;

            float startX = cx + ux * gap;
            float startY = cy + uy * gap;

            Matrix4x4 oldMat = GUI.matrix;
            // Rotate the GUI by 45° (or 135°) around the start point so a
            // horizontal Rect renders as a diagonal arm. dx*dy chooses
            // between the two diagonals; sign of dx picks the half-line.
            float angle = Mathf.Atan2(uy, ux) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, new Vector2(startX, startY));

            if (_outline > 0f)
            {
                DrawRect(new Rect(startX - _outline, startY - th * 0.5f - _outline,
                                  length + _outline * 2f, th + _outline * 2f),
                         _outlineColor);
            }
            DrawRect(new Rect(startX, startY - th * 0.5f, length, th), _color);

            GUI.matrix = oldMat;
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
