using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Lives on the chassis-root GameObject (next to <see cref="RobotDrillBinder"/>)
    /// and forwards <c>OnCollisionStay</c> events to the chassis's
    /// <see cref="DrillBlock"/> children. Necessary because Unity routes
    /// physics callbacks to the GameObject hosting the Rigidbody — for a
    /// drill block placed on a chassis cell, that's the chassis root,
    /// not the drill's own child GameObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the <c>TipCollisionForwarder</c> pattern from
    /// <c>Robogame.Movement.TipBlock</c>, adapted for the chassis-level
    /// 1:N case: a chassis may carry multiple drill blocks, and a
    /// collision's contacts may involve any subset of them. The
    /// forwarder builds a per-collider lookup at <see cref="RefreshDrills"/>
    /// time so dispatch is an O(contactCount) hash lookup per fire.
    /// </para>
    /// <para>
    /// Added on demand by <see cref="RobotDrillBinder"/>: the binder
    /// ensures one forwarder exists on the chassis root whenever a
    /// drill block is bound. The forwarder is idempotent — extra calls
    /// to <see cref="RefreshDrills"/> reconcile the lookup against the
    /// current drill set.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DrillCollisionForwarder : MonoBehaviour
    {
        private DrillBlock[] _drills;
        // Each drill may own multiple colliders (the drill's own GameObject
        // collider plus any in children); we map every leaf collider back
        // to its owning DrillBlock so an OnCollisionStay's contacts can
        // be routed in O(1) per contact.
        private readonly Dictionary<Collider, DrillBlock> _colliderToDrill = new();

        private void OnEnable() => RefreshDrills();

        /// <summary>
        /// Rebuild the drill set + collider → drill lookup. Call after
        /// adding or removing drill blocks on the chassis.
        /// </summary>
        public void RefreshDrills()
        {
            _drills = GetComponentsInChildren<DrillBlock>(includeInactive: true);
            _colliderToDrill.Clear();
            for (int i = 0; i < _drills.Length; i++)
            {
                DrillBlock drill = _drills[i];
                if (drill == null) continue;
                Collider[] cols = drill.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int c = 0; c < cols.Length; c++)
                {
                    Collider col = cols[c];
                    if (col == null) continue;
                    _colliderToDrill[col] = drill;
                }
            }
        }

        public int BoundDrillCount => _drills == null ? 0 : _drills.Length;

        private void OnCollisionStay(Collision collision)
        {
            if (_colliderToDrill.Count == 0) return;
            int contactCount = collision.contactCount;
            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint c = collision.GetContact(i);
                DispatchContact(c.thisCollider, collision.collider);
            }
        }

        /// <summary>
        /// Route a single contact pair to the owning drill block (if any).
        /// Public so PlayMode tests can drive synthetic contacts without
        /// having to construct a <see cref="Collision"/> object (Unity's
        /// physics callbacks aren't directly invokable from tests).
        /// </summary>
        public bool DispatchContact(Collider thisCollider, Collider otherCollider)
        {
            if (thisCollider == null || otherCollider == null) return false;
            if (!_colliderToDrill.TryGetValue(thisCollider, out DrillBlock drill)) return false;
            if (drill == null) return false;
            return drill.HandleContact(otherCollider);
        }
    }
}
