using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Coarse "what kind of vehicle is this" tag baked into a chassis
    /// blueprint. Drives spawn-time behaviour that doesn't fit naturally
    /// inside the per-block components (e.g. planes need an initial
    /// forward velocity so they don't have to taxi from zero).
    /// </summary>
    public enum ChassisKind
    {
        Ground,
        Plane,
    }

    /// <summary>
    /// Serialisable description of a robot: a list of <c>(blockId, gridPos)</c>
    /// pairs plus a small amount of metadata. Pass A's "what crosses scene
    /// boundaries" payload — owned by <c>GameStateController</c>, consumed
    /// by the runtime <c>ChassisFactory</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored as a <see cref="ScriptableObject"/> so designers can hand-author
    /// default loadouts in the editor; the same shape is used at runtime for
    /// the player's mutable in-progress build (an in-memory instance created
    /// via <see cref="ScriptableObject.CreateInstance{T}()"/>).
    /// </para>
    /// <para>
    /// Block IDs are stable strings (<see cref="BlockDefinition.Id"/>), not
    /// asset references — saved blueprints stay valid across asset moves and
    /// are trivially JSON-serialisable for disk save / netcode.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Blueprint_New",
        menuName = "Robogame/Chassis Blueprint",
        order = 2)]
    public sealed class ChassisBlueprint : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Stable BlockDefinition.Id (e.g. 'block.cube').")]
            public string BlockId;

            [Tooltip("Grid coordinate, integer triple in robot-local space.")]
            public Vector3Int Position;

            [Tooltip("Mount orientation: the direction the block's local +Y points in chassis space " +
                     "(i.e. the face normal of the cube it's attached to). Default (0,0,0) is treated " +
                     "as +Y so legacy entries stay upright. Most cosmetic blocks ignore this; rotors, " +
                     "aerofoils, weapons, and thrusters use it to face outward from the mount face.")]
            public Vector3Int Up;

            [Tooltip("Per-block 'variable part' dimensions. Interpretation depends on the block kind:\n" +
                     "  • AeroSurfaceBlock (Aero / AeroFin): x=span, y=thickness, z=chord (metres).\n" +
                     "  • RopeBlock: x=length in chassis cells (1..16, rounded to int). Tip block " +
                     "(Hook/Mace) is placed at rope.cell + x*up.\n" +
                     "  • All other blocks: ignored.\n" +
                     "Vector3.zero means 'use the block-default'. Saved blueprints carry these so " +
                     "a wing-tuned plane stays a wing-tuned plane after a reload.")]
            public Vector3 Dims;

            [Tooltip("Per-block pitch / incidence in degrees. Interpretation depends on the block kind:\n" +
                     "  • AeroSurfaceBlock (Aero / AeroFin): geometric angle of attack offset. " +
                     "Adds to the airflow-derived AoA in the lift formula. Visual mesh feathers " +
                     "by this amount around the span axis (chord-axis teeter is the separate " +
                     "Teeter field). ±18° soft limit before stall warning.\n" +
                     "  • RotorBlock: collective pitch baked into adopted blades at adopt time. " +
                     "0 = use rotor's authored default.\n" +
                     "  • All other blocks: ignored.")]
            public float Pitch;

            [Tooltip("Player-chosen rotation about the block's mount (Up) axis, in degrees " +
                     "(0/90/180/270). Lets a thruster/weapon face a direction the mount face alone " +
                     "can't give. 0 = no rotation; every pre-v8 save defaults here. Applied on top of " +
                     "the Up orientation at placement (BlockGrid.OrientationFromUp).")]
            public int Yaw;

            [Tooltip("Per-block teeter tilt in degrees (foils only): chord-axis rotation that " +
                     "swings the tip up/down like a teeter-totter — dihedral on a plane wing, " +
                     "coning on a rotor blade. Visual-only in v1 (no lift contribution). " +
                     "Stored local-frame, like Pitch. 0 = flat; every pre-v9 save defaults here.")]
            public float Teeter;

            [Tooltip("Per-block server-authoritative scalar config. Interpretation depends on the block kind:\n" +
                     "  • ThrusterBlock: max thrust (N).\n" +
                     "  • RudderBlock: yaw authority.\n" +
                     "  • RotorBlock: RPM.\n" +
                     "  • All other blocks: ignored.\n" +
                     "0 means 'use the block's authored default' — keeps every pre-v4 save " +
                     "(blockConfig absent → 0) behaviour-identical. Migrated off the per-machine " +
                     "Thruster./Rudder./Rotor.RPM Tweakables; PHYSICS_PLAN §1.5 / §5.")]
            public float BlockConfig;

            [Tooltip("Per-block player-authored concoction id (Concoction.Id). Explosive weapons " +
                     "(Bomb / Mortar) resolve it from ConcoctionRegistry at fire time to scale " +
                     "damage / explosion-size / knockback, and it adds a CPU surcharge. Empty " +
                     "string (the default, and every pre-v7 save) = no concoction → baseline stats " +
                     "and zero surcharge. See docs/decisions/0004-concoction-persistence.md.")]
            public string ConcoctionId;

            /// <summary>Returns <see cref="Up"/> with the legacy zero → +Y fallback applied.</summary>
            public Vector3Int EffectiveUp => Up == Vector3Int.zero ? Vector3Int.up : Up;

            /// <summary>Returns <see cref="ConcoctionId"/> with null coalesced to empty string.</summary>
            public string EffectiveConcoctionId => string.IsNullOrEmpty(ConcoctionId) ? string.Empty : ConcoctionId;

            /// <summary>Yaw normalized to one of 0/90/180/270 degrees.</summary>
            public int EffectiveYaw => ((Yaw % 360) + 360) % 360 / 90 * 90;

            public Entry(string blockId, Vector3Int position)
            {
                BlockId = blockId;
                Position = position;
                Up = Vector3Int.up;
                Dims = Vector3.zero;
                Pitch = 0f;
                BlockConfig = 0f;
                ConcoctionId = string.Empty;
                Yaw = 0;
                Teeter = 0f;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
                Dims = Vector3.zero;
                Pitch = 0f;
                BlockConfig = 0f;
                ConcoctionId = string.Empty;
                Yaw = 0;
                Teeter = 0f;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up, Vector3 dims)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
                Dims = dims;
                Pitch = 0f;
                BlockConfig = 0f;
                ConcoctionId = string.Empty;
                Yaw = 0;
                Teeter = 0f;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up, Vector3 dims, float pitch)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
                Dims = dims;
                Pitch = pitch;
                BlockConfig = 0f;
                ConcoctionId = string.Empty;
                Yaw = 0;
                Teeter = 0f;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up, Vector3 dims, float pitch, float blockConfig)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
                Dims = dims;
                Pitch = pitch;
                BlockConfig = blockConfig;
                ConcoctionId = string.Empty;
                Yaw = 0;
                Teeter = 0f;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up, Vector3 dims, float pitch, float blockConfig, string concoctionId)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
                Dims = dims;
                Pitch = pitch;
                BlockConfig = blockConfig;
                ConcoctionId = string.IsNullOrEmpty(concoctionId) ? string.Empty : concoctionId;
                Yaw = 0;
                Teeter = 0f;
            }
        }

        [Tooltip("Human-readable name shown in the garage UI.")]
        [SerializeField] private string _displayName = "Untitled Chassis";

        [Tooltip("Coarse vehicle category. Drives spawn-time behaviour " +
                 "(e.g. planes are launched with forward velocity).")]
        [SerializeField] private ChassisKind _kind = ChassisKind.Ground;

        [Tooltip("If true, every Rotor cell on this chassis spawns a 4-blade " +
                 "aerofoil ring producing real lift via the standard AeroSurfaceBlock " +
                 "math (helicopter / rotorcraft). When false (default) rotors are " +
                 "purely cosmetic. Per-rotor opt-in lands when the blueprint format " +
                 "supports per-cell config.")]
        [SerializeField] private bool _rotorsGenerateLift = false;

        [Tooltip("Server-authoritative chassis-level plane control tuning. " +
                 "Migrated off the per-machine Plane.* Tweakables.")]
        [SerializeField] private PlaneTuningConfig _planeTuning = new();

        [Tooltip("Server-authoritative chassis-level ground drive tuning. " +
                 "Migrated off the per-machine Ground.* Tweakables.")]
        [SerializeField] private GroundTuningConfig _groundTuning = new();

        [Tooltip("Server-authoritative chassis Rigidbody damping. " +
                 "Migrated off the per-machine Chassis.* Tweakables.")]
        [SerializeField] private ChassisDampingConfig _chassisDamping = new();

        [Tooltip("Server-authoritative chassis-level thruster feel (idle + response). " +
                 "Per-thruster max thrust rides Entry.BlockConfig.")]
        [SerializeField] private ThrusterTuningConfig _thrusterTuning = new();

        [Tooltip("Block placements that make up this chassis.")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value) return;
                _displayName = value;
                Revision++;
            }
        }

        public ChassisKind Kind
        {
            get => _kind;
            set => _kind = value;
        }

        /// <summary>
        /// Whether <see cref="Block.BlockIds.Rotor"/> cells on this chassis
        /// are propulsion rotors (spawn aerofoils, generate lift) or pure
        /// cosmetic spinners. See <c>docs/subsystems/physics.md</c> §2 — this
        /// is the temporary blueprint-level switch until per-cell config
        /// lands; until then, "this chassis is a helicopter" is the right
        /// granularity.
        /// </summary>
        public bool RotorsGenerateLift
        {
            get => _rotorsGenerateLift;
            set => _rotorsGenerateLift = value;
        }

        /// <summary>
        /// Server-authoritative chassis-level drive tuning. Never null —
        /// a pre-v4 save / .asset that lacks the field gets the
        /// field-initializer instance whose values equal the historical
        /// Tweakable defaults, so behaviour is unchanged on load.
        /// </summary>
        public PlaneTuningConfig PlaneTuning
        {
            get => _planeTuning ??= new PlaneTuningConfig();
            set => _planeTuning = value ?? new PlaneTuningConfig();
        }

        public GroundTuningConfig GroundTuning
        {
            get => _groundTuning ??= new GroundTuningConfig();
            set => _groundTuning = value ?? new GroundTuningConfig();
        }

        public ChassisDampingConfig ChassisDamping
        {
            get => _chassisDamping ??= new ChassisDampingConfig();
            set => _chassisDamping = value ?? new ChassisDampingConfig();
        }

        public ThrusterTuningConfig ThrusterTuning
        {
            get => _thrusterTuning ??= new ThrusterTuningConfig();
            set => _thrusterTuning = value ?? new ThrusterTuningConfig();
        }

        public Entry[] Entries => _entries;

        /// <summary>
        /// Runtime edit counter (not serialized). Bumped by every mutation a
        /// save would persist — <see cref="SetEntries"/> and
        /// <see cref="DisplayName"/> — so <c>GameStateController</c> can tell
        /// dirty from clean with an int compare instead of diffing entries.
        /// TRACE[LOG-166].
        /// </summary>
        public int Revision { get; private set; }

        /// <summary>
        /// Replace the entries array. Used by the editor wizard and the
        /// in-game garage. The chokepoint that enforces canonical ordering:
        /// every path that mutates the entry list goes through here so the
        /// stored array is always sorted by <see cref="BlockEntries.Compare"/>.
        /// That ordering is the netcode contract — see
        /// <c>docs/subsystems/netcode.md</c> §6.
        /// </summary>
        /// <remarks>
        /// Sorts <paramref name="entries"/> in place. Callers that need to
        /// preserve a specific authoring order should pass a copy.
        /// </remarks>
        public void SetEntries(Entry[] entries)
        {
            _entries = entries ?? Array.Empty<Entry>();
            BlockEntries.SortCanonical(_entries);
            Revision++;
        }
    }
}
