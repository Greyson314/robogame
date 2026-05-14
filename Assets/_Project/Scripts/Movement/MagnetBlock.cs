using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: horseshoe magnet. Sits at the tip of a rope and pulls
    /// enemy chassis Rigidbodies in a sphere toward itself; on contact
    /// it <i>latches</i> via a SpringJoint, after which the chassis can
    /// drag the target around on the rope's length leash. The signature
    /// loop is: plane swings the magnet near a target → field yanks the
    /// target into the magnet → contact latches → plane flies off and
    /// the target trails along behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three force phases, all live concurrently while attached:
    /// </para>
    /// <list type="number">
    ///   <item><b>Pull field</b> — per-FixedUpdate
    ///   <c>Physics.OverlapSphereNonAlloc</c> in the configured radius;
    ///   non-kinematic Rigidbodies get a falloff-scaled
    ///   <see cref="Rigidbody.AddForce"/> toward the magnet. Brings
    ///   approaching targets in.</item>
    ///   <item><b>Spring tether</b> — once a contact lands, a
    ///   <see cref="SpringJoint"/> with rest distance 0 keeps the target
    ///   pinned to the magnet. Bounded force, no break threshold; the
    ///   rope's existing chassis↔tip leash (8000 N spring at total rope
    ///   length) is the actual escape ceiling.</item>
    ///   <item><b>Rope chain</b> — VerletRopeSimulator maintains chain
    ///   length between the chassis and the tip body. When the tip body
    ///   goes non-kinematic (which Attach below does), the simulator
    ///   switches into "pin tip to its PhysX position" mode and the
    ///   chain conforms around the moving tip + target pair.</item>
    /// </list>
    /// <para>
    /// <b>Damage contract.</b> The magnet itself deals zero contact KE
    /// damage (<see cref="DamagePerKj"/> = 0). The base TipBlock cooldown
    /// + audio path still runs for the "thonk" of impact. Damage to
    /// targets emerges naturally from the chassis-drag dynamics —
    /// chassis-ram impulses when the plane bashes the target into walls,
    /// scrap-depot grinders, etc. Magnets pull and hold; they don't
    /// instakill.
    /// </para>
    /// <para>
    /// <b>Self-damage.</b> Session 60 also fixed the long-standing bug
    /// where chassis-side <c>MomentumImpactHandler</c> damaged the tip
    /// block on every contact via its IDamageable fallback path. Tip
    /// blocks are now exempt from that path; they take damage only from
    /// direct ranged hits.
    /// </para>
    /// </remarks>
    public sealed class MagnetBlock : TipBlock
    {
        // Cool ferrite blue-grey for the body, accent cyan for the field
        // poles. Reads against the warm hook / cool mace palette so the
        // player can identify the swung tip at gameplay distance.
        private static readonly Color s_bodyColor = new(0.32f, 0.36f, 0.44f);
        private static readonly Color s_poleColor = new(0.30f, 0.85f, 0.95f);

        // Horseshoe geometry (rope-segment-local space; +Z = down the
        // rope = swing direction, +Y = chassis-forward, +X = right).
        private const float BodyThickness = 0.42f;
        private const float BridgeLength  = 1.40f;
        private const float PoleLength    = 1.70f;
        private const float ColliderRadius = 0.78f;

        // -----------------------------------------------------------------
        // Pull tuning
        // -----------------------------------------------------------------

        [Header("Pull field")]
        [Tooltip("Radius of the pull sphere (m). Enemies whose Rigidbody centroid sits inside this radius are pulled toward the magnet.")]
        [SerializeField, Min(0.5f)] private float _pullRadius = 6.0f;

        [Tooltip("Force magnitude (N) applied per FixedUpdate. Lower than v1's 1500 N — the magnet's purpose is to *guide* the target into the magnet's mouth, not catapult it. 600 N drags a 5 kg dummy at ~120 m/s² peak, which is firm but not catastrophic.")]
        [SerializeField, Min(0f)] private float _pullForce = 600f;

        [Tooltip("Force falloff with distance. 1.0 = linear, 0 = uniform, 2.0 = quadratic.")]
        [SerializeField, Range(0f, 3f)] private float _falloffExponent = 1.0f;

        [Tooltip("Seconds between pull-field VFX pulses. Cosmetic; the actual force is applied every FixedUpdate.")]
        [SerializeField, Min(0.1f)] private float _pulseInterval = 0.35f;

        [Header("Latch (tether spring)")]
        [Tooltip("Spring stiffness of the tether (N/m). Same mechanism as HookBlock — F = spring × distance pulls the target back to the magnet, bounded by stretch alone.")]
        [SerializeField, Min(0f)] private float _tetherSpring = 320f;

        [Tooltip("Damper on the tether (N·s/m). Higher than the hook's because the magnet's pull field already accelerated the target — extra damping bleeds off that approach energy before it oscillates.")]
        [SerializeField, Min(0f)] private float _tetherDamper = 110f;

        [Tooltip("Cooldown after release before the magnet can re-latch. Stops the pull field from immediately re-snagging a target the player just shook off.")]
        [SerializeField, Min(0f)] private float _relatchCooldown = 0.5f;

        // Pre-sized buffer for OverlapSphereNonAlloc so the pull loop
        // doesn't allocate per FixedUpdate. 32 is comfortably above the
        // block count any reasonable target packs.
        private static readonly Collider[] s_overlapBuffer = new Collider[32];

        private float _nextPulseAt = -1f;

        // -----------------------------------------------------------------
        // Latch state (mirrors HookBlock — see that class for the
        // SpringJoint rationale)
        // -----------------------------------------------------------------

        private SpringJoint _tetherJoint;
        private Rigidbody _tetherTarget;
        private float _releaseTime = -999f;

        /// <summary>True while the magnet is physically tethered to a target.</summary>
        public bool IsLatched => _tetherJoint != null;

        /// <summary>The Rigidbody we're tethered to, or null when free.</summary>
        public Rigidbody TetherTarget => _tetherTarget;

        // -----------------------------------------------------------------
        // Visual
        // -----------------------------------------------------------------

        protected override void BuildTipVisual()
        {
            ClearOwnColliders();

            // Bridge — wide flat slab connecting the two poles.
            Vector3 bridgeCentre = new(0f, 0f, BodyThickness * 0.5f);
            Vector3 bridgeSize   = new(BridgeLength, BodyThickness, BodyThickness);
            BuildVisualCube("MagnetBridge", bridgeCentre, bridgeSize, s_bodyColor);

            // Two parallel pole shafts.
            float poleOffsetX = (BridgeLength - BodyThickness) * 0.5f;
            Vector3 northCentre = new( poleOffsetX, 0f, BodyThickness + PoleLength * 0.5f - 0.05f);
            Vector3 southCentre = new(-poleOffsetX, 0f, BodyThickness + PoleLength * 0.5f - 0.05f);
            Vector3 poleSize    = new(BodyThickness, BodyThickness, PoleLength);
            BuildVisualCube("MagnetPoleN", northCentre, poleSize, s_bodyColor);
            BuildVisualCube("MagnetPoleS", southCentre, poleSize, s_bodyColor);

            // Cyan pole tip caps.
            float tipLen = 0.35f;
            float tipCentreZ = BodyThickness + PoleLength - tipLen * 0.5f - 0.05f;
            Vector3 tipSize = new(BodyThickness * 0.95f, BodyThickness * 0.95f, tipLen);
            BuildVisualCube("MagnetPoleTipN", new(poleOffsetX, 0f, tipCentreZ), tipSize, s_poleColor);
            BuildVisualCube("MagnetPoleTipS", new(-poleOffsetX, 0f, tipCentreZ), tipSize, s_poleColor);

            // Single compound sphere covering both poles + bridge.
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = ColliderRadius;
            sc.center = new Vector3(0f, 0f, BodyThickness + PoleLength * 0.4f);
            sc.isTrigger = false;
        }

        private void BuildVisualCube(string name, Vector3 centre, Vector3 size, Color color)
        {
            Transform t = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, name, PrimitiveType.Cube, stripCollider: true);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale    = size;
            Tint(t.GetComponent<Renderer>(), color);
        }

        private void ClearOwnColliders()
        {
            Collider[] existing = GetComponents<Collider>();
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                if (Application.isPlaying) Destroy(existing[i]);
                else                       DestroyImmediate(existing[i]);
            }
        }

        private static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = new();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_AlbedoColor"), color);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"),   color);
            mpb.SetColor(Shader.PropertyToID("_Color"),       color);
            r.SetPropertyBlock(mpb);
        }

        // -----------------------------------------------------------------
        // Damage contract — zero contact KE damage. The base class still
        // runs cooldown bookkeeping + audio so the impact reads as a
        // "thonk", but no HP is applied to the contacted target. The
        // headline value is the *pull + latch*, not the hit; damage
        // emerges from whatever the chassis drags the target through.
        // -----------------------------------------------------------------

        protected override float DamagePerKj => 0f;

        // -----------------------------------------------------------------
        // Tether lifecycle (mirrors HookBlock)
        // -----------------------------------------------------------------

        public override void AttachToHost(Rigidbody hostRb, Rigidbody ownerChassisRb)
        {
            base.AttachToHost(hostRb, ownerChassisRb);
            _releaseTime = -999f;
        }

        public override void DetachFromHost()
        {
            ReleaseTether();
            base.DetachFromHost();
        }

        private void OnDestroy()
        {
            // Mirror HookBlock.OnDestroy — without this, the SpringJoint
            // lives on the rope-tip body even after our BlockBehaviour is
            // destroyed, and the rope keeps yanking the chassis toward a
            // ghost target. ReleaseTether also flips the host body
            // kinematic so the rope returns to simulator-driven flight.
            ReleaseTether();
        }

        protected internal override void HandleCollision(Collision collision)
        {
            // Base handler runs cooldown bookkeeping + plays TipImpact
            // audio. DamagePerKj = 0 so no HP applied.
            base.HandleCollision(collision);

            if (_tetherJoint != null) return;
            if (Time.time < _releaseTime + _relatchCooldown) return;
            Rigidbody targetRb = collision.rigidbody;
            if (targetRb == null || targetRb.isKinematic) return;
            if (targetRb == _ownerChassisRb) return;

            Latch(targetRb, collision.GetContact(0).point);
        }

        private void Latch(Rigidbody targetRb, Vector3 worldContactPoint)
        {
            if (_hostRb == null || _tetherJoint != null) return;

            // Flip the tip body non-kinematic so PhysX can integrate the
            // SpringJoint forces. VerletRopeSimulator auto-detects this
            // via IsTipExternallyConstrained and switches the chain into
            // pinned-tip mode.
            _hostRb.isKinematic = false;

            SpringJoint joint = _hostRb.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = targetRb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor          = _hostRb.transform.InverseTransformPoint(worldContactPoint);
            joint.connectedAnchor = targetRb.transform.InverseTransformPoint(worldContactPoint);
            joint.spring   = _tetherSpring;
            joint.damper   = _tetherDamper;
            joint.minDistance = 0f;
            joint.maxDistance = 0f;
            joint.tolerance   = 0.025f;
            joint.breakForce  = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
            joint.enableCollision     = false;
            joint.enablePreprocessing = false;

            _tetherJoint  = joint;
            _tetherTarget = targetRb;
        }

        /// <summary>Cleanly release any active latch. Public so player input / AI can release on demand.</summary>
        public void ReleaseTether()
        {
            if (_tetherJoint != null)
            {
                if (Application.isPlaying) Destroy(_tetherJoint);
                else                       DestroyImmediate(_tetherJoint);
                _tetherJoint = null;
            }
            if (_hostRb != null) _hostRb.isKinematic = true;
            _tetherTarget = null;
            _releaseTime  = Time.time;
        }

        // -----------------------------------------------------------------
        // Pull field + target-tracking tick
        // -----------------------------------------------------------------

        private void FixedUpdate()
        {
            if (_hostRb == null) return;

            // Latched-target cleanup. The target chassis may have died
            // (HP→0); Unity sets connectedBody to null. Tear down to
            // free the tip and restore simulator-driven flight.
            if (_tetherJoint != null && (_tetherTarget == null || _tetherJoint.connectedBody == null))
            {
                ReleaseTether();
            }

            ApplyPullForces();
            MaybePulseVfx();
        }

        private void ApplyPullForces()
        {
            Vector3 worldOrigin = _hostRb.transform.position;
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldOrigin, _pullRadius, s_overlapBuffer,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = s_overlapBuffer[i];
                if (c == null) continue;
                Rigidbody targetRb = c.attachedRigidbody;
                if (targetRb == null || targetRb.isKinematic) continue;

                // Self-skip: chassis + tip body.
                if (targetRb == _ownerChassisRb) continue;
                if (targetRb == _hostRb) continue;

                // Dedup against earlier buffer entries.
                bool alreadySeen = false;
                for (int j = 0; j < i; j++)
                {
                    Collider prior = s_overlapBuffer[j];
                    if (prior == null) continue;
                    if (prior.attachedRigidbody == targetRb) { alreadySeen = true; break; }
                }
                if (alreadySeen) continue;

                Vector3 delta = worldOrigin - targetRb.worldCenterOfMass;
                float distance = delta.magnitude;
                if (distance < 0.05f) continue;

                float t = 1f - Mathf.Clamp01(distance / _pullRadius);
                float gain = Mathf.Pow(t, Mathf.Max(0.01f, _falloffExponent));
                Vector3 dir = delta / distance;
                targetRb.AddForce(dir * (_pullForce * gain), ForceMode.Force);
            }

            // Clear scratch slots so destroyed colliders don't linger.
            for (int i = 0; i < hitCount; i++) s_overlapBuffer[i] = null;
        }

        private void MaybePulseVfx()
        {
            float now = Time.time;
            if (now < _nextPulseAt) return;
            _nextPulseAt = now + _pulseInterval;
            if (_hostRb == null) return;
            VfxSpawner.Spawn(
                VfxKind.FlipBurst,
                transform.position,
                Quaternion.identity,
                scale: 0.6f);
        }
    }
}
