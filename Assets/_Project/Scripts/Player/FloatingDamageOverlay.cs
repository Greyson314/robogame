using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// HUD overlay that draws short-lived floating damage numbers above
    /// any block that takes damage on screen. Subscribes to
    /// <see cref="BlockBehaviour.DamageDealt"/> so it works regardless of
    /// which weapon path (projectile, bomb splash, rope tip, momentum
    /// impact) actually applied the damage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IMGUI-based to match <see cref="HitMarkerOverlay"/> + reuse the
    /// same camera GameObject; UI Toolkit is the longer-term target for
    /// in-game HUD.
    /// </para>
    /// <para>
    /// Numbers rise <see cref="_riseHeight"/> world-metres over
    /// <see cref="_lifetime"/> seconds, fading from full alpha to zero.
    /// Off-screen numbers (behind camera, outside frustum) are silently
    /// skipped each frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FloatingDamageOverlay : MonoBehaviour
    {
        [Header("Animation")]
        [Tooltip("Seconds the number stays on screen.")]
        [SerializeField, Min(0.1f)] private float _lifetime = 0.85f;

        [Tooltip("World-space metres the number rises over its lifetime.")]
        [SerializeField, Min(0f)] private float _riseHeight = 1.2f;

        [Header("Look")]
        [Tooltip("Damage threshold to drop chip taps below 1 HP. Anything under this is suppressed " +
                 "so the screen doesn't fill with rounding-error numbers from rope-bumps.")]
        [SerializeField, Min(0f)] private float _minDamage = 1f;

        [SerializeField] private Color _color = new Color(0.95f, 0.55f, 0.10f, 0.95f);
        [SerializeField] private Color _heavyColor = new Color(0.95f, 0.20f, 0.10f, 0.95f);
        [SerializeField] private float _heavyThreshold = 50f;

        [SerializeField, Min(8)] private int _fontSize = 18;

        // Hard cap on simultaneous numbers — a splash explosion can fire
        // dozens of DamageDealt events in one frame; rendering all of
        // them tanks IMGUI without adding meaningful info to the player.
        [SerializeField, Min(8)] private int _maxNumbers = 64;

        private struct Floater
        {
            public Vector3 WorldPos;
            public float SpawnTime;
            public float Damage;
        }
        private readonly List<Floater> _floaters = new(64);
        private Camera _camera;
        private GUIStyle _style;
        private GUIStyle _styleHeavy;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            BlockBehaviour.DamageDealt += HandleDamage;
        }

        private void OnDisable()
        {
            BlockBehaviour.DamageDealt -= HandleDamage;
        }

        private void HandleDamage(BlockBehaviour block, float damage)
        {
            if (block == null || damage < _minDamage) return;
            if (_floaters.Count >= _maxNumbers)
            {
                // Drop the oldest. Floaters list is roughly age-ordered
                // (we append on damage, drain in OnGUI by age).
                _floaters.RemoveAt(0);
            }
            _floaters.Add(new Floater
            {
                WorldPos = block.transform.position,
                SpawnTime = Time.unscaledTime,
                Damage = damage,
            });
        }

        private void OnGUI()
        {
            if (_camera == null) return;
            if (_floaters.Count == 0) return;

            EnsureStyles();

            float now = Time.unscaledTime;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                Floater f = _floaters[i];
                float age = now - f.SpawnTime;
                if (age > _lifetime)
                {
                    _floaters.RemoveAt(i);
                    continue;
                }

                float t = age / _lifetime;
                Vector3 worldPos = f.WorldPos + Vector3.up * (t * _riseHeight);
                Vector3 screen = _camera.WorldToScreenPoint(worldPos);
                if (screen.z <= 0f) continue; // behind camera
                if (screen.x < 0 || screen.x > Screen.width) continue;

                float alpha = 1f - t;
                bool heavy = f.Damage >= _heavyThreshold;
                Color c = heavy ? _heavyColor : _color;
                c.a *= alpha;

                GUIStyle s = heavy ? _styleHeavy : _style;
                Color savedColor = GUI.color;
                GUI.color = c;
                string text = f.Damage.ToString("F0");
                Vector2 size = s.CalcSize(new GUIContent(text));
                Rect r = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
                GUI.Label(r, text, s);
                GUI.color = savedColor;
            }
        }

        private void EnsureStyles()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _style.normal.textColor = Color.white; // GUI.color tints
            _styleHeavy = new GUIStyle(_style)
            {
                fontSize = Mathf.RoundToInt(_fontSize * 1.3f),
            };
            _styleHeavy.normal.textColor = Color.white;
        }
    }
}
