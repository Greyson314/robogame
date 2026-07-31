using Robogame.Block;
using Robogame.Combat;
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
        [Tooltip("Crosshair colour while every weapon pool is empty or reloading. " +
                 "Blended over the base/enemy colour to dim the crosshair as a " +
                 "glance-state \"you can't fire right now\" signal — the detailed " +
                 "ammo breakdown stays in VehicleStatsHud.")]
        [SerializeField] private Color _reloadColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
        [SerializeField] private Color _outlineColor = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField, Min(0f)] private float _outline = 1f;

        [Header("Target detection")]
        [Tooltip("Raycast layers used to detect a damageable under the reticle.")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("Maximum aim-detection distance.")]
        [SerializeField, Min(1f)] private float _aimRange = 300f;

        [Header("Ammo readout")]
        [Tooltip("Show the chassis's total loaded ammo as a small number under the crosshair. " +
                 "Quick glance-state; the per-weapon breakdown is in VehicleStatsHud.")]
        [SerializeField] private bool _showAmmoCount = true;

        [Tooltip("Font size of the under-crosshair ammo number.")]
        [SerializeField, Min(8)] private int _ammoFontSize = 12;

        private Camera _camera;
        private FollowCamera _follow;
        private bool _hasEnemyTarget;
        // Ammo-state mirror, refreshed in Update so OnGUI is allocation-
        // free. _anyCanFire goes false when every weapon pool is in a
        // reload or empty — the crosshair tints toward _reloadColor.
        private WeaponAmmoState _ammoCached;
        private Transform _ammoCacheTarget;
        private bool _anyCanFire;
        private bool _hasAnyPool;
        private int _totalLoaded;
        private string _ammoText = "0";
        private int _lastAmmoText = int.MinValue;
        private GUIStyle _ammoStyle;
        private static readonly RaycastHit[] s_hits = new RaycastHit[8];

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            RefreshAmmoState();

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
            BlockGrid localGrid = localRobot != null ? localRobot.Grid : null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                ref RaycastHit h = ref s_hits[i];
                if (h.collider == null) continue;
                IDamageable dmg = h.collider.GetComponentInParent<IDamageable>();
                if (dmg == null || !dmg.IsAlive) continue;
                Robot otherRobot = (dmg as Component)?.GetComponentInParent<Robot>();
                if (otherRobot != null && otherRobot == localRobot) continue;
                // Reparented own blocks (rotor foils adopted under a
                // scene-root kinematic hub) have no Robot ancestor, so the
                // check above misses them and the crosshair went red on the
                // player's own spinning foils. Resolve via grid membership —
                // same self-check as RobotDrive.ComputeAimPoint.
                if (localGrid != null)
                {
                    BlockBehaviour bb = h.collider.GetComponentInParent<BlockBehaviour>();
                    if (bb != null
                        && localGrid.TryGetBlock(bb.GridPosition, out BlockBehaviour ownBlock)
                        && ownBlock == bb)
                    {
                        continue;
                    }
                }
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    _hasEnemyTarget = true;
                }
            }
        }

        // Resolves the chassis's WeaponAmmoState from the FollowCamera
        // target and snapshots loaded-rounds totals. Re-resolves only
        // when the target changes (respawn) — cheap per-frame loop after
        // that, no GetComponent walk on the hot path.
        private void RefreshAmmoState()
        {
            Transform target = _follow != null ? _follow.Target : null;
            if (target != _ammoCacheTarget)
            {
                _ammoCacheTarget = target;
                _ammoCached = target != null ? target.GetComponentInParent<WeaponAmmoState>() : null;
            }
            _anyCanFire = false;
            _hasAnyPool = false;
            _totalLoaded = 0;
            if (_ammoCached == null) return;
            foreach (var kvp in _ammoCached.EnumeratePools())
            {
                _hasAnyPool = true;
                _totalLoaded += kvp.Value.current;
                if (kvp.Value.current > 0 && !kvp.Value.reloading) _anyCanFire = true;
            }
            if (_totalLoaded != _lastAmmoText)
            {
                _lastAmmoText = _totalLoaded;
                _ammoText = _totalLoaded.ToString();
            }
        }

        private void OnGUI()
        {
            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            float len = _armLength;
            float th  = _thickness;
            float gap = _gap;

            // Layered tint: base/enemy is the primary signal; if the
            // chassis has weapons AND none can fire right now, blend
            // 70% toward the reload colour to dim the crosshair as a
            // glance "you can't shoot" cue. Both signals coexist (a red
            // crosshair that's also dim = enemy in sight while reloading).
            Color tint = _hasEnemyTarget ? _enemyColor : _color;
            if (_hasAnyPool && !_anyCanFire) tint = Color.Lerp(tint, _reloadColor, 0.7f);

            // Horizontal & vertical arms (left, right, up, down).
            DrawBar(cx - gap - len, cy - th * 0.5f, len, th, tint);
            DrawBar(cx + gap,       cy - th * 0.5f, len, th, tint);
            DrawBar(cx - th * 0.5f, cy - gap - len, th, len, tint);
            DrawBar(cx - th * 0.5f, cy + gap,       th, len, tint);

            if (_dotSize > 0f)
            {
                DrawBar(cx - _dotSize * 0.5f, cy - _dotSize * 0.5f, _dotSize, _dotSize, tint);
            }

            if (_showAmmoCount && _hasAnyPool)
            {
                if (_ammoStyle == null)
                {
                    _ammoStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = _ammoFontSize,
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                    };
                    _ammoStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
                }
                _ammoStyle.normal.textColor = tint;
                float labelW = 48f;
                float labelH = _ammoFontSize + 4f;
                Rect r = new Rect(cx - labelW * 0.5f, cy + gap + len + 2f, labelW, labelH);
                GUI.Label(r, _ammoText, _ammoStyle);
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
