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

        // The joint linking the rope's last segment Rigidbody to the
        // contacted target Rigidbody. Null while not grappled.
        private ConfigurableJoint _grappleJoint;
        // The Rigidbody we're grappled to. Tracked separately from
        // _grappleJoint.connectedBody so the FixedUpdate guard can
        // tell apart "joint exists, target gone" from "no joint."
        private Rigidbody _grappleTarget;
        // Last release time (Time.time). Used to gate immediate
        // re-attach so the player can pull the rope free between swings.
        private float _releaseTime = -999f;

        // The RopeBlock's chassis↔tip ConfigurableJoint, cached at
        // adoption time. While grappled we soften this joint's spring
        // to prevent resonance between two locked-distance constraints
        // (chassis-tip + grapple) on a low-mass tip body — see Attach
        // / Release for the swap rationale.
        private ConfigurableJoint _chassisTipJoint;
        private SoftJointLimitSpring _origChassisSpring;
        private bool _origChassisSpringSaved;
        // Soft values applied while grappled. Tuned so the rope still
        // pulls the chassis back toward the grapple anchor (so the
        // player can swing) but the spring force never spikes hard
        // enough to instantly catapult a low-mass target into the
        // chassis. See the bug writeup in the session-30 addendum.
        private const float GrappledChassisSpring = 600f;
        private const float GrappledChassisDamper = 250f;

        // While grappled, temporarily fatten the tip Rigidbody. The
        // chassis↔tip joint applies a restoring force F to the tip,
        // which a Locked grapple joint would otherwise transmit to the
        // target as full-velocity impulse (target a = F / m_target,
        // unbounded for a low-mass barbell dummy). Heavier tip absorbs
        // the impulse first — its own velocity gain caps the throughput
        // before the constraint solver pushes the target. Restored on
        // Release.
        private const float GrappledTipMass = 25f;
        private float _origTipMass = 0.5f;
        private bool _origTipMassSaved;

        // Tuning. Per PHYSICS_PLAN §1.5 these are NOT Tweakables (they
        // affect gameplay outcomes — whether the hook detaches under a
        // given pull, how often a swung hook can re-grapple). They live
        // as serialized inspector fields for the tuning pass and migrate
        // to per-block blueprint config when PHYSICS_PLAN §6 lands.
        [Header("Grapple")]
        [Tooltip("Joint break force (N). Hook releases when the linear " +
                 "force on the joint exceeds this — acts as a tug-of-war " +
                 "limit so a heavy target can be grappled but not towed " +
                 "indefinitely. PhysX detects the break automatically.")]
        [SerializeField] private float _grappleBreakForce = 1200f;

        [Tooltip("Joint break torque (N·m). Hook releases when angular " +
                 "stress on the joint exceeds this. Mostly cosmetic — " +
                 "angular axes are free, so torque buildup is rare.")]
        [SerializeField] private float _grappleBreakTorque = 800f;

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
            _origChassisSpringSaved = false;
            if (_hostRb == null) return;
            ConfigurableJoint[] joints = _hostRb.GetComponents<ConfigurableJoint>();
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) continue;
                _chassisTipJoint = joints[i];
                _origChassisSpring = joints[i].linearLimitSpring;
                _origChassisSpringSaved = true;
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
        /// Build the grapple joint between the rope's last segment and
        /// <paramref name="targetRb"/>, anchored at the world contact
        /// point so the hook "sticks" exactly where it bit. Linear
        /// motion is locked (the hook doesn't slide along the target);
        /// angular motion is free (the target can spin about the
        /// contact without the hook fighting it). PhysX auto-releases
        /// when the linear force exceeds <see cref="_grappleBreakForce"/>.
        /// </summary>
        private void Attach(Rigidbody targetRb, Vector3 worldContactPoint)
        {
            if (_hostRb == null || _grappleJoint != null) return;

            // The rope's tip-end Rigidbody is kinematic during free flight
            // so the Verlet simulator can drive it without PhysX integrator
            // fighting back. A ConfigurableJoint on a kinematic body acts
            // only as an immovable anchor — the chassis wouldn't be pulled
            // toward the target. Flip back to non-kinematic before adding
            // the joint so PhysX integrates joint forces in both directions.
            _hostRb.isKinematic = false;

            ConfigurableJoint joint = _hostRb.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = targetRb;
            joint.autoConfigureConnectedAnchor = false;

            // Anchor at the world contact point, expressed in each
            // body's local space.
            joint.anchor          = _hostRb.transform.InverseTransformPoint(worldContactPoint);
            joint.connectedAnchor = targetRb.transform.InverseTransformPoint(worldContactPoint);

            // Lock linear motion: the hook bites and holds.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            // Free angular motion: target can spin freely about the
            // contact, hook doesn't torque-lock it into a weird pose.
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;

            // PhysX auto-releases when force / torque exceeds these.
            // Component is destroyed on break; FixedUpdate poll catches
            // the null and re-arms the cooldown.
            joint.breakForce  = _grappleBreakForce;
            joint.breakTorque = _grappleBreakTorque;

            // Don't let the joint pair collide with each other through
            // the joint — the host segment and the target chassis
            // already collide normally via PhysX, and joint-pair
            // collision causes contact storms while grappled.
            joint.enableCollision     = false;
            joint.enablePreprocessing = false;

            _grappleJoint  = joint;
            _grappleTarget = targetRb;

            // Soften the chassis-tip joint while grappled. Two
            // locked-distance constraints (chassis-tip + grapple) on
            // a low-mass tip body compound: the chassis-tip spring
            // (8000 N/m default) applies large restoring forces in
            // a single FixedUpdate step, the grapple joint
            // transmits them as impulses to the target before the
            // grapple's own breakForce check trips, and a low-mass
            // target gets catapulted toward the chassis. Lowering
            // the spring + bumping the damper smears the restoring
            // force over more frames so the system stays inside
            // PhysX's normal force envelope.
            if (_chassisTipJoint != null)
            {
                _chassisTipJoint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = GrappledChassisSpring,
                    damper = GrappledChassisDamper,
                };
            }

            // Temporarily fatten the tip Rigidbody so its inertia
            // absorbs the chassis-tip joint impulse before the locked
            // grapple joint transmits it to the target. Without this,
            // even with the soft spring above, a 0.5 kg tip rigidly
            // coupled to a 1–5 kg target chassis launches that target
            // before forces reach steady state.
            _origTipMass = _hostRb.mass;
            _origTipMassSaved = true;
            _hostRb.mass = GrappledTipMass;
        }

        /// <summary>
        /// Cleanly release any active grapple and start the re-attach
        /// cooldown. Safe to call when not grappled — no-op in that case.
        /// Public so player input wiring (or AI) can release on demand
        /// once that input lands.
        /// </summary>
        public void Release()
        {
            if (_grappleJoint != null)
            {
                if (Application.isPlaying) Destroy(_grappleJoint);
                else                       DestroyImmediate(_grappleJoint);
                _grappleJoint = null;
            }
            // Restore the chassis-tip joint's original spring/damper —
            // we softened it in Attach to prevent grapple resonance.
            if (_chassisTipJoint != null && _origChassisSpringSaved)
            {
                _chassisTipJoint.linearLimitSpring = _origChassisSpring;
            }
            // Restore the tip Rigidbody's original mass.
            if (_hostRb != null && _origTipMassSaved)
            {
                _hostRb.mass = _origTipMass;
                _origTipMassSaved = false;
            }
            // Restore kinematic mode so the simulator owns the body again
            // (matches the inverse of Attach above; see RopeBlock comment
            // on tipRb.isKinematic for the rationale).
            if (_hostRb != null) _hostRb.isKinematic = true;
            _grappleTarget = null;
            _releaseTime   = Time.time;
        }

        private void FixedUpdate()
        {
            // Detect "PhysX broke the joint at end of last fixed step":
            // the component is destroyed, _grappleJoint reads null, but
            // _hostRb is still non-kinematic from when Attach flipped it.
            // Without this branch, the tip would stay non-kinematic
            // forever (leak) after a breakForce-triggered release.
            if (_grappleJoint == null)
            {
                if (_hostRb != null && !_hostRb.isKinematic)
                {
                    // Path: PhysX broke the joint; nobody called Release.
                    // Reverse Attach's state changes here — kinematic
                    // mode, chassis-tip spring, and tip mass.
                    _hostRb.isKinematic = true;
                    if (_chassisTipJoint != null && _origChassisSpringSaved)
                    {
                        _chassisTipJoint.linearLimitSpring = _origChassisSpring;
                    }
                    if (_origTipMassSaved)
                    {
                        _hostRb.mass = _origTipMass;
                        _origTipMassSaved = false;
                    }
                    _grappleTarget = null;
                    _releaseTime = Time.time;
                }
                return;
            }

            // Joint still alive — but the target chassis may have been
            // destroyed (HP→0); Unity sets connectedBody to null and
            // _grappleTarget to fake-null. Tear down cleanly.
            if (_grappleTarget == null || _grappleJoint.connectedBody == null)
            {
                Release();
            }
        }
    }
}
