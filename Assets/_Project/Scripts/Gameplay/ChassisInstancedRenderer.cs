using System.Collections.Generic;
using Robogame.Block;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Renders a frozen in-arena chassis's static structural blocks via
    /// GPU instancing instead of ~150 individual MeshRenderers. This is
    /// the PERFORMANCE.md §8.2 lever: it collapses ~150 draws + ~150
    /// per-cascade shadow draws per chassis into a handful of instanced
    /// calls. Attached to every assembled chassis by
    /// <see cref="ChassisAssembler"/>; dormant outside arena scenes
    /// (the garage needs individual renderers for build mode).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Correctness over coverage (v1).</b> Only <see cref="BlockCategory.Structure"/>
    /// blocks at full health are instanced, and only single-submesh ones
    /// with one mesh+material. The instant a block takes damage it is
    /// <i>evicted</i> — its real MeshRenderer is re-enabled and it falls
    /// back to the proven per-renderer damage-darken path
    /// (<see cref="BlockBehaviour.UpdateDamageVisual"/>). This sidesteps
    /// the unknown of MK Toon per-instance colour entirely: instanced
    /// blocks are always base-material (visually identical), damaged
    /// blocks use the existing path. The dominant case — an intact
    /// chassis flying around — is fully batched, which is exactly the
    /// measured "looking at the bots costs 40–60 fps" scenario.
    /// </para>
    /// <para>
    /// Blocks keep their GameObject + collider (damage raycasts,
    /// destruction, physics are unchanged); only the MeshRenderer is
    /// disabled while batched. Blocks are rigid relative to the single
    /// chassis Rigidbody, so each block's chassis-local matrix is cached
    /// once and only multiplied by the chassis transform per frame — no
    /// per-frame allocations (PERFORMANCE.md §2.1, invariant 6).
    /// </para>
    /// <para>
    /// Moving / special blocks (Movement, Weapon, Cpu beacon, Module,
    /// Cosmetic) are never batched — they render normally, so rotors,
    /// wheels, the CPU beacon, etc. are untouched.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChassisInstancedRenderer : MonoBehaviour
    {
        private const float FullHealthEpsilon = 0.999f;
        private const int MaxInstancesPerCall = 1023; // Graphics instancing cap.

        private sealed class Group
        {
            public Mesh Mesh;
            public Material Material;
            public int Layer;
            public readonly List<Transform> Transforms = new();
            public readonly List<Matrix4x4> LocalMatrices = new(); // chassis-local, constant
            public readonly List<BlockBehaviour> Blocks = new();
            public readonly List<MeshRenderer> Renderers = new();
            public Matrix4x4[] WorldScratch = System.Array.Empty<Matrix4x4>();
            public int Count;
        }

        private readonly List<Group> _groups = new();
        private readonly Dictionary<BlockBehaviour, Group> _blockToGroup = new();
        private Transform _root;
        private bool _active;
        private int _framesSinceEnable;

        private static bool IsArenaScene()
        {
            string n = SceneManager.GetActiveScene().name;
            return n == "Arena" || n == "WaterArena" || n == "PlanetArena";
        }

        private void OnEnable()
        {
            _framesSinceEnable = 0;
        }

        private void OnDisable()
        {
            BlockBehaviour.DamageDealt -= OnAnyBlockDamaged;
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                // Defer one frame after enable so every block GO /
                // binder has finished its own Awake/OnEnable pass
                // (Phase 5 post-activation in ChassisAssembler).
                if (_framesSinceEnable++ < 1) return;
                if (!IsArenaScene()) { enabled = false; return; }
                Build();
                _active = true;
                if (_groups.Count == 0) { enabled = false; return; }
                BlockBehaviour.DamageDealt += OnAnyBlockDamaged;
            }

            Matrix4x4 chassisToWorld = _root.localToWorldMatrix;
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                Group g = _groups[gi];
                if (g.Count == 0) continue;
                for (int i = 0; i < g.Count; i++)
                    g.WorldScratch[i] = chassisToWorld * g.LocalMatrices[i];

                var rp = new RenderParams(g.Material)
                {
                    layer = g.Layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                };
                for (int start = 0; start < g.Count; start += MaxInstancesPerCall)
                {
                    int n = Mathf.Min(MaxInstancesPerCall, g.Count - start);
                    Graphics.RenderMeshInstanced(rp, g.Mesh, 0, g.WorldScratch, n, start);
                }
            }
        }

        private void Build()
        {
            Robot robot = GetComponent<Robot>();
            _root = transform;
            if (robot == null || robot.Grid == null) return;

            Matrix4x4 worldToChassis = _root.worldToLocalMatrix;
            var byKey = new Dictionary<(int, int), Group>();

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

                Mesh mesh = mf.sharedMesh;
                Material mat = mr.sharedMaterial;
                var key = (mesh.GetInstanceID(), mat.GetInstanceID());
                if (!byKey.TryGetValue(key, out Group g))
                {
                    g = new Group { Mesh = mesh, Material = mat, Layer = mr.gameObject.layer };
                    byKey[key] = g;
                    _groups.Add(g);
                }

                g.Transforms.Add(mf.transform);
                g.LocalMatrices.Add(worldToChassis * mf.transform.localToWorldMatrix);
                g.Blocks.Add(b);
                g.Renderers.Add(mr);
                _blockToGroup[b] = g;
                mr.enabled = false;
                b.Destroyed += OnBlockDestroyed;
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                g.Count = g.Blocks.Count;
                g.WorldScratch = new Matrix4x4[g.Count];
            }
        }

        // First damage to a batched block: hand it back to its real
        // MeshRenderer so the existing per-renderer darken path takes
        // over. Cheap (rare relative to frame rate) and keeps damage
        // visuals identical to before.
        private void OnAnyBlockDamaged(BlockBehaviour b, float _)
        {
            if (b == null) return;
            if (_blockToGroup.TryGetValue(b, out Group g)) Evict(b, g, reenableRenderer: true);
        }

        private void OnBlockDestroyed(BlockBehaviour b)
        {
            if (b == null) return;
            if (_blockToGroup.TryGetValue(b, out Group g)) Evict(b, g, reenableRenderer: false);
        }

        private void Evict(BlockBehaviour b, Group g, bool reenableRenderer)
        {
            int idx = g.Blocks.IndexOf(b);
            if (idx < 0) { _blockToGroup.Remove(b); return; }

            MeshRenderer mr = g.Renderers[idx];
            b.Destroyed -= OnBlockDestroyed;
            if (reenableRenderer && mr != null) mr.enabled = true;

            int last = g.Count - 1;
            g.Transforms[idx] = g.Transforms[last];
            g.LocalMatrices[idx] = g.LocalMatrices[last];
            g.Blocks[idx] = g.Blocks[last];
            g.Renderers[idx] = g.Renderers[last];
            g.Transforms.RemoveAt(last);
            g.LocalMatrices.RemoveAt(last);
            g.Blocks.RemoveAt(last);
            g.Renderers.RemoveAt(last);
            g.Count = last;
            _blockToGroup.Remove(b);
        }
    }
}
