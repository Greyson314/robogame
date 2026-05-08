using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlockOccupancy"/>'s swept-volume
    /// dispatcher and the strict-overlap predicate. No scene state.
    /// </summary>
    public sealed class BlockOccupancyTests
    {
        private const float Eps = 1e-4f;

        // -----------------------------------------------------------------
        // Constant sync — Block can't see Movement, so the foil defaults
        // are mirrored in BlockOccupancy. This test catches drift.
        // -----------------------------------------------------------------

        [Test]
        public void FoilDefaults_StayInSyncWithAeroSurfaceBlock()
        {
            Assert.AreEqual(AeroSurfaceBlock.DefaultSpan,      BlockOccupancy.FoilDefaultSpan);
            Assert.AreEqual(AeroSurfaceBlock.DefaultThickness, BlockOccupancy.FoilDefaultThickness);
            Assert.AreEqual(AeroSurfaceBlock.DefaultChord,     BlockOccupancy.FoilDefaultChord);
        }

        // -----------------------------------------------------------------
        // Default (non-scalable) blocks
        // -----------------------------------------------------------------

        [Test]
        public void Default_BoundsIsUnitCubeAtCell()
        {
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Cube, new Vector3Int(2, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Assert.That(b.center.x, Is.EqualTo(2f).Within(Eps));
            Assert.That(b.center.y, Is.EqualTo(1f).Within(Eps));
            Assert.That(b.center.z, Is.EqualTo(0f).Within(Eps));
            Assert.That(b.size.x,   Is.EqualTo(1f).Within(Eps));
            Assert.That(b.size.y,   Is.EqualTo(1f).Within(Eps));
            Assert.That(b.size.z,   Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void StrictOverlap_AdjacentUnitCubes_DoNotOverlap()
        {
            Bounds a = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 0, 0), 1f);
            Bounds b = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(1, 0, 0), 1f);
            Assert.IsFalse(BlockOccupancy.StrictOverlap(a, b),
                "Strict overlap must not flag face-touching cells (regression: Bounds.Intersects is inclusive).");
        }

        [Test]
        public void StrictOverlap_SameCell_DoesOverlap()
        {
            Bounds a = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 0, 0), 1f);
            Bounds b = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 0, 0), 1f);
            Assert.IsTrue(BlockOccupancy.StrictOverlap(a, b));
        }

        // -----------------------------------------------------------------
        // Aerofoil — horizontal
        // -----------------------------------------------------------------

        [Test]
        public void HorizontalFoil_DefaultDims_FitsInsideHostCell()
        {
            // span=1, thickness=0.08, chord=0.9 → all under 1, no shift.
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(2, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Assert.That(b.center.x, Is.EqualTo(2f).Within(Eps));
            Assert.That(b.center.y, Is.EqualTo(1f).Within(Eps));
            Assert.That(b.center.z, Is.EqualTo(0f).Within(Eps));
            Assert.That(b.size.x, Is.EqualTo(AeroSurfaceBlock.DefaultSpan).Within(Eps));
            Assert.That(b.size.y, Is.EqualTo(AeroSurfaceBlock.DefaultThickness).Within(Eps));
            Assert.That(b.size.z, Is.EqualTo(AeroSurfaceBlock.DefaultChord).Within(Eps));
        }

        [Test]
        public void HorizontalFoil_Span2_ExtendsOneCellOutwardOnPositiveX()
        {
            // gridPos.x > 0 → outward shift is +X by (span-1)/2 = 0.5.
            // Final x range: cell 2 + shift 0.5 ± span/2 = 1.5 .. 3.5.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(2, 0, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(b.min.x, Is.EqualTo(1.5f).Within(Eps));
            Assert.That(b.max.x, Is.EqualTo(3.5f).Within(Eps));
        }

        [Test]
        public void HorizontalFoil_Span2_ExtendsOneCellOutwardOnNegativeX()
        {
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(-2, 0, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(b.min.x, Is.EqualTo(-3.5f).Within(Eps));
            Assert.That(b.max.x, Is.EqualTo(-1.5f).Within(Eps));
        }

        [Test]
        public void HorizontalFoil_Span2_ThinThickness_DoesNotPokeIntoYNeighbour()
        {
            // The foil's y extent is its thickness (0.08). A neighbor cube
            // at y=1 (range 0.5..1.5) must NOT report overlap with the
            // foil at y=0 (range -0.04..0.04).
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(2, 0, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds neighbour = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(2, 1, 0), 1f);
            Assert.IsFalse(BlockOccupancy.StrictOverlap(foil, neighbour));
        }

        [Test]
        public void HorizontalFoil_Span2_OverlapsNeighbourInExtensionDirection()
        {
            // Span-2 foil at (1,0,0): x-range [0.5..2.5]. Neighbour cube at
            // (2,0,0): x-range [1.5..2.5]. The foil pokes into the cube's
            // cell — must report overlap.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 0, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds neighbour = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(2, 0, 0), 1f);
            Assert.IsTrue(BlockOccupancy.StrictOverlap(foil, neighbour));
        }

        // -----------------------------------------------------------------
        // Aerofoil — vertical fin
        // -----------------------------------------------------------------

        [Test]
        public void VerticalFin_Span2_ExtendsAlongY()
        {
            // Tail fin has its long axis along chassis-local Y.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.AeroFin, new Vector3Int(0, 1, -3), Vector3Int.up, dims, cellSize: 1f);
            // y center shifts from 1 to 1.5 (signY=+1, magnitude 0.5).
            Assert.That(b.min.y, Is.EqualTo(0.5f).Within(Eps));
            Assert.That(b.max.y, Is.EqualTo(2.5f).Within(Eps));
            // Thickness collapses x: range [-0.04..0.04].
            Assert.That(b.size.x, Is.EqualTo(0.08f).Within(Eps));
        }

        // -----------------------------------------------------------------
        // CellSize scaling
        // -----------------------------------------------------------------

        [Test]
        public void OverlapTopology_IsScaleInvariant()
        {
            // Same overlap question, different cell sizes — answer must agree.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds foil1 = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 0, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds neigh1 = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(2, 0, 0), 1f);
            Bounds foil2 = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 0, 0), Vector3Int.up, dims, cellSize: 4f);
            Bounds neigh2 = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(2, 0, 0), 4f);
            Assert.AreEqual(
                BlockOccupancy.StrictOverlap(foil1, neigh1),
                BlockOccupancy.StrictOverlap(foil2, neigh2));
        }
    }
}
