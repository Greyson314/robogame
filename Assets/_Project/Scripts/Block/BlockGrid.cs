using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Spatial container for the blocks that make up a robot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Coordinate system.</b> Grid coordinates are integer triples
    /// in the local space of this transform; world position = local
    /// origin + <c>gridPos * CellSize</c>.
    /// </para>
    /// <para>
    /// <b>Storage.</b> Backed by a <see cref="Dictionary{TKey,TValue}"/>
    /// for O(1) lookup. Not Unity-serializable; rebuild at load time
    /// from authoring data (will be addressed when we add save/load).
    /// </para>
    /// <para>
    /// <b>Block graph.</b> Use <see cref="GetNeighbors"/> for the six
    /// axis-aligned neighbours; downstream systems (damage propagation,
    /// connectivity check on CPU loss) build on this.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockGrid : MonoBehaviour
    {
        [Tooltip("Edge length of one grid cell, in world units. Use the same value across the project.")]
        [SerializeField, Min(0.01f)] private float _cellSize = 1f;

        [Tooltip("Draw cell wireframes for occupied positions in the Scene view when selected.")]
        [SerializeField] private bool _drawGizmos = true;

        private readonly Dictionary<Vector3Int, BlockBehaviour> _blocks = new();

        public float CellSize => _cellSize;
        public int Count => _blocks.Count;
        public IReadOnlyDictionary<Vector3Int, BlockBehaviour> Blocks => _blocks;

        /// <summary>Fired immediately after a block is added to the grid.</summary>
        public event Action<BlockBehaviour> BlockPlaced;

        /// <summary>Fired immediately before a block is removed from the grid (block + GameObject still valid).</summary>
        public event Action<BlockBehaviour> BlockRemoving;

        private void Awake()
        {
            // The dictionary isn't Unity-serialized, so cells authored in the
            // editor would be invisible at runtime. Rebuild from existing
            // BlockBehaviour children before anyone (e.g. Robot) reads us.
            RebuildFromChildren();
        }

        /// <summary>
        /// Repopulate <see cref="Blocks"/> from any <see cref="BlockBehaviour"/>
        /// components found beneath this transform. Safe to call multiple times;
        /// idempotent except for the <see cref="BlockBehaviour.Destroyed"/> hookup.
        /// </summary>
        public void RebuildFromChildren()
        {
            _blocks.Clear();
            BlockBehaviour[] children = GetComponentsInChildren<BlockBehaviour>(includeInactive: true);
            foreach (BlockBehaviour b in children)
            {
                if (b == null) continue;
                Vector3Int pos = b.GridPosition;
                if (_blocks.ContainsKey(pos))
                {
                    Debug.LogWarning($"[Robogame] BlockGrid.RebuildFromChildren: duplicate cell {pos} on '{b.name}', skipping.", b);
                    continue;
                }
                _blocks[pos] = b;
                b.Destroyed -= HandleBlockDestroyed; // avoid double-subscribe
                b.Destroyed += HandleBlockDestroyed;
            }
        }

        // -----------------------------------------------------------------
        // Coordinate conversion
        // -----------------------------------------------------------------

        public Vector3 GridToLocal(Vector3Int gridPos) => (Vector3)gridPos * _cellSize;
        public Vector3 GridToWorld(Vector3Int gridPos) => transform.TransformPoint(GridToLocal(gridPos));

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return new Vector3Int(
                Mathf.RoundToInt(local.x / _cellSize),
                Mathf.RoundToInt(local.y / _cellSize),
                Mathf.RoundToInt(local.z / _cellSize));
        }

        // -----------------------------------------------------------------
        // Mutators
        // -----------------------------------------------------------------

        /// <summary>
        /// Spawn a block at <paramref name="gridPos"/>. Returns the created
        /// <see cref="BlockBehaviour"/>, or <c>null</c> if the cell is occupied
        /// or the definition is invalid.
        /// </summary>
        public BlockBehaviour PlaceBlock(BlockDefinition definition, Vector3Int gridPos)
        {
            if (definition == null)
            {
                Debug.LogError("[Robogame] BlockGrid.PlaceBlock: definition is null.", this);
                return null;
            }
            if (_blocks.ContainsKey(gridPos))
            {
                Debug.LogWarning($"[Robogame] BlockGrid: cell {gridPos} already occupied.", this);
                return null;
            }

            GameObject go;
            if (definition.Prefab != null)
            {
                go = Instantiate(definition.Prefab, transform);
            }
            else
            {
                // Fallback visual: unit cube primitive. Strip its rigidbody if any.
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform, worldPositionStays: false);
            }

            go.name = $"Block_{definition.Id}_{gridPos.x}_{gridPos.y}_{gridPos.z}";
            go.transform.localPosition = GridToLocal(gridPos);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * _cellSize;

            BlockBehaviour block = go.GetComponent<BlockBehaviour>();
            if (block == null) block = go.AddComponent<BlockBehaviour>();
            block.Initialize(definition, gridPos);

            _blocks[gridPos] = block;
            block.Destroyed += HandleBlockDestroyed;
            BlockPlaced?.Invoke(block);
            return block;
        }

        /// <summary>Remove and destroy a block. Returns true if a block was present.</summary>
        public bool RemoveBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out BlockBehaviour block)) return false;
            BlockRemoving?.Invoke(block);
            _blocks.Remove(gridPos);
            if (block != null)
            {
                block.Destroyed -= HandleBlockDestroyed;
                if (Application.isPlaying) Destroy(block.gameObject);
                else DestroyImmediate(block.gameObject);
            }
            return true;
        }

        /// <summary>Drop every block. Used during scaffolding/rebuild.</summary>
        public void Clear()
        {
            // Iterate over a copy because RemoveBlock mutates the dictionary.
            var positions = new List<Vector3Int>(_blocks.Keys);
            foreach (Vector3Int pos in positions)
            {
                RemoveBlock(pos);
            }
            _blocks.Clear();

            // Belt-and-braces: in edit mode the dictionary may have been empty
            // (it isn't serialized), so blast any leftover BlockBehaviour
            // children to keep this idempotent across editor scaffold runs.
            BlockBehaviour[] strays = GetComponentsInChildren<BlockBehaviour>(includeInactive: true);
            foreach (BlockBehaviour stray in strays)
            {
                if (stray == null) continue;
                if (Application.isPlaying) Destroy(stray.gameObject);
                else DestroyImmediate(stray.gameObject);
            }
        }

        /// <summary>
        /// Remove a block from the grid <i>without</i> destroying its GameObject.
        /// Used to spawn detached blocks as physics debris.
        /// </summary>
        public BlockBehaviour DetachBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out BlockBehaviour block)) return null;
            BlockRemoving?.Invoke(block);
            _blocks.Remove(gridPos);
            if (block != null)
            {
                block.Destroyed -= HandleBlockDestroyed;
            }
            return block;
        }

        private void HandleBlockDestroyed(BlockBehaviour block)
        {
            if (block == null) return;
            // Defer the remove by one frame so subscribers (e.g. Robot connectivity)
            // can react with the block still present in the grid for context.
            // For now we just remove inline — Robot listens to BlockRemoving above.
            RemoveBlock(block.GridPosition);
        }

        // -----------------------------------------------------------------
        // Damage propagation
        // -----------------------------------------------------------------

        /// <summary>
        /// Apply damage centred at <paramref name="gridPos"/>, falling off through
        /// neighbouring blocks. <paramref name="ringDamage"/>[i] is the damage applied
        /// to blocks <i>i</i> graph-steps from the centre (index 0 = direct hit).
        /// </summary>
        /// <remarks>
        /// Uses BFS across existing block neighbours, so damage only propagates
        /// through actually-connected blocks (no leaping across gaps).
        /// </remarks>
        public void ApplySplashDamage(Vector3Int gridPos, IReadOnlyList<float> ringDamage)
        {
            if (ringDamage == null || ringDamage.Count == 0) return;
            if (!_blocks.ContainsKey(gridPos)) return;

            var visited = new HashSet<Vector3Int> { gridPos };
            var frontier = new Queue<(Vector3Int pos, int ring)>();
            frontier.Enqueue((gridPos, 0));

            // Snapshot the queue per ring so all blocks in a ring take damage
            // before any of them fall off — avoids order-dependent destruction.
            var pendingDamage = new List<(BlockBehaviour block, float amount)>();

            while (frontier.Count > 0)
            {
                var (pos, ring) = frontier.Dequeue();
                if (ring >= ringDamage.Count) continue;

                if (_blocks.TryGetValue(pos, out BlockBehaviour b) && b.IsAlive)
                {
                    pendingDamage.Add((b, ringDamage[ring]));
                }

                if (ring + 1 < ringDamage.Count)
                {
                    foreach (Vector3Int offset in s_neighborOffsets)
                    {
                        Vector3Int next = pos + offset;
                        if (visited.Add(next) && _blocks.ContainsKey(next))
                        {
                            frontier.Enqueue((next, ring + 1));
                        }
                    }
                }
            }

            foreach (var (block, amount) in pendingDamage)
            {
                if (block != null && block.IsAlive)
                {
                    block.TakeDamage(amount);
                }
            }
        }

        // -----------------------------------------------------------------
        // Connectivity
        // -----------------------------------------------------------------

        /// <summary>
        /// Return all grid positions reachable from <paramref name="root"/> by
        /// walking through existing blocks on the six axes.
        /// </summary>
        public HashSet<Vector3Int> GetReachableFrom(Vector3Int root)
        {
            var reachable = new HashSet<Vector3Int>();
            if (!_blocks.ContainsKey(root)) return reachable;

            var stack = new Stack<Vector3Int>();
            stack.Push(root);
            reachable.Add(root);

            while (stack.Count > 0)
            {
                Vector3Int p = stack.Pop();
                foreach (Vector3Int offset in s_neighborOffsets)
                {
                    Vector3Int n = p + offset;
                    if (_blocks.ContainsKey(n) && reachable.Add(n))
                    {
                        stack.Push(n);
                    }
                }
            }
            return reachable;
        }

        /// <summary>
        /// Find every block <i>not</i> reachable from <paramref name="root"/>.
        /// Useful for "CPU lost connection" detachment passes.
        /// </summary>
        public List<BlockBehaviour> FindDisconnectedFrom(Vector3Int root)
        {
            var reachable = GetReachableFrom(root);
            var orphans = new List<BlockBehaviour>();
            foreach (var kvp in _blocks)
            {
                if (!reachable.Contains(kvp.Key)) orphans.Add(kvp.Value);
            }
            return orphans;
        }

        // -----------------------------------------------------------------
        // Queries
        // -----------------------------------------------------------------

        public bool TryGetBlock(Vector3Int gridPos, out BlockBehaviour block) => _blocks.TryGetValue(gridPos, out block);
        public bool HasBlock(Vector3Int gridPos) => _blocks.ContainsKey(gridPos);

        private static readonly Vector3Int[] s_neighborOffsets = new[]
        {
            new Vector3Int( 1,  0,  0), new Vector3Int(-1,  0,  0),
            new Vector3Int( 0,  1,  0), new Vector3Int( 0, -1,  0),
            new Vector3Int( 0,  0,  1), new Vector3Int( 0,  0, -1),
        };

        /// <summary>Yield each existing block adjacent to <paramref name="gridPos"/> on the six axes.</summary>
        public IEnumerable<BlockBehaviour> GetNeighbors(Vector3Int gridPos)
        {
            foreach (Vector3Int offset in s_neighborOffsets)
            {
                if (_blocks.TryGetValue(gridPos + offset, out BlockBehaviour neighbor))
                {
                    yield return neighbor;
                }
            }
        }

        // -----------------------------------------------------------------
        // Gizmos
        // -----------------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            foreach (Vector3Int pos in _blocks.Keys)
            {
                Gizmos.DrawWireCube(GridToLocal(pos), Vector3.one * _cellSize);
            }

            // Origin marker.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Vector3.zero, _cellSize * 0.15f);
        }
#endif
    }
}
