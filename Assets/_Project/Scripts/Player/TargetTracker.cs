using System;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Single source of truth for "what enemy robot is under the
    /// screen-centre reticle". Owns the one camera-centre raycast
    /// (option B): <see cref="AimReticle"/> reads
    /// <see cref="CurrentTarget"/> instead of running its own ray, so the
    /// per-frame raycast count doesn't grow. Fires
    /// <see cref="TargetChanged"/> only on a state change (not per frame),
    /// which drives the relevance-gated chassis outline.
    /// </summary>
    /// <remarks>
    /// Lives on the camera GameObject alongside
    /// <see cref="FollowCamera"/> / <see cref="AimReticle"/>. Allocation-
    /// free in the steady state (RaycastNonAlloc into a static buffer;
    /// the event fires only on change).
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public sealed class TargetTracker : MonoBehaviour
    {
        [Tooltip("Raycast layers used to detect a damageable under the reticle.")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("Maximum aim-detection distance.")]
        [SerializeField, Min(1f)] private float _aimRange = 300f;

        private Camera _camera;
        private FollowCamera _follow;
        private static readonly RaycastHit[] s_hits = new RaycastHit[8];

        /// <summary>The enemy robot currently under the reticle, or null.</summary>
        public Robot CurrentTarget { get; private set; }

        /// <summary>(previous, next). Fires only when the target changes.</summary>
        public event Action<Robot, Robot> TargetChanged;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            Robot next = Probe();
            // Treat a destroyed/!alive current target as "gone" so the
            // outline releases even if the ray still grazes its corpse.
            if (next != CurrentTarget)
            {
                Robot prev = CurrentTarget;
                CurrentTarget = next;
                TargetChanged?.Invoke(prev, next);
            }
        }

        private Robot Probe()
        {
            if (_camera == null) return null;

            Ray ray = _camera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            int n = Physics.RaycastNonAlloc(ray, s_hits, _aimRange, _targetMask, QueryTriggerInteraction.Ignore);
            if (n == 0) return null;

            Robot localRobot = _follow != null && _follow.Target != null
                ? _follow.Target.GetComponentInParent<Robot>()
                : null;

            float bestDist = float.MaxValue;
            Robot best = null;
            for (int i = 0; i < n; i++)
            {
                ref RaycastHit h = ref s_hits[i];
                if (h.collider == null) continue;
                IDamageable dmg = h.collider.GetComponentInParent<IDamageable>();
                if (dmg == null || !dmg.IsAlive) continue;
                Robot otherRobot = (dmg as Component)?.GetComponentInParent<Robot>();
                if (otherRobot == null) continue;
                if (otherRobot == localRobot) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = otherRobot;
                }
            }
            return best;
        }
    }
}
