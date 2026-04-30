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

        [Header("Visuals")]
        [Tooltip("Prefab spawned when this block is placed. Must contain a BlockBehaviour at the root.")]
        [SerializeField] private GameObject _prefab;

        [Tooltip("Tint applied to the spawned block's MeshRenderer when no custom prefab/material is set. " +
                 "Lets placeholder primitives read at a glance.")]
        [SerializeField] private Color _tintColor = Color.white;

        public string Id => _id;
        public string DisplayName => _displayName;
        public BlockCategory Category => _category;
        public float MaxHealth => _maxHealth;
        public float Mass => _mass;
        public int CpuCost => _cpuCost;
        public GameObject Prefab => _prefab;
        public Color TintColor => _tintColor;

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
