using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Categorises a block by its functional role on a robot.
    /// </summary>
    public enum BlockCategory
    {
        Structure,
        Cpu,
        Movement,
        Weapon,
        Module,
        Cosmetic
    }

    /// <summary>
    /// Static, designer-authored data for a single block type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <see cref="BlockDefinition"/> asset exists per block type
    /// (e.g. "Cube 1x1", "Wheel Small", "Laser SMG"). Per-instance runtime
    /// state (current HP, owner, position in the block graph) lives on the
    /// block's <c>MonoBehaviour</c> at runtime — never on the definition.
    /// </para>
    /// <para>
    /// Definitions are intentionally engine-agnostic where possible so they
    /// can later be net-serialised or hot-loaded via Addressables.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "BlockDef_New",
        menuName = "Robogame/Block Definition",
        order = 0)]
    public sealed class BlockDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable ID used for serialization and netcode. NEVER change this once shipped.")]
        [SerializeField] private string _id = "block.unset";

        [Tooltip("Human-readable name shown in the garage UI.")]
        [SerializeField] private string _displayName = "Untitled Block";

        [Tooltip("Functional category — determines what subsystems care about this block.")]
        [SerializeField] private BlockCategory _category = BlockCategory.Structure;

        [Header("Stats")]
        [Tooltip("Block HP. 0 means indestructible (avoid on gameplay-relevant blocks).")]
        [SerializeField, Min(0f)] private float _maxHealth = 100f;

        [Tooltip("Mass in kilograms. Affects inertia, turn rate, and acceleration.")]
        [SerializeField, Min(0f)] private float _mass = 1f;

        [Tooltip("CPU / power cost. Sum across all blocks must not exceed the robot's CPU budget.")]
        [SerializeField, Min(0)] private int _cpuCost = 1;

        [Tooltip("If true, no other block can attach to any of this block's faces. " +
                 "Used by the build-mode placement check to enforce \"can't build on top " +
                 "of a wing / weapon / thruster\" rules. Default false (block hosts on " +
                 "all 6 faces). AUTHORITATIVE — author via BlockDefinitionWizard " +
                 "(ADR-0008; the old hardcoded fallback lists are gone).")]
        [SerializeField] private bool _isLeafBlock = false;

        [Tooltip("If true, this block can only be placed on a side face of a host " +
                 "(chassis ±X or ±Z); top / bottom (±Y) mounts are rejected at " +
                 "placement time. Used for wheels: the stem is horizontal, so " +
                 "mounting a wheel on the top of a cube would point the stem " +
                 "straight up. Default false. AUTHORITATIVE — author via " +
                 "BlockDefinitionWizard (ADR-0008).")]
        [SerializeField] private bool _sideMountOnly = false;

        [Tooltip("If true, this block exposes per-instance variant config (foil " +
                 "span/thickness/chord/pitch, rope segment count, rotor collective). " +
                 "The build-mode variant panel renders sliders only for variable " +
                 "blocks; the hotbar marks them with a 'VAR' badge. AUTHORITATIVE — " +
                 "author via BlockDefinitionWizard (ADR-0008).")]
        [SerializeField] private bool _hasVariantConfig = false;

        [Tooltip("If true, this block can only mount on the TOP face of a host " +
                 "(chassis +Y). The mortar is the model case — its tube fires " +
                 "upward into a lob, so side/bottom mounts are nonsensical. " +
                 "Default false. AUTHORITATIVE — author via BlockDefinitionWizard " +
                 "(ADR-0008).")]
        [SerializeField] private bool _topMountOnly = false;

        [Tooltip("Chassis-level drive subsystem this block implies. ChassisAssembler " +
                 "unions the needs over the blueprint: Ground -> GroundDriveSubsystem, " +
                 "Flight -> aero control surfaces via the Plane control scheme (ADR-0009), Hover -> HoverDriveSubsystem. " +
                 "None for blocks with purely per-block behaviour (thruster, rudder). " +
                 "ADR-0008.")]
        [SerializeField] private DriveNeed _driveNeed = DriveNeed.None;

        [Tooltip("If non-empty, placing this block auto-places the named companion " +
                 "block at cell + mount-up (the rotor's mechanism cube is the model " +
                 "case). Ownership resolution, cascade removal, and the lateral " +
                 "attach restriction below all read this spec. ADR-0008.")]
        [SerializeField] private string _companionBlockId = "";

        [Tooltip("When this block has a companion: block ids that may attach to the " +
                 "companion's LATERAL faces (perpendicular to mount-up). Empty = " +
                 "nothing may attach laterally. Rotor authors its blade/rope ring " +
                 "here. Ignored when CompanionBlockId is empty.")]
        [SerializeField] private string[] _companionLateralAttachIds = System.Array.Empty<string>();

        [Header("Visuals")]
        [Tooltip("Prefab spawned when this block is placed. Must contain a BlockBehaviour at the root.")]
        [SerializeField] private GameObject _prefab;

        [Tooltip("Tint applied to the spawned block's MeshRenderer when no custom prefab/material is set. " +
                 "Lets placeholder primitives read at a glance.")]
        [SerializeField] private Color _tintColor = Color.white;

        [Tooltip("Optional shared material used as the block's base when no custom Prefab is set. " +
                 "Authored by BlockMaterials so we can centralise shader + outline choice per category. " +
                 "Falls back to the primitive's default material if null.")]
        [SerializeField] private Material _material;

        // TRACE[LOG-132]: inventor-aesthetic model wiring — authored FBX
        // visuals ride the definition (behaviours are AddComponent'ed at
        // runtime, so serialized fields on prefabs can't carry them).
        [Tooltip("Optional authored visual model (FBX). Block components that " +
                 "support model visuals (WheelBlock first; sweep in progress) " +
                 "instantiate it in place of their procedural primitives. " +
                 "Null = keep the procedural rig.")]
        [SerializeField] private GameObject _visualModel;

        [Tooltip("Uniform scale for the visual model instance. Components may " +
                 "layer their own sizing on top (e.g. WheelBlock scales a 1 m " +
                 "authored wheel to its physics radius).")]
        [SerializeField, Min(0.01f)] private float _visualModelScale = 1f;

        [Tooltip("Local offset for the visual model instance.")]
        [SerializeField] private Vector3 _visualModelOffset = Vector3.zero;

        [Tooltip("When true, BlockBehaviour attaches the visual model " +
                 "generically at placement (host mesh hidden) — for blocks " +
                 "with no moving-part component (drill, rope, tips, modules). " +
                 "Leave false when a component owns the model (wheel under " +
                 "its Spin pivot, weapons via WeaponModelRig).")]
        [SerializeField] private bool _visualModelStatic = false;

        [Header("Component data (kind-specific)")]
        [Tooltip("Optional ScriptableObject carrying per-kind authored stats. " +
                 "Examples: WeaponDefinition for Weapon blocks, BombDefinition for BombBay blocks. " +
                 "The block component (ProjectileGun, BombBayBlock, etc.) is responsible for casting " +
                 "to its expected type via GetComponentData<T>(); falls back to the component's own " +
                 "SerializeField defaults if null.\n\n" +
                 "Reference type is the ScriptableObject base because Robogame.Block can't take a " +
                 "dependency on Robogame.Combat without an asmdef cycle. The cast is the price.")]
        [SerializeField] private ScriptableObject _componentData;

        public string Id => _id;
        public string DisplayName => _displayName;
        public BlockCategory Category => _category;
        public float MaxHealth => _maxHealth;
        public float Mass => _mass;
        public int CpuCost => _cpuCost;
        /// <summary>Authoritative flag (ADR-0008); consumers should call
        /// <see cref="BlockConnectivity.IsLeaf"/> for the null-safe read.</summary>
        public bool IsLeafBlockRaw => _isLeafBlock;

        /// <summary>Authoritative flag (ADR-0008); consumers should call
        /// <see cref="BlockConnectivity.RequiresSideMount"/>.</summary>
        public bool SideMountOnlyRaw => _sideMountOnly;

        /// <summary>Authoritative flag (ADR-0008); consumers should call
        /// <see cref="BlockVariants.HasVariantConfig"/>.</summary>
        public bool HasVariantConfigRaw => _hasVariantConfig;

        /// <summary>Authoritative flag (ADR-0008); consumers should call
        /// <see cref="BlockConnectivity.RequiresTopMount"/>.</summary>
        public bool TopMountOnlyRaw => _topMountOnly;

        /// <summary>Chassis-level drive subsystem this block implies (ADR-0008).</summary>
        public DriveNeed DriveSubsystemNeed => _driveNeed;

        /// <summary>Companion block auto-placed at cell + mount-up, or empty (ADR-0008).</summary>
        public string CompanionBlockId => _companionBlockId ?? "";

        /// <summary>True when this block auto-places a companion (ADR-0008).</summary>
        public bool HasCompanion => !string.IsNullOrEmpty(_companionBlockId);

        /// <summary>Ids allowed on the companion's lateral faces (ADR-0008).</summary>
        public System.Collections.Generic.IReadOnlyList<string> CompanionLateralAttachIds
            => _companionLateralAttachIds ?? System.Array.Empty<string>();
        public GameObject Prefab => _prefab;
        public Color TintColor => _tintColor;
        public Material Material => _material;
        public GameObject VisualModel => _visualModel;
        public float VisualModelScale => _visualModelScale;
        public Vector3 VisualModelOffset => _visualModelOffset;
        public bool VisualModelStatic => _visualModelStatic;
        public ScriptableObject ComponentData => _componentData;

        /// <summary>Convenience cast for consumers that know the expected type.</summary>
        public T GetComponentData<T>() where T : ScriptableObject => _componentData as T;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_id) || _id == "block.unset")
            {
                Debug.LogWarning($"[Robogame] BlockDefinition '{name}' has no stable ID set.", this);
            }
        }
#endif
    }

    /// <summary>
    /// Chassis-level drive subsystem a block implies (ADR-0008). Serialized
    /// on <see cref="BlockDefinition"/> — keep the numeric values stable.
    /// </summary>
    public enum DriveNeed
    {
        None = 0,
        Ground = 1,
        Flight = 2,
        Hover = 3,
    }
}
