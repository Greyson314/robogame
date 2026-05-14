using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Allocation-free graph queries over a chassis: face-adjacency BFS
    /// from a root cell, "would removing X orphan anything", "where's the
    /// CPU". One implementation, four-plus consumers — placement
    /// validation, removal validation, blueprint validation, damage
    /// detachment all share the same primitive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Allocation contract.</b> Every method takes a caller-owned
    /// <see cref="Buffers"/> instance and reuses its internal collections.
    /// Per-frame callers (<see cref="Robogame.Gameplay.BlockEditor"/>)
    /// hold one <see cref="Buffers"/> as an instance field; one-shot
    /// callers (<see cref="BlueprintValidator"/>) can mint a local one.
    /// No method allocates per call past the buffers' initial capacity.
    /// </para>
    /// <para>
    /// <b>Two grid models.</b> Runtime callers operate on a live
    /// <see cref="BlockGrid"/> (Dictionary&lt;Vector3Int, BlockBehaviour&gt;);
    /// pre-instantiation validators operate on a HashSet&lt;Vector3Int&gt;
    /// of positions only. Both shapes have an overload — the BFS body is
    /// identical except for the "does cell exist" check.
    /// </para>
    /// </remarks>
    public static class BlockGraph
    {
        /// <summary>
        /// Reusable scratch buffers for one BFS query. Hold one as an
        /// instance field on a per-frame caller and pass it in. The
        /// <see cref="Visited"/> set IS the result of the most recent
        /// call — read it after BfsFrom returns.
        /// </summary>
        public sealed class Buffers
        {
            /// <summary>Cells reachable from the BFS root, including the root itself.</summary>
            public readonly HashSet<Vector3Int> Visited = new HashSet<Vector3Int>(64);

            /// <summary>Frontier queue; empty after BfsFrom returns.</summary>
            public readonly Queue<Vector3Int> Frontier = new Queue<Vector3Int>(64);

            /// <summary>Reset both collections to empty without releasing capacity.</summary>
            public void Clear()
            {
                Visited.Clear();
                Frontier.Clear();
            }
        }

        // The six axis-aligned face neighbours. Static so callers don't
        // need to allocate a stepper array every frame.
        private static readonly Vector3Int[] s_face =
        {
            new Vector3Int( 1,  0,  0), new Vector3Int(-1,  0,  0),
            new Vector3Int( 0,  1,  0), new Vector3Int( 0, -1,  0),
            new Vector3Int( 0,  0,  1), new Vector3Int( 0,  0, -1),
        };

        // -----------------------------------------------------------------
        // BFS
        // -----------------------------------------------------------------

        /// <summary>
        /// Face-adjacency BFS from <paramref name="root"/> over the live
        /// blocks in <paramref name="grid"/>. Result is in
        /// <c>buffers.Visited</c> when the call returns. Pass an
        /// <paramref name="ignoreCell"/> to simulate "what if this cell
        /// were gone" (used by the orphan check). No-op if the root cell
        /// isn't in the grid.
        /// </summary>
        public static void BfsFrom(BlockGrid grid, Vector3Int root, Buffers buffers, Vector3Int? ignoreCell = null)
        {
            if (buffers == null) return;
            buffers.Clear();
            if (grid == null) return;
            if (!grid.HasBlock(root)) return;

            buffers.Visited.Add(root);
            buffers.Frontier.Enqueue(root);
            while (buffers.Frontier.Count > 0)
            {
                Vector3Int c = buffers.Frontier.Dequeue();
                for (int i = 0; i < s_face.Length; i++)
                {
                    Vector3Int n = c + s_face[i];
                    if (ignoreCell.HasValue && n == ignoreCell.Value) continue;
                    if (buffers.Visited.Contains(n)) continue;
                    if (!grid.HasBlock(n)) continue;
                    buffers.Visited.Add(n);
                    buffers.Frontier.Enqueue(n);
                }
                EnqueueRopeBridge(grid, c, buffers, ignoreCell);
            }
        }

        /// <summary>
        /// Face-adjacency BFS from <paramref name="root"/> over a set of
        /// positions. Used by the blueprint validator and any other
        /// pre-instantiation reachability check that doesn't have a live
        /// grid. Result is in <c>buffers.Visited</c>.
        /// </summary>
        public static void BfsFrom(HashSet<Vector3Int> positions, Vector3Int root, Buffers buffers, Vector3Int? ignoreCell = null)
        {
            if (buffers == null) return;
            buffers.Clear();
            if (positions == null) return;
            if (!positions.Contains(root)) return;

            buffers.Visited.Add(root);
            buffers.Frontier.Enqueue(root);
            while (buffers.Frontier.Count > 0)
            {
                Vector3Int c = buffers.Frontier.Dequeue();
                for (int i = 0; i < s_face.Length; i++)
                {
                    Vector3Int n = c + s_face[i];
                    if (ignoreCell.HasValue && n == ignoreCell.Value) continue;
                    if (buffers.Visited.Contains(n)) continue;
                    if (!positions.Contains(n)) continue;
                    buffers.Visited.Add(n);
                    buffers.Frontier.Enqueue(n);
                }
            }
        }

        /// <summary>
        /// Rope-bridge-aware variant of <see cref="BfsFrom(HashSet{Vector3Int},Vector3Int,Buffers,System.Nullable{Vector3Int})"/>.
        /// <paramref name="entries"/> lets the BFS resolve rope chain
        /// lengths from blueprint data so a rope at one cell virtually
        /// connects to its tip cell (one chain length along mount-up),
        /// mirroring the live-grid behaviour in
        /// <see cref="BfsFrom(BlockGrid,Vector3Int,Buffers,System.Nullable{Vector3Int})"/>.
        /// Used by <see cref="BlueprintValidator"/> to validate
        /// rope+hook chassis layouts before instantiation.
        /// </summary>
        public static void BfsFrom(HashSet<Vector3Int> positions,
            IReadOnlyDictionary<Vector3Int, ChassisBlueprint.Entry> entries,
            Vector3Int root, Buffers buffers, Vector3Int? ignoreCell = null)
        {
            if (buffers == null) return;
            buffers.Clear();
            if (positions == null) return;
            if (!positions.Contains(root)) return;

            buffers.Visited.Add(root);
            buffers.Frontier.Enqueue(root);
            while (buffers.Frontier.Count > 0)
            {
                Vector3Int c = buffers.Frontier.Dequeue();
                for (int i = 0; i < s_face.Length; i++)
                {
                    Vector3Int n = c + s_face[i];
                    if (ignoreCell.HasValue && n == ignoreCell.Value) continue;
                    if (buffers.Visited.Contains(n)) continue;
                    if (!positions.Contains(n)) continue;
                    buffers.Visited.Add(n);
                    buffers.Frontier.Enqueue(n);
                }
                EnqueueRopeBridgeFromEntries(positions, entries, c, buffers, ignoreCell);
            }
        }

        // -----------------------------------------------------------------
        // CPU + connectivity helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Locate the first cell whose definition is a
        /// <see cref="BlockCategory.Cpu"/> on <paramref name="grid"/>.
        /// Iteration order is dictionary order — when more than one CPU
        /// is present (an authoring error caught by the validator), this
        /// picks an arbitrary-but-stable one.
        /// </summary>
        public static Vector3Int? FindCpuCell(BlockGrid grid)
        {
            if (grid == null) return null;
            foreach (var kvp in grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Category == BlockCategory.Cpu) return kvp.Key;
            }
            return null;
        }

        /// <summary>
        /// True if any non-CPU block survives, but won't be reachable from
        /// the CPU once <paramref name="cell"/> is removed.
        /// <paramref name="orphanCount"/> is populated either way.
        /// </summary>
        /// <remarks>
        /// Returns false when there's no CPU on the grid (the caller's
        /// "CPU is sacred" rule is what actually protects the structure
        /// in that edge case) or when <paramref name="cell"/> IS the CPU
        /// cell (also protected upstream).
        /// </remarks>
        public static bool WouldOrphanIfRemoved(BlockGrid grid, Vector3Int cell, Buffers buffers, out int orphanCount)
        {
            orphanCount = 0;
            if (grid == null || buffers == null) return false;
            Vector3Int? cpu = FindCpuCell(grid);
            if (!cpu.HasValue || cpu.Value == cell) return false;

            BfsFrom(grid, cpu.Value, buffers, ignoreCell: cell);
            int total = grid.Count - 1; // pretend `cell` is gone
            orphanCount = total - buffers.Visited.Count;
            return orphanCount > 0;
        }

        /// <summary>
        /// Two-cell variant of <see cref="WouldOrphanIfRemoved(BlockGrid,Vector3Int,Buffers,out int)"/>.
        /// Used by cascade-removal paths (e.g. removing a rotor + its
        /// auto-placed mechanism cube together so the orphan check
        /// doesn't false-positive on the cube that was the rotor's only
        /// dependent).
        /// </summary>
        public static bool WouldOrphanIfRemoved(BlockGrid grid, Vector3Int cellA, Vector3Int cellB, Buffers buffers, out int orphanCount)
        {
            orphanCount = 0;
            if (grid == null || buffers == null) return false;
            Vector3Int? cpu = FindCpuCell(grid);
            if (!cpu.HasValue) return false;
            if (cpu.Value == cellA || cpu.Value == cellB) return false;

            BfsFromIgnoreTwo(grid, cpu.Value, cellA, cellB, buffers);
            int total = grid.Count - 2; // pretend both are gone
            orphanCount = total - buffers.Visited.Count;
            return orphanCount > 0;
        }

        private static void BfsFromIgnoreTwo(BlockGrid grid, Vector3Int root, Vector3Int ignA, Vector3Int ignB, Buffers buffers)
        {
            if (buffers == null) return;
            buffers.Clear();
            if (grid == null) return;
            if (!grid.HasBlock(root)) return;
            if (root == ignA || root == ignB) return;

            buffers.Visited.Add(root);
            buffers.Frontier.Enqueue(root);
            while (buffers.Frontier.Count > 0)
            {
                Vector3Int c = buffers.Frontier.Dequeue();
                for (int i = 0; i < s_face.Length; i++)
                {
                    Vector3Int n = c + s_face[i];
                    if (n == ignA || n == ignB) continue;
                    if (buffers.Visited.Contains(n)) continue;
                    if (!grid.HasBlock(n)) continue;
                    buffers.Visited.Add(n);
                    buffers.Frontier.Enqueue(n);
                }
                EnqueueRopeBridge(grid, c, buffers, ignA, ignB);
            }
        }

        // -----------------------------------------------------------------
        // Rope-bridge virtual edges
        // -----------------------------------------------------------------
        // The rope spans `chain length in cells` cells from its grid cell
        // to its tip cell; the intermediate cells are unclaimed (the
        // chain visually "passes through" them). For connectivity, BFS
        // treats rope.cell ↔ rope.tipCell as a single virtual edge —
        // mirrors the host-resolution walk in
        // <see cref="PlacementRules"/> so what's reachable matches what's
        // legally placeable.

        private static void EnqueueRopeBridge(BlockGrid grid, Vector3Int c, Buffers buffers, Vector3Int? ignoreCell)
        {
            if (!grid.TryGetBlock(c, out BlockBehaviour bb) || bb == null || bb.Definition == null) return;
            string id = bb.Definition.Id;
            if (id == BlockIds.Rope)
            {
                Vector3Int tip = RopeGeometry.TipCell(bb);
                TryEnqueueLive(grid, tip, buffers, ignoreCell);
            }
            else if (id == BlockIds.Hook || id == BlockIds.Mace || id == BlockIds.Magnet)
            {
                Vector3Int up = bb.Up == Vector3Int.zero ? Vector3Int.up : bb.Up;
                for (int dist = 1; dist <= RopeGeometry.MaxLengthCells; dist++)
                {
                    Vector3Int probe = c - up * dist;
                    if (!grid.TryGetBlock(probe, out BlockBehaviour rope) || rope == null) continue;
                    if (rope.Definition == null) break;
                    if (rope.Definition.Id == BlockIds.Rope
                        && rope.Up == up
                        && RopeGeometry.ChainCellCount(rope) == dist)
                    {
                        TryEnqueueLive(grid, probe, buffers, ignoreCell);
                    }
                    break;
                }
            }
        }

        // Two-cell-ignore variant for cascade-removal checks (rotor + mechanism cube).
        private static void EnqueueRopeBridge(BlockGrid grid, Vector3Int c, Buffers buffers, Vector3Int ignA, Vector3Int ignB)
        {
            if (!grid.TryGetBlock(c, out BlockBehaviour bb) || bb == null || bb.Definition == null) return;
            string id = bb.Definition.Id;
            if (id == BlockIds.Rope)
            {
                Vector3Int tip = RopeGeometry.TipCell(bb);
                if (tip != ignA && tip != ignB) TryEnqueueLive(grid, tip, buffers, ignoreCell: null);
            }
            else if (id == BlockIds.Hook || id == BlockIds.Mace || id == BlockIds.Magnet)
            {
                Vector3Int up = bb.Up == Vector3Int.zero ? Vector3Int.up : bb.Up;
                for (int dist = 1; dist <= RopeGeometry.MaxLengthCells; dist++)
                {
                    Vector3Int probe = c - up * dist;
                    if (!grid.TryGetBlock(probe, out BlockBehaviour rope) || rope == null) continue;
                    if (rope.Definition == null) break;
                    if (rope.Definition.Id == BlockIds.Rope
                        && rope.Up == up
                        && RopeGeometry.ChainCellCount(rope) == dist
                        && probe != ignA && probe != ignB)
                    {
                        TryEnqueueLive(grid, probe, buffers, ignoreCell: null);
                    }
                    break;
                }
            }
        }

        private static void TryEnqueueLive(BlockGrid grid, Vector3Int cell, Buffers buffers, Vector3Int? ignoreCell)
        {
            if (ignoreCell.HasValue && cell == ignoreCell.Value) return;
            if (buffers.Visited.Contains(cell)) return;
            if (!grid.HasBlock(cell)) return;
            buffers.Visited.Add(cell);
            buffers.Frontier.Enqueue(cell);
        }

        private static void EnqueueRopeBridgeFromEntries(HashSet<Vector3Int> positions,
            IReadOnlyDictionary<Vector3Int, ChassisBlueprint.Entry> entries,
            Vector3Int c, Buffers buffers, Vector3Int? ignoreCell)
        {
            if (entries == null) return;
            if (!entries.TryGetValue(c, out ChassisBlueprint.Entry entry)) return;
            if (entry.BlockId == BlockIds.Rope)
            {
                Vector3Int tip = RopeGeometry.TipCell(entry);
                TryEnqueuePositions(positions, tip, buffers, ignoreCell);
            }
            else if (entry.BlockId == BlockIds.Hook
                     || entry.BlockId == BlockIds.Mace
                     || entry.BlockId == BlockIds.Magnet)
            {
                Vector3Int up = entry.EffectiveUp;
                for (int dist = 1; dist <= RopeGeometry.MaxLengthCells; dist++)
                {
                    Vector3Int probe = c - up * dist;
                    if (!entries.TryGetValue(probe, out ChassisBlueprint.Entry ropeEntry)) continue;
                    if (ropeEntry.BlockId == BlockIds.Rope
                        && ropeEntry.EffectiveUp == up
                        && RopeGeometry.ChainCellCount(ropeEntry) == dist)
                    {
                        TryEnqueuePositions(positions, probe, buffers, ignoreCell);
                    }
                    break;
                }
            }
        }

        private static void TryEnqueuePositions(HashSet<Vector3Int> positions, Vector3Int cell, Buffers buffers, Vector3Int? ignoreCell)
        {
            if (ignoreCell.HasValue && cell == ignoreCell.Value) return;
            if (buffers.Visited.Contains(cell)) return;
            if (!positions.Contains(cell)) return;
            buffers.Visited.Add(cell);
            buffers.Frontier.Enqueue(cell);
        }
    }
}
