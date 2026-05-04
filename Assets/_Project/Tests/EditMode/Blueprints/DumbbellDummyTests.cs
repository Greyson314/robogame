using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Asserts shape invariants on the dumbbell test dummy (renamed +
    /// reshaped in session 22 from the original barbell): two
    /// end-weight cubes flanking a single-cell handle. Sized so a
    /// hook tip can wrap around the handle.
    /// </summary>
    public sealed class DumbbellDummyTests
    {
        private const string DumbbellPath = "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DumbbellDummy.asset";

        private static ChassisBlueprint LoadDumbbell()
            => AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DumbbellPath);

        [Test]
        public void Dumbbell_PassesValidation()
        {
            ChassisBlueprint bp = LoadDumbbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the dumbbell first.");
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsTrue(result.IsValid, $"Dumbbell validation failed:\n{result}");
        }

        [Test]
        public void Dumbbell_HasTwoEndWeightsAndASingleHandleCell()
        {
            ChassisBlueprint bp = LoadDumbbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the dumbbell first.");

            // End weights are 3×3×3 cubes (27 cells each) immediately
            // adjacent to the handle at z=±1..±3.
            int negEndCells = bp.Entries.Count(e => e.Position.z >= -3 && e.Position.z <= -1);
            int posEndCells = bp.Entries.Count(e => e.Position.z >=  1 && e.Position.z <=  3);
            Assert.AreEqual(27, negEndCells, $"Expected 27 cells in the negative-Z end weight, got {negEndCells}.");
            Assert.AreEqual(27, posEndCells, $"Expected 27 cells in the positive-Z end weight, got {posEndCells}.");

            // Handle: exactly the CPU at (0,0,0). No rod cells between
            // the end weights — a dumbbell has a thin handle, not a bar.
            int handleCells = bp.Entries.Count(e => e.Position.z == 0);
            Assert.AreEqual(1, handleCells, $"Expected exactly 1 handle cell at z=0, got {handleCells}.");
        }

        [Test]
        public void Dumbbell_HasExactlyOneCpu()
        {
            ChassisBlueprint bp = LoadDumbbell();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the dumbbell first.");
            int cpuCount = bp.Entries.Count(e => e.BlockId == BlockIds.Cpu);
            Assert.AreEqual(1, cpuCount, $"Dumbbell should have exactly one CPU; found {cpuCount}.");
        }
    }
}
