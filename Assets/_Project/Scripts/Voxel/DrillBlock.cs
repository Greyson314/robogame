using Robogame.Core;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// A drill block. When its collider contacts a <see cref="DigChunk"/>,
    /// it emits a <see cref="BrushKind.CapsuleSubtract"/> swept from the
    /// drill tip's previous-tick world position to the current one, with
    /// a constant radius. Carves voxel terrain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 3b — the first gameplay-visible terraforming feature. The
    /// drill is conceptually a "tip-block sibling to HookBlock / MaceBlock"
    /// per TERRAFORMING_PLAN §12, but for Phase 3b we ship a minimal
    /// standalone MonoBehaviour without the rope-adoption plumbing —
    /// the rope path can be layered on later by mirroring TipBlock's
    /// attach/forwarder pattern.
    /// </para>
    /// <para>
    /// Emission rate is gated by <see cref="_emitInterval"/>: even if
    /// Unity fires <c>OnCollisionStay</c> every physics tick (50 Hz at
    /// the default fixed timestep), the drill only emits one brush per
    /// interval. Default 0.033 s ≈ 30 Hz, matching TERRAFORMING_PLAN
    /// §4's drill-tick rate.
    /// </para>
    /// <para>
    /// Audio: <see cref="AudioCue.DrillContact"/> on each emit. VFX:
    /// <see cref="VfxKind.DebrisDust"/> at the drill tip. The audio cue
    /// is new in the catalogue; the library entry is left to the audio
    /// pass to author (per the AUDIO_PLAN.md missing-cue logging path).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DrillBlock : MonoBehaviour
    {
        [Tooltip("Drill-bit radius in metres. Defines the carved tunnel's cross-section.")]
        [SerializeField, Min(0.05f)] private float _radius = 0.5f;

        [Tooltip("Minimum seconds between emitted brush ops. 0.033 ≈ 30 Hz, matching the design's drill tick rate.")]
        [SerializeField, Min(0.005f)] private float _emitInterval = 0.033f;

        private Vector3 _lastTipPosWorld;
        private bool _haveLastTipPos;
        private float _lastEmitTime = float.NegativeInfinity;

        /// <summary>The world-space point that drives capsule emission. Defaults to <c>transform.position</c>.</summary>
        public Vector3 TipWorldPosition => transform.position;

        public float Radius => _radius;

        /// <summary>
        /// Emit a single <see cref="BrushKind.CapsuleSubtract"/> spanning
        /// the drill tip's motion from the previous tick to the current
        /// position. Returns the number of SDF cells changed across all
        /// chunks the brush touched.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="OnCollisionStay"/> in game; also exposed
        /// so PlayMode tests can drive synthetic drill cycles without a
        /// full physics simulation.
        /// </remarks>
        public int Drill(DigZone zone)
        {
            if (zone == null) return 0;

            Vector3 currentTip = TipWorldPosition;
            Vector3 prevTip = _haveLastTipPos ? _lastTipPosWorld : currentTip;

            BrushOp op = new BrushOp
            {
                kind = BrushKind.CapsuleSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(prevTip),
                p1 = Vector3Fixed.FromVector3(currentTip),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(_radius * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };

            int changed = zone.ApplyBrush(op);

            _lastTipPosWorld = currentTip;
            _haveLastTipPos = true;
            _lastEmitTime = Time.time;

            if (changed > 0)
            {
                AudioRouter.PlayOneShot(AudioCue.DrillContact, currentTip);
                VfxSpawner.Spawn(VfxKind.DebrisDust, currentTip, Quaternion.identity, scale: 0.5f);
            }
            return changed;
        }

        private void OnCollisionStay(Collision collision)
        {
            // Standalone-drill path (rare in production: drill not under a
            // chassis Rigidbody). When the drill IS under a chassis,
            // Unity routes physics callbacks to the chassis-root
            // Rigidbody GameObject, not this child — see
            // <see cref="DrillCollisionForwarder"/>.
            HandleContact(collision.collider);
        }

        private void FixedUpdate()
        {
            // Surface-contact-only drilling fails for a body-mounted
            // chassis drill: wheels keep the chassis above the terrain
            // surface, so the drill's BoxCollider doesn't physically
            // intersect the chunk's surface MeshCollider. Poll
            // DigField each fixed step: if the tip sits inside ANY
            // registered DigZone's volume, emit a brush. Brushes on
            // already-exterior cells are no-ops (max-fold idempotent),
            // so this only costs work where it actually carves.
            if (Time.time - _lastEmitTime < _emitInterval) return;
            IDigZone zone = DigField.ZoneAt(transform.position);
            if (zone is DigZone concrete) Drill(concrete);
        }

        /// <summary>
        /// Process a single physics contact with <paramref name="otherCollider"/>.
        /// If the other collider belongs to a <see cref="DigChunk"/>, emits
        /// one <see cref="BrushKind.CapsuleSubtract"/> through the chunk's
        /// parent <see cref="DigZone"/> (throttled to <see cref="_emitInterval"/>).
        /// Returns <c>true</c> if a brush was actually emitted.
        /// </summary>
        /// <remarks>
        /// Public so <see cref="DrillCollisionForwarder"/> can forward
        /// chassis-root <c>OnCollisionStay</c> events to drill blocks
        /// living on child GameObjects (Unity's physics-message routing
        /// only fires on the Rigidbody's GameObject, not children).
        /// </remarks>
        public bool HandleContact(Collider otherCollider)
        {
            if (otherCollider == null) return false;
            if (Time.time - _lastEmitTime < _emitInterval) return false;

            DigChunk chunk = otherCollider.GetComponentInParent<DigChunk>();
            if (chunk == null) return false;
            DigZone zone = chunk.GetComponentInParent<DigZone>();
            if (zone == null) return false;

            Drill(zone);
            return true;
        }

        private void LateUpdate()
        {
            // Track tip position even when not in contact so the first
            // contact's swept capsule covers the actual motion (not a
            // teleport from the previous contact point).
            _lastTipPosWorld = TipWorldPosition;
            _haveLastTipPos = true;
        }
    }
}
