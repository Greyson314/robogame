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

        // -----------------------------------------------------------------
        // Swept-volume occupancy
        // -----------------------------------------------------------------

        [Test]
        public void Span2WingPokingIntoNeighbour_FailsValidation()
        {
            // Wing at (1,0,0) with span=2 extends x∈[0.5..2.5]; the cube at
            // (2,0,0) sits in x∈[1.5..2.5]. Strict overlap.
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero, new Vector3Int(1, 0, 0), Vector3Int.up,
                       new Vector3(2f, 0.08f, 0.9f))
                .Block(BlockIds.Cube, 2, 0, 0)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("overlaps"));
        }

        [Test]
        public void DefaultDimWingsAtAdjacentCells_PassValidation()
        {
            // Span=1 default foils stay inside their host cells; placing
            // them face-adjacent must not flag overlap (regression check
            // for the strict-overlap predicate vs Bounds.Intersects).
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero, 1, 0, 0)
                .Block(BlockIds.Aero, 2, 0, 0)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
        }

        [Test]
        public void AdjacentUnitCubes_PassValidation()
        {
            // Defends the strict-overlap predicate's edge-touching = no-overlap
            // contract. If this test fails, every shipped blueprint breaks.
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Row(BlockIds.Cube, new Vector3Int(1, 0, 0), new Vector3Int(4, 0, 0))
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
        }
    }
}
