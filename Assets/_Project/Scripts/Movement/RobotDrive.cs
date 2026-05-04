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

        private Vector3 CenterOfMassOffset => _tuning != null ? _tuning.CenterOfMassOffset : _centerOfMassOffset;
        private float LinearDamping        => Tweakables.Get(Tweakables.ChassisLinDamp);
        private float AngularDamping       => Tweakables.Get(Tweakables.ChassisAngDamp);

        [Header("Aim (camera-ray reticle)")]
        [Tooltip("Layers the cursor / reticle can latch onto.")]
        [SerializeField] private LayerMask _aimMask = ~0;

        [Tooltip("Maximum aim distance.")]
        [SerializeField, Min(1f)] private float _aimRange = 300f;

        [Tooltip("Camera used for the aim ray. Defaults to Camera.main.")]
        [SerializeField] private Camera _aimCamera;

        private readonly List<IDriveSubsystem> _subs = new List<IDriveSubsystem>(8);
        private bool _orderDirty;

        private Rigidbody _rb;
        private IInputSource _input;
        private Vector3 _aimPoint;

        public bool IsOperational => isActiveAndEnabled;

        /// <summary>Last computed world-space aim target.</summary>
        public Vector3 AimPoint => _aimPoint;

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
            _rb.centerOfMass = CenterOfMassOffset;

            _input = GetComponentInParent<IInputSource>();
            if (_aimCamera == null) _aimCamera = Camera.main;
            _aimPoint = transform.position + transform.forward * 30f;
        }

        private void OnEnable()
        {
            Tweakables.Changed += ApplyTweakables;
        }

        private void OnDisable()
        {
            Tweakables.Changed -= ApplyTweakables;
        }

        private void ApplyTweakables()
        {
            // Rigidbody damping is cached on the body, so we have to push
            // it after every settings change. COM is recomputed by Robot's
            // mass aggregation pipeline on its own cadence.
            if (_rb == null) return;
            _rb.linearDamping = LinearDamping;
            _rb.angularDamping = AngularDamping;
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

            DriveControl control = new DriveControl(
                move,
                vertical,
                _input != null && _input.FireHeld,
                _aimPoint,
                deltaTime);

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
            if (_aimCamera == null) return transform.position + transform.forward * 30f;

            Mouse mouse = Mouse.current;
            Vector2 screen = mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Ray ray = _aimCamera.ScreenPointToRay(screen);

            // RaycastNonAlloc + skip self so the cursor doesn't latch onto our chassis.
            int count = Physics.RaycastNonAlloc(ray, s_aimHits, _aimRange, _aimMask, QueryTriggerInteraction.Ignore);
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
                if (s_aimHits[i].distance < bestDist)
                {
                    bestDist = s_aimHits[i].distance;
                    best = s_aimHits[i].point;
                }
            }
            return best;
        }
    }
}
