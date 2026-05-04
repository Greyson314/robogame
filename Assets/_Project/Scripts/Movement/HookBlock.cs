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
        private const float TipLength   = 1.50f; // upturned barb tip Z extent

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

        public override void AttachToHost(Rigidbody hostRb, Rigidbody ownerChassisRb)
        {
            base.AttachToHost(hostRb, ownerChassisRb);
            // Reset release time so a fresh adoption can grapple immediately.
            _releaseTime = -999f;
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
            _grappleTarget = null;
            _releaseTime   = Time.time;
        }

        private void FixedUpdate()
        {
            // Only do work while grappled. Branch exits immediately
            // for the common "not grappled" case.
            if (_grappleJoint == null) return;

            // Catch two cases the OnJointBreak callback would miss:
            //   1. PhysX broke the joint at end of last fixed step;
            //      the component is already destroyed → ref is null.
            //   2. The target chassis was destroyed (HP→0); Unity
            //      sets connectedBody to null but keeps the joint
            //      alive, which would otherwise NRE on next access.
            // Either way: tear down cleanly + start cooldown.
            if (_grappleTarget == null || _grappleJoint.connectedBody == null)
            {
                Release();
            }
        }
    }
}
