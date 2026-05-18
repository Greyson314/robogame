using System.Collections.Generic;
using Robogame.Block;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Collapses a frozen in-arena chassis's static structural blocks
    /// from ~150 individual MeshRenderers (each a draw + a per-cascade
    /// shadow draw) into one combined mesh per material — the
    /// PERFORMANCE.md §8.2 lever. Attached to every assembled chassis
    /// by <see cref="ChassisAssembler"/>; dormant outside arena scenes
    /// (the garage needs individual renderers for build mode).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mesh-combine, not GPU instancing.</b> The block shader (MK
    /// Toon) has no instancing variant, so <c>RenderMeshInstanced</c>
    /// submits invisible geometry. Combining meshes renders through the
    /// exact same shader path as the original individual renderers, so
    /// it is guaranteed visually identical, and the combined child sits
    /// under the chassis root in root-local space — it moves with the
    /// chassis for free, <b>zero per-frame cost</b>.
    /// </para>
    /// <para>
    /// <b>Correctness over coverage.</b> Only full-health
    /// <see cref="BlockCategory.Structure"/> blocks with a single
    /// single-submesh mesh+material are combined. The instant a block
    /// is damaged or destroyed it is <i>evicted</i>: its real
    /// MeshRenderer is re-enabled (resuming the proven per-renderer
    /// damage-darken path) and the combined mesh is rebuilt without it,
    /// debounced to once per frame regardless of how many blocks were
    /// hit that frame (PERFORMANCE.md §5.6 pattern). Blocks keep their
    /// GameObject + collider, so damage/physics/destruction are
    /// unchanged. Moving/special blocks (Movement, Weapon, Cpu beacon,
    /// Module, Cosmetic) are never combined.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChassisInstancedRenderer : MonoBehaviour
    {
        private const float FullHealthEpsilon = 0.999f;

        private sealed class Group
        {
            public Material Material;
            public int Layer;
            public GameObject Child;          // combined-mesh renderer GO
            public MeshFilter Filter;
            public Mesh CombinedMesh;
            public readonly List<BlockBehaviour> Blocks = new();
            public readonly List<MeshRenderer> Renderers = new();
            public readonly List<MeshFilter> SourceFilters = new();
            public bool Dirty;
        }

        private readonly List<Group> _groups = new();
        private readonly Dictionary<BlockBehaviour, Group> _blockToGroup = new();
        private Transform _root;
        private bool _built;
        private bool _anyDirty;
        private int _framesSinceEnable;

        private static bool IsArenaScene()
        {
            string n = SceneManager.GetActiveScene().name;
            return n == "Arena" || n == "WaterArena" || n == "PlanetArena";
        }

        private void OnEnable() => _framesSinceEnable = 0;

        private void OnDisable()
        {
            BlockBehaviour.DamageDealt -= OnAnyBlockDamaged;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _groups.Count; i++)
                if (_groups[i].CombinedMesh != null) Destroy(_groups[i].CombinedMesh);
        }

        private void LateUpdate()
        {
            if (!_built)
            {
                // Defer one frame so every block GO / binder has run its
                // own Awake/OnEnable (ChassisAssembler Phase 5).
                if (_framesSinceEnable++ < 1) return;
                _built = true;
                if (!IsArenaScene()) { enabled = false; return; }
                Build();
                if (_groups.Count == 0) { enabled = false; return; }
                BlockBehaviour.DamageDealt += OnAnyBlockDamaged;
                return;
            }

            if (!_anyDirty) return;
            _anyDirty = false;
            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                if (!g.Dirty) continue;
                g.Dirty = false;
                Recombine(g);
            }
        }

        private void Build()
        {
            Robot robot = GetComponent<Robot>();
            _root = transform;
            if (robot == null || robot.Grid == null) return;

            Matrix4x4 worldToChassis = _root.worldToLocalMatrix;
            var byMat = new Dictionary<int, Group>();

            foreach (KeyValuePair<Vector3Int, BlockBehaviour> kv in robot.Grid.Blocks)
            {
                BlockBehaviour b = kv.Value;
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Category != BlockCategory.Structure) continue;
                if (b.HealthFraction < FullHealthEpsilon) continue; // damaged → normal path

                var filters = b.GetComponentsInChildren<MeshFilter>(includeInactive: false);
                if (filters.Length != 1) continue;
                MeshFilter mf = filters[0];
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || mr.sharedMaterial == null) continue;
                if (mf.sharedMesh.subMeshCount != 1) continue;
                if (mr.sharedMaterials.Length != 1) continue;

                Material mat = mr.sharedMaterial;
                int key = mat.GetInstanceID();
                if (!byMat.TryGetValue(key, out Group g))
                {
                    g = new Group { Material = mat, Layer = mr.gameObject.layer };
                    byMat[key] = g;
                    _groups.Add(g);
                }
                g.Blocks.Add(b);
                g.Renderers.Add(mr);
                g.SourceFilters.Add(mf);
                _blockToGroup[b] = g;
                mr.enabled = false;
                b.Destroyed += OnBlockDestroyed;
            }

            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                Group g = _groups[i];
                g.Child = new GameObject($"CombinedStructure_{g.Material.name}")
                {
                    layer = g.Layer,
                };
                g.Child.transform.SetParent(_root, worldPositionStays: false);
                g.Child.transform.localPosition = Vector3.zero;
                g.Child.transform.localRotation = Quaternion.identity;
                g.Child.transform.localScale = Vector3.one;
                g.Filter = g.Child.AddComponent<MeshFilter>();
                var cmr = g.Child.AddComponent<MeshRenderer>();
                cmr.sharedMaterial = g.Material;
                cmr.shadowCastingMode = ShadowCastingMode.On;
                cmr.receiveShadows = true;
                Recombine(g);
            }
        }

        // (Re)build a group's combined mesh from its current block set,
        // in chassis-root-local space. Destroys the previous mesh to
        // avoid leaking runtime Mesh objects across rebuilds.
        private void Recombine(Group g)
        {
            int n = g.Blocks.Count;
            if (n == 0)
            {
                if (g.CombinedMesh != null) { Destroy(g.CombinedMesh); g.CombinedMesh = null; }
                g.Filter.sharedMesh = null;
                return;
            }

            Matrix4x4 worldToChassis = _root.worldToLocalMatrix;
            var combines = new CombineInstance[n];
            for (int i = 0; i < n; i++)
            {
                MeshFilter src = g.SourceFilters[i];
                combines[i] = new CombineInstance
                {
                    mesh = src.sharedMesh,
                    subMeshIndex = 0,
                    transform = worldToChassis * src.transform.localToWorldMatrix,
                };
            }

            Mesh old = g.CombinedMesh;
            var combined = new Mesh
            {
                name = $"CombinedStructure_{g.Material.name}",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave,
            };
            combined.CombineMeshes(combines, mergeSubMeshes: true, useMatrices: true);
            g.CombinedMesh = combined;
            g.Filter.sharedMesh = combined;
            if (old != null) Destroy(old);
        }

        private void OnAnyBlockDamaged(BlockBehaviour b, float _)
        {
            if (b != null && _blockToGroup.TryGetValue(b, out Group g))
                Evict(b, g, reenableRenderer: true);
        }

        private void OnBlockDestroyed(BlockBehaviour b)
        {
            if (b != null && _blockToGroup.TryGetValue(b, out Group g))
                Evict(b, g, reenableRenderer: false);
        }

        // Hand a block back to its real MeshRenderer (damage) or just
        // drop it (destroyed), and flag the group for a debounced
        // rebuild so the ghost geometry leaves the combined mesh.
        private void Evict(BlockBehaviour b, Group g, bool reenableRenderer)
        {
            int idx = g.Blocks.IndexOf(b);
            _blockToGroup.Remove(b);
            if (idx < 0) return;

            b.Destroyed -= OnBlockDestroyed;
            MeshRenderer mr = g.Renderers[idx];
            if (reenableRenderer && mr != null) mr.enabled = true;

            int last = g.Blocks.Count - 1;
            g.Blocks[idx] = g.Blocks[last];
            g.Renderers[idx] = g.Renderers[last];
            g.SourceFilters[idx] = g.SourceFilters[last];
            g.Blocks.RemoveAt(last);
            g.Renderers.RemoveAt(last);
            g.SourceFilters.RemoveAt(last);

            g.Dirty = true;
            _anyDirty = true;
        }
    }
}
