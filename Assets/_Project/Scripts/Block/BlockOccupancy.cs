using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Static dispatcher for per-block-type swept-volume bounds, plus the
    /// strict-overlap predicate the placement and validation paths share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why static, not per-component: the placement check runs *before*
    /// the candidate block is instantiated (the build-mode editor needs
    /// to decide whether a click would create a valid block). A
    /// MonoBehaviour interface can't answer that question. The dispatcher
    /// switches on stable <see cref="BlockIds"/> strings and routes to
    /// inline math (currently aerofoils only).
    /// </para>
    /// <para>
    /// Foil constants are read from <see cref="FoilDefaults"/> — the
    /// authoritative single-source-of-truth for foil shape constants
    /// in <see cref="Robogame.Block"/>. <c>Robogame.Movement.AeroSurfaceBlock</c>
    /// reads from the same place; the per-side aliases here exist only
    /// for source-compat with shipped tests / call sites.
    /// </para>
    /// <para>
    /// The bounds are in chassis-local space, scaled by <c>cellSize</c>.
    /// For runtime checks pass the live grid's <see cref="BlockGrid.CellSize"/>.
    /// For pure-data validation (<see cref="BlueprintValidator"/>) pass
    /// 1.0 — overlap topology is scale-invariant, so the unit-cell
    /// answer matches whatever cell size the chassis ships with.
    /// </para>
    /// <para>
    /// Allocation-free: every helper is O(1) per entry and reuses
    /// stack <see cref="Vector3"/> values. No per-frame heap churn.
    /// </para>
    /// </remarks>
    public static class BlockOccupancy
    {
        // Aliases of the authoritative FoilDefaults constants, kept here
        // for backwards compatibility with shipped tests / call sites.
        // The single source of truth is Block.FoilDefaults.
        public const float FoilDefaultSpan      = FoilDefaults.DefaultSpan;
        public const float FoilDefaultThickness = FoilDefaults.DefaultThickness;
        public const float FoilDefaultChord     = FoilDefaults.DefaultChord;

        /// <summary>
        /// Swept-volume AABB in chassis-local space for the given block.
        /// Falls back to a 1x1x1 cell-aligned cube for any block id that
        /// doesn't have a registered scalable shape.
        /// </summary>
        public static Bounds ComputeSweptBoundsLocal(
            string blockId, Vector3Int gridPos, Vector3Int up, Vector3 dims, float cellSize)
        {
            if (blockId == BlockIds.Aero || blockId == BlockIds.AeroFin)
                return ComputeFoilSweptBoundsLocal(gridPos, up, dims, cellSize);
            return DefaultUnitCellBoundsLocal(gridPos, cellSize);
        }

        /// <summary>1x1x1 chassis-local AABB centred on the cell. Default for non-scalable blocks.</summary>
        public static Bounds DefaultUnitCellBoundsLocal(Vector3Int gridPos, float cellSize)
            => new Bounds((Vector3)gridPos * cellSize, Vector3.one * cellSize);

        /// <summary>
        /// Strict AABB overlap: returns true only when the bounds
        /// interpenetrate. Edge-touching does NOT count, so two unit
        /// cubes at adjacent integer cells correctly report no overlap.
        /// </summary>
        public static bool StrictOverlap(in Bounds a, in Bounds b)
        {
            Vector3 amin = a.min, amax = a.max;
            Vector3 bmin = b.min, bmax = b.max;
            return amin.x < bmax.x && amax.x > bmin.x
                && amin.y < bmax.y && amax.y > bmin.y
                && amin.z < bmax.z && amax.z > bmin.z;
        }

        /// <summary>
        /// Would a candidate placement at <paramref name="gridPos"/> with
        /// the given block id / up / dims interpenetrate any existing
        /// block on the chassis? Used by
        /// <see cref="Robogame.Gameplay.BlockEditor"/> to gate placement.
        /// O(<see cref="BlockGrid.Count"/>) per call; cheap because
        /// placements are click-driven, not per-frame.
        /// </summary>
        public static bool WouldOverlapInGrid(
            BlockGrid grid, string blockId, Vector3Int gridPos, Vector3Int up, Vector3 dims)
        {
            if (grid == null) return false;
            float cellSize = grid.CellSize;
            Bounds candidate = ComputeSweptBoundsLocal(blockId, gridPos, up, dims, cellSize);
            foreach (var kvp in grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                Bounds existing = ComputeSweptBoundsLocal(
                    b.Definition.Id, b.GridPosition, b.Up, b.Dims, cellSize);
                if (StrictOverlap(candidate, existing)) return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Foil-specific helpers — mirrors the geometry contract in
        // AeroSurfaceBlock (ComputeFoilMeshScale + ComputeWingShift).
        // The constants are shared via FoilDefaults; the math itself
        // is duplicated because Block can't reference Movement and the
        // formula is short. If the formula grows, push it onto a
        // FoilBlockData ScriptableObject sidecar (per §3.5).
        // -----------------------------------------------------------------

        private static void ResolveFoilDims(Vector3 raw, out float span, out float thickness, out float chord)
        {
            span      = raw.x > 0f ? raw.x : FoilDefaults.DefaultSpan;
            thickness = raw.y > 0f ? raw.y : FoilDefaults.DefaultThickness;
            chord     = raw.z > 0f ? raw.z : FoilDefaults.DefaultChord;
        }

        private static Bounds ComputeFoilSweptBoundsLocal(
            Vector3Int gridPos, Vector3Int up, Vector3 dims, float cellSize)
        {
            ResolveFoilDims(dims, out float span, out float thickness, out float chord);

            // Build-mode foils use the "vertical-treatment" geometry:
            // span along foil-local +Y (the mount-up direction so the
            // wing extends OUT from the host face), thickness along
            // foil-local +X, chord along foil-local +Z. Rotor blades
            // use the alternate horizontal layout but they're never
            // placed via the build editor — only the build path runs
            // through this dispatcher.
            Vector3 halfExtentsLocal = new Vector3(thickness, span, chord) * 0.5f;

            // Outward shift along foil-local +Y (mount-normal direction)
            // for span > 1. OrientationFromUp(up) rotates this to the
            // chassis-local outward direction.
            float magnitude = Mathf.Max(0f, span * 0.5f - 0.5f);
            Vector3 shiftLocal = new Vector3(0f, magnitude, 0f);

            Quaternion rot = BlockGrid.OrientationFromUp(up);

            // Abs-sum of rotated half-extents along each axis is the AABB
            // half-extent of the rotated OBB. Correct for any rotation.
            Vector3 hx = rot * new Vector3(halfExtentsLocal.x, 0f, 0f);
            Vector3 hy = rot * new Vector3(0f, halfExtentsLocal.y, 0f);
            Vector3 hz = rot * new Vector3(0f, 0f, halfExtentsLocal.z);
            Vector3 chassisHalfExtents = new Vector3(
                Mathf.Abs(hx.x) + Mathf.Abs(hy.x) + Mathf.Abs(hz.x),
                Mathf.Abs(hx.y) + Mathf.Abs(hy.y) + Mathf.Abs(hz.y),
                Mathf.Abs(hx.z) + Mathf.Abs(hy.z) + Mathf.Abs(hz.z));

            Vector3 centerLocal = (Vector3)gridPos + rot * shiftLocal;

            return new Bounds(centerLocal * cellSize, chassisHalfExtents * 2f * cellSize);
        }
    }
}
