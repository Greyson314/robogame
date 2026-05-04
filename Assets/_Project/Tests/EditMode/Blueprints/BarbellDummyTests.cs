using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Asserts shape invariants on the barbell test dummy (added in
    /// session 19 phase 6): two end masses, a thin rod between, and
    /// enough surface area for hooks / maces to bite into.
    /// </summary>
    public sealed class BarbellDummyTests
    {
        private const string BarbellPath = "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_BarbellDummy.asset";

        private static ChassisBlueprint LoadBarbell()
            => AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(BarbellPath);

        [Test]
        public void Barbell_PassesValidation()
        {
            ChassisBlueprint bp = LoadBarbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the barbell first.");
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsTrue(result.IsValid, $"Barbell validation failed:\n{result}");
        }

        [Test]
        public void Barbell_HasTwoEndMassesAndAThinRod()
        {
            ChassisBlueprint bp = LoadBarbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the barbell first.");

            // Two end masses at z extremes (3×3×3 each = 27 cells).
            int negEndCells = bp.Entries.Count(e => e.Position.z >= -7 && e.Position.z <= -5);
            int posEndCells = bp.Entries.Count(e => e.Position.z >=  5 && e.Position.z <=  7);
            Assert.AreEqual(27, negEndCells, $"Expected 27 cells in the negative-Z end mass, got {negEndCells}.");
            Assert.AreEqual(27, posEndCells, $"Expected 27 cells in the positive-Z end mass, got {posEndCells}.");

            // Rod cells (z between the bells, exclusive) sit on the x=0,y=0 axis.
            var rodCells = bp.Entries
                .Where(e => e.Position.z > -5 && e.Position.z < 5)
                .ToList();
            foreach (var e in rodCells)
            {
                Assert.AreEqual(0, e.Position.x, $"Rod cell {e.Position} drifted off the x=0 axis.");
                Assert.AreEqual(0, e.Position.y, $"Rod cell {e.Position} drifted off the y=0 axis.");
            }
            // 9 rod cells (z=-4..-1, 0, 1..4 inclusive) — CPU sits at (0,0,0).
            Assert.AreEqual(9, rodCells.Count, $"Expected 9 rod cells (z=-4..4), got {rodCells.Count}.");
        }

        [Test]
        public void Barbell_HasExactlyOneCpu()
        {
            ChassisBlueprint bp = LoadBarbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the barbell first.");
            int cpuCount = bp.Entries.Count(e => e.BlockId == BlockIds.Cpu);
            Assert.AreEqual(1, cpuCount, $"Barbell should have exactly one CPU; found {cpuCount}.");
        }
    }
}
