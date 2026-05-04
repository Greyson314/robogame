using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Spinning rotor block. Two modes:
    /// </summary>
    /// <list type="number">
    /// <item><description><b>Cosmetic (default).</b> Renders a small mast +
    /// rotating disc with crossed bars on top of the host block. No
    /// Rigidbody, no ropes, no lift. Pure transform writes — effectively
    /// free.</description></item>
    /// <item><description><b>Lift-generating</b> (when
    /// <see cref="GeneratesLift"/> is true). Spawns a single kinematic
    /// <see cref="Rigidbody"/> "hub" at scene root and <i>adopts</i> any
    /// <see cref="AeroSurfaceBlock"/> placed in a grid cell adjacent to
    /// the rotor on a face perpendicular to the spin axis. The adopted
    /// foils are reparented under the hub (preserving world position),
    /// rotated to face the spin tangent with a fixed collective pitch,
    /// and switched into rotor mode via
    /// <see cref="AeroSurfaceBlock.ConfigureRotorMode"/>. PhysX synthesises
    /// tangential blade velocity from the hub's per-step
    /// <see cref="Rigidbody.MoveRotation"/> delta; the foil's own speed²×AoA
    /// math produces lift; lift force lands on the chassis. Cost: one
    /// kinematic rb + reparent of the existing foil transforms — no
    /// extra GameObjects, no joints, no dynamic Rigidbodies.</description></item>
    /// </list>
    /// <remarks>
    /// <para>
    /// As far as the chassis grid is concerned, the rotor is a normal
    /// 6-face cube (see <see cref="BlockGrid.GetNeighbors"/>) — splash
    /// damage, mass, and CPU connectivity all come from the standard
    /// <see cref="BlockBehaviour"/> path. The lift mode is opt-in per
    /// <i>blueprint</i> via <see cref="ChassisBlueprint.RotorsGenerateLift"/>;
    /// flipped on imperatively by <see cref="Gameplay.ChassisFactory"/>
    /// after blocks are placed. Per-rotor opt-in lands when blueprints
    /// can carry per-cell config — see <c>docs/PHYSICS_PLAN.md</c> §2.
    /// </para>
    /// <para>
    /// <b>Rotor + foils are separate parts.</b> The player places a
    /// rotor block and one or more aerofoil blocks adjacent to it in
    /// the garage; at game-start the rotor scans its 4 spin-plane
    /// neighbours and adopts any aerofoils it finds. A bare rotor with
    /// no adjacent foils still spins cosmetically but generates no
    /// lift. Reparenting adopted foils under the hub does NOT remove
    /// them from the chassis <see cref="BlockGrid"/> dictionary —
    /// connectivity, splash, and damage paths still see them at their
    /// original grid positions.
    /// </para>
    /// <para>
    /// <b>No reaction torque.</b> The kinematic hub doesn't receive
    /// forces, and the foils' drag/sideslip terms are suppressed by
    /// <see cref="AeroSurfaceBlock"/>'s rotor-mode branch — a symmetric
    /// blade ring would otherwise dump the rotor's drag torque into the
    /// chassis as anti-torque, which is realistic but not the design
    /// (Robocraft did the same; arcade kinematic rotors don't kick
    /// reaction torque into the airframe).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class RotorBlock : MonoBehaviour
    {
        [Header("Spin")]
        [Tooltip("Local axis on the rotor block to spin around. Y by default — i.e. a top-mounted rotor like a helicopter main rotor.")]
        [SerializeField] private Vector3 _spinAxisLocal = Vector3.up;

        [Header("Lift mode (opt-in)")]
        [Tooltip("Collective pitch baked into each adopted aerofoil's mounting rotation, degrees. Acts as the rotor's fixed AoA — produces lift via the standard AeroSurfaceBlock math when the hub spins. Real helicopters use ~6° at hover.")]
        [SerializeField, Range(0f, 20f)] private float _collectivePitchDeg = 6f;

        /// <summary>
        /// Per-instance RPM override (rev/min). When &gt;= 0, used in place
        /// of the <see cref="Tweakables.RotorRpm"/> global. Set by the
        /// stress-tower spawner so the tower can run at a fixed RPM
        /// independently of the player's chassis rotors. Set to a negative
        /// value (the default) to fall back to the global tweakable.
        /// </summary>
        public float RpmOverride { get; set; } = -1f;

        /// <summary>
        /// When true, this rotor builds a kinematic hub + ring of aerofoil
        /// blades and produces real lift via the standard
        /// <see cref="AeroSurfaceBlock"/> path. When false (default), the
        /// rotor is purely cosmetic. Flip BEFORE the first
        /// <see cref="OnEnable"/> if possible (the binder + chassis
        /// factory does this); flipping at runtime triggers a rebuild.
        /// </summary>
        public bool GeneratesLift
        {
            get => _generatesLift;
            set
            {
                if (_generatesLift == value) return;
                _generatesLift = value;
                if (isActiveAndEnabled) Rebuild();
            }
        }

        private bool _generatesLift = false;

        // Visual rig (children of the host block — pure transform writes).
        private Transform _spinVisual;
        private float _angleRad;

        // Lift-mode physics rig (only when GeneratesLift = true).
        private GameObject _hubGo;
        private Rigidbody  _hub;

        // Aerofoils we've reparented under the hub. We keep enough info
        // to put each one back where we found it on teardown so that a
        // mid-game Rebuild (e.g. parent-change after debris detach)
        // doesn't strand the foil at scene root.
        private struct AdoptedFoil
        {
            public AeroSurfaceBlock Aero;
            public Transform        OriginalParent;
            public Vector3          OriginalLocalPos;
            public Quaternion       OriginalLocalRot;
        }
        private readonly List<AdoptedFoil> _adoptedFoils = new List<AdoptedFoil>();

        // Property cache.
        private static readonly int s_albedoColorId   = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_baseColorId     = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId   = Shader.PropertyToID("_Color");
        private static readonly int s_emissionColorId = Shader.PropertyToID("_EmissionColor");

        // Visual hub signals "this thing spins" via cyan accents — same
        // tech-energy palette token the CPU beacon uses, deliberately. A
        // rotor is functionally a moving part and the hue cues the
        // player to expect motion on placement.
        private static readonly Color s_hubColor   = new Color(0.20f, 0.85f, 0.95f);
        private static readonly Color s_mastColor  = new Color(0.30f, 0.32f, 0.36f);
        private static readonly Color s_barColor   = new Color(0.25f, 0.27f, 0.30f);
        private static readonly Color s_bladeColor = new Color(0.22f, 0.24f, 0.28f);

        // Spin pivot offset above the host cube top face. Matches the CPU
        // beacon mast height so a rotor placed next to a CPU reads as the
        // same vocabulary of "block + thing on top."
        private const float MastHeight = 0.55f;
        private const float SpinHeight = 0.78f;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            BlockVisuals.HideHostMesh(gameObject);
            BuildBlockVisual();
        }

        private void OnEnable()
        {
            BuildLiftRig();
        }

        private void OnDisable()
        {
            DestroyLiftRig();
        }

        private void OnDestroy() => DestroyLiftRig();

        // The host block can be reparented at runtime (Robot.DetachAsDebris
        // hands orphaned blocks their own Rigidbody). Rebuild the lift rig
        // so it tracks the new ancestor body.
        private void OnTransformParentChanged()
        {
            if (isActiveAndEnabled) Rebuild();
        }

        private void Rebuild()
        {
            DestroyLiftRig();
            BuildLiftRig();
        }

        // -----------------------------------------------------------------
        // Live values
        // -----------------------------------------------------------------

        private float LiveRpm => RpmOverride >= 0f ? RpmOverride : Tweakables.Get(Tweakables.RotorRpm);

        // -----------------------------------------------------------------
        // Visual rig (always built — even cosmetic-only rotors show this)
        // -----------------------------------------------------------------

        private void BuildBlockVisual()
        {
            // Mast — small dark cylinder rising from the cube top. Same
            // visual vocabulary as the CPU beacon mast (block + stem +
            // tip), but matte and shorter so the cyan hub on top reads
            // as the loud bit, not the mast.
            Transform mast = BlockVisuals.GetOrCreatePrimitiveChild(transform, "RotorMast", PrimitiveType.Cylinder);
            mast.localScale    = new Vector3(0.18f, 0.18f, 0.18f);
            mast.localPosition = new Vector3(0f, MastHeight, 0f);

            // Spin pivot — empty transform we rotate per-frame. Holds
            // the visible hub disc + bars; this is the only thing that
            // moves visually on a cosmetic rotor.
            Transform spin = BlockVisuals.GetOrCreateChild(transform, "RotorSpin");
            spin.localPosition = new Vector3(0f, SpinHeight, 0f);
            spin.localRotation = Quaternion.identity;

            // Visual hub disc — flat cyan puck.
            Transform disc = BlockVisuals.GetOrCreatePrimitiveChild(spin, "Disc", PrimitiveType.Cylinder);
            disc.localScale    = new Vector3(0.35f, 0.05f, 0.35f);
            disc.localPosition = Vector3.zero;

            // Two intersecting bars give the spinning rig a clear
            // direction-of-rotation read at a glance. Cube primitives,
            // not cylinders, so the silhouette stays angular.
            Transform barA = BlockVisuals.GetOrCreatePrimitiveChild(spin, "BarA", PrimitiveType.Cube);
            barA.localScale    = new Vector3(1.30f, 0.07f, 0.10f);
            barA.localPosition = Vector3.zero;

            Transform barB = BlockVisuals.GetOrCreatePrimitiveChild(spin, "BarB", PrimitiveType.Cube);
            barB.localScale    = new Vector3(0.10f, 0.07f, 1.30f);
            barB.localPosition = Vector3.zero;

            Tint(mast.GetComponent<Renderer>(), s_mastColor, Color.black);
            Tint(disc.GetComponent<Renderer>(), s_hubColor,  s_hubColor * 0.4f);
            Tint(barA.GetComponent<Renderer>(), s_barColor,  Color.black);
            Tint(barB.GetComponent<Renderer>(), s_barColor,  Color.black);

            _spinVisual = spin;
        }

        private static void Tint(Renderer r, Color baseColor, Color emission)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_albedoColorId, baseColor);
            mpb.SetColor(s_baseColorId,   baseColor);
            mpb.SetColor(s_legacyColorId, baseColor);
            if (emission.maxColorComponent > 0f)
                mpb.SetColor(s_emissionColorId, emission);
            r.SetPropertyBlock(mpb);
        }

        // -----------------------------------------------------------------
        // Lift rig (only when GeneratesLift = true)
        // -----------------------------------------------------------------

        private void BuildLiftRig()
        {
            if (!_generatesLift) return;
            // Need a chassis ancestor for the blades to push against.
            Rigidbody chassis = GetComponentInParent<Rigidbody>();
            if (chassis == null)
            {
                Debug.LogWarning(
                    $"[Robogame] RotorBlock '{name}': GeneratesLift=true but no chassis Rigidbody ancestor found — skipping lift rig.",
                    this);
                return;
            }

            // Hub: kinematic Rigidbody at scene root. We MovePosition /
            // MoveRotation it each FixedUpdate; PhysX uses the per-step
            // delta to populate GetPointVelocity at child positions, so
            // each blade samples (ω_hub × r) when it computes lift —
            // exactly what we want for blade-tangential airspeed.
            //
            // Why scene-root? Per BEST_PRACTICES §3.1, child Rigidbodies
            // of a moving parent Rigidbody fight the solver. The hub has
            // to live at scene root regardless of being kinematic. The
            // chassis bulk velocity (forward flight, etc.) is added back
            // in by AeroSurfaceBlock.ConfigureRotorMode passing the
            // chassis as the force target.
            _hubGo = new GameObject($"RotorHub_{name}");
            _hub   = _hubGo.AddComponent<Rigidbody>();
            _hub.isKinematic   = true;
            _hub.useGravity    = false;
            _hub.interpolation = RigidbodyInterpolation.Interpolate;
            _hub.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _hub.transform.SetPositionAndRotation(GetHubWorldPos(), GetHubWorldRot(0f));

            AdoptAdjacentAerofoils(chassis);
        }

        // Scan the 4 grid neighbours of this rotor's cell that lie in
        // the spin plane (perpendicular to _spinAxisLocal). For each
        // one that hosts an AeroSurfaceBlock, reparent it under the
        // hub (preserving world position), override its rotation so
        // its lift axis aligns with the hub up and its chord faces the
        // spin tangent (with collective pitch baked in), and switch
        // it into rotor mode so it samples its airspeed from the hub
        // and dumps lift onto the chassis.
        private void AdoptAdjacentAerofoils(Rigidbody chassis)
        {
            BlockBehaviour rotorBlock = GetComponent<BlockBehaviour>();
            if (rotorBlock == null) return;
            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid == null) return;

            Vector3Int rotorCell = rotorBlock.GridPosition;

            // _spinAxisLocal is in the rotor block's local space. The
            // BlockGrid root is the rotor's parent, so block-local →
            // grid-space is just the block's localRotation.
            Vector3 spinAxisGrid = (transform.localRotation * _spinAxisLocal).normalized;

            // Six axial offsets. We skip any neighbour direction that
            // lies along the spin axis (a foil "above" or "below" the
            // hub isn't a blade). The 0.9 dot threshold tolerates
            // off-axis spin directions cleanly.
            Vector3Int[] all =
            {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };

            foreach (Vector3Int off in all)
            {
                Vector3 offN = ((Vector3)off).normalized;
                if (Mathf.Abs(Vector3.Dot(offN, spinAxisGrid)) > 0.9f) continue;

                Vector3Int neighborCell = rotorCell + off;
                if (!grid.TryGetBlock(neighborCell, out BlockBehaviour neighbor)) continue;
                if (neighbor == null) continue;

                AeroSurfaceBlock aero = neighbor.GetComponent<AeroSurfaceBlock>();
                if (aero == null) continue;

                // Remember where to put it back on teardown.
                AdoptedFoil record = new AdoptedFoil
                {
                    Aero             = aero,
                    OriginalParent   = aero.transform.parent,
                    OriginalLocalPos = aero.transform.localPosition,
                    OriginalLocalRot = aero.transform.localRotation,
                };

                // Reparent under the hub, keeping the foil at its
                // currently-placed world position. As the hub spins,
                // the foil orbits with it.
                aero.transform.SetParent(_hub.transform, worldPositionStays: true);

                // Compute the foil's hub-local radial direction and the
                // tangent perpendicular to both radial and the spin
                // axis. Override the foil's rotation so its local +Z =
                // tangent (chord into wind) and local +Y = spin axis
                // (lift up), with collective pitch tilting the leading
                // edge up around the radial axis.
                Vector3 radialHubLocal = aero.transform.localPosition;
                // Project radial onto spin plane (drop the axial component).
                radialHubLocal = radialHubLocal - Vector3.Project(radialHubLocal, _spinAxisLocal);
                if (radialHubLocal.sqrMagnitude < 1e-6f) continue;
                radialHubLocal.Normalize();
                Vector3 tangentHubLocal = Vector3.Cross(_spinAxisLocal.normalized, radialHubLocal).normalized;
                Quaternion lookRot  = Quaternion.LookRotation(tangentHubLocal, _spinAxisLocal.normalized);
                Quaternion pitchRot = Quaternion.AngleAxis(_collectivePitchDeg, Vector3.right);
                aero.transform.localRotation = lookRot * pitchRot;

                // Switch into rotor mode: sample velocity from the
                // kinematic hub (so PhysX's GetPointVelocity returns
                // ω×r at this foil's world position), apply lift to
                // the chassis (so the chassis actually rises).
                aero.ConfigureRotorMode(hub: _hub, chassis: chassis);

                _adoptedFoils.Add(record);
            }
        }

        private void DestroyLiftRig()
        {
            // Put adopted foils back where they were. If the foil has
            // already been destroyed (mid-flight damage), Unity's null
            // check picks that up via the implicit Object cast.
            foreach (AdoptedFoil rec in _adoptedFoils)
            {
                if (rec.Aero == null) continue;
                if (rec.OriginalParent == null) continue;
                rec.Aero.transform.SetParent(rec.OriginalParent, worldPositionStays: false);
                rec.Aero.transform.localPosition = rec.OriginalLocalPos;
                rec.Aero.transform.localRotation = rec.OriginalLocalRot;
            }
            _adoptedFoils.Clear();

            if (_hubGo != null)
            {
                if (Application.isPlaying) Destroy(_hubGo);
                else                       DestroyImmediate(_hubGo);
                _hubGo = null;
                _hub   = null;
            }
        }

        // -----------------------------------------------------------------
        // Per-frame: drive the visual + (when in lift mode) the kinematic hub.
        // -----------------------------------------------------------------

        private void FixedUpdate()
        {
            // Garage / build-mode parking pins the chassis Rigidbody as
            // kinematic + frozen for static inspection. Honour that here:
            // a self-spinning rotor while everything else is frozen
            // makes it hard to look at the chassis. Also keep the lift
            // hub stationary so MovePosition/MoveRotation deltas don't
            // produce phantom airspeed at the blades. Cosmetic-only
            // rotors with no chassis ancestor (debris, previews) keep
            // spinning — there's no chassis to be "frozen" against.
            Rigidbody chassis = GetComponentInParent<Rigidbody>();
            bool frozen = chassis != null && chassis.isKinematic;
            if (frozen)
            {
                if (_spinVisual != null)
                {
                    _spinVisual.localRotation = Quaternion.AngleAxis(_angleRad * Mathf.Rad2Deg, _spinAxisLocal);
                }
                if (_hub != null)
                {
                    // Track the chassis position/rotation but don't
                    // accumulate spin: hub orientation stays at _angleRad
                    // (last live value) so the blades freeze in place.
                    _hub.transform.SetPositionAndRotation(GetHubWorldPos(), GetHubWorldRot(_angleRad));
                }
                return;
            }

            float dt = Time.fixedDeltaTime;
            float omega = LiveRpm * (Mathf.PI * 2f / 60f); // rad/s
            _angleRad += omega * dt;
            // Wrap so the float doesn't drift to billions over long sessions.
            const float twoPi = Mathf.PI * 2f;
            if (_angleRad >  twoPi) _angleRad -= twoPi;
            if (_angleRad < -twoPi) _angleRad += twoPi;

            // Visual spin (always on).
            if (_spinVisual != null)
            {
                _spinVisual.localRotation = Quaternion.AngleAxis(_angleRad * Mathf.Rad2Deg, _spinAxisLocal);
            }

            // Lift-mode hub: drive via MovePosition / MoveRotation so
            // PhysX synthesises tangential velocity at the blade
            // positions for the next solver step.
            if (_hub != null)
            {
                _hub.MovePosition(GetHubWorldPos());
                _hub.MoveRotation(GetHubWorldRot(_angleRad));
            }
        }

        private Vector3 GetHubWorldPos()
            => transform.TransformPoint(new Vector3(0f, SpinHeight, 0f));

        private Quaternion GetHubWorldRot(float angleRad)
            => transform.rotation * Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, _spinAxisLocal);
    }
}
