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
    /// Foil constants are duplicated here from
    /// <c>Robogame.Movement.AeroSurfaceBlock</c> deliberately:
    /// <c>Robogame.Block</c> cannot reference <c>Robogame.Movement</c>
    /// (asmdef circular dep). The
    /// <c>FoilDefaults_StayInSyncWithAeroSurfaceBlock</c> test asserts
    /// the two values match — if you change one, the test will fail
    /// until you change the other.
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
        // Mirrors of AeroSurfaceBlock.Default* — kept in sync via test.
        public const float FoilDefaultSpan      = 1.00f;
        public const float FoilDefaultThickness = 0.08f;
        public const float FoilDefaultChord     = 0.90f;

        /// <summary>
        /// Swept-volume AABB in chassis-local space for the given block.
        /// Falls back to a 1x1x1 cell-aligned cube for any block id that
        /// doesn't have a registered scalable shape.
        /// </summary>
        public static Bounds ComputeSweptBoundsLocal(
            string blockId, Vector3Int gridPos, Vector3Int up, Vector3 dims, float cellSize)
        {
            if (blockId == BlockIds.Aero)
                return ComputeFoilSweptBoundsLocal(vertical: false, gridPos, up, dims, cellSize);
            if (blockId == BlockIds.AeroFin)
                return ComputeFoilSweptBoundsLocal(vertical: true, gridPos, up, dims, cellSize);
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
        // Foil-specific helpers
        // -----------------------------------------------------------------
        // Mirrors AeroSurfaceBlock.{ResolveDims, ComputeWingShift,
        // ApplyOrientationToVisual}'s geometry. Duplicated rather than
        // shared because Block can't reference Movement; sync test
        // catches drift.

        private static void ResolveFoilDims(Vector3 raw, out float span, out float thickness, out float chord)
        {
            span      = raw.x > 0f ? raw.x : FoilDefaultSpan;
            thickness = raw.y > 0f ? raw.y : FoilDefaultThickness;
            chord     = raw.z > 0f ? raw.z : FoilDefaultChord;
        }

        private static Vector3 ComputeFoilWingShift(Vector3Int cellPos, float span, bool vertical)
        {
            // A span-1 foil exactly fills its cell, so no shift; span > 1
            // shifts outward by half the over-cell extent so the wing
            // extends away from chassis origin instead of straddling.
            float magnitude = Mathf.Max(0f, span * 0.5f - 0.5f);
            if (vertical)
            {
                int signY = cellPos.y > 0 ? 1 : (cellPos.y < 0 ? -1 : 0);
                return new Vector3(0f, signY * magnitude, 0f);
            }
            int signX = cellPos.x > 0 ? 1 : (cellPos.x < 0 ? -1 : 0);
            return new Vector3(signX * magnitude, 0f, 0f);
        }

        private static Bounds ComputeFoilSweptBoundsLocal(
            bool vertical, Vector3Int gridPos, Vector3Int up, Vector3 dims, float cellSize)
        {
            ResolveFoilDims(dims, out float span, out float thickness, out float chord);

            Vector3 halfExtentsLocal = vertical
                ? new Vector3(thickness, span, chord) * 0.5f
                : new Vector3(span, thickness, chord) * 0.5f;

            Vector3 shiftLocal = ComputeFoilWingShift(gridPos, span, vertical);

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
