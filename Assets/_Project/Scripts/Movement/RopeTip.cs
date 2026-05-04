using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Marker component on the <b>last</b> segment of a rope chain. Carries a
    /// non-trigger sphere collider so the rope tip physically bounces off the
    /// world (arena geometry, dummies, other chassis) instead of phasing
    /// through everything. Doubles as the future hook for "ropes deal
    /// damage" — currently a stub gated behind <see cref="DealsDamage"/>,
    /// which defaults to <c>false</c>. See <c>docs/PHYSICS_PLAN.md</c>
    /// for the migration plan that flips this on for proper flail combat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why tip-only collision?</b> Two reasons:
    /// </para>
    /// <para>
    /// 1. <i>Cost.</i> A per-segment capsule collider on every rope segment
    /// quadruples the active-collider count for a rotor + ropes loadout,
    /// which is exactly the §16 budget knob that gets tight first as
    /// physics blocks pile on (BEST_PRACTICES §16). Tip-only is one
    /// extra collider per rope, full stop.
    /// </para>
    /// <para>
    /// 2. <i>Failure mode.</i> Per-segment collision under sustained spin
    /// causes the joint solver and the contact solver to fight on every
    /// step — segments lodge against contact normals while joints try to
    /// pull them back, the angular limits get violated, and the chain
    /// "explodes" or jitters. Tip-only sidesteps that entirely: the only
    /// joint→contact coupling is one segment away from the chassis at
    /// most, so contact impulses propagate clean through the angular
    /// limits as the swing they actually represent.
    /// </para>
    /// <para>
    /// <b>Known v0.5 trade-off.</b> A long rope can clip through walls in
    /// the middle while its tip stays outside. The Verlet rope migration
    /// (PHYSICS_PLAN.md) makes per-segment world collision cheap enough
    /// to turn on wholesale; until then, accept the visual artifact.
    /// </para>
    /// <para>
    /// <b>Self-collision.</b> The rope's owning chassis is responsible for
    /// calling <see cref="IgnoreChassisCollisions"/> at build time so the
    /// tip doesn't whack the chassis it's mounted to. This is a one-shot
    /// pass at rope-build cost; PhysX caches the ignore pairs internally.
    /// Detached debris is intentionally NOT in the ignore set — a rope
    /// flailing at chunks of its own former chassis is acceptable
    /// behaviour.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RopeTip : MonoBehaviour
    {
        /// <summary>
        /// When true, <see cref="OnCollisionEnter"/> will apply damage to
        /// any <see cref="IDamageable"/> on the contacted collider, scaled
        /// by relative contact velocity. Currently always <c>false</c> —
        /// gating exists so a future "flail" block can flip this on per
        /// rope without touching the rest of the rope pipeline. See
        /// <c>docs/PHYSICS_PLAN.md</c> §3 for the formula and the
        /// network-authority story.
        /// </summary>
        public bool DealsDamage { get; set; } = false;

        /// <summary>
        /// HP per (m/s of relative contact speed). Stub default; real
        /// numbers come from a damage-budget design pass when ropes
        /// actually do damage. Tunable from the inspector for now so a
        /// future debugging session has a knob to turn before this
        /// graduates to a Tweakable.
        /// </summary>
        [SerializeField] private float _damagePerVelocity = 0.5f;

        /// <summary>
        /// Relative contact speed below which no damage is dealt. Stops
        /// a rope tip resting against a wall from bleeding HP every
        /// physics step.
        /// </summary>
        [SerializeField] private float _minSpeedForDamage = 4f;

        private SphereCollider _collider;

        /// <summary>
        /// Build-time setup called by the rope/rotor that owns this tip.
        /// Adds a non-trigger sphere collider sized to <paramref name="radius"/>
        /// (typically a small multiple of the segment radius so the tip
        /// reads visually as the "weight" at the end of the chain).
        /// </summary>
        public void Initialize(float radius)
        {
            _collider = GetComponent<SphereCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<SphereCollider>();
            _collider.radius = Mathf.Max(0.05f, radius);
            _collider.isTrigger = false;
            // Default friction/bounce material is fine for a v0.5 cosmetic
            // tip — we want it to slide off arena geometry, not stick.

            // Park the tip on Unity's built-in Ignore Raycast layer (2).
            // FollowCamera's obstruction sphere-cast uses
            // ~(1<<2) by default; without this, a tip swinging through
            // the line between target and camera would yank the camera
            // in toward the chassis on every revolution and snap back
            // out as the tip cleared the line — the exact "camera
            // zooms in really close then immediately back out" symptom
            // observed on the Plane (which has a tail rotor) but not
            // the Buggy. Ignore Raycast is purely a query-layer flag,
            // so collision against arena geometry / dummies is
            // unaffected.
            gameObject.layer = 2; // built-in Ignore Raycast
        }

        /// <summary>
        /// Make this tip's collider ignore every collider on
        /// <paramref name="chassisRoot"/> (and its children). Call once
        /// at rope-build time. PhysX caches the ignore pair internally,
        /// so the cost is paid once and amortised across every contact
        /// query for the lifetime of the chassis.
        /// </summary>
        /// <remarks>
        /// Walks <see cref="Component.GetComponentsInChildren{T}(bool)"/>
        /// once and pairs each found collider with <see cref="_collider"/>.
        /// Includes inactive children so a temporarily disabled block
        /// doesn't get re-included on re-enable.
        /// </remarks>
        public void IgnoreChassisCollisions(Transform chassisRoot)
        {
            if (_collider == null) return;
            if (chassisRoot == null) return;
            Collider[] cols = chassisRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || c == _collider) continue;
                Physics.IgnoreCollision(_collider, c, ignore: true);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!DealsDamage) return;
            // STUB: real damage formula and server-authority gating land
            // when the flail block ships. See PHYSICS_PLAN.md §3.
            float speed = collision.relativeVelocity.magnitude;
            if (speed < _minSpeedForDamage) return;

            IDamageable target = collision.collider.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) return;

            float dmg = _damagePerVelocity * (speed - _minSpeedForDamage);
            target.TakeDamage(dmg);
        }
    }
}
