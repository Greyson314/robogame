using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data regressions for the placement rule library and the
    /// per-block specialisations it pulls in. Doesn't exercise the
    /// runtime grid (that needs a scene) — the focus here is on the
    /// math + per-face flag pieces that the editor + validator share.
    /// </summary>
    public sealed class PlacementRulesTests
    {
        // -----------------------------------------------------------------
        // BlockOccupancy.StrictOverlap — FP epsilon regression
        // -----------------------------------------------------------------

        [Test]
        public void Foil_DefaultDims_OnSideFace_DoesNotFalsePositiveAdjacentCube()
        {
            // Span=1 foil at (1,0,0) up=+X. Its swept bounds is a
            // narrow box centered at (1,0,0) with thickness 0.08
            // (chassis-Y), span 1.0 (chassis-X), chord 0.9 (chassis-Z).
            // The foil's min.x edge-touches the host cube's +X face at
            // x=0.5. Without StrictOverlap's FP epsilon, quaternion
            // rotation precision shifts foil.min.x down to 0.5 - ~1e-7
            // and the strict-< comparison flags spurious overlap. This
            // test is the regression guard — it WILL fail if someone
            // re-tightens the predicate.
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0), Vector3.zero, cellSize: 1f);
            Bounds host = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 0, 0), 1f);
            Assert.IsFalse(BlockOccupancy.StrictOverlap(foil, host),
                "Default-dim foil edge-touching its host's +X face must not flag overlap (FP-precision regression).");
        }

        [Test]
        public void Foil_DefaultDims_OnSideFace_DoesNotFalsePositivePerpendicularBlade()
        {
            // Blade A on +X side of mechanism at (0,2,0): cell=(1,2,0) up=+X.
            // Blade B on +Z side: cell=(0,2,1) up=+Z.
            // Their foil meshes occupy non-overlapping volumes around
            // the mechanism cube — they share a corner near the cube's
            // edge but don't interpenetrate.
            Bounds bladeA = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(1, 2, 0), new Vector3Int(1, 0, 0), Vector3.zero, 1f);
            Bounds bladeB = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 2, 1), new Vector3Int(0, 0, 1), Vector3.zero, 1f);
            Assert.IsFalse(BlockOccupancy.StrictOverlap(bladeA, bladeB),
                "Two perpendicular default-span blades around a mechanism cube must not flag overlap.");
        }

        [Test]
        public void StrictOverlap_GenuineSpan2OverlapStillDetected()
        {
            // Sanity: the FP epsilon mustn't mask real overlap.
            // Span-2 top-mount foil at (0,1,0) extends to y=2.5; cube
            // at (0,2,0) sits in y=[1.5..2.5]. Strict overlap on y.
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, new Vector3(2f, 0.08f, 0.9f), 1f);
            Bounds neighbour = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(0, 2, 0), 1f);
            Assert.IsTrue(BlockOccupancy.StrictOverlap(foil, neighbour),
                "Span-2 foil pokes into the cell directly above; placement must reject (genuine overlap).");
        }

        // -----------------------------------------------------------------
        // BlockConnectivity.IsConnectiveFace — rotor spin-axis exception
        // -----------------------------------------------------------------

        [Test]
        public void IsConnectiveFace_Rotor_SpinAxisFaceIsConnective()
        {
            BlockDefinition rotorDef = MakeBlockDefinition(BlockIds.Rotor);
            try
            {
                Vector3Int spinAxis = new Vector3Int(0, 1, 0);
                // Placement up matches rotor's spin axis → connective.
                Assert.IsTrue(BlockConnectivity.IsConnectiveFace(rotorDef, spinAxis, spinAxis),
                    "Rotor's spin-axis face must accept the mechanism cube — that's the only way to extend a rotor.");
            }
            finally { Object.DestroyImmediate(rotorDef); }
        }

        [Test]
        public void IsConnectiveFace_Rotor_LateralFacesAreNotConnective()
        {
            BlockDefinition rotorDef = MakeBlockDefinition(BlockIds.Rotor);
            try
            {
                Vector3Int spinAxis = new Vector3Int(0, 1, 0);
                // Placement on rotor's +X face → leaf rejection.
                Assert.IsFalse(BlockConnectivity.IsConnectiveFace(rotorDef, spinAxis, new Vector3Int(1, 0, 0)),
                    "Rotor's lateral faces remain leaf — wings don't mount on a rotor's side.");
                Assert.IsFalse(BlockConnectivity.IsConnectiveFace(rotorDef, spinAxis, new Vector3Int(0, 0, -1)));
            }
            finally { Object.DestroyImmediate(rotorDef); }
        }

        [Test]
        public void IsConnectiveFace_Cube_AcceptsAllFaces()
        {
            BlockDefinition cubeDef = MakeBlockDefinition(BlockIds.Cube);
            try
            {
                Vector3Int up = new Vector3Int(0, 1, 0);
                Assert.IsTrue(BlockConnectivity.IsConnectiveFace(cubeDef, up, new Vector3Int(1, 0, 0)));
                Assert.IsTrue(BlockConnectivity.IsConnectiveFace(cubeDef, up, new Vector3Int(0, 1, 0)));
                Assert.IsTrue(BlockConnectivity.IsConnectiveFace(cubeDef, up, new Vector3Int(0, 0, -1)));
            }
            finally { Object.DestroyImmediate(cubeDef); }
        }

        [Test]
        public void IsConnectiveFace_OtherLeaves_RejectAllFaces()
        {
            // Wings, weapons, etc. are leaves on every face — only the
            // rotor exception fires.
            BlockDefinition wingDef = MakeBlockDefinition(BlockIds.Aero);
            try
            {
                Assert.IsFalse(BlockConnectivity.IsConnectiveFace(wingDef, Vector3Int.up, new Vector3Int(1, 0, 0)));
                Assert.IsFalse(BlockConnectivity.IsConnectiveFace(wingDef, Vector3Int.up, Vector3Int.up));
            }
            finally { Object.DestroyImmediate(wingDef); }
        }

        // -----------------------------------------------------------------

        // BlockDefinition has a private _id field — set it via reflection
        // so EditMode tests don't have to ship asset files. This is the
        // same path other EditMode tests use to assemble test SOs.
        private static BlockDefinition MakeBlockDefinition(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            FieldInfo idField = typeof(BlockDefinition).GetField("_id",
                BindingFlags.Instance | BindingFlags.NonPublic);
            idField.SetValue(def, id);
            return def;
        }
    }
}
