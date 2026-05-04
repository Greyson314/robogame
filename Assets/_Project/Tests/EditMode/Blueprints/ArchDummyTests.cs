using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Asserts shape invariants on the arch test dummy (renamed +
    /// reshaped in session 24, was the dumbbell, originally the
    /// barbell). Two grounded pillars + a top beam — designed for the
    /// hook-grapple test loop where the J-hook scoops the top beam.
    /// </summary>
    public sealed class ArchDummyTests
    {
        private const string ArchPath = "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_ArchDummy.asset";

        private static ChassisBlueprint LoadArch()
            => AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(ArchPath);

        [Test]
        public void Arch_PassesValidation()
        {
            ChassisBlueprint bp = LoadArch();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the arch first.");
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsTrue(result.IsValid, $"Arch validation failed:\n{result}");
        }

        [Test]
        public void Arch_HasTwoPillarsAndATopBeam()
        {
            ChassisBlueprint bp = LoadArch();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the arch first.");

            int leftPillar  = bp.Entries.Count(e => e.Position.x == -2 && e.Position.y >= 0 && e.Position.y <= 6 && e.Position.z == 0);
            int rightPillar = bp.Entries.Count(e => e.Position.x ==  2 && e.Position.y >= 0 && e.Position.y <= 6 && e.Position.z == 0);
            Assert.AreEqual(7, leftPillar,  $"Expected 7 cells in left pillar, got {leftPillar}.");
            Assert.AreEqual(7, rightPillar, $"Expected 7 cells in right pillar, got {rightPillar}.");

            int beam = bp.Entries.Count(e => e.Position.y == 7 && e.Position.z == 0 && e.Position.x >= -2 && e.Position.x <= 2);
            Assert.AreEqual(5, beam, $"Expected 5 top-beam cells, got {beam}.");
        }

        [Test]
        public void Arch_HasExactlyOneCpu()
        {
            ChassisBlueprint bp = LoadArch();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the arch first.");
            int cpuCount = bp.Entries.Count(e => e.BlockId == BlockIds.Cpu);
            Assert.AreEqual(1, cpuCount, $"Arch should have exactly one CPU; found {cpuCount}.");
        }
    }
}
