using System.Collections.Generic;
using Robogame.Block;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// HUD overlay that draws short-lived floating damage numbers above
    /// any chassis taking damage on screen. Subscribes to
    /// <see cref="BlockBehaviour.DamageDealt"/> so it works regardless of
    /// which weapon path (projectile, bomb splash, rope tip, momentum
    /// impact) actually applied the damage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per-target summation.</b> Hitting the same chassis 10 times
    /// with the SMG doesn't show "2" ten times — it shows "2", then "4",
    /// then "6", … updating in place. After
    /// <see cref="_summationWindow"/> seconds without a fresh hit on
    /// that chassis the accumulator freezes, animates up + fades out,
    /// and the next hit on that chassis spawns a new accumulator
    /// (resetting from the next damage value).
    /// </para>
    /// <para>
    /// IMGUI-based to match <see cref="HitMarkerOverlay"/> and reuse
    /// the camera GameObject; UI Toolkit is the longer-term target for
    /// in-game HUD. Allocation hot path: lookup is a linear scan over
    /// active accumulators (count is bounded by simultaneously-engaged
    /// targets, &lt; 8 in practice), no per-event GC.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FloatingDamageOverlay : MonoBehaviour
    {
        [Header("Animation")]
        [Tooltip("Seconds the number stays on screen after it freezes (no longer accumulating).")]
        [SerializeField, Min(0.1f)] private float _lifetime = 0.85f;

        [Tooltip("World-space metres the number rises over its post-freeze lifetime.")]
        [SerializeField, Min(0f)] private float _riseHeight = 1.2f;

        [Tooltip("Seconds without a fresh hit on the same chassis before its accumulator freezes. " +
                 "Subsequent damage on the same chassis after this window starts a new accumulator.")]
        [SerializeField, Min(0.1f)] private float _summationWindow = 1.0f;

        [Header("Look")]
        [Tooltip("Damage threshold to drop chip taps below 1 HP. Anything under this is suppressed " +
                 "so the screen doesn't fill with rounding-error numbers from rope-bumps.")]
        [SerializeField, Min(0f)] private float _minDamage = 1f;

        [SerializeField] private Color _color = new Color(0.95f, 0.55f, 0.10f, 0.95f);
        [SerializeField] private Color _heavyColor = new Color(0.95f, 0.20f, 0.10f, 0.95f);
        [SerializeField] private float _heavyThreshold = 50f;

        [SerializeField, Min(8)] private int _fontSize = 18;

        // Hard cap on simultaneous accumulators. With per-target
        // summation a 16-chassis MP arena tops out at 16; the cap is a
        // safety net for pathological splash-fire cases.
        [SerializeField, Min(8)] private int _maxAccumulators = 32;

        private sealed class Accumulator
        {
            // The chassis Robot we're billing damage to. Fake-null
            // after destroy; checked at each render pass.
            public Robot Target;
            // World-space anchor — the latest damaged block's position,
            // refreshed on every fresh hit so the number tracks the
            // chassis as it moves and points at the most-recent impact.
            public Vector3 AnchorPos;
            // Damage running total (in HP).
            public float TotalDamage;
            // Time of last fresh hit. Drives the freeze transition.
            public float LastHitTime;
            // Time we transitioned to frozen, < 0 while still accumulating.
            public float FreezeTime;
            // Cached "F0" render string + the TotalDamage it was built
            // for. Rebuilt only when the total changes (a fresh hit), not
            // per OnGUI event — OnGUI runs 2-6x/frame during combat.
            public float CachedDamage;
            public string CachedText;
        }

        private readonly List<Accumulator> _accumulators = new(16);
        // Pool of reusable Accumulator objects so the steady-state
        // alloc count is bounded — per-event new is the bug we're
        // explicitly avoiding here.
        private readonly Stack<Accumulator> _pool = new(16);

        private Camera _camera;
        private GUIStyle _style;
        private GUIStyle _styleHeavy;
        // One reusable GUIContent for CalcSize so the render loop doesn't
        // allocate a new one per number per OnGUI event.
        private readonly GUIContent _measure = new();

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
            // Drop active accumulators back to the pool — scene
            // reload should not leak Robot references across scenes.
            for (int i = 0; i < _accumulators.Count; i++) _pool.Push(_accumulators[i]);
            _accumulators.Clear();
        }

        private void HandleDamage(BlockBehaviour block, float damage)
        {
            if (block == null || damage < _minDamage) return;
            // Resolve the target chassis. Walk parents — every block
            // belongs to a Robot via the chassis hierarchy. Damage on
            // detached debris (no parent Robot) is ignored: the player
            // doesn't need a number for already-doomed wreckage.
            Robot target = block.GetComponentInParent<Robot>();
            if (target == null) return;

            float now = Time.unscaledTime;
            Accumulator acc = FindActiveForTarget(target);
            if (acc != null)
            {
                acc.TotalDamage += damage;
                acc.AnchorPos = block.transform.position;
                acc.LastHitTime = now;
                return;
            }

            // No active accumulator — try to claim a slot. If we're at
            // the cap, evict the oldest frozen one (they're already on
            // their way out visually, dropping one is invisible).
            if (_accumulators.Count >= _maxAccumulators) EvictOldestFrozen();
            if (_accumulators.Count >= _maxAccumulators) return; // still full of active — drop

            Accumulator next = _pool.Count > 0 ? _pool.Pop() : new Accumulator();
            next.Target = target;
            next.AnchorPos = block.transform.position;
            next.TotalDamage = damage;
            next.LastHitTime = now;
            next.FreezeTime = -1f;
            next.CachedText = null; // force rebuild; a pooled instance may carry a stale string
            _accumulators.Add(next);
        }

        private Accumulator FindActiveForTarget(Robot target)
        {
            for (int i = 0; i < _accumulators.Count; i++)
            {
                Accumulator a = _accumulators[i];
                // == on Robot honours Unity's fake-null check; a
                // destroyed chassis stops matching automatically and
                // its accumulator falls through to the freeze path.
                if (a.Target == target && a.FreezeTime < 0f) return a;
            }
            return null;
        }

        private void EvictOldestFrozen()
        {
            int oldest = -1;
            float oldestFreeze = float.MaxValue;
            for (int i = 0; i < _accumulators.Count; i++)
            {
                Accumulator a = _accumulators[i];
                if (a.FreezeTime >= 0f && a.FreezeTime < oldestFreeze)
                {
                    oldestFreeze = a.FreezeTime;
                    oldest = i;
                }
            }
            if (oldest < 0) return;
            _pool.Push(_accumulators[oldest]);
            _accumulators.RemoveAt(oldest);
        }

        private void OnGUI()
        {
            if (_camera == null) return;
            if (_accumulators.Count == 0) return;

            EnsureStyles();

            float now = Time.unscaledTime;
            for (int i = _accumulators.Count - 1; i >= 0; i--)
            {
                Accumulator a = _accumulators[i];

                // Promote to frozen the first frame past the summation
                // window. Frozen accumulators are no longer matched by
                // FindActiveForTarget so a fresh hit creates a new one.
                if (a.FreezeTime < 0f && now - a.LastHitTime >= _summationWindow)
                {
                    a.FreezeTime = now;
                }

                // Drop frozen accumulators that have animated out.
                if (a.FreezeTime >= 0f && now - a.FreezeTime > _lifetime)
                {
                    _pool.Push(a);
                    _accumulators.RemoveAt(i);
                    continue;
                }

                // Drop accumulators whose chassis is gone (Unity-fake-null).
                if (a.Target == null)
                {
                    _pool.Push(a);
                    _accumulators.RemoveAt(i);
                    continue;
                }

                // Animation phase: 0 while accumulating, 0..1 after
                // freeze. Drives rise + alpha.
                float frozenAge = a.FreezeTime < 0f ? 0f : (now - a.FreezeTime);
                float t = Mathf.Clamp01(frozenAge / Mathf.Max(0.01f, _lifetime));

                Vector3 worldPos = a.AnchorPos + Vector3.up * (t * _riseHeight);
                Vector3 screen = _camera.WorldToScreenPoint(worldPos);
                if (screen.z <= 0f) continue;
                if (screen.x < 0 || screen.x > Screen.width) continue;

                // Active accumulators stay full alpha. Frozen ones fade.
                float alpha = a.FreezeTime < 0f ? 1f : (1f - t);
                bool heavy = a.TotalDamage >= _heavyThreshold;
                Color c = heavy ? _heavyColor : _color;
                c.a *= alpha;

                GUIStyle s = heavy ? _styleHeavy : _style;
                Color savedColor = GUI.color;
                GUI.color = c;
                if (a.CachedText == null || a.CachedDamage != a.TotalDamage)
                {
                    a.CachedDamage = a.TotalDamage;
                    a.CachedText = a.TotalDamage.ToString("F0");
                }
                _measure.text = a.CachedText;
                Vector2 size = s.CalcSize(_measure);
                Rect r = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
                GUI.Label(r, a.CachedText, s);
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
