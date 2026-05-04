using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Base class for placeable blocks that attach to the tip of a rope
    /// (e.g. <see cref="HookBlock"/>, <see cref="MaceBlock"/>) and deal
    /// contact damage on collision per <c>docs/PHYSICS_PLAN.md</c> §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At chassis build time the tip block sits in the grid like any other
    /// block (so connectivity and damage routing flow normally). At
    /// game-start, an adjacent <see cref="RopeBlock"/> finds it via
    /// <see cref="RopeBlock.AdoptAdjacentTipBlock"/>, reparents it under
    /// the rope's last segment, sums its mass into the segment's
    /// rigidbody, and wires a <see cref="TipCollisionForwarder"/> on the
    /// segment that pipes <c>OnCollisionEnter</c> into
    /// <see cref="HandleCollision"/>.
    /// </para>
    /// <para>
    /// Damage formula: <c>dmg = ½ × μ × v_rel² × dmgPerKj × 1e-3</c> where
    /// μ is the reduced mass of the contact (collapses to host segment
    /// mass against static / kinematic targets). Same shape as
    /// <see cref="Combat.MomentumImpactHandler"/> but routed to the
    /// directly-contacted block (not via splash) and tuned via the
    /// <c>Combat.Rope*</c> tweakables. Per-pair cooldown debounces the
    /// multi-fire that <c>OnCollisionEnter</c> can produce on sustained
    /// high-velocity contact.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public abstract class TipBlock : MonoBehaviour
    {
        // The rope segment we've been attached to. Set by RopeBlock when
        // it adopts us. Null while we're still parented in the chassis
        // grid (the inactive "garaged" state).
        protected Rigidbody _hostRb;
        // The chassis Rigidbody our rope is anchored to. Used to suppress
        // self-damage (the tip should never hurt its own ride).
        protected Rigidbody _ownerChassisRb;

        // Per-pair debounce: opponent → last-hit time. Mirrors the
        // structure used by MomentumImpactHandler so a single bounce at
        // high speed doesn't fire OnCollisionEnter five times in a row
        // and instakill a target.
        private readonly Dictionary<Object, float> _cooldownByOther = new Dictionary<Object, float>(8);

        /// <summary>Block mass in kg, read from the underlying BlockBehaviour's definition.</summary>
        public float Mass
        {
            get
            {
                BlockBehaviour bb = GetComponent<BlockBehaviour>();
                return bb != null && bb.Definition != null ? bb.Definition.Mass : 1f;
            }
        }

        /// <summary>
        /// Subclass hook: build the visible mesh + collider for this tip.
        /// Called once during <see cref="Awake"/>. The collider must be
        /// non-trigger and live on the same GameObject (so reparenting
        /// under the rope segment carries it along).
        /// </summary>
        protected abstract void BuildTipVisual();

        /// <summary>
        /// Called by <see cref="RopeBlock"/> once we've been adopted onto
        /// the rope. Subclasses can use this to ignore collisions with
        /// the chassis (so a swinging tip doesn't damage its own ride),
        /// register debug logging, etc.
        /// </summary>
        public virtual void AttachToHost(Rigidbody hostRb, Rigidbody ownerChassisRb)
        {
            _hostRb = hostRb;
            _ownerChassisRb = ownerChassisRb;
            if (hostRb != null && ownerChassisRb != null)
                IgnoreChassisColliders(ownerChassisRb.transform);
        }

        /// <summary>
        /// Called by <see cref="RopeBlock"/> when teardown happens (rope
        /// rebuild, debris detach). Clears the host references so a stale
        /// pointer to a destroyed segment doesn't drive damage logic.
        /// </summary>
        public virtual void DetachFromHost()
        {
            _hostRb = null;
            _ownerChassisRb = null;
            _cooldownByOther.Clear();
        }

        /// <summary>
        /// Called by <see cref="TipCollisionForwarder"/> when the host
        /// segment's <c>OnCollisionEnter</c> fires. Implements the
        /// PHYSICS_PLAN §3 damage path with cooldown + speed gate.
        /// </summary>
        internal void HandleCollision(Collision collision)
        {
            if (_hostRb == null) return;
            // Suppress self-damage: don't hit our own chassis.
            Rigidbody otherRb = collision.rigidbody;
            if (otherRb != null && otherRb == _ownerChassisRb) return;

            // Per-pair cooldown. Use the rb when present (so multiple
            // colliders on one chassis dedupe correctly), else fall back
            // to the contact collider (for static geometry).
            Object key = (Object)otherRb ?? collision.collider;
            float now = Time.time;
            float cooldown = Mathf.Max(0.02f, Tweakables.Get(Tweakables.RopeHitCooldown));
            if (_cooldownByOther.TryGetValue(key, out float lastTime) && (now - lastTime) < cooldown)
                return;
            _cooldownByOther[key] = now;

            // Speed gate.
            float minSpeed = Mathf.Max(0f, Tweakables.Get(Tweakables.RopeMinSpeed));
            Vector3 vRel = collision.relativeVelocity;
            float speed = vRel.magnitude;
            if (speed < minSpeed) return;

            // Reduced mass. Static / kinematic targets collapse μ → m_self
            // since they don't share kinetic energy on impact.
            float mSelf = _hostRb.mass;
            float mOther = (otherRb != null && !otherRb.isKinematic) ? otherRb.mass : float.PositiveInfinity;
            float mu = float.IsPositiveInfinity(mOther) ? mSelf : mSelf * mOther / (mSelf + mOther);
            float energyJ = 0.5f * mu * speed * speed;
            float energyKj = energyJ * 0.001f;
            float dmgPerKj = Mathf.Max(0f, Tweakables.Get(Tweakables.RopeDamagePerKj));
            float damage = energyKj * dmgPerKj;
            if (damage <= 0f) return;

            // Apply to the contacted IDamageable. Walk parents so a
            // collider on a child of a BlockBehaviour resolves to the
            // block itself.
            IDamageable target = collision.collider.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) return;
            target.TakeDamage(damage);
        }

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        protected virtual void Awake()
        {
            BlockVisuals.HideHostMesh(gameObject);
            BuildTipVisual();
        }

        private void IgnoreChassisColliders(Transform chassisRoot)
        {
            Collider self = GetComponent<Collider>();
            if (self == null) return;
            Collider[] cols = chassisRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || c == self) continue;
                Physics.IgnoreCollision(self, c, ignore: true);
            }
        }
    }

    /// <summary>
    /// Internal helper component placed on a rope segment when a
    /// <see cref="TipBlock"/> is adopted as that segment's tip. Forwards
    /// the segment's <c>OnCollisionEnter</c> callback to the tip block
    /// (collisions only fire on the GameObject that owns the Rigidbody,
    /// not on child colliders' GameObjects).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TipCollisionForwarder : MonoBehaviour
    {
        public TipBlock Tip;
        private void OnCollisionEnter(Collision collision)
        {
            if (Tip != null) Tip.HandleCollision(collision);
        }
    }
}
