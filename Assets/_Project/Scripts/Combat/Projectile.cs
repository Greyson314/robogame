using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Self-driven projectile bullet. Has no collider; instead each
    /// <see cref="FixedUpdate"/> sweeps a <see cref="Physics.Raycast"/>
    /// from the previous position to the next so even fast pellets
    /// can't tunnel through a single grid cell. The firing
    /// <see cref="Robot"/> is filtered out so weapons mounted inside
    /// their own chassis don't shoot themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why no <see cref="Rigidbody"/>?</b> Rigidbody collision callbacks
    /// (<c>OnCollisionEnter</c> / <c>OnTriggerEnter</c>) fire on the
    /// physics step *after* the collision and aren't deterministic
    /// across machines — exactly the multiplayer trap our README calls
    /// out. Manual swept raycast applies damage on the same frame the
    /// hit happens and is trivially server-authoritative once we wire
    /// Netcode in: the server runs <see cref="FixedUpdate"/> and clients
    /// just play back tracers.
    /// </para>
    /// <para>
    /// <b>Lifecycle.</b> Spawned + configured by <see cref="ProjectileGun.Fire"/>
    /// via <see cref="Launch"/>, returned to the pool on impact or after
    /// <see cref="MaxLifetimeSeconds"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        public const float MaxLifetimeSeconds = 4f;

        // Reusable raycast buffer — every projectile shares it. Fine because
        // FixedUpdate is single-threaded and we read results synchronously
        // before the next bullet's tick.
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];

        private Vector3 _velocity;
        private float _gravity;
        private float _expireAt;
        private float[] _splashRings;
        private LayerMask _hitMask;
        private Robot _owner;
        private System.Action<Projectile> _onDespawn;
        private TrailRenderer _trail;
        private bool _alive;

        /// <summary>
        /// Configure and start the projectile. Position + forward are set
        /// so any visual children inherit the firing pose; velocity drives
        /// motion from this frame onward.
        /// </summary>
        public void Launch(
            Vector3 origin,
            Vector3 velocity,
            float gravity,
            float[] splashRings,
            LayerMask hitMask,
            Robot owner,
            System.Action<Projectile> onDespawn)
        {
            _velocity = velocity;
            _gravity = gravity;
            _splashRings = splashRings;
            _hitMask = hitMask;
            _owner = owner;
            _onDespawn = onDespawn;
            _expireAt = Time.time + MaxLifetimeSeconds;
            _alive = true;

            transform.position = origin;
            if (velocity.sqrMagnitude > 1e-4f)
                transform.forward = velocity.normalized;

            // Trail carries state across pool checkouts — clear it so the
            // next shot doesn't draw a streak from where the previous
            // bullet expired.
            if (_trail == null) _trail = GetComponent<TrailRenderer>();
            if (_trail != null)
            {
                _trail.Clear();
                _trail.emitting = true;
            }
        }

        private void FixedUpdate()
        {
            if (!_alive) return;

            if (Time.time >= _expireAt)
            {
                Despawn();
                return;
            }

            float dt = Time.fixedDeltaTime;
            // Gravity is opt-in (zero by default for our SMG pellets).
            // Applied at the top of the step so the swept ray uses the
            // post-gravity velocity — small bias but matches the visible
            // arc when gravity > 0.
            if (_gravity > 0f) _velocity += Vector3.down * (_gravity * dt);

            Vector3 prev = transform.position;
            Vector3 step = _velocity * dt;
            float dist = step.magnitude;
            if (dist < 1e-5f)
            {
                // Edge case: stalled bullet. Despawn rather than spin
                // forever — happens only if Launch was called with a
                // zero velocity, which is a caller bug.
                Despawn();
                return;
            }

            Vector3 dir = step / dist;
            if (TryRaycastIgnoringSelf(prev, dir, dist, out RaycastHit hit))
            {
                transform.position = hit.point;
                ApplyHit(hit);
                Despawn();
                return;
            }

            transform.position = prev + step;
            transform.forward = dir;
        }

        /// <summary>Stop emitting and hand back to the pool. Idempotent.</summary>
        private void Despawn()
        {
            if (!_alive) return;
            _alive = false;
            if (_trail != null) _trail.emitting = false;
            _onDespawn?.Invoke(this);
        }

        private bool TryRaycastIgnoringSelf(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, s_hitBuffer, dist, _hitMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_hitBuffer[i];
                // Layer mask is set up to exclude obvious noise; the
                // owner-robot check is the only filter that depends on
                // runtime state and so has to live here.
                if (_owner != null && h.collider.GetComponentInParent<Robot>() == _owner) continue;
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
            // Robot-aware splash first — keeps connectivity / mass
            // bookkeeping correct. The hit block's actual grid cell is
            // the splash centre; hit.point sits on a face boundary and
            // WorldToGrid would sometimes round into an empty neighbour.
            BlockBehaviour block = hit.collider.GetComponentInParent<BlockBehaviour>();
            if (block != null)
            {
                Robot targetRobot = block.GetComponentInParent<Robot>();
                if (targetRobot != null && targetRobot != _owner && targetRobot.Grid != null)
                {
                    targetRobot.Grid.ApplySplashDamage(block.GridPosition, _splashRings);
                    return;
                }
            }

            // Fallback path for non-Robot damageables (training dummies,
            // destructibles). Direct-hit damage only — splash falloff
            // doesn't have a meaningful neighbour graph to walk.
            IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg == null || _splashRings == null || _splashRings.Length == 0) return;
            Robot owner = (dmg as Component)?.GetComponentInParent<Robot>();
            if (owner != null && owner == _owner) return;
            dmg.TakeDamage(_splashRings[0]);
        }
    }
}
