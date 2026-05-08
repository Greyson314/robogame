using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// A free-body rope that dangles below its host block. Uses a Verlet
    /// particle solver (<see cref="VerletRopeSimulator"/>) for the chain
    /// body; only the hub-end (chassis) and tip-end (Hook / Mace host)
    /// are real Rigidbodies. Per <c>docs/PHYSICS_PLAN.md</c> § 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Migration from joint chains.</b> Prior to this PR the rope was
    /// N rigidbodies + N <see cref="ConfigurableJoint"/>s, which scaled
    /// poorly under stress (joint solver tax) and was unshipable for
    /// MP (N rigid-body poses to replicate per chain per tick). The
    /// Verlet implementation replicates as 2 rigidbody poses + spawn
    /// time data and re-simulates client-side.
    /// </para>
    /// <para>
    /// <b>What still uses real Rigidbodies.</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Hub-end</b>: the chassis itself. Found via
    ///         <c>GetComponentInParent&lt;Rigidbody&gt;()</c> on the rope
    ///         block. The chain's particle 0 is anchored to the host
    ///         cell's top face in chassis-local space.</item>
    ///   <item><b>Tip-end</b>: a fresh Rigidbody at scene root, owned by
    ///         this rope. It hosts the adopted Hook / Mace block + the
    ///         <see cref="TipCollisionForwarder"/>, so contact damage
    ///         + grapple joints work exactly as before. The simulator
    ///         drives this body's position via <c>MovePosition</c> each
    ///         step so PhysX still synthesises a velocity (collision
    ///         resolution against world geometry stays sane).</item>
    /// </list>
    /// <para>
    /// <b>Visual</b>: one cylinder per particle pair, parented to a
    /// container at scene root. <see cref="VerletRopeChain.OnPostSolve"/>
    /// updates their transforms each FixedUpdate after the constraint
    /// solver settles.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class RopeBlock : MonoBehaviour
    {
        [Header("Visual")]
        [Tooltip("Tint applied to the segment cylinders. Defaults to a dark slate; " +
                 "override per-instance for team colours / banners later.")]
        [SerializeField] private Color _segmentColor = new Color(0.18f, 0.20f, 0.22f);

        [Tooltip("Verlet constraint solver iterations per sub-step. 8 handles a 32-segment rope cleanly. " +
                 "Bump if you see visible stretching under high swing rates; drop for cosmetic chains.")]
        [SerializeField, Range(1, 32)] private int _verletIterations = 8;

        [Tooltip("Sub-steps per FixedUpdate. Each sub-step runs the full integrate+constraint pass with " +
                 "dt/N. Higher = stabler when chassis moves fast (no implicit-velocity ringing in PBD). " +
                 "4 is a strong default; 1 disables sub-stepping.")]
        [SerializeField, Range(1, 8)] private int _verletSubSteps = 4;

        [Tooltip("Bending stiffness in [0, 1]. Skip-one constraints pull non-adjacent particles toward " +
                 "2 × segmentLength apart, so the rope drapes into smooth S-curves instead of folding " +
                 "into discrete Z-shapes. 0 = beads-on-a-string. ~0.4 = fluid rope. ~0.8 = stiff cable.")]
        [SerializeField, Range(0f, 1f)] private float _bendingStiffness = 0.4f;

        [Tooltip("If the rope tip ends up more than this multiplier of the rope's rest length away " +
                 "from the chassis anchor, the rope snaps. Releases the adopted tip back to its " +
                 "chassis grid cell first; the rope cell is then removed via the standard damage " +
                 "path so the connectivity flood-fill orphans the tip block as physics debris. " +
                 "Set to a high number (e.g. 10) to effectively disable.")]
        [SerializeField, Min(1f)] private float _maxStretchFactor = 2f;

        // -----------------------------------------------------------------
        // Defaults / per-block resolved live values
        // -----------------------------------------------------------------

        /// <summary>Block-default segment count when the entry's Dims.x is 0.</summary>
        public const int DefaultSegmentCount = 8;
        /// <summary>Min/max for the build-mode variant config slider.</summary>
        public const int MinSegmentCount = 2, MaxSegmentCount = 32;

        // Live geometry. Segment count is per-block (carried on the
        // ChassisBlueprint.Entry that placed this rope) so a player's
        // long-rope grappling hook and short-rope mace coexist on the
        // same chassis. Length / radius / mass / damping remain Tweakables
        // since the user explicitly wants them tweakable mid-match for
        // tuning rope feel.
        private int LiveSegmentCount
        {
            get
            {
                BlockBehaviour bb = GetComponent<BlockBehaviour>();
                int authored = bb != null ? Mathf.RoundToInt(bb.Dims.x) : 0;
                int raw = authored > 0 ? authored : DefaultSegmentCount;
                return Mathf.Clamp(raw, MinSegmentCount, MaxSegmentCount);
            }
        }
        private float LiveSegmentLength  => Mathf.Max(0.05f, Tweakables.Get(Tweakables.RopeSegmentLength));
        private float LiveSegmentRadius  => Mathf.Max(0.01f, Tweakables.Get(Tweakables.RopeSegmentRadius));
        private float LiveSegmentMass    => Mathf.Max(0.001f, Tweakables.Get(Tweakables.RopeSegmentMass));
        private float LiveLinearDamping  => Mathf.Max(0f, Tweakables.Get(Tweakables.RopeLinearDamping));

        // -----------------------------------------------------------------
        // Runtime state
        // -----------------------------------------------------------------

        private VerletRopeChain _chain;
        private GameObject _segmentContainer;       // visual cylinders
        private Transform[] _segmentVisuals;        // one per (P[i], P[i+1])
        private Rigidbody _hubRb;                   // chassis Rigidbody
        private Rigidbody _tipRb;                   // per-rope, scene-root, hosts tip block
        private GameObject _tipGo;
        private SphereCollider _tipCollider;
        private BlockBehaviour _block;
        // Grid subscription used for the late-adoption path: a tip block
        // (Hook/Mace) regenerated by RepairPad after the chassis was
        // already built needs to be hooked onto the rope's tip body.
        // RopeBlock.Build's TryAdoptTipBlock only runs once per OnEnable,
        // so without this BlockPlaced subscription the regenerated hook
        // would sit at its chassis grid cell instead of swinging at the
        // rope tip. Cached so OnDisable can unsubscribe cleanly.
        private BlockGrid _gridSubscribed;
        // Set true on max-stretch break so the per-step distance check
        // doesn't keep firing during the GameObject's destruction frame.
        private bool _broken;
        // Hard distance limit between chassis and tipRb (= total rope
        // length). The Verlet particle simulation enforces chain shape
        // but doesn't transmit force back to the chassis Rigidbody, so
        // without this joint a grappled hook would let the plane fly
        // off forever (particles just stretch indefinitely while the
        // chassis is unconstrained). The ConfigurableJoint provides
        // the physics-side coupling: chassis can move freely up to
        // (totalLen) from the tip; beyond that PhysX yanks it back.
        private ConfigurableJoint _chassisTipJoint;

        // Visual-interpolation buffers. Each FixedUpdate's OnPostSolve
        // shifts the previous physics-step positions into _prevParticleVis
        // and copies the freshly-solved positions into _curParticleVis.
        // LateUpdate lerps between them using Unity's standard
        // interpolation fraction so cylinder visuals flow smoothly with
        // the (Interpolate-mode) chassis + tip Rigidbodies instead of
        // snapping at 50Hz.
        private Vector3[] _prevParticleVis;
        private Vector3[] _curParticleVis;
        private float _lastSolveTime;
        // Reusable per-frame buffer for interpolated positions; sized
        // once at Build to avoid per-frame allocations.
        private Vector3[] _renderParticleScratch;
        // Temporal-smoothed particle positions for cylinder rendering.
        // Absorbs sub-frame jitter that PBD's solver leaves at high
        // damping (no inertia → constraint residuals oscillate frame-
        // to-frame). Smoothing is heaviest near the hub (where the
        // jitter manifests) and almost zero at the tip; the lag from
        // smoothing is well under one render frame and imperceptible.
        // See the LateUpdate comment for the full rationale + the
        // canonical fixes deferred to a future session.
        private Vector3[] _visualSmoothed;

        // Adopted tip block (Hook or Mace) — same lifecycle as before.
        private TipBlock _adoptedTip;
        private Transform _tipOriginalParent;
        private Vector3 _tipOriginalLocalPos;
        private Quaternion _tipOriginalLocalRot;

        // Tracks whether the most recent Build() ran the live (non-kinematic)
        // path or the static (kinematic / garage) path. Update() polls the
        // chassis Rigidbody's isKinematic flag and triggers a Rebuild on
        // transitions — this is what catches the garage's spawn → park
        // sequence (chassis is non-kinematic when blocks first OnEnable, then
        // GarageController.ParkChassis flips it kinematic the same frame).
        private bool _builtKinematic;

        // Property cache for tinting without instantiating a new material.
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_baseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            // The host cube is just a placement / damage hull. Hide its
            // mesh so the rope segments are what the player actually sees.
            BlockVisuals.HideHostMesh(gameObject);
        }

        private void OnEnable()
        {
            Tweakables.Changed += OnTweakablesChanged;
            _block = GetComponent<BlockBehaviour>();
            if (_block != null) _block.DimsChanged += OnBlockDimsChanged;

            // Subscribe to grid placements so a tip block (Hook/Mace)
            // re-placed AFTER initial chassis build (RepairPad regen,
            // future build-mode add) can be adopted onto our tip body.
            // ChassisFactory.Build adds RobotTipBlockBinder before
            // RobotRopeBinder, so by the time BlockPlaced fires for a
            // new tip cell, its TipBlock component is already attached
            // and TryAdoptTipBlock will find it via the standard
            // neighbour scan.
            _gridSubscribed = GetComponentInParent<BlockGrid>();
            if (_gridSubscribed != null) _gridSubscribed.BlockPlaced += OnGridBlockPlaced;

            // Rebuild on every OnEnable so the rope-chassis anchor is
            // fresh against the *current* chassis Rigidbody. Same reason
            // the joint-chain version did this — chassis hot-swap from
            // Robot.CaptureTemplate or respawn invalidates stale anchors.
            Rebuild();
        }

        private void OnBlockDimsChanged(BlockBehaviour _) => Rebuild();

        private void OnDisable()
        {
            Tweakables.Changed -= OnTweakablesChanged;
            if (_block != null) _block.DimsChanged -= OnBlockDimsChanged;
            if (_gridSubscribed != null)
            {
                _gridSubscribed.BlockPlaced -= OnGridBlockPlaced;
                _gridSubscribed = null;
            }
            // Intentionally do NOT destroy the chain here. Robot.CaptureTemplate
            // briefly SetActive(false → true) on the chassis to clone it as a
            // cold-storage template; tearing down here triggers a SetParent
            // on the adopted tip into a transitioning chassis hierarchy
            // (Unity throws). The chain lives at scene root; the cascade
            // doesn't touch it. Real teardown happens in OnDestroy and in
            // explicit Rebuild() calls.
        }

        // OnDestroy can fire while the chassis is mid-tear-down (e.g.
        // GarageController.Respawn → Destroy(Chassis)). Reparenting the
        // adopted tip into a transitioning chassis hierarchy throws —
        // skip the reparent in that path. The tip GameObject is being
        // destroyed alongside the chassis anyway. Mirrors the same fix
        // applied to RotorBlock for foil reparent during chassis destroy.
        private void OnDestroy() => DestroyChain(reparentTip: false);

        private void OnTweakablesChanged() => Rebuild();

        // The host block can be reparented at runtime — Robot.DetachAsDebris
        // hands orphaned blocks their own Rigidbody and reparents to scene
        // root. When that happens our anchor changes; rebuild against it
        // so the rope stays hooked up to whichever rigidbody now owns us.
        private void OnTransformParentChanged() => Rebuild();

        // BlockPlaced subscriber: late-adoption path. When a Hook/Mace is
        // re-placed at our adjacent grid cell after we've already Built,
        // attempt to adopt it onto our tip body. Cheap-fail when the
        // block isn't adjacent or we already have an adopted tip — the
        // 6-direction scan inside TryAdoptTipBlock is itself the source
        // of truth, but the manhattan-distance early-out keeps us from
        // running it for every block placed on the chassis.
        private void OnGridBlockPlaced(BlockBehaviour placed)
        {
            if (placed == null || placed.Definition == null) return;
            if (_broken || _adoptedTip != null || _tipRb == null) return;
            if (_block == null) return;
            // Adjacency: must be a manhattan-1 neighbour of our cell.
            Vector3Int delta = placed.GridPosition - _block.GridPosition;
            int manhattan = Mathf.Abs(delta.x) + Mathf.Abs(delta.y) + Mathf.Abs(delta.z);
            if (manhattan != 1) return;
            // Tip-block filter: only adopt blocks that actually have a
            // TipBlock component (Hook / Mace). Reduces redundant scans
            // for non-tip neighbours like cubes.
            if (placed.GetComponent<TipBlock>() == null) return;

            TryAdoptTipBlock(_tipRb);
        }

        // Per-physics-step max-stretch check. Once the rope's tip body
        // ends up further than _maxStretchFactor × restLength from the
        // chassis hub anchor, snap the rope: the adopted tip is returned
        // to its chassis grid cell, then the rope cell is removed via
        // the standard damage path. Robot.HandleBlockRemoving's
        // connectivity flood-fill orphans the tip block (its only
        // structural neighbour was us) and detaches it as physics
        // debris — clean "rope snapped, hook flew off" outcome.
        private void FixedUpdate()
        {
            if (_broken) return;
            if (_chain == null || _hubRb == null || _tipRb == null) return;

            Vector3 hubWorld = _hubRb.transform.TransformPoint(_chain.HubAnchorLocal);
            Vector3 tipWorld = _tipRb.position;
            float distSqr = (tipWorld - hubWorld).sqrMagnitude;
            float restLength = LiveSegmentCount * LiveSegmentLength;
            if (restLength <= 0f) return;
            float limit = restLength * _maxStretchFactor;
            if (distSqr > limit * limit)
            {
                BreakRope();
            }
        }

        private void BreakRope()
        {
            if (_broken) return;
            _broken = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[RopeBlock] '{name}' broke — tip stretched past {_maxStretchFactor}x rest length.", this);
#endif

            // Hand the hook back to its chassis grid cell BEFORE removing
            // our grid entry. Without this, our own OnDestroy →
            // DestroyChain(reparentTip:false) destroys the hook as a
            // child of the doomed tip body. After ReleaseAdoptedTip(true),
            // the hook is parented at its blueprint cell again; the
            // connectivity-flood-fill from the chassis CPU then orphans
            // it (since its only structural neighbour was us) and
            // Robot.HandleBlockRemoving detaches it as physics debris.
            ReleaseAdoptedTip(reparent: true);

            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid != null && _block != null)
            {
                grid.RemoveBlock(_block.GridPosition);
            }
        }

        private void Update()
        {
            // Cheap safety net for ancestor-rigidbody swaps the parent
            // change callback didn't catch (e.g. the chassis Rigidbody
            // being destroyed mid-flight in some edge case).
            Rigidbody current = GetComponentInParent<Rigidbody>();
            if (current != _hubRb) { Rebuild(); return; }
            // Detect the garage's spawn → park transition. Block OnEnable
            // (and therefore the first Build) runs while the chassis is
            // still non-kinematic, so the live rig builds first; the
            // chassis flips kinematic a moment later via
            // GarageController.ParkChassis, and this catches that. Same
            // path catches the inverse (garage → arena hand-off would
            // be a fresh chassis instance, but if a kinematic chassis is
            // ever flipped non-kinematic in place, we rebuild live).
            if (current != null && current.isKinematic != _builtKinematic) Rebuild();
        }

        // -----------------------------------------------------------------
        // Build / teardown
        // -----------------------------------------------------------------

        private void Rebuild()
        {
            DestroyChain(reparentTip: true);
            Build();
        }

        private void Build()
        {
            _hubRb = GetComponentInParent<Rigidbody>();
            if (_hubRb == null) return; // no chassis to hang from yet

            int N = LiveSegmentCount + 1;            // +1 because particles flank segments
            float segLen = LiveSegmentLength;
            float segRad = LiveSegmentRadius;
            float segMass = LiveSegmentMass;
            float linDamp = LiveLinearDamping;

            _builtKinematic = _hubRb.isKinematic;

            // Default state for the live path: host cell hidden so the
            // dangling chain replaces it visually. The static path
            // re-shows it in BuildStaticVisual below.
            BlockVisuals.SetHostMeshVisible(gameObject, false);

            // Garage / build-mode parking pins the chassis as kinematic +
            // FreezeAll for static inspection. Don't spawn the live Verlet
            // rig + scene-root tip Rigidbody + tip-block adoption in that
            // mode: an adopted Hook / Mace is reparented out of the chassis
            // grid hierarchy, which makes BlockEditor.UpdateTarget reject
            // raycasts on it (its `IsChildOf(chassis)` check fails) and the
            // player can't right-click to remove it. Mirrors the same gate
            // RotorBlock.BuildLiftRig has for foils. The live rig builds
            // fresh on the next non-kinematic spawn — ArenaController calls
            // ChassisFactory.Build on a fresh GameObject, which re-fires
            // OnEnable with a non-kinematic chassis.
            if (_builtKinematic)
            {
                BuildStaticVisual(N, segLen, segRad);
                return;
            }

            // Anchor at the TOP face of the host cell (matches the joint-
            // chain behaviour pre-Verlet so existing builds visually behave
            // the same).
            Vector3 hubAnchorWorld = transform.position + transform.up * 0.5f;
            Vector3 hubAnchorLocal = _hubRb.transform.InverseTransformPoint(hubAnchorWorld);

            // Spawn the tip-end Rigidbody at scene root. This is what
            // hosts the adopted tip block + the collision forwarder.
            // Default sphere collider; replaced when an adopted tip
            // brings its own geometry.
            Vector3 tipSpawnPos = hubAnchorWorld - transform.up * (segLen * (N - 1));
            _tipGo = new GameObject($"RopeTip_{name}");
            _tipGo.transform.position = tipSpawnPos;
            _tipRb = _tipGo.AddComponent<Rigidbody>();
            _tipRb.mass = segMass;
            _tipRb.linearDamping = linDamp;
            _tipRb.angularDamping = 5f;
            // KINEMATIC for free flight. The simulator drives position
            // and rotation via MovePosition / MoveRotation; PhysX does
            // no force or impulse integration in that mode, so the
            // tip's visual is a pure function of the simulator's solve
            // — no chassis-speed-correlated jitter from PhysX integrator
            // overshoot. HookBlock.Attach flips this back to non-
            // kinematic when it adds the grapple joint (so the joint
            // can pull the chassis), and Release flips it back to
            // kinematic when the grapple ends. PhysX still triggers
            // OnCollisionEnter on kinematic bodies that are MovePosition'd
            // through a non-kinematic Rigidbody, so damage forwarding +
            // grapple contact detection both still work.
            _tipRb.isKinematic = true;
            // Interpolation must match the rope cylinders (which have no
            // Rigidbody and therefore render at their freshly-computed
            // particle positions every frame). With Interpolate set, the
            // tip Rigidbody's visual lagged the rope cylinders by one
            // physics step — the hook visibly glitched against the
            // already-rendered rope end. None matches the cylinders.
            // Interpolate to match the chassis Rigidbody's interpolation
            // mode + the visual interpolation we apply to rope cylinders
            // in LateUpdate. Without this, the tip's visual would render
            // at raw physics-step positions while the chassis flows
            // smoothly, producing a 50Hz "snap" cadence on the hook
            // against the smooth chassis.
            _tipRb.interpolation = RigidbodyInterpolation.Interpolate;
            _tipRb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            // Gravity is integrated by the simulator on the tip particle.
            // Leaving it on the Rigidbody too would double-count: the
            // particle falls AND the body falls, the chain stretches as
            // both ends are pulled by gravity and the constraint solver
            // can't resolve. The Rigidbody only exists for collisions +
            // joint anchoring; the simulator drives its position via
            // MovePosition each step.
            _tipRb.useGravity = false;
            // No rotation freeze. Direct rotation writes + FreezeRotation
            // confuse Unity's Rigidbody.Interpolate (it caches rotation
            // history per physics step, but constraint-blocked rotations
            // bypass that path). Simulator uses MoveRotation now, which
            // integrates cleanly through PhysX so Interpolate has clean
            // prev/cur references and the hook visual flows smoothly.
            _tipRb.constraints = RigidbodyConstraints.None;
            // Default tip sphere — replaced by the adopted tip's collider
            // (kept on the tip block GameObject) when adoption succeeds.
            _tipCollider = _tipGo.AddComponent<SphereCollider>();
            _tipCollider.radius = segRad * 1.6f;
            _tipGo.layer = 2; // Ignore Raycast: keeps cursor / aim from latching onto the tip
            // Don't let the tip Rigidbody collide with the chassis — same
            // self-suppression the old default RopeTip did.
            IgnoreCollidersAgainstChassis(_tipCollider, _hubRb.transform);

            // Chassis ↔ Tip distance joint. The Verlet simulator handles
            // the chain SHAPE (catenary curve, swing physics) but does
            // NOT transmit force back to the chassis Rigidbody — particles
            // just enforce their own segment lengths in isolation. Without
            // this physics-side joint, a grappled hook would let the plane
            // fly off forever while the rope visually stretches. The joint
            // is a hard linear-distance limit: chassis can move freely
            // anywhere within (totalRopeLength) of the tip; beyond that
            // PhysX applies the constraint to halt the chassis. Always-
            // on (works in both free-flight and grappled modes; in free
            // flight the simulator keeps the chain inside the limit so
            // the joint is inactive). Linear motion Limited; angular Free
            // so the chain still swings naturally.
            float totalLen = segLen * (N - 1);
            _chassisTipJoint = _tipGo.AddComponent<ConfigurableJoint>();
            _chassisTipJoint.connectedBody = _hubRb;
            _chassisTipJoint.autoConfigureConnectedAnchor = false;
            _chassisTipJoint.anchor          = Vector3.zero;             // tipRb origin
            _chassisTipJoint.connectedAnchor = hubAnchorLocal;           // chassis-local hub attach
            _chassisTipJoint.xMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.yMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.zMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.angularXMotion = ConfigurableJointMotion.Free;
            _chassisTipJoint.angularYMotion = ConfigurableJointMotion.Free;
            _chassisTipJoint.angularZMotion = ConfigurableJointMotion.Free;
            _chassisTipJoint.linearLimit = new SoftJointLimit { limit = totalLen, contactDistance = 0f };
            // Soft spring on the limit gives a slight rubber-band tug
            // instead of a brick-wall halt — matches the "rope stretches
            // a little, then yanks" feel real grappling has. Spring
            // stiffness chosen so a chassis at a few m/s past the limit
            // is decelerated within ~half a second; tune higher for
            // stiffer cables, lower for stretchier ropes.
            _chassisTipJoint.linearLimitSpring = new SoftJointLimitSpring { spring = 8000f, damper = 250f };
            _chassisTipJoint.enableCollision     = false;
            _chassisTipJoint.enablePreprocessing = false;

            // Build the chain.
            _chain = new VerletRopeChain
            {
                Particles = new VerletParticle[N],
                Count = N,
                HubRb = _hubRb,
                HubAnchorLocal = hubAnchorLocal,
                TipRb = _tipRb,
                SegmentLength = segLen,
                LinearDamping = linDamp,
                Iterations = _verletIterations,
                SubSteps = _verletSubSteps,
                BendingStiffness = _bendingStiffness,
                OnPostSolve = OnPostSolve,
            };

            // Initial particle positions: even spacing along chassis-down
            // from the hub anchor to the tip body. Verlet's prevPosition
            // = position seeds zero starting velocity.
            Vector3 down = -transform.up;
            _prevParticleVis = new Vector3[N];
            _curParticleVis = new Vector3[N];
            _renderParticleScratch = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                Vector3 p = hubAnchorWorld + down * (segLen * i);
                _chain.Particles[i].Position = p;
                _chain.Particles[i].PrevPosition = p;
                _prevParticleVis[i] = p;
                _curParticleVis[i] = p;
            }
            _lastSolveTime = Time.fixedTime;
            // Tip rigidbody starts at the last particle.
            _tipRb.position = _chain.Particles[N - 1].Position;

            // Adopt a Hook / Mace neighbour, if present.
            if (TryAdoptTipBlock(_tipRb))
            {
                // Adopted tip handles its own collider; suppress the
                // default sphere we added above.
                if (_tipCollider != null)
                {
                    Destroy(_tipCollider);
                    _tipCollider = null;
                }
            }

            // Visual cylinders.
            BuildVisuals(N - 1, segRad);

            // Register with the simulator. The simulator drives
            // FixedUpdate; this RopeBlock just holds the data + visuals.
            VerletRopeSimulator.GetOrCreate().Register(_chain);
        }

        // Hook called by the simulator after constraints settle each
        // FixedUpdate. Snapshots particle positions into the visual-
        // interpolation buffers; the actual cylinder transform updates
        // happen in LateUpdate so they can flow smoothly with the
        // (Interpolate-mode) chassis + tip Rigidbodies.
        private void OnPostSolve()
        {
            if (_chain == null) return;
            if (_prevParticleVis == null || _curParticleVis == null) return;

            int N = _chain.Count;
            // Promote previous CURRENT to PREVIOUS, then copy fresh
            // post-solve positions into CURRENT.
            for (int i = 0; i < N; i++)
            {
                _prevParticleVis[i] = _curParticleVis[i];
                _curParticleVis[i] = _chain.Particles[i].Position;
            }
            _lastSolveTime = Time.fixedTime;
        }

        // Render visual cylinders at interpolated particle positions.
        // Unity's standard render-time interpolation fraction
        // alpha = (Time.time - Time.fixedTime) / Time.fixedDeltaTime
        // is the same one Rigidbody.Interpolate uses, so the rope
        // visual rate-matches the (interpolated) chassis Rigidbody and
        // the (interpolated) tip Rigidbody — no 50Hz snap cadence.
        private void LateUpdate()
        {
            if (_segmentVisuals == null) return;
            if (_prevParticleVis == null || _curParticleVis == null) return;
            if (_renderParticleScratch == null) return;

            int N = _renderParticleScratch.Length;
            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - _lastSolveTime) / Time.fixedDeltaTime)
                : 1f;
            for (int i = 0; i < N; i++)
            {
                _renderParticleScratch[i] = Vector3.LerpUnclamped(_prevParticleVis[i], _curParticleVis[i], alpha);
            }

            // Anchor render endpoints to the LIVE Rigidbody positions.
            // The simulator runs in FixedUpdate, BEFORE PhysX integrates
            // the chassis for that step, so its particles track the
            // chassis from "one physics step ago." Unity's chassis
            // Interpolate renders one step further forward; without
            // this anchor override, the rope visually trails the chassis
            // attachment by a step's worth of motion. Linear lerp of
            // (hubShift, tipShift) preserves the chain shape while
            // pinning both ends to the live anchors.
            if (_hubRb != null && _tipRb != null && _chain != null && N >= 2)
            {
                Vector3 liveHub = _hubRb.transform.TransformPoint(_chain.HubAnchorLocal);
                Vector3 liveTip = _tipRb.transform.position;
                Vector3 hubShift = liveHub - _renderParticleScratch[0];
                Vector3 tipShift = liveTip - _renderParticleScratch[N - 1];
                for (int i = 0; i < N; i++)
                {
                    float t = (float)i / (N - 1);
                    _renderParticleScratch[i] += Vector3.LerpUnclamped(hubShift, tipShift, t);
                }
            }

            // Visual temporal smoothing. PBD's constraint solver leaves
            // small per-frame residual error in particle positions —
            // particle 1 wants to be exactly segLen from the chassis,
            // but with a moving chassis + finite iterations the actual
            // post-solve position oscillates by a small amount each
            // frame. With high damping that oscillation isn't absorbed
            // by particle inertia (because there is no inertia by
            // design) so it shows up as visible jitter, worst on the
            // hub-side particles where the constraint is tightest.
            //
            // The canonical fixes are: (a) more iterations / smaller
            // sub-steps until the residual is below a visible threshold;
            // (b) XPBD with explicit compliance terms that handle stiff
            // constraints without residual oscillation; (c) a Featherstone-
            // style multibody solver. All three are bigger lifts than
            // the visible-quality bug warrants at this point. The cheap
            // workaround applied here is a per-particle temporal low-
            // pass filter: heavy smoothing near the hub (where jitter
            // manifests), near-zero at the tip (preserves responsiveness
            // for grapple). The induced visual lag is sub-frame and
            // not perceptible.
            //
            // FUTURE: revisit when the Verlet solver gets an XPBD pass.
            // PHYSICS_PLAN § 2 doesn't mandate XPBD but it's the natural
            // next step if rope feel under aggressive damping matters.
            if (_visualSmoothed == null || _visualSmoothed.Length != N)
            {
                _visualSmoothed = new Vector3[N];
                for (int i = 0; i < N; i++) _visualSmoothed[i] = _renderParticleScratch[i];
            }
            for (int i = 0; i < N; i++)
            {
                float distFromHub = N > 1 ? (float)i / (N - 1) : 1f;
                // 0.35 (heaviest near hub) → 0.95 (nearly raw at tip).
                float lerpFactor = Mathf.Lerp(0.35f, 0.95f, distFromHub);
                _visualSmoothed[i] = Vector3.LerpUnclamped(_visualSmoothed[i], _renderParticleScratch[i], lerpFactor);
            }

            for (int i = 0; i < _segmentVisuals.Length; i++)
            {
                Transform t = _segmentVisuals[i];
                if (t == null) continue;
                Vector3 a = _visualSmoothed[i];
                Vector3 b = _visualSmoothed[i + 1];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                t.position = mid;
                t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
                Vector3 s = t.localScale;
                s.y = len * 0.5f;
                t.localScale = s;
            }
        }

        // -----------------------------------------------------------------
        // Adopted tip block (Hook / Mace)
        // -----------------------------------------------------------------

        private bool TryAdoptTipBlock(Rigidbody tipBody)
        {
            BlockBehaviour ropeHost = GetComponent<BlockBehaviour>();
            if (ropeHost == null) return false;
            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid == null) return false;

            Vector3Int[] all =
            {
                new Vector3Int( 0,-1, 0), new Vector3Int( 0, 1, 0),
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };
            TipBlock tip = null;
            foreach (Vector3Int off in all)
            {
                if (!grid.TryGetBlock(ropeHost.GridPosition + off, out BlockBehaviour neighbor)) continue;
                if (neighbor == null) continue;
                tip = neighbor.GetComponent<TipBlock>();
                if (tip != null) break;
            }
            if (tip == null) return false;

            _adoptedTip            = tip;
            _tipOriginalParent     = tip.transform.parent;
            _tipOriginalLocalPos   = tip.transform.localPosition;
            _tipOriginalLocalRot   = tip.transform.localRotation;

            // Reparent under the tip-end Rigidbody.
            tip.transform.SetParent(tipBody.transform, worldPositionStays: false);
            tip.transform.localPosition = Vector3.zero;
            tip.transform.localRotation = Quaternion.identity;

            // Mass: sum the tip block's authored mass into the tip-end
            // body so swing dynamics reflect the weight at the end.
            tipBody.mass += tip.Mass;

            // Forward collisions to the tip block.
            TipCollisionForwarder fwd = tipBody.gameObject.AddComponent<TipCollisionForwarder>();
            fwd.Tip = tip;
            tip.AttachToHost(tipBody, _hubRb);
            return true;
        }

        private void ReleaseAdoptedTip(bool reparent = true)
        {
            if (_adoptedTip == null) return;
            _adoptedTip.DetachFromHost();
            if (reparent)
            {
                if (_tipOriginalParent != null)
                {
                    _adoptedTip.transform.SetParent(_tipOriginalParent, worldPositionStays: false);
                    _adoptedTip.transform.localPosition = _tipOriginalLocalPos;
                    _adoptedTip.transform.localRotation = _tipOriginalLocalRot;
                }
                else
                {
                    _adoptedTip.transform.SetParent(null, worldPositionStays: true);
                }
            }
            // reparent=false: chassis is mid-destroy. The tip block is a
            // chassis-grid child being destroyed alongside the chassis,
            // so leaving its parent as the about-to-die tip-end body is
            // fine — Unity will tear both down together.
            _adoptedTip = null;
            _tipOriginalParent = null;
        }

        // -----------------------------------------------------------------
        // Visuals
        // -----------------------------------------------------------------

        /// <summary>
        /// Static garage-mode visual: a single cylinder hanging from the
        /// host cell's bottom face, length = total rope length. Parented
        /// under the host transform so it tracks the chassis without
        /// needing a Verlet solver. Reuses <see cref="_segmentContainer"/>
        /// so the existing <see cref="DestroyChain"/> teardown path
        /// covers it. No tip Rigidbody, no chassis-tip joint, no
        /// adoption — Hook/Mace stay at their grid cells with native
        /// colliders so build-mode placement and right-click removal
        /// work normally.
        /// </summary>
        private void BuildStaticVisual(int particleCount, float segLen, float segRad)
        {
            // Show the host cell so the player can see where the rope
            // attaches in build mode — the live path hides it because the
            // dangling chain visual replaces the cube; the static path
            // has no chain-mid visual, so the cell mesh IS the visual cue.
            BlockVisuals.SetHostMeshVisible(gameObject, true);

            _segmentContainer = new GameObject($"Rope_{name}_StaticVisual");
            _segmentContainer.transform.SetParent(transform, worldPositionStays: false);
            _segmentContainer.transform.localPosition = Vector3.zero;
            _segmentContainer.transform.localRotation = Quaternion.identity;

            // Geometry: by default the rope dangles to its full live length
            // straight down so the player can see how long the rope will
            // be in flight. If a tip block (Hook / Mace) is placed on an
            // adjacent grid cell, the cylinder instead spans from the rope
            // cell centre to the tip cell centre — visually meeting the
            // tip so the player doesn't see "rope cell, hook one cell
            // below, then 8 cells of dead rope hanging into empty space".
            // The arena's live-rope path adopts the tip and the chain
            // dangles to its full length there; the garage trade-off is
            // visual coherence over scale fidelity.
            float fullLen = segLen * (particleCount - 1);
            Vector3 startLocal = new Vector3(0f, -0.5f, 0f); // bottom face of host cell
            Vector3 endLocal;
            if (TryGetAdjacentTipCellLocal(out Vector3 tipCellLocal))
            {
                // tipCellLocal is the tip block's grid centre expressed in
                // host-local space. Cylinder ends inside the tip cell so
                // the tip block's visual sits at the rope's end without a
                // gap.
                endLocal = tipCellLocal;
            }
            else
            {
                endLocal = new Vector3(0f, -0.5f - fullLen, 0f);
            }

            Vector3 axisLocal = endLocal - startLocal;
            float length = axisLocal.magnitude;
            if (length < 1e-4f)
            {
                // Degenerate: no cylinder needed (host cube already at the
                // tip cell, e.g. tip and rope cells overlap).
                return;
            }

            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Vis_Static";
            Object.Destroy(cyl.GetComponent<Collider>());
            cyl.transform.SetParent(_segmentContainer.transform, worldPositionStays: false);
            // Cylinder primitive: long axis is local +Y, mesh height 2 →
            // localScale.y is HALF the visual length. Centre at the
            // midpoint of (start, end). Rotation aligns local +Y with the
            // start→end direction so the cylinder spans the gap whether
            // the tip is below, beside, or above (sideways tip placements
            // are uncommon but supported by the grid; visualise honestly).
            Vector3 mid = (startLocal + endLocal) * 0.5f;
            cyl.transform.localPosition = mid;
            cyl.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axisLocal / length);
            cyl.transform.localScale    = new Vector3(segRad * 2f, length * 0.5f, segRad * 2f);
            Renderer mr = cyl.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                mpb.SetColor(s_albedoColorId, _segmentColor);
                mpb.SetColor(s_baseColorId,   _segmentColor);
                mpb.SetColor(s_legacyColorId, _segmentColor);
                mr.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// Look for a <see cref="TipBlock"/> in any of the 6 grid neighbours
        /// of this rope's host cell. If found, return the tip cell's
        /// position in host-local space (the cylinder spans from the host
        /// cell to that local position). Mirrors
        /// <see cref="TryAdoptTipBlock"/>'s neighbour scan so the static
        /// visual previews exactly which tip the live path will adopt.
        /// </summary>
        private bool TryGetAdjacentTipCellLocal(out Vector3 tipCellLocal)
        {
            tipCellLocal = default;
            BlockBehaviour ropeHost = GetComponent<BlockBehaviour>();
            if (ropeHost == null) return false;
            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid == null) return false;
            Vector3Int[] all =
            {
                new Vector3Int( 0,-1, 0), new Vector3Int( 0, 1, 0),
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };
            for (int i = 0; i < all.Length; i++)
            {
                Vector3Int neighbourCell = ropeHost.GridPosition + all[i];
                if (!grid.TryGetBlock(neighbourCell, out BlockBehaviour neighbour)) continue;
                if (neighbour == null) continue;
                if (neighbour.GetComponent<TipBlock>() == null) continue;
                // Convert neighbour world position to host-local. The tip
                // cell is one chassis-grid cell away from the rope cell;
                // working in host-local makes the cylinder transform
                // naturally with the chassis without re-deriving every
                // FixedUpdate.
                Vector3 neighbourWorld = neighbour.transform.position;
                tipCellLocal = transform.InverseTransformPoint(neighbourWorld);
                return true;
            }
            return false;
        }

        private void BuildVisuals(int segmentVisualCount, float segRad)
        {
            _segmentContainer = new GameObject($"Rope_{name}_Segments");
            _segmentVisuals = new Transform[segmentVisualCount];
            for (int i = 0; i < segmentVisualCount; i++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.name = $"Vis_{i}";
                Object.Destroy(seg.GetComponent<Collider>());
                seg.transform.SetParent(_segmentContainer.transform, worldPositionStays: false);
                // Cylinder's mesh is height 2 along Y. localScale.x/z =
                // 2*radius gives the rope a real radius; localScale.y is
                // overwritten in OnPostSolve to half the segment length.
                seg.transform.localScale = new Vector3(segRad * 2f, 1f, segRad * 2f);
                Renderer mr = seg.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    mr.GetPropertyBlock(mpb);
                    mpb.SetColor(s_albedoColorId, _segmentColor);
                    mpb.SetColor(s_baseColorId,   _segmentColor);
                    mpb.SetColor(s_legacyColorId, _segmentColor);
                    mr.SetPropertyBlock(mpb);
                }
                _segmentVisuals[i] = seg.transform;
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void IgnoreCollidersAgainstChassis(Collider self, Transform chassisRoot)
        {
            if (self == null || chassisRoot == null) return;
            Collider[] cols = chassisRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || c == self) continue;
                Physics.IgnoreCollision(self, c, ignore: true);
            }
        }

        private void DestroyChain(bool reparentTip)
        {
            // Detach any adopted tip first — its parent (the tip-end body)
            // is about to be destroyed.
            ReleaseAdoptedTip(reparentTip);

            // Unregister from the simulator before tearing down so we
            // don't get one more solve against destroyed targets.
            if (_chain != null)
            {
                VerletRopeSimulator sim = VerletRopeSimulator.GetOrCreate();
                if (sim != null) sim.Unregister(_chain);
                _chain = null;
            }

            if (_tipGo != null)
            {
                if (Application.isPlaying) Destroy(_tipGo);
                else                       DestroyImmediate(_tipGo);
                _tipGo = null;
                _tipRb = null;
                _tipCollider = null;
                _chassisTipJoint = null; // GC'd along with _tipGo
            }

            if (_segmentContainer != null)
            {
                if (Application.isPlaying) Destroy(_segmentContainer);
                else                       DestroyImmediate(_segmentContainer);
                _segmentContainer = null;
                _segmentVisuals = null;
            }

            _hubRb = null;
        }
    }
}
