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
                 "all 6 faces). Specialty blocks should set this true; the placement " +
                 "system also has a hardcoded fallback list (see BlockConnectivity) so " +
                 "shipped assets without the flag still behave correctly.")]
        [SerializeField] private bool _isLeafBlock = false;

        [Tooltip("If true, this block can only be placed on a side face of a host " +
                 "(chassis ±X or ±Z); top / bottom (±Y) mounts are rejected at " +
                 "placement time. Used for wheels: the stem is horizontal, so " +
                 "mounting a wheel on the top of a cube would point the stem " +
                 "straight up. Default false (block can mount on any of the 6 faces). " +
                 "BlockConnectivity has a hardcoded fallback list — shipped wheel " +
                 "assets behave correctly without re-authoring the SO.")]
        [SerializeField] private bool _sideMountOnly = false;

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
        /// <summary>Raw flag from the asset; consumers should call
        /// <see cref="BlockConnectivity.IsLeaf"/> instead, which also
        /// applies the hardcoded fallback list.</summary>
        public bool IsLeafBlockRaw => _isLeafBlock;

        /// <summary>Raw flag from the asset; consumers should call
        /// <see cref="BlockConnectivity.RequiresSideMount"/>.</summary>
        public bool SideMountOnlyRaw => _sideMountOnly;
        public GameObject Prefab => _prefab;
        public Color TintColor => _tintColor;
        public Material Material => _material;
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
}
