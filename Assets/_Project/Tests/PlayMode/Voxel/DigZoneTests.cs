using NUnit.Framework;
using Robogame.Core;
using Robogame.Voxel;
using UnityEngine;

namespace Robogame.Tests.PlayMode.Voxel
{
    /// <summary>
    /// Phase 1b machine gate per TERRAFORMING_PLAN.md §12: a DigZone
    /// instantiated programmatically, seeded with the half-space initial
    /// SDF, must produce a non-empty surface mesh and respond to a
    /// SphereSubtract brush by mutating the SDF at the brush location and
    /// re-extracting the surface. Tests the end-to-end pipeline (BrushOp
    /// → BrushApplicator max-fold → SurfaceNetsMesher → Mesh upload)
    /// without requiring a scene file or visual playtest.
    /// </summary>
    public sealed class DigZoneTests
    {
        private GameObject _go;
        private DigZone _zone;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestDigZone");
            _go.transform.position = Vector3.zero;
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();
            _go.AddComponent<MeshCollider>();
            _zone = _go.AddComponent<DigZone>();
            // Awake fires synchronously on AddComponent — _zone is now
            // initialised with the default 33-sample / 32-cell chunk.
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;
        }

        // ------------------------------------------------------------------
        // Initial state — the half-space seed must produce a non-empty
        // flat-plane mesh and register with DigField.
        // ------------------------------------------------------------------

        [Test]
        public void Awake_HalfSpaceSeed_ProducesNonEmptyMesh()
        {
            Assert.IsNotNull(_zone.CurrentMesh, "DigZone must have a mesh after Awake.");
            Assert.Greater(_zone.CurrentMesh.vertexCount, 0,
                "Half-space SDF seed straddles y = dim/2; the mesher must emit a surface plane there.");
        }

        [Test]
        public void OnEnable_RegistersWithDigField()
        {
            // Active component → DigField sees this zone via ZoneAt at the chunk centre.
            IDigZone zoneAt = DigField.ZoneAt(_zone.WorldBounds.center);
            Assert.AreSame(_zone, zoneAt,
                "DigField.ZoneAt must return the registered DigZone for points inside its bounds.");
        }

        [Test]
        public void WorldBounds_MatchesChunkSizeAndOrigin()
        {
            Bounds b = _zone.WorldBounds;
            float expectedSide = _zone.ChunkSizeCells * _zone.CellSize;
            Assert.AreEqual(expectedSide, b.size.x, 1e-4f);
            Assert.AreEqual(expectedSide, b.size.y, 1e-4f);
            Assert.AreEqual(expectedSide, b.size.z, 1e-4f);
            // Origin at (0,0,0); chunk centre at half-side along each axis.
            Assert.AreEqual(expectedSide * 0.5f, b.center.x, 1e-4f);
        }

        // ------------------------------------------------------------------
        // Brush application — Phase 1b's load-bearing assertion. The
        // SphereSubtract must (a) mutate the SDF where the brush hit and
        // (b) cause the mesher to re-extract a different surface.
        // ------------------------------------------------------------------

        [Test]
        public void ApplyBrush_SphereSubtractAtChunkCentre_MutatesSdfInsideBrush()
        {
            // Brush centre = chunk centre. The chunk centre cell is on the
            // half-space dividing plane (y = dim/2). For an interior cell
            // just below (y = dim/2 - 1) the half-space init sets sdf < 0
            // (interior). After the brush, that cell — being inside the
            // sphere — must become sdf >= 0 (exterior).
            Vector3 centre = _zone.WorldBounds.center;
            int dim = _zone.Dim;
            int dimSq = dim * dim;
            int midX = dim / 2;
            int midY = dim / 2;
            int midZ = dim / 2;
            int interiorIdx = midZ * dimSq + (midY - 1) * dim + midX;

            sbyte sdfBefore = _zone.Sdf[interiorIdx];
            Assert.Less(sdfBefore, 0,
                "Pre-condition: cell just below the half-space plane must be interior.");

            int changed = ApplyCentreSphere(centre, radiusMeters: 2.0f);
            Assert.Greater(changed, 0, "Brush must mutate at least one cell.");

            sbyte sdfAfter = _zone.Sdf[interiorIdx];
            Assert.GreaterOrEqual(sdfAfter, 0,
                $"Cell at (midX, midY-1, midZ) was interior (sdf={sdfBefore}); inside a centred 2m sphere brush it must become exterior (sdf={sdfAfter}).");
        }

        [Test]
        public void ApplyBrush_CellsOutsideBrushAabb_UnchangedSdf()
        {
            // A cell at the chunk corner (deep interior, far from the centre)
            // must NOT have its SDF changed by a small centred brush — the
            // brush AABB doesn't reach it.
            int dim = _zone.Dim;
            int dimSq = dim * dim;
            int cornerIdx = 0 * dimSq + 0 * dim + 0;   // (0,0,0)
            sbyte sdfBefore = _zone.Sdf[cornerIdx];

            ApplyCentreSphere(_zone.WorldBounds.center, radiusMeters: 2.0f);

            sbyte sdfAfter = _zone.Sdf[cornerIdx];
            Assert.AreEqual(sdfBefore, sdfAfter,
                "Cells outside the brush AABB must not be touched (TERRAFORMING_PLAN §2: max-fold restricted to brush AABB).");
        }

        [Test]
        public void ApplyBrush_RemeshesSurface_VertexCountChanges()
        {
            int preCount = _zone.CurrentMesh.vertexCount;

            int changed = ApplyCentreSphere(_zone.WorldBounds.center, radiusMeters: 2.0f);
            Assume.That(changed, Is.GreaterThan(0));

            int postCount = _zone.CurrentMesh.vertexCount;
            Assert.AreNotEqual(preCount, postCount,
                "Carving out a dome from a flat half-space must change the active-cell count → vertex count.");
        }

        [Test]
        public void ApplyBrush_MeshColliderSwapped()
        {
            MeshCollider mc = _go.GetComponent<MeshCollider>();
            Assert.IsNotNull(mc.sharedMesh, "Pre-brush: collider must reference a cooked mesh.");

            ApplyCentreSphere(_zone.WorldBounds.center, radiusMeters: 2.0f);

            Assert.IsNotNull(mc.sharedMesh,
                "Post-brush: collider must still reference a cooked mesh (atomic swap, not transient null).");
            Assert.AreSame(_zone.CurrentMesh, mc.sharedMesh,
                "Collider's sharedMesh must match the DigZone's CurrentMesh.");
        }

        // ------------------------------------------------------------------
        // Monotonicity invariant — TERRAFORMING_PLAN §2. Applying the same
        // brush twice produces no further change (idempotent under max-fold).
        // ------------------------------------------------------------------

        [Test]
        public void ApplyBrush_AppliedTwice_SecondApplicationChangesNothing()
        {
            Vector3 centre = _zone.WorldBounds.center;
            int firstChanged = ApplyCentreSphere(centre, radiusMeters: 2.0f);
            Assume.That(firstChanged, Is.GreaterThan(0));

            int secondChanged = ApplyCentreSphere(centre, radiusMeters: 2.0f);
            Assert.AreEqual(0, secondChanged,
                "Re-applying the same SphereSubtract must change nothing: SDF is at the max already. " +
                "If this fails, the max-fold invariant is broken — every brush would keep churning SDF.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private int ApplyCentreSphere(Vector3 worldCentre, float radiusMeters)
        {
            BrushOp op = new BrushOp
            {
                kind = BrushKind.SphereSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(worldCentre),
                p1 = Vector3Fixed.FromVector3(worldCentre),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(radiusMeters * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };
            return _zone.ApplyBrush(op);
        }
    }
}
