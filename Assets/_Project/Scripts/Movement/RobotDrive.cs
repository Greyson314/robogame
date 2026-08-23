using System.Collections;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Movement
{
    /// <summary>
    /// Chassis-level aggregator for the composite drive. Owns rigidbody
    /// configuration, computes the player's <see cref="AimPoint"/> once,
    /// and dispatches a single <see cref="DriveControl"/> snapshot to every
    /// registered <see cref="IDriveSubsystem"/> per physics step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements <see cref="IMovementProvider"/> so <c>PlayerController</c>
    /// finds it via the existing input → movement seam without changes.
    /// </para>
    /// <para>
    /// Subsystems can live anywhere in the chassis hierarchy — they all
    /// register here. The aggregator never enumerates children itself, so
    /// adding/removing subsystems at runtime (block destruction, in-game
    /// build) is O(1) on the registry side.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RobotDrive : MonoBehaviour, IMovementProvider
    {
        [Tooltip("Optional chassis tuning profile. If assigned, OVERRIDES the inline values below.")]
        [SerializeField] private ChassisTuning _tuning;

        [Header("Chassis")]
        [Tooltip("Centre-of-mass offset (chassis-local). Pulling it down makes ground vehicles tip-resistant.")]
        [SerializeField] private Vector3 _centerOfMassOffset = new Vector3(0f, -0.5f, 0f);

        [Tooltip("Linear damping on the chassis rigidbody.")]
        [SerializeField, Min(0f)] private float _linearDamping = 0.2f;

        [Tooltip("Angular damping on the chassis rigidbody.")]
        [SerializeField, Min(0f)] private float _angularDamping = 2f;

        /// <summary>
        /// Server-authoritative chassis blueprint, set by
        /// <c>ChassisAssembler</c> before the root activates. The carrier
        /// for gameplay-observable drive tuning (damping here; plane/ground
        /// tuning read by the subsystems via this). Null for a RobotDrive
        /// used outside the assembler (tests / hand-built scenes), in which
        /// case the SerializeField defaults — equal to the historical
        /// Tweakable defaults — apply. Movement → Block is an existing
        /// asmdef edge; Movement must NOT reach Robot (Robots → Movement),
        /// so the blueprint rides RobotDrive, not Robot, for subsystems.
        /// </summary>
        private ChassisBlueprint _blueprint;
        public ChassisBlueprint Blueprint
        {
            get => _blueprint;
            set { _blueprint = value; _schemeResolved = false; }
        }

        // TRACE[ADR-0009]: the control scheme is resolved ONCE per chassis
        // from the frozen blueprint (explicit override or composition) and
        // turns the raw axes into a DriveIntent every tick. Set by
        // ChassisAssembler (definition-aware); lazily id-resolved otherwise.
        private ControlScheme _scheme = ControlScheme.Auto;
        private bool _schemeResolved;

        /// <summary>Concrete control scheme in force for this chassis (never Auto once resolved).</summary>
        public ControlScheme Scheme
        {
            get
            {
                if (!_schemeResolved || _scheme == ControlScheme.Auto)
                {
                    _scheme = ControlSchemes.ResolveFromIds(_blueprint);
                    _schemeResolved = true;
                }
                return _scheme;
            }
            set
            {
                _scheme = value == ControlScheme.Auto ? ControlSchemes.ResolveFromIds(_blueprint) : value;
                _schemeResolved = true;
            }
        }

        /// <summary>
        /// The control snapshot from the most recent <see cref="ApplyMovement"/>,
        /// for consumers that are not <see cref="IDriveSubsystem"/>s (passive
        /// aero surfaces serving pitch/roll/yaw demands). Zero intent while
        /// hooked or before the first tick.
        /// </summary>
        public DriveControl LastControl { get; private set; }

        private Vector3 CenterOfMassOffset => _tuning != null ? _tuning.CenterOfMassOffset : _centerOfMassOffset;
        private float LinearDamping  => Blueprint != null ? Blueprint.ChassisDamping.LinearDamping  : _linearDamping;
        private float AngularDamping => Blueprint != null ? Blueprint.ChassisDamping.AngularDamping : _angularDamping;

        [Header("Aim (camera-ray reticle)")]
        [Tooltip("Layers the cursor / reticle can latch onto.")]
        [SerializeField] private LayerMask _aimMask = ~0;

        [Tooltip("Maximum aim distance.")]
        [SerializeField, Min(1f)] private float _aimRange = 300f;

        [Tooltip("Camera used for the aim ray. Defaults to Camera.main.")]
        [SerializeField] private Camera _aimCamera;

        [Header("Diagnostics (temporary)")]
        [Tooltip("Log inertia tensor + COM once after activation, then chassis-local angular velocity every second for ~10s. Used for tilt-bias debugging; remove once resolved.")]
        [SerializeField] private bool _logChassisInertia = false;

        private readonly List<IDriveSubsystem> _subs = new List<IDriveSubsystem>(8);
        private bool _orderDirty;

        private Rigidbody _rb;
        private IInputSource _input;
        private Vector3 _aimPoint;

        public bool IsOperational => isActiveAndEnabled;

        // Hook-suppression counter. Incremented by each hook that latches
        // onto this chassis, decremented on release/destroy. While > 0,
        // ApplyMovement still computes the aim ray but skips the drive-
        // subsystem ticks — chassis Rigidbody is still dynamic (joints
        // pull it around), but no input-driven forces are applied.
        // Session-100 hook QoL: "hooked = movement-disabled," handwaves
        // physics in favor of fun.
        private int _hookedByCount;

        /// <summary>True while at least one external hook holds this chassis. Input is gated; physics remain.</summary>
        public bool IsHooked => _hookedByCount > 0;

        /// <summary>Increment the hook-suppression count. Called by HookBlock.Attach.</summary>
        public void AddHookSuppression() => _hookedByCount++;

        /// <summary>Decrement the hook-suppression count (clamped at 0). Called by HookBlock.Release.</summary>
        public void RemoveHookSuppression()
        {
            if (_hookedByCount > 0) _hookedByCount--;
        }

        /// <summary>Last computed world-space aim target.</summary>
        public Vector3 AimPoint => _aimPoint;

        /// <summary>
        /// Optional override for the aim target. When non-null,
        /// <see cref="AimPoint"/> is forced to this value instead of being
        /// computed from the camera-cursor ray. Used by AI bots so the
        /// chassis's WeaponMount converges on a script-controlled point
        /// (typically the player's chassis position) rather than wherever
        /// the human player's mouse is pointing.
        /// </summary>
        public Vector3? AimPointOverride { get; set; }

        /// <summary>
        /// External multiplier applied to drive-force output this tick.
        /// Defaults to 1 (no penalty). Wired up by
        /// <c>ScrapCarryMovementPenalty</c> (Gameplay tier) so a chassis
        /// hauling scrap moves slower. Keeping the field on RobotDrive
        /// instead of reading <c>Robot.CarryWeightMoveMultiplier</c>
        /// directly preserves the Movement → Robots asmdef separation
        /// (Robots references Movement, not the other way around).
        /// </summary>
        public float CarrySpeedMultiplier { get; set; } = 1f;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.None;
            _rb.linearDamping = LinearDamping;
            _rb.angularDamping = AngularDamping;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // No centerOfMass write here. Robot.RecalculateAggregates is
            // the single source of truth for COM + inertia tensor post the
            // session-25 inertia fix; it pulls the tuning offset from us
            // via GetComponent<RobotDrive>() to keep the chassis-frame
            // alignment intact (asmdef cycle would block the reverse path).

            _input = GetComponentInParent<IInputSource>();
            if (_aimCamera == null) _aimCamera = Camera.main;
            _aimPoint = transform.position + transform.forward * 30f;
        }

        /// <summary>Public accessor for Robot.RecalculateAggregates to read the tuning offset.</summary>
        public Vector3 GetCenterOfMassOffset() => CenterOfMassOffset;

        private void OnEnable()
        {
            // Subscribe to Tweakables.Changed for the dev-only chassis-
            // damping override. In shipping builds DevTuningOverride.Apply*
            // is a compile-stripped no-op, so the subscription is the
            // entire override surface and there's no MP-desync risk.
            // Also re-push once now so a build-mode chassis spawn picks
            // up the override at hand-off.
            Robogame.Core.Tweakables.Changed += PushChassisDamping;
            PushChassisDamping();
            if (_logChassisInertia) StartCoroutine(LogInertiaDiagnostics());
        }

        private void OnDisable()
        {
            Robogame.Core.Tweakables.Changed -= PushChassisDamping;
        }

        private void PushChassisDamping()
        {
            if (_rb == null) return;
            ChassisDampingConfig cfg = Blueprint != null
                ? Blueprint.ChassisDamping
                : new ChassisDampingConfig { LinearDamping = _linearDamping, AngularDamping = _angularDamping };
            Robogame.Block.DevTuningOverride.ApplyChassisDamping(ref cfg);
            _rb.linearDamping = cfg.LinearDamping;
            _rb.angularDamping = cfg.AngularDamping;
        }

        private IEnumerator LogInertiaDiagnostics()
        {
            // Wait one fixed step so RotorBlock.OnEnable has reparented foils
            // off the chassis and PhysX has recomputed inertia from the
            // post-cascade collider distribution.
            yield return new WaitForFixedUpdate();
            if (_rb == null) yield break;

            Vector3 it = _rb.inertiaTensor;
            Quaternion itr = _rb.inertiaTensorRotation;
            Vector3 itrEuler = itr.eulerAngles;
            Debug.Log(
                $"[Diag] {name} mass={_rb.mass:F2}kg " +
                $"COM(local)={_rb.centerOfMass} " +
                $"COM(world)={_rb.worldCenterOfMass} " +
                $"inertiaTensor={it} " +
                $"inertiaTensorRotation(euler)={itrEuler}",
                this);

            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSeconds(1f);
                if (_rb == null) yield break;
                Vector3 omegaWorld = _rb.angularVelocity;
                Vector3 omegaLocal = transform.InverseTransformDirection(omegaWorld);
                Vector3 fwdWorld = transform.forward;
                Vector3 rightWorld = transform.right;
                float bankSin = -Vector3.Dot(rightWorld, Vector3.up);
                Debug.Log(
                    $"[Diag t={i + 1}s] {name} " +
                    $"omega(local)={omegaLocal} (x=pitch y=yaw z=roll) " +
                    $"bankSin(right.y inv)={bankSin:F3} " +
                    $"vel(world)={_rb.linearVelocity}",
                    this);
            }
        }

        // -----------------------------------------------------------------
        // Registry
        // -----------------------------------------------------------------

        /// <summary>
        /// Add a subsystem. Idempotent. Tolerates being called before
        /// <see cref="Awake"/> (subsystems on the same GameObject can OnEnable
        /// in arbitrary order relative to the aggregator).
        /// </summary>
        public void Register(IDriveSubsystem s)
        {
            if (s == null || _subs.Contains(s)) return;
            _subs.Add(s);
            _orderDirty = true;
        }

        /// <summary>Remove a subsystem. Safe to call from OnDisable / OnDestroy.</summary>
        public void Unregister(IDriveSubsystem s)
        {
            if (s == null) return;
            _subs.Remove(s);
        }

        /// <summary>
        /// Redirect every registered subsystem's force target (CSP replay,
        /// ADR-0002). A non-null body routes the next <see cref="ApplyMovement"/>
        /// dispatch onto the prediction mirror instead of the chassis; null
        /// restores the chassis. The caller must restore (null) before the
        /// live FixedUpdate step, and keep the chassis transform synced to the
        /// mirror pose while redirected so subsystems compute force points
        /// correctly. Idempotent; safe with zero subsystems.
        /// </summary>
        public void SetReplayForceTarget(Rigidbody body)
        {
            for (int i = 0; i < _subs.Count; i++)
                _subs[i]?.SetForceTarget(body);
        }

        // -----------------------------------------------------------------
        // Per-physics-step dispatch
        // -----------------------------------------------------------------

        public void ApplyMovement(Vector2 move, float vertical, float deltaTime)
        {
            if (!IsOperational) return;

            if (_orderDirty)
            {
                _subs.Sort(SortByOrder);
                _orderDirty = false;
            }

            if (_aimCamera == null) _aimCamera = Camera.main;
            _aimPoint = ComputeAimPoint();

            // Hook-suppression: a hook latched onto this chassis disables
            // its movement until release/destroy. Aim still updates (so
            // weapons / camera don't freeze mid-grapple), but drive
            // subsystems skip their tick — no thrust, no yaw, no ground
            // drive, no hover thrust. The chassis Rigidbody remains
            // dynamic so the grapple joint can pull it around physically.
            if (IsHooked)
            {
                LastControl = new DriveControl(Vector2.zero, 0f, DriveIntent.Zero, false, _aimPoint, deltaTime, CarrySpeedMultiplier);
                return;
            }

            DriveControl control = new DriveControl(
                move,
                vertical,
                DriveIntent.FromScheme(Scheme, move, vertical),
                _input != null && _input.FireHeld,
                _aimPoint,
                deltaTime,
                CarrySpeedMultiplier);
            LastControl = control;

            for (int i = 0; i < _subs.Count; i++)
            {
                IDriveSubsystem s = _subs[i];
                if (s != null && s.IsOperational) s.Tick(control);
            }
        }

        private static int SortByOrder(IDriveSubsystem a, IDriveSubsystem b) =>
            a.Order.CompareTo(b.Order);

        // -----------------------------------------------------------------
        // Aim
        // -----------------------------------------------------------------

        private static readonly RaycastHit[] s_aimHits = new RaycastHit[16];

        private Vector3 ComputeAimPoint()
        {
            using var _scope = Robogame.Core.PerfMarkers.AimComputeAimPoint.Auto();
            // AI-driven chassis: skip the camera-ray entirely so the player's
            // mouse can't accidentally retarget the bot's gun.
            if (AimPointOverride.HasValue) return AimPointOverride.Value;
            if (_aimCamera == null) return transform.position + transform.forward * 30f;

            Mouse mouse = Mouse.current;
            Vector2 screen = mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Ray ray = _aimCamera.ScreenPointToRay(screen);

            // RaycastNonAlloc + skip self so the cursor doesn't latch onto our chassis.
            int count = Physics.RaycastNonAlloc(ray, s_aimHits, _aimRange, _aimMask, QueryTriggerInteraction.Ignore);
            // Two-tier resolution: prefer damageable targets (chassis,
            // dummies, destructibles) over inert geometry (ground,
            // arena walls). Without this, a ground-vs-ground aim from
            // an above-chassis camera tilts the screen-centre ray
            // through the ground BEFORE reaching the enemy — every
            // shot lands at the player's feet, and the only way to
            // hit is to tilt the camera below the horizon. Walking
            // hits twice keeps the priority logic explicit while
            // staying allocation-free (s_aimHits is a static buffer).
            float bestDamageableDist = float.MaxValue;
            Vector3 bestDamageable = Vector3.zero;
            bool foundDamageable = false;

            float bestDist = float.MaxValue;
            Vector3 best = ray.origin + ray.direction * _aimRange;
            // Cache the chassis grid for the in-loop self-check.
            BlockGrid ourGrid = GetComponent<BlockGrid>();
            for (int i = 0; i < count; i++)
            {
                Collider hitCol = s_aimHits[i].collider;
                // Direct: collider attached to the chassis Rigidbody.
                if (hitCol.attachedRigidbody == _rb) continue;
                // Indirect: collider belongs to a block in our grid that
                // got reparented away (e.g. RotorBlock adopts foils
                // under a kinematic hub at scene root — the foil's
                // collider's attachedRigidbody is the hub, NOT the
                // chassis, so the direct check above misses it). Resolve
                // via the BlockGrid: every chassis block keeps its grid
                // entry regardless of GameObject parent.
                if (ourGrid != null)
                {
                    BlockBehaviour bb = hitCol.GetComponentInParent<BlockBehaviour>();
                    if (bb != null
                        && ourGrid.TryGetBlock(bb.GridPosition, out BlockBehaviour ourBlock)
                        && ourBlock == bb)
                    {
                        continue;
                    }
                }
                float d = s_aimHits[i].distance;
                // Damageable check: any IDamageable in the parent
                // hierarchy. GetComponentInParent walks up; allocation-
                // free for interfaces in modern Unity.
                if (hitCol.GetComponentInParent<Robogame.Core.IDamageable>() != null
                    && d < bestDamageableDist)
                {
                    bestDamageableDist = d;
                    bestDamageable = s_aimHits[i].point;
                    foundDamageable = true;
                }
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s_aimHits[i].point;
                }
            }
            // Damageable wins if found, regardless of whether ground
            // sat in front of it. Falls through to the closest non-
            // self hit (or the ray's endpoint) when nothing damageable
            // is in view.
            return foundDamageable ? bestDamageable : best;
        }
    }
}
