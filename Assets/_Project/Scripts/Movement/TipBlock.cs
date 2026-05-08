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
        [Header("Damage stats (per-tip-block)")]
        [Tooltip("Reduced-mass kinetic energy → HP coefficient (HP per kJ). " +
                 "PHYSICS_PLAN §5: gameplay-observable values must live on the " +
                 "block, not in per-machine Tweakables. Sub-classes (Hook / Mace) " +
                 "tune this in their inspector for per-tip balance.")]
        [SerializeField, Min(0f)] private float _damagePerKj = 2.0f;

        /// <summary>
        /// Effective HP-per-kJ used by the damage path. Defaults to the
        /// SerializeField above; subclasses (e.g. <see cref="HookBlock"/>)
        /// can override to suppress contact damage entirely while
        /// keeping the audio + cooldown plumbing.
        /// </summary>
        protected virtual float DamagePerKj => _damagePerKj;

        [Tooltip("Relative-velocity floor below which contacts deal no damage. " +
                 "Stops idle rope-bumps from chip-damaging blocks.")]
        [SerializeField, Min(0f)] private float _minSpeedForDamage = 4.0f;

        [Tooltip("Per-pair contact cooldown (s). Prevents one bounce from " +
                 "registering five OnCollisionEnter hits on a wide chassis.")]
        [SerializeField, Min(0.02f)] private float _hitCooldown = 0.10f;

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
        private readonly Dictionary<UnityEngine.Object, float> _cooldownByOther = new Dictionary<UnityEngine.Object, float>(8);

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
        /// <remarks>
        /// <c>protected internal virtual</c> so a subclass (e.g.
        /// <see cref="HookBlock"/>) can extend this with grapple-attach
        /// behaviour while still calling <c>base.HandleCollision</c> to
        /// preserve the damage path. The forwarder lives in the same
        /// assembly and dispatches via the base type, so
        /// <c>internal</c> is enough for cross-class access without
        /// breaking external API surface.
        /// </remarks>
        protected internal virtual void HandleCollision(Collision collision)
        {
            if (_hostRb == null) return;
            // Suppress self-damage: don't hit our own chassis.
            Rigidbody otherRb = collision.rigidbody;
            if (otherRb != null && otherRb == _ownerChassisRb) return;

            // Per-pair cooldown. Use the rb when present (so multiple
            // colliders on one chassis dedupe correctly), else fall back
            // to the contact collider (for static geometry).
            UnityEngine.Object key = (UnityEngine.Object)otherRb ?? collision.collider;
            float now = Time.time;
            float cooldown = Mathf.Max(0.02f, _hitCooldown);
            if (_cooldownByOther.TryGetValue(key, out float lastTime) && (now - lastTime) < cooldown)
                return;
            _cooldownByOther[key] = now;

            // Speed gate. Below this we treat the contact as a soft
            // bump — no audio, no damage, no cooldown bookkeeping cost.
            float minSpeed = Mathf.Max(0f, _minSpeedForDamage);
            Vector3 vRel = collision.relativeVelocity;
            float speed = vRel.magnitude;
            if (speed < minSpeed) return;

            // Audio: play the impact "thonk" before the damage branch so
            // a damage-suppressed tip (HookBlock returns 0) still gets
            // the contact sound. Position from the first contact point
            // so the cue spatialises at where the swing actually landed.
            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            AudioRouter.PlayOneShot(AudioCue.TipImpact, contactPoint);

            // Damage. Subclasses can suppress contact damage by
            // overriding DamagePerKj to return 0 — the audio + cooldown
            // paths above still run so the swing reads as a hit.
            float dmgPerKj = Mathf.Max(0f, DamagePerKj);
            if (dmgPerKj <= 0f) return;

            // Reduced mass. Static / kinematic targets collapse μ → m_self
            // since they don't share kinetic energy on impact.
            float mSelf = _hostRb.mass;
            float mOther = (otherRb != null && !otherRb.isKinematic) ? otherRb.mass : float.PositiveInfinity;
            float mu = float.IsPositiveInfinity(mOther) ? mSelf : mSelf * mOther / (mSelf + mOther);
            float energyJ = 0.5f * mu * speed * speed;
            float energyKj = energyJ * 0.001f;
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
            // BlockGrid.PlaceBlock spawns a Unity Cube primitive which
            // ships with a full-cell BoxCollider. The tip block wants its
            // own (smaller / shaped) collider instead — strip the
            // primitive collider so we don't end up with two contact
            // volumes registering independent hits.
            Collider existing = GetComponent<Collider>();
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing);
                else                       DestroyImmediate(existing);
            }
            BuildTipVisual();
        }

        private void IgnoreChassisColliders(Transform chassisRoot)
        {
            // Hook's J-shape ships 3 BoxColliders on the host
            // (shaft + barb arm + barb tip) so the trap volume reads
            // physically. Mace ships 1 sphere. Pair every host collider
            // against every chassis collider so a swinging hook can't
            // bash its own chassis with any of its three faces.
            Collider[] selfCols = GetComponents<Collider>();
            if (selfCols.Length == 0) return;
            Collider[] cols = chassisRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int s = 0; s < selfCols.Length; s++)
            {
                Collider self = selfCols[s];
                if (self == null) continue;
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null || c == self) continue;
                    Physics.IgnoreCollision(self, c, ignore: true);
                }
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
