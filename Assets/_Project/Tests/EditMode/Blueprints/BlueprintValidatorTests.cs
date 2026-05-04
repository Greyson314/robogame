using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    public sealed class BlueprintValidatorTests
    {
        [Test]
        public void EmptyBlueprint_FailsWithNoEntriesError()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground).Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("no entries"));
        }

        [Test]
        public void BlueprintWithoutCpu_FailsValidation()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cube, 0, 0, 0)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("no CPU"));
        }

        [Test]
        public void BlueprintWithDuplicateCells_FailsValidation()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Cube, 0, 0, 0) // collide
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("Duplicate cell"));
        }

        [Test]
        public void BlueprintWithOrphanedCell_FailsConnectivity()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Cube, 5, 0, 0) // not face-adjacent to anything
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("not connected to the CPU"));
        }

        [Test]
        public void ChainOfCubes_PassesConnectivity()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Row(BlockIds.Cube, new Vector3Int(1, 0, 0), new Vector3Int(5, 0, 0))
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
        }

        [Test]
        public void RotorWithFoils_OnSimpleCpu_PassesConnectivity()
        {
            // Rotor sits on top of CPU; mechanism cube at +Y above rotor;
            // four foils ring the mechanism cube. All connected via face
            // adjacency back to CPU.
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .RotorWithFoils(new Vector3Int(0, 1, 0))
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
        }

        [Test]
        public void TwoCpus_ProducesWarningButStillValid()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Cpu, 1, 0, 0)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
            Assert.That(r.Warnings, Has.Some.Contains("CPU"));
        }
    }
}
