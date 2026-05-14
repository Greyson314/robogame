using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Single source of truth for "is this block placement / removal
    /// legal?" The runtime build-mode editor and the pre-instantiation
    /// blueprint validator both compose over this — when one rule
    /// shifts, both consumers shift together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each rule is a pure function that returns a
    /// <see cref="PlacementError"/> (one of the discrete failure modes)
    /// or <see cref="PlacementError.None"/> when the rule passes. The
    /// <see cref="EvaluatePlacement"/> aggregator runs them in priority
    /// order and short-circuits on the first failure, so a UI consumer
    /// can highlight *which* rule rejected the placement.
    /// </para>
    /// <para>
    /// Allocation contract: rules take a caller-owned
    /// <see cref="BlockGraph.Buffers"/> for any BFS work, and an optional
    /// pre-built CPU-reachable set so the editor can amortise its
    /// BFS over multiple per-frame validity checks. Hot-path callers
    /// hold the buffers + set as instance fields and pass them in.
    /// </para>
    /// <para>
    /// Builds on the §3.4 diagnosis in
    /// <c>docs/BUILDING_ARCHITECTURE_REVIEW.md</c>: the four BFS
    /// implementations have collapsed into <see cref="BlockGraph"/>;
    /// the rule overlap / divergence between editor and validator
    /// collapses into this class.
    /// </para>
    /// </remarks>
    public static class PlacementRules
    {
        /// <summary>
        /// Discrete failure modes for a placement / removal attempt.
        /// Mapped 1-to-1 with the rule that detects them so a UI layer
        /// can render specific feedback ("Host is leaf — can't build on
        /// a wing") rather than a generic red ghost.
        /// </summary>
        public enum PlacementError
        {
            None = 0,
            CellOccupied,
            HostMissing,
            HostNotCpuReachable,
            HostIsLeaf,
            HostFaceRejectsBlockType,
            InvalidMountFace,
            SecondCpu,
            WouldOverlapNeighbour,
            WouldOrphanOnRemoval,
        }

        /// <summary>
        /// What the player wants to place. Bundles the BlockDefinition
        /// reference + per-instance config so each rule can reach the
        /// data it needs without ad-hoc parameter packing.
        /// </summary>
        public readonly struct Candidate
        {
            public readonly BlockDefinition Definition;
            public readonly Vector3Int Cell;
            public readonly Vector3Int Up;
            public readonly Vector3 Dims;
            public readonly float Pitch;

            public Candidate(BlockDefinition definition, Vector3Int cell, Vector3Int up,
                Vector3 dims = default, float pitch = 0f)
            {
                Definition = definition;
                Cell = cell;
                Up = up == Vector3Int.zero ? Vector3Int.up : up;
                Dims = dims;
                Pitch = pitch;
            }
        }

        // -----------------------------------------------------------------
        // Per-rule evaluators
        // -----------------------------------------------------------------

        public static PlacementError CheckCellOccupied(BlockGrid grid, in Candidate c)
        {
            if (grid == null) return PlacementError.None;
            return grid.HasBlock(c.Cell) ? PlacementError.CellOccupied : PlacementError.None;
        }

        public static PlacementError CheckHostExists(BlockGrid grid, in Candidate c)
        {
            if (grid == null || grid.Count == 0) return PlacementError.None;
            Vector3Int hostCell = ResolveHostCell(grid, c);
            if (!grid.TryGetBlock(hostCell, out BlockBehaviour host) || host == null)
                return PlacementError.HostMissing;
            return PlacementError.None;
        }

        public static PlacementError CheckHostIsConnective(BlockGrid grid, in Candidate c)
        {
            if (grid == null || grid.Count == 0) return PlacementError.None;
            Vector3Int hostCell = ResolveHostCell(grid, c);
            if (!grid.TryGetBlock(hostCell, out BlockBehaviour host) || host == null)
                return PlacementError.None; // covered by CheckHostExists
            // Per-face + per-block-id check — rotor's spin-axis face
            // accepts the mechanism cube; rope's tip face accepts hook
            // / mace; mechanism-cube lateral faces accept aero only
            // (the rotor's adoption pass otherwise leaves them static).
            switch (BlockConnectivity.AcceptsPlacement(grid, host, c.Up, c.Definition))
            {
                case BlockConnectivity.AcceptDecision.None: return PlacementError.None;
                case BlockConnectivity.AcceptDecision.HostFaceRejectsBlockType:
                    return PlacementError.HostFaceRejectsBlockType;
                default: return PlacementError.HostIsLeaf;
            }
        }

        /// <summary>
        /// Is the host cell reachable from the CPU via face-adjacency?
        /// Caller passes a precomputed reachable set
        /// (<paramref name="cpuReachable"/>) — typically built once per
        /// frame by <see cref="BlockGraph.BfsFrom(BlockGrid,Vector3Int,BlockGraph.Buffers,System.Nullable{Vector3Int})"/>
        /// and shared across multiple placement checks. When the set is
        /// null (no CPU on the grid yet) this rule is a no-op so the
        /// player can drop the first CPU.
        /// </summary>
        public static PlacementError CheckHostIsCpuReachable(BlockGrid grid, in Candidate c, IReadOnlyCollection<Vector3Int> cpuReachable)
        {
            if (grid == null || cpuReachable == null) return PlacementError.None;
            if (grid.Count == 0) return PlacementError.None;
            Vector3Int hostCell = ResolveHostCell(grid, c);
            HashSet<Vector3Int> set = cpuReachable as HashSet<Vector3Int>;
            bool reachable = set != null
                ? set.Contains(hostCell)
                : ContainsLinear(cpuReachable, hostCell);
            return reachable ? PlacementError.None : PlacementError.HostNotCpuReachable;
        }

        public static PlacementError CheckMountFace(in Candidate c)
        {
            if (c.Definition == null) return PlacementError.None;
            return BlockConnectivity.IsValidMountFace(c.Definition, c.Up)
                ? PlacementError.None
                : PlacementError.InvalidMountFace;
        }

        public static PlacementError CheckSecondCpu(BlockGrid grid, in Candidate c)
        {
            if (grid == null || c.Definition == null) return PlacementError.None;
            if (c.Definition.Category != BlockCategory.Cpu) return PlacementError.None;
            foreach (var kvp in grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Category == BlockCategory.Cpu) return PlacementError.SecondCpu;
            }
            return PlacementError.None;
        }

        public static PlacementError CheckSweptOverlap(BlockGrid grid, in Candidate c)
        {
            if (grid == null || c.Definition == null) return PlacementError.None;
            return BlockOccupancy.WouldOverlapInGrid(grid, c.Definition.Id, c.Cell, c.Up, c.Dims)
                ? PlacementError.WouldOverlapNeighbour
                : PlacementError.None;
        }

        // -----------------------------------------------------------------
        // Aggregators
        // -----------------------------------------------------------------

        /// <summary>
        /// Run every placement rule in priority order against
        /// <paramref name="candidate"/>. Short-circuits on the first
        /// failing rule so the returned error is always the
        /// most-actionable one.
        /// </summary>
        public static PlacementError EvaluatePlacement(BlockGrid grid, in Candidate candidate, IReadOnlyCollection<Vector3Int> cpuReachable)
        {
            PlacementError e;
            // Order matters for UX: cell-occupied is the most-immediate
            // "no" signal; host checks come next so the player sees
            // "needs a face to mount on" before the more nuanced rules.
            if ((e = CheckCellOccupied(grid, candidate)) != PlacementError.None) return e;
            if ((e = CheckHostExists(grid, candidate)) != PlacementError.None) return e;
            if ((e = CheckHostIsConnective(grid, candidate)) != PlacementError.None) return e;
            if ((e = CheckHostIsCpuReachable(grid, candidate, cpuReachable)) != PlacementError.None) return e;
            if ((e = CheckSecondCpu(grid, candidate)) != PlacementError.None) return e;
            if ((e = CheckMountFace(candidate)) != PlacementError.None) return e;
            if ((e = CheckSweptOverlap(grid, candidate)) != PlacementError.None) return e;
            return PlacementError.None;
        }

        /// <summary>
        /// Removal-side rules: a removal is rejected only if it would
        /// orphan one or more blocks from the CPU. Other removal rules
        /// (CPU is sacred, etc.) live in the caller because they're
        /// policy decisions, not graph facts.
        /// </summary>
        public static PlacementError EvaluateRemoval(BlockGrid grid, Vector3Int cell, BlockGraph.Buffers buffers, out int orphanCount)
        {
            orphanCount = 0;
            if (grid == null || buffers == null) return PlacementError.None;
            return BlockGraph.WouldOrphanIfRemoved(grid, cell, buffers, out orphanCount)
                ? PlacementError.WouldOrphanOnRemoval
                : PlacementError.None;
        }

        /// <summary>
        /// Two-cell variant: rejects the removal only if removing BOTH
        /// <paramref name="cellA"/> and <paramref name="cellB"/> together
        /// would orphan one or more blocks from the CPU. Used by the
        /// rotor → mechanism-cube cascade so the orphan check sees the
        /// post-cascade graph, not the post-rotor-only graph.
        /// </summary>
        public static PlacementError EvaluateRemoval(BlockGrid grid, Vector3Int cellA, Vector3Int cellB, BlockGraph.Buffers buffers, out int orphanCount)
        {
            orphanCount = 0;
            if (grid == null || buffers == null) return PlacementError.None;
            return BlockGraph.WouldOrphanIfRemoved(grid, cellA, cellB, buffers, out orphanCount)
                ? PlacementError.WouldOrphanOnRemoval
                : PlacementError.None;
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// Resolve the host cell for a candidate. For Hook / Mace, walks
        /// back from <paramref name="c"/>.Cell along -c.Up looking for a
        /// rope whose chain length equals the walk distance and whose
        /// mount-up matches the tip's; if one is found, that rope is the
        /// host. For every other case, the host is the standard
        /// face-adjacent neighbour at <c>c.Cell - c.Up</c>.
        /// </summary>
        /// <remarks>
        /// Centralised so HostExists, HostIsConnective, and
        /// HostIsCpuReachable agree on what "the host" means for tip
        /// blocks. Mirrors the rope-bridge virtual edge in
        /// <see cref="BlockGraph"/>: if the BFS reaches a rope's tip cell
        /// via the rope, this resolver is what finds the rope-as-host
        /// when the placement rules ask.
        /// </remarks>
        private static Vector3Int ResolveHostCell(BlockGrid grid, in Candidate c)
        {
            if (grid != null && c.Definition != null && IsTipBlockId(c.Definition.Id))
            {
                for (int dist = 1; dist <= RopeGeometry.MaxLengthCells; dist++)
                {
                    Vector3Int probe = c.Cell - c.Up * dist;
                    if (!grid.TryGetBlock(probe, out BlockBehaviour blk) || blk == null) continue;
                    if (blk.Definition == null) break;
                    if (blk.Definition.Id == BlockIds.Rope
                        && blk.Up == c.Up
                        && RopeGeometry.ChainCellCount(blk) == dist)
                    {
                        return probe;
                    }
                    // First block along -up that isn't a matching rope
                    // ends the walk — host then falls back to the standard
                    // face-adjacent neighbour (which may or may not be
                    // this block, depending on `dist`).
                    break;
                }
            }
            return c.Cell - c.Up;
        }

        private static bool IsTipBlockId(string id) =>
            id == BlockIds.Hook || id == BlockIds.Mace || id == BlockIds.Magnet;

        private static bool ContainsLinear(IReadOnlyCollection<Vector3Int> set, Vector3Int value)
        {
            // Fallback path when the caller passes any IReadOnlyCollection
            // that isn't a HashSet. Per-frame callers pass HashSet directly
            // so this branch never fires on the hot path.
            foreach (Vector3Int v in set)
            {
                if (v == value) return true;
            }
            return false;
        }
    }
}
