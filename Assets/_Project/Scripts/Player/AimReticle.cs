using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Player
{
    /// <summary>
    /// Minimal screen-centre crosshair drawn via OnGUI. No prefabs, no
    /// Canvas — just paints four short bars and a centre dot in immediate
    /// mode. Lives on the same GameObject as <see cref="FollowCamera"/>
    /// (typically Main Camera) so the reticle and the camera-ray aim
    /// stay in lockstep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-frame raycasts the screen-centre against everything on
    /// <see cref="_targetMask"/>; when the hit is a non-self
    /// <see cref="IDamageable"/>, the reticle flips to
    /// <see cref="_enemyColor"/>. Quick "I have a target" read for the
    /// player without committing to full target-locking UI.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AimReticle : MonoBehaviour
    {
        [Tooltip("Length of each crosshair arm, in pixels.")]
        [SerializeField, Min(1f)] private float _armLength = 8f;

        [Tooltip("Thickness of each arm, in pixels.")]
        [SerializeField, Min(1f)] private float _thickness = 2f;

        [Tooltip("Gap between the centre and the inner edge of each arm.")]
        [SerializeField, Min(0f)] private float _gap = 4f;

        [Tooltip("Diameter of the centre dot. 0 to hide.")]
        [SerializeField, Min(0f)] private float _dotSize = 2f;

        [SerializeField] private Color _color = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color _enemyColor = new Color(0.95f, 0.30f, 0.20f, 0.95f);
        [SerializeField] private Color _outlineColor = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField, Min(0f)] private float _outline = 1f;

        [Header("Target detection")]
        [Tooltip("Raycast layers used to detect a damageable under the reticle.")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("Maximum aim-detection distance.")]
        [SerializeField, Min(1f)] private float _aimRange = 300f;

        private Camera _camera;
        private FollowCamera _follow;
        private bool _hasEnemyTarget;
        private static readonly RaycastHit[] s_hits = new RaycastHit[8];

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            // Cheap target check: ray from screen-centre, look for an
            // IDamageable that isn't the local chassis. Allocates zero
            // (RaycastNonAlloc into a static buffer).
            _hasEnemyTarget = false;
            if (_camera == null) return;

            Ray ray = _camera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            int n = Physics.RaycastNonAlloc(ray, s_hits, _aimRange, _targetMask, QueryTriggerInteraction.Ignore);
            if (n == 0) return;

            // Find the closest non-self damageable.
            Robot localRobot = _follow != null && _follow.Target != null
                ? _follow.Target.GetComponentInParent<Robot>()
                : null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                ref RaycastHit h = ref s_hits[i];
                if (h.collider == null) continue;
                IDamageable dmg = h.collider.GetComponentInParent<IDamageable>();
                if (dmg == null || !dmg.IsAlive) continue;
                Robot otherRobot = (dmg as Component)?.GetComponentInParent<Robot>();
                if (otherRobot != null && otherRobot == localRobot) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    _hasEnemyTarget = true;
                }
            }
        }

        private void OnGUI()
        {
            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            float len = _armLength;
            float th  = _thickness;
            float gap = _gap;

            Color tint = _hasEnemyTarget ? _enemyColor : _color;

            // Horizontal & vertical arms (left, right, up, down).
            DrawBar(cx - gap - len, cy - th * 0.5f, len, th, tint);
            DrawBar(cx + gap,       cy - th * 0.5f, len, th, tint);
            DrawBar(cx - th * 0.5f, cy - gap - len, th, len, tint);
            DrawBar(cx - th * 0.5f, cy + gap,       th, len, tint);

            if (_dotSize > 0f)
            {
                DrawBar(cx - _dotSize * 0.5f, cy - _dotSize * 0.5f, _dotSize, _dotSize, tint);
            }
        }

        private void DrawBar(float x, float y, float w, float h, Color color)
        {
            if (_outline > 0f)
            {
                Rect outline = new Rect(x - _outline, y - _outline, w + _outline * 2f, h + _outline * 2f);
                DrawRect(outline, _outlineColor);
            }
            DrawRect(new Rect(x, y, w, h), color);
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
