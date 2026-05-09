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
        // Aerofoil — span always extends along mount-up (the face normal).
        // Top mount → up. Side mount → sideways. Front mount → forward.
        // -----------------------------------------------------------------

        [Test]
        public void Foil_DefaultDims_FitsInsideHostCell()
        {
            // span=1, thickness=0.08, chord=0.9 → all under 1, no shift.
            // For up=+Y the foil-local frame matches chassis frame, so
            // mesh axes (thickness, span, chord) → chassis (X, Y, Z).
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(2, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Assert.That(b.center.x, Is.EqualTo(2f).Within(Eps));
            Assert.That(b.center.y, Is.EqualTo(1f).Within(Eps));
            Assert.That(b.center.z, Is.EqualTo(0f).Within(Eps));
            Assert.That(b.size.x, Is.EqualTo(AeroSurfaceBlock.DefaultThickness).Within(Eps));
            Assert.That(b.size.y, Is.EqualTo(AeroSurfaceBlock.DefaultSpan).Within(Eps));
            Assert.That(b.size.z, Is.EqualTo(AeroSurfaceBlock.DefaultChord).Within(Eps));
        }

        [Test]
        public void Foil_TopMount_Span2_ExtendsUpward()
        {
            // up=+Y → span aligns with chassis +Y. Outward shift = 0.5.
            // y-center: 1 + 0.5 = 1.5. y-range: [0.5, 2.5].
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(b.min.y, Is.EqualTo(0.5f).Within(Eps));
            Assert.That(b.max.y, Is.EqualTo(2.5f).Within(Eps));
        }

        [Test]
        public void Foil_RightSideMount_Span2_ExtendsRightward()
        {
            // up=+X → span aligns with chassis +X.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0), dims, cellSize: 1f);
            Assert.That(b.min.x, Is.EqualTo(0.5f).Within(Eps));
            Assert.That(b.max.x, Is.EqualTo(2.5f).Within(Eps));
        }

        [Test]
        public void Foil_FrontMount_Span2_ExtendsForward()
        {
            // up=+Z → span aligns with chassis +Z.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1), dims, cellSize: 1f);
            Assert.That(b.min.z, Is.EqualTo(0.5f).Within(Eps));
            Assert.That(b.max.z, Is.EqualTo(2.5f).Within(Eps));
        }

        [Test]
        public void Foil_BottomMount_Span2_ExtendsDownward()
        {
            // up=-Y → span aligns with chassis -Y. Foil hangs down from host.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, -1, 0), new Vector3Int(0, -1, 0), dims, cellSize: 1f);
            Assert.That(b.min.y, Is.EqualTo(-2.5f).Within(Eps));
            Assert.That(b.max.y, Is.EqualTo(-0.5f).Within(Eps));
        }

        [Test]
        public void Foil_TopMount_Span2_OverlapsCellDirectlyAbove()
        {
            // Span-2 top-mount foil at (0,1,0) extends y=[0.5..2.5];
            // a cube at (0,2,0) sits in y=[1.5..2.5] — strict overlap.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds neighbour = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 2, 0), 1f);
            Assert.IsTrue(BlockOccupancy.StrictOverlap(foil, neighbour),
                "Span-2 foil pokes into the cell directly above; placement must reject.");
        }

        [Test]
        public void Foil_TopMount_Span2_DoesNotPokeLaterally()
        {
            // Top-mount foil's lateral extent is just chord (0.9) wide along
            // chassis +Z and thickness (0.08) along chassis +X. Sideways
            // neighbours at (±1, 1, 0) etc. must NOT overlap.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds lateral = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(1, 1, 0), 1f);
            Assert.IsFalse(BlockOccupancy.StrictOverlap(foil, lateral),
                "Thin top-mount foil must not bleed into lateral neighbours.");
        }

        [Test]
        public void Foil_AeroAndAeroFin_HaveSameGeometry()
        {
            // Single-rule model: both Aero and AeroFin extend span along
            // mount-up. The build-mode binder collapses them, so the
            // dispatcher should too.
            Vector3 dims = new Vector3(2f, 0.08f, 0.9f);
            Bounds aero = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds aeroFin = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.AeroFin, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(aero.center, Is.EqualTo(aeroFin.center));
            Assert.That(aero.size,   Is.EqualTo(aeroFin.size));
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
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Bounds neigh1 = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 2, 0), 1f);
            Bounds foil2 = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 4f);
            Bounds neigh2 = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 2, 0), 4f);
            Assert.AreEqual(
                BlockOccupancy.StrictOverlap(foil1, neigh1),
                BlockOccupancy.StrictOverlap(foil2, neigh2));
        }
    }
}
