using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Simple hitscan weapon. Reads from an <see cref="IInputSource"/> on (or above)
    /// this GameObject, raycasts forward when fire is held, and damages whatever
    /// <see cref="IDamageable"/> it hits — using ring-falloff splash if the hit
    /// belongs to a <see cref="BlockBehaviour"/> on a <see cref="Robot"/>.
    /// </summary>
    /// <remarks>
    /// Lives at the Robot level for now; per-block weapons come later when we
    /// have block prefabs. Uses <see cref="Physics.Raycast"/> on a configurable
    /// layer mask — set <see cref="_hitMask"/> to exclude the firing robot.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HitscanGun : MonoBehaviour
    {
        [Header("Damage")]
        [Tooltip("Per-ring damage applied via BlockGrid splash. Index 0 = direct hit.")]
        [SerializeField] private float[] _splashRings = { 80f, 30f, 10f };

        [Header("Firing")]
        [Tooltip("Seconds between shots while fire is held.")]
        [SerializeField, Min(0.01f)] private float _cooldown = 0.15f;

        [Tooltip("Maximum hit distance.")]
        [SerializeField, Min(1f)] private float _range = 100f;

        [Tooltip("Layers the raycast can hit.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Origin (auto if blank)")]
        [Tooltip("Transform that defines the muzzle position + forward direction. Defaults to this transform.")]
        [SerializeField] private Transform _muzzle;

        [Header("Tracer")]
        [SerializeField] private bool _drawTracer = true;
        [SerializeField, Min(0f)] private float _tracerLifetime = 0.25f;
        [SerializeField, Min(0.001f)] private float _tracerWidth = 0.1f;
        [SerializeField] private Color _tracerHitColor = new Color(1f, 0.25f, 0.1f, 1f);
        [SerializeField] private Color _tracerMissColor = new Color(1f, 0.9f, 0.2f, 1f);

        private IInputSource _input;
        private Robot _ownerRobot;
        private float _nextFireTime;

        // Shared across all hitscan guns: avoid one alloc per shot.
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[16];
        private static readonly Stack<LineRenderer> s_tracerPool = new Stack<LineRenderer>(16);
        private static Material s_tracerMaterial;

        private struct PendingTracer { public LineRenderer Lr; public float ReleaseTime; }
        private static readonly List<PendingTracer> s_activeTracers = new List<PendingTracer>(16);

        private void Awake()
        {
            if (_muzzle == null) _muzzle = transform;
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
        }

        /// <summary>Override the muzzle transform (used by <see cref="WeaponBlock"/> at spawn).</summary>
        public void SetMuzzle(Transform muzzle)
        {
            if (muzzle != null) _muzzle = muzzle;
        }

        private void Update()
        {
            ReleaseExpiredTracers();
            if (_input == null || !_input.FireHeld) return;
            if (Time.time < _nextFireTime) return;
            _nextFireTime = Time.time + _cooldown;
            Fire();
        }

        private static void ReleaseExpiredTracers()
        {
            float now = Time.time;
            for (int i = s_activeTracers.Count - 1; i >= 0; i--)
            {
                if (s_activeTracers[i].ReleaseTime > now) continue;
                LineRenderer lr = s_activeTracers[i].Lr;
                s_activeTracers.RemoveAt(i);
                if (lr == null) continue;
                lr.gameObject.SetActive(false);
                s_tracerPool.Push(lr);
            }
        }

        private void Fire()
        {
            Vector3 origin = _muzzle.position;
            Vector3 direction = _muzzle.forward;

            bool didHit = RaycastIgnoringSelf(origin, direction, _range, out RaycastHit hit);
            Vector3 endPoint = didHit ? hit.point : origin + direction * _range;
            if (didHit) ApplyHit(hit);

            if (_drawTracer)
            {
                SpawnTracer(origin, endPoint, didHit ? _tracerHitColor : _tracerMissColor);
            }
        }

        private void SpawnTracer(Vector3 from, Vector3 to, Color color)
        {
            LineRenderer lr;
            if (s_tracerPool.Count > 0)
            {
                lr = s_tracerPool.Pop();
                lr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Tracer");
                lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.numCapVertices = 2;
                if (s_tracerMaterial == null)
                {
                    s_tracerMaterial = new Material(Shader.Find("Sprites/Default"));
                }
                lr.sharedMaterial = s_tracerMaterial;
            }

            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = _tracerWidth;
            lr.endWidth = _tracerWidth * 0.5f;
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g, color.b, 0f);

            s_activeTracers.Add(new PendingTracer { Lr = lr, ReleaseTime = Time.time + _tracerLifetime });
        }

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
            // RaycastAll + filter so a weapon mounted INSIDE its own chassis
            // (the turret block sits on top of body cubes) doesn't hit its
            // own blocks. Anything belonging to the owning Robot is skipped.
            int count = Physics.RaycastNonAlloc(origin, dir, s_hitBuffer, maxDist, _hitMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_hitBuffer[i];
                if (_ownerRobot != null && h.collider.GetComponentInParent<Robot>() == _ownerRobot) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        private void ApplyHit(RaycastHit hit)
        {
            // Prefer Robot-aware splash so connectivity / mass-loss bookkeeping fires.
            BlockBehaviour block = hit.collider.GetComponentInParent<BlockBehaviour>();
            if (block != null)
            {
                Robot targetRobot = block.GetComponentInParent<Robot>();
                if (targetRobot != null && targetRobot != _ownerRobot && targetRobot.Grid != null)
                {
                    // Use the hit block's actual grid cell as the splash centre.
                    // hit.point sits on a face boundary, so WorldToGrid would
                    // sometimes round to an empty neighbour cell and the splash
                    // would no-op. Going via the block we hit is bulletproof.
                    targetRobot.Grid.ApplySplashDamage(block.GridPosition, _splashRings);
                    return;
                }
            }

            // Fallback: any IDamageable in the hit hierarchy — but never our own.
            IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg == null || _splashRings.Length == 0) return;
            Robot owner = (dmg as Component)?.GetComponentInParent<Robot>();
            if (owner != null && owner == _ownerRobot) return;
            dmg.TakeDamage(_splashRings[0]);
        }
    }
}
