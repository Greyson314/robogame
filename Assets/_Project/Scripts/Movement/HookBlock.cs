using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: large J-shaped grappling hook. Sized to enclose a
    /// chassis cell (~1 m × 1 m × 1 m) inside its trap zone so the
    /// player can swing the rope under a target's exposed bar / handle
    /// and catch it on the way back up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visual is three cubes — vertical shaft, horizontal barb arm,
    /// vertical barb tip — laid out in the rope segment's local frame
    /// (segment-local +Z = world down, segment-local +Y = chassis
    /// forward). Compound BoxCollider on the host approximates the
    /// J's hit volume so the trap reads physically, not just visually.
    /// </para>
    /// <para>
    /// Damage on contact comes from the base <see cref="TipBlock"/>;
    /// per-block mass lives on the <see cref="BlockDefinition"/> (set
    /// by <c>BlockDefinitionWizard</c>) and feeds the kinetic-energy
    /// formula through <see cref="TipBlock.Mass"/>. The mass differs
    /// from the mace's so identical swing speeds yield different KE.
    /// </para>
    /// </remarks>
    public sealed class HookBlock : TipBlock
    {
        // Rust / iron tone, separate from the rope's slate so the hook
        // reads against the chain at gameplay distance.
        private static readonly Color s_hookColor = new Color(0.45f, 0.32f, 0.18f);

        // Geometry (in segment-local space). Centralised so the visual
        // cubes and the matching BoxColliders stay in sync.
        // Coordinate system: +Z = down the rope (world down at rest);
        //                    +Y = chassis-forward direction;
        //                    +X = chassis-right.
        private const float ThicknessX  = 0.45f; // hook's narrow side
        private const float ThicknessYZ = 0.40f; // bar / arm thickness
        private const float ShaftLength = 1.70f; // vertical shaft Z extent
        private const float ArmLength   = 1.70f; // horizontal barb arm Y extent
        // Barb tip is intentionally ~half the shaft height so the J reads
        // as a J (open mouth above the tip) instead of a U (tip reaches
        // shaft top). The mouth is the gap targets enter through —
        // without a clear gap, the hook's silhouette looks like a closed
        // bracket rather than a hook.
        private const float TipLength   = 0.85f;

        // -----------------------------------------------------------------
        // Grapple state
        // -----------------------------------------------------------------

        // The SpringJoint linking the rope's tip-end body to the contacted
        // target. Session-60 redesign: was a Locked ConfigurableJoint with
        // breakForce, but a locked-motion constraint between two free
        // bodies applies whatever impulse PhysX needs to keep them
        // coincident — those impulses spike enormously under target /
        // chassis acceleration, then trip the breakForce and the grapple
        // snaps. A SpringJoint with rest distance 0 applies a *force*
        // proportional to the current separation: bounded, smooth, no
        // break threshold needed. The rope's own chassis↔tip leash
        // (RopeBlock's ConfigurableJoint at totalRopeLength, 8000 N spring)
        // is what prevents the chassis flying off forever; this joint is
        // just "target wants to be where the tip is."
        private SpringJoint _grappleJoint;
        // The Rigidbody we're grappled to. Tracked separately so the
        // FixedUpdate guard can distinguish "joint exists, target gone"
        // (target chassis was destroyed mid-grapple) from "no joint."
        private Rigidbody _grappleTarget;
        // Last release time (Time.time). Used to gate immediate
        // re-attach so the player can pull the rope free between swings.
        private float _releaseTime = -999f;

        // The RopeBlock's chassis↔tip ConfigurableJoint, cached at
        // adoption time. Read-only — the previous design softened its
        // spring during grapple to fight resonance between two locked
        // constraints; the SpringJoint design doesn't need that
        // band-aid because spring forces are bounded by definition.
        private ConfigurableJoint _chassisTipJoint;

        // Tuning. Per PHYSICS_PLAN §1.5 these are NOT Tweakables (they
        // affect gameplay outcomes — how hard the tether pulls, whether
        // the rope effectively snags a target). Inspector-tweakable for
        // balance; migrate to per-block blueprint config in PHYSICS_PLAN §6.
        [Header("Grapple")]
        [Tooltip("Spring stiffness of the tether (N/m). The target is " +
                 "pulled toward the tip with F = spring × distance. At " +
                 "spring 300 and 1 m separation that's 300 N — enough to " +
                 "drag a 5 kg dummy at ~60 m/s² baseline, gentle enough " +
                 "that the target doesn't get catapulted on first contact.")]
        [SerializeField, Min(0f)] private float _tetherSpring = 300f;

        [Tooltip("Damper on the spring tether (N·s/m). Bleeds off " +
                 "oscillation energy so the target doesn't bob into and " +
                 "out of the tip on every swing.")]
        [SerializeField, Min(0f)] private float _tetherDamper = 80f;

        [Tooltip("Cooldown after a grapple release before the hook can " +
                 "re-attach. Prevents a swinging hook from immediately " +
                 "re-snagging a target the player just pulled free of.")]
        [SerializeField, Min(0f)] private float _reattachCooldown = 0.5f;

        /// <summary>True while the hook is physically attached to a target.</summary>
        public bool IsGrappled => _grappleJoint != null;

        /// <summary>The Rigidbody we're attached to, or null if not grappled.</summary>
        public Rigidbody GrappleTarget => _grappleTarget;

        protected override void BuildTipVisual()
        {
            // Idempotent: clear any prior BoxColliders before adding the
            // new compound. Awake / EnsureRig may run twice in
            // pathological cases (asset reimport, scene reload).
            ClearBoxColliders();

            // Shaft — vertical, going down the rope. Starts at the hook
            // origin (segment centre) and extends +Z by ShaftLength.
            Vector3 shaftCentre = new Vector3(0f, 0f, ShaftLength * 0.5f);
            Vector3 shaftSize   = new Vector3(ThicknessX, ThicknessYZ, ShaftLength);
            BuildVisualCube("HookShaft", shaftCentre, shaftSize);
            AddBoxCollider(shaftCentre, shaftSize);

            // Barb arm — horizontal, sitting under the shaft, extending
            // forward (+Y) by ArmLength. Top face flush with shaft bottom.
            Vector3 armCentre = new Vector3(0f, ArmLength * 0.5f, ShaftLength + ThicknessYZ * 0.5f);
            Vector3 armSize   = new Vector3(ThicknessX, ArmLength, ThicknessYZ);
            BuildVisualCube("HookBarbArm", armCentre, armSize);
            AddBoxCollider(armCentre, armSize);

            // Barb tip — vertical, going back up from the end of the arm.
            // Sits at Y = ArmLength, spans Z from (top of shaft + a sliver)
            // down to flush with the arm's top face, leaving a clear
            // mouth opening at the top of the J.
            float tipCentreZ = ShaftLength - TipLength * 0.5f;
            Vector3 tipCentre = new Vector3(0f, ArmLength, tipCentreZ);
            Vector3 tipSize   = new Vector3(ThicknessX, ThicknessYZ, TipLength);
            BuildVisualCube("HookBarbTip", tipCentre, tipSize);
            AddBoxCollider(tipCentre, tipSize);
        }

        private void BuildVisualCube(string name, Vector3 centre, Vector3 size)
        {
            Transform t = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, name, PrimitiveType.Cube, stripCollider: true);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale    = size;
            Tint(t.GetComponent<Renderer>(), s_hookColor);
        }

        private void AddBoxCollider(Vector3 centre, Vector3 size)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.center = centre;
            bc.size = size;
            bc.isTrigger = false;
        }

        private void ClearBoxColliders()
        {
            BoxCollider[] existing = GetComponents<BoxCollider>();
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
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_AlbedoColor"), color);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"),   color);
            mpb.SetColor(Shader.PropertyToID("_Color"),       color);
            r.SetPropertyBlock(mpb);
        }

        // -----------------------------------------------------------------
        // Grapple lifecycle
        // -----------------------------------------------------------------

        /// <summary>
        /// Hook intentionally suppresses contact damage. The hook's
        /// purpose is to grapple — its KE damage path was killing the
        /// target block on first contact, leaving the joint with
        /// nothing to anchor to. The base class still plays the
        /// TipImpact "thonk" sound and runs the per-pair cooldown;
        /// only the TakeDamage call is skipped. The mace remains the
        /// dedicated contact-damage tip.
        /// </summary>
        protected override float DamagePerKj => 0f;

        public override void AttachToHost(Rigidbody hostRb, Rigidbody ownerChassisRb)
        {
            base.AttachToHost(hostRb, ownerChassisRb);
            // Reset release time so a fresh adoption can grapple immediately.
            _releaseTime = -999f;
            // Cache the chassis↔tip joint that RopeBlock added to our
            // host Rigidbody. There is exactly one at this point — the
            // grapple joint hasn't been added yet. Distinguishing later
            // by connectedBody works too but caching now is cheaper and
            // unambiguous.
            CacheChassisTipJoint();
        }

        private void CacheChassisTipJoint()
        {
            _chassisTipJoint = null;
            if (_hostRb == null) return;
            ConfigurableJoint[] joints = _hostRb.GetComponents<ConfigurableJoint>();
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) continue;
                _chassisTipJoint = joints[i];
                break;
            }
        }

        public override void DetachFromHost()
        {
            // Release the grapple before the host segment goes away.
            // Calling Release while detaching avoids leaving an orphan
            // joint on a soon-to-be-destroyed segment (rope rebuilds
            // happen on tweakable changes etc.).
            Release();
            base.DetachFromHost();
        }

        // Hook GameObject is being destroyed — typically via combat
        // damage on its BlockBehaviour (collision splash on impact, mace
        // contact, etc). The grapple joint we created in Attach lives
        // on the rope's tip-body GameObject, NOT on us, so without an
        // explicit teardown here it survives our destruction and keeps
        // pulling the chassis toward the (now-stale) grapple target.
        // Symptom in session 35 follow-up: "hook disappears, rope
        // remains, plane is yanked back as though attached to nothing."
        // Release also restores the tip body's mass, kinematic state,
        // and the chassis-tip joint's spring — the same teardown a
        // normal R-key release does — so the rope returns to a clean
        // dangling state that a later RepairPad regen can re-adopt.
        private void OnDestroy()
        {
            Release();
        }

        protected internal override void HandleCollision(Collision collision)
        {
            // Damage path runs first, unconditionally. A hook still
            // deals KE damage on impact even when already grappled —
            // the cooldown there is per-pair, so a sustained pull
            // won't keep dealing damage.
            base.HandleCollision(collision);

            // Skip grapple if already attached, on cooldown, or contact
            // is with our own chassis or a non-physics target.
            if (_grappleJoint != null) return;
            if (Time.time < _releaseTime + _reattachCooldown) return;
            Rigidbody targetRb = collision.rigidbody;
            if (targetRb == null) return;
            if (targetRb == _ownerChassisRb) return;

            Attach(targetRb, collision.GetContact(0).point);
        }

        /// <summary>
        /// Build the tether spring between the rope's tip-end body and
        /// <paramref name="targetRb"/>, anchored at the world contact
        /// point so the hook "sticks" exactly where it bit. A SpringJoint
        /// at rest distance 0 pulls the target toward the tip with a
        /// force that scales with separation — bounded, smooth, and
        /// (critically) doesn't need a breakForce because the rope's
        /// existing chassis↔tip linear-limit joint is the actual leash.
        /// </summary>
        /// <remarks>
        /// <b>Why SpringJoint, not ConfigurableJoint:Locked.</b> A locked
        /// constraint on two free bodies applies whatever impulse the
        /// solver needs to keep them coincident. Those impulses spike
        /// arbitrarily high under acceleration (target jinks, chassis
        /// banks), trip the old <c>breakForce</c>, and the grapple
        /// snaps. A spring applies <i>force</i> = stiffness × distance,
        /// so the force envelope is bounded by tether stretch alone —
        /// no impulse spike, no catapult, no resonance.
        /// </remarks>
        private void Attach(Rigidbody targetRb, Vector3 worldContactPoint)
        {
            if (_hostRb == null || _grappleJoint != null) return;

            // The rope's tip-end Rigidbody is kinematic during free flight
            // so the Verlet simulator can drive it via MovePosition. The
            // simulator auto-flips to "pin tip to its PhysX position" mode
            // when the body goes non-kinematic (see
            // VerletRopeSimulator.IsTipExternallyConstrained), so the
            // chain follows the tip while PhysX integrates the spring.
            _hostRb.isKinematic = false;

            SpringJoint joint = _hostRb.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = targetRb;
            joint.autoConfigureConnectedAnchor = false;

            // Anchor at the world contact point, expressed in each
            // body's local space. SpringJoint computes distance between
            // the two anchors and applies F = spring × (distance - minDistance)
            // restoring toward each other; minDistance/maxDistance = 0
            // means "pull together, no slack zone."
            joint.anchor          = _hostRb.transform.InverseTransformPoint(worldContactPoint);
            joint.connectedAnchor = targetRb.transform.InverseTransformPoint(worldContactPoint);

            joint.spring   = _tetherSpring;
            joint.damper   = _tetherDamper;
            joint.minDistance = 0f;
            joint.maxDistance = 0f;
            joint.tolerance   = 0.025f; // small rest band so micro-jitter doesn't oscillate

            // No break force — the chassis↔tip leash in RopeBlock is
            // what stops infinite drag. The spring stays attached even
            // through high tension; release is via R-key or target death.
            joint.breakForce  = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            // Don't let the joint pair collide with each other through
            // the joint — joint-pair collision causes contact storms
            // while two bodies are being actively pulled together.
            joint.enableCollision     = false;
            joint.enablePreprocessing = false;

            _grappleJoint  = joint;
            _grappleTarget = targetRb;

            // No chassis-tip spring softening, no tip mass fattening.
            // Both were band-aids for the locked-joint impulse-spike
            // problem the SpringJoint design avoids by construction.
        }

        /// <summary>
        /// Cleanly release any active grapple and start the re-attach
        /// cooldown. Safe to call when not grappled — no-op in that case.
        /// Public so player input wiring (or AI) can release on demand.
        /// </summary>
        public void Release()
        {
            if (_grappleJoint != null)
            {
                if (Application.isPlaying) Destroy(_grappleJoint);
                else                       DestroyImmediate(_grappleJoint);
                _grappleJoint = null;
            }
            // Restore kinematic mode so the Verlet simulator owns the
            // body again. Matches the inverse of Attach.
            if (_hostRb != null) _hostRb.isKinematic = true;
            _grappleTarget = null;
            _releaseTime   = Time.time;
        }

        private void FixedUpdate()
        {
            if (_grappleJoint == null) return;

            // Target chassis may have been destroyed (HP→0); Unity sets
            // connectedBody to null and _grappleTarget to fake-null.
            // Tear down cleanly so the tip body returns to kinematic
            // and the rope chain resumes simulator-driven flight.
            if (_grappleTarget == null || _grappleJoint.connectedBody == null)
            {
                Release();
            }
        }
    }
}
