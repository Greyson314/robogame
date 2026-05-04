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

        // Vertical layout in rotor-block local space (1 unit = 1 grid cell).
        //
        //   y=+1.5 ┃                    ← top of mechanism cell
        //          ┃   disc + bars      ← spin pivot
        //   y=+1.0 ┃─── MechanismHeight ← center of mechanism cell
        //          ┃   mast (upper)
        //   y=+0.5 ┃                    ← top of rotor cell (cell-above boundary)
        //          ┃   mast (lower)
        //   y= 0.0 ┃─── rotor cell center (transform pivot)
        //          ┃
        //   y=-0.5 ┃                    ← bottom of rotor cell
        //
        // The rotor visually owns two cells: its own grid cell (the stem)
        // plus the cell one step along the spin axis (the mechanism). The
        // blueprint convention is to place an invisible structural Cube at
        // the mechanism cell so connectivity carries through to the foil
        // ring; the rotor hides that cube's host mesh at runtime.
        private const float MechanismHeight = 1.0f;

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

        private void Start()
        {
            // Defer mesh suppression to Start so every block in the
            // chassis has already gone through its own Awake/OnEnable;
            // the grid lookup will reliably resolve the mechanism cube.
            HideMechanismCellMesh();
        }

        private void OnDisable()
        {
            // Intentionally do NOT tear down the lift rig here. Robot.CaptureTemplate
            // briefly SetActive(false) → Instantiate → SetActive(true) on the chassis
            // to clone it as a cold-storage template; that cascade fires OnDisable on
            // every block. Tearing the rig down in that window fails — the foils'
            // OriginalParent (the chassis grid root) is itself mid-deactivation, and
            // Unity rejects SetParent into a transitioning GameObject. Skipping the
            // teardown is safe: the hub lives at scene root and is unaffected by the
            // chassis cascade. BuildLiftRig is idempotent (returns early when the rig
            // already exists), so the post-snapshot OnEnable is a no-op. Real teardown
            // happens in OnDestroy and in explicit Rebuild() calls.
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
            // Mast — tall dark cylinder spanning the rotor cell + into the
            // mechanism cell. Cylinder primitive native height = 2 m
            // (Y -1..+1); scale Y = 0.75 → height 1.5 m; localPosition.y =
            // 0.25 → centered at 0.25 → spans -0.5..+1.0 in rotor-local
            // space. That's the rotor cell's bottom face up to the
            // mechanism cell's center.
            Transform mast = BlockVisuals.GetOrCreatePrimitiveChild(transform, "RotorMast", PrimitiveType.Cylinder);
            mast.localScale    = new Vector3(0.25f, 0.75f, 0.25f);
            mast.localPosition = new Vector3(0f, 0.25f, 0f);

            // Spin pivot — empty transform we rotate per-frame. Holds
            // the visible hub disc + bars at the mechanism cell center.
            Transform spin = BlockVisuals.GetOrCreateChild(transform, "RotorSpin");
            spin.localPosition = new Vector3(0f, MechanismHeight, 0f);
            spin.localRotation = Quaternion.identity;

            // Visual hub disc — wider cyan puck at the mechanism cell. The
            // mechanism cube's host mesh is hidden at runtime (see
            // HideMechanismCellMesh) so this disc reads as the rotor head
            // floating on top of the stem.
            Transform disc = BlockVisuals.GetOrCreatePrimitiveChild(spin, "Disc", PrimitiveType.Cylinder);
            disc.localScale    = new Vector3(0.70f, 0.06f, 0.70f);
            disc.localPosition = Vector3.zero;

            // Two intersecting bars give the spinning rig a clear
            // direction-of-rotation read at a glance. Length 0.95 stays
            // within the mechanism cell so the bars don't visually clip
            // into adjacent foil cells; the foil mesh begins at x=0.5
            // local and extends out to x=1.5 — bars end at x=0.475.
            Transform barA = BlockVisuals.GetOrCreatePrimitiveChild(spin, "BarA", PrimitiveType.Cube);
            barA.localScale    = new Vector3(0.95f, 0.08f, 0.10f);
            barA.localPosition = Vector3.zero;

            Transform barB = BlockVisuals.GetOrCreatePrimitiveChild(spin, "BarB", PrimitiveType.Cube);
            barB.localScale    = new Vector3(0.10f, 0.08f, 0.95f);
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
            // Idempotent: already-built rig survives the OnEnable/OnDisable
            // cascade triggered by Robot.CaptureTemplate (see OnDisable comment).
            // Returning early here keeps a single hub + adoption set across that
            // transient deactivation. Explicit Rebuild() callers (parent-change,
            // GeneratesLift toggle) tear down first via DestroyLiftRig before
            // calling BuildLiftRig, so the early-out doesn't block them.
            if (_hubGo != null) return;
            // Need a chassis ancestor for the blades to push against.
            Rigidbody chassis = GetComponentInParent<Rigidbody>();
            if (chassis == null)
            {
                Debug.LogWarning(
                    $"[Robogame] RotorBlock '{name}': GeneratesLift=true but no chassis Rigidbody ancestor found — skipping lift rig.",
                    this);
                return;
            }
            // Garage / build-mode parking pins the chassis as kinematic +
            // FreezeAll for static inspection. Don't reparent foils under a
            // scene-root hub in that mode: the foils need to stay under the
            // chassis grid root for the static display path to render them
            // at their placed cells. The lift rig builds fresh on the next
            // non-kinematic spawn — ArenaController calls ChassisFactory.Build
            // on a fresh GameObject, which re-fires OnEnable with a non-
            // kinematic chassis. Closes B1 from
            // docs/changes/17-rotor-foil-decoupling-followups.md.
            if (chassis.isKinematic) return;

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
            // Deactivate immediately: the adoption pass below calls
            // SetParent on each foil to put it under the hub. Unity throws
            // "Cannot set the parent ... while activating or deactivating
            // the parent" when the hub is mid-activation (which a freshly-
            // created GameObject is). Same SetActive-during-mutation trick
            // ChassisFactory.Build uses for AddComponent + reflection.
            // We re-activate at the end of BuildLiftRig once every adoption
            // is wired up; AeroSurfaceBlock.OnEnable's _rotorMode guard
            // preserves the configuration the cascade re-fires.
            _hubGo.SetActive(false);
            _hub   = _hubGo.AddComponent<Rigidbody>();
            _hub.isKinematic   = true;
            _hub.useGravity    = false;
            _hub.interpolation = RigidbodyInterpolation.Interpolate;
            _hub.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _hub.transform.SetPositionAndRotation(GetHubWorldPos(), GetHubWorldRot(0f));

            AdoptAdjacentAerofoils(chassis);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[RotorBlock] '{name}': BuildLiftRig adopted {_adoptedFoils.Count} foil(s).", this);
#endif

            _hubGo.SetActive(true);
        }

        // Scan the 4 lateral cells around the *mechanism cell* (one cell
        // along the spin axis from the rotor's own cell). For each one
        // that hosts an AeroSurfaceBlock, reparent it under the hub
        // (preserving world position), override its rotation so its
        // lift axis aligns with the hub up and its chord faces the spin
        // tangent (with collective pitch baked in), and switch it into
        // rotor mode so it samples its airspeed from the hub and dumps
        // lift onto the chassis.
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
            Vector3Int spinAxisGridInt = Vector3Int.RoundToInt(spinAxisGrid);
            // The mechanism cell is one cell along the spin axis. Foils
            // ring this cell (not the rotor's own cell) so that a player
            // who places foils at the same level as the rotor's "head"
            // sees them adopted, not at the stem level.
            Vector3Int mechanismCell = rotorCell + spinAxisGridInt;

            // Six axial offsets. We skip any direction that lies along
            // the spin axis (a foil "above" or "below" the mechanism
            // cell isn't a blade). The 0.9 dot threshold tolerates
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

                Vector3Int neighborCell = mechanismCell + off;
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

                // Snapshot world position BEFORE reparenting. We restore
                // it explicitly after SetParent because (a) worldPositionStays
                // can drift by floating-point if the hub's world transform
                // has a tiny translation/rotation accumulated from physics,
                // and (b) we want exact placement at the cell we found.
                Vector3 foilWorldPos = aero.transform.position;

                // Reparent under the hub, keeping the foil at its
                // currently-placed world position. As the hub spins,
                // the foil orbits with it.
                aero.transform.SetParent(_hub.transform, worldPositionStays: true);
                aero.transform.position = foilWorldPos;

                // Build the world-space rotation for this blade: forward =
                // spin tangent (chord into the wind), up = spin axis
                // (lift direction), then tilt the leading edge up by
                // _collectivePitchDeg around the world-space radial axis.
                // The radial-axis pitch is the physically correct
                // collective formulation: each blade tilts about its own
                // radial line, regardless of which side of the rotor it
                // sits on. The previous local-+X formulation only matched
                // this for blades aligned with world +X.
                Vector3 spinAxisWorld = transform.TransformDirection(_spinAxisLocal).normalized;
                Vector3 radialWorld   = foilWorldPos - _hub.transform.position;
                radialWorld -= Vector3.Project(radialWorld, spinAxisWorld);
                if (radialWorld.sqrMagnitude < 1e-6f) continue;
                radialWorld.Normalize();
                Vector3 tangentWorld = Vector3.Cross(spinAxisWorld, radialWorld).normalized;
                Quaternion worldRot = Quaternion.LookRotation(tangentWorld, spinAxisWorld);
                Quaternion pitchRot = Quaternion.AngleAxis(_collectivePitchDeg, radialWorld);
                aero.transform.rotation = pitchRot * worldRot;

                // Switch into rotor mode: sample velocity from the
                // kinematic hub (so PhysX's GetPointVelocity returns
                // ω×r at this foil's world position), apply lift to
                // the chassis (so the chassis actually rises).
                // Pass the rotor's transform + local spin axis so the
                // foil applies lift along the rotor's axis (not its
                // tilted transform.up). With collective pitch the
                // pitched transform.up has a tangential component; left
                // unchecked the four blades' lift sums to a yaw torque
                // on the chassis at full rotor power. See
                // AeroSurfaceBlock.ConfigureRotorMode docstring.
                aero.ConfigureRotorMode(
                    hub: _hub,
                    chassis: chassis,
                    rotorTransform: transform,
                    spinAxisLocal: _spinAxisLocal);

                _adoptedFoils.Add(record);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log(
                    $"[RotorBlock] '{name}': adopted '{aero.name}' " +
                    $"world={aero.transform.position:F3}, hub={_hub.transform.position:F3}",
                    aero);
#endif
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
            => transform.TransformPoint(new Vector3(0f, MechanismHeight, 0f));

        private Quaternion GetHubWorldRot(float angleRad)
            => transform.rotation * Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, _spinAxisLocal);

        // -----------------------------------------------------------------
        // Mechanism cell mesh suppression
        // -----------------------------------------------------------------

        /// <summary>
        /// Hide the mesh on the cube placed at the mechanism cell (the
        /// cell one step along the spin axis from the rotor's own cell).
        /// The cube is there to anchor the foil ring to the chassis grid
        /// for connectivity; visually it would clip with the rotor's disc
        /// and bars. Suppressing the host mesh leaves the collider in
        /// place (so damage routing still works) but lets the rotor
        /// visual read as one continuous "mast + head" silhouette.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="Start"/> rather than <see cref="Awake"/>:
        /// at <c>Awake</c> time other blocks may not have been placed in
        /// the grid yet. <c>Start</c> fires after every block in the
        /// chassis has gone through its own <c>Awake</c>+<c>OnEnable</c>.
        /// Safe to no-op if the mechanism cell is empty (bare rotor).
        /// </remarks>
        private void HideMechanismCellMesh()
        {
            BlockBehaviour rotorBlock = GetComponent<BlockBehaviour>();
            if (rotorBlock == null) return;
            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid == null) return;
            Vector3 spinAxisGrid = (transform.localRotation * _spinAxisLocal).normalized;
            Vector3Int spinAxisGridInt = Vector3Int.RoundToInt(spinAxisGrid);
            Vector3Int mechanismCell = rotorBlock.GridPosition + spinAxisGridInt;
            if (!grid.TryGetBlock(mechanismCell, out BlockBehaviour neighbor)) return;
            if (neighbor == null) return;
            BlockVisuals.HideHostMesh(neighbor.gameObject);
        }
    }
}
