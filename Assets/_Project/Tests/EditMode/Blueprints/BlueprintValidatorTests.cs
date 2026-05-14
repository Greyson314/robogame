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
        public void RotorWithFoilsLayout_PassesConnectivity()
        {
            // CPU at origin, rotor on top, mechanism cube above the rotor,
            // four foils ring the mechanism cube. All connected via face
            // adjacency back to CPU. Constructed via raw entries (the
            // validator's job is shape-validation independent of any
            // authoring helper — the equivalent player-buildable path
            // lives on ScriptedChassisBuilder + BuildSession.TryPlace).
            Vector3Int rotorCell = new Vector3Int(0, 1, 0);
            Vector3Int mechCell  = new Vector3Int(0, 2, 0);
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Rotor, rotorCell, Vector3Int.up),
                new ChassisBlueprint.Entry(BlockIds.Cube,  mechCell,  Vector3Int.up),
                new ChassisBlueprint.Entry(BlockIds.Aero,  mechCell + new Vector3Int( 1, 0, 0), new Vector3Int( 1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Aero,  mechCell + new Vector3Int(-1, 0, 0), new Vector3Int(-1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Aero,  mechCell + new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0, 1)),
                new ChassisBlueprint.Entry(BlockIds.Aero,  mechCell + new Vector3Int( 0, 0,-1), new Vector3Int( 0, 0,-1)),
            };
            BlueprintPlan plan = new BlueprintPlan("X", ChassisKind.Ground, entries, rotorsGenerateLift: true);
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
            // Foil at (1,1,0) with up=+Y and span=2 extends along chassis
            // +Y to y∈[0.5..2.5]; the cube at (1,2,0) sits in y∈[1.5..2.5].
            // Strict overlap on y. (Foil span axis = mount-up direction.)
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Cube, 1, 0, 0)
                .Block(BlockIds.Aero, new Vector3Int(1, 1, 0), Vector3Int.up,
                       new Vector3(2f, 0.08f, 0.9f))
                .Block(BlockIds.Cube, 1, 2, 0)
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

        // -----------------------------------------------------------------
        // Pitch range
        // -----------------------------------------------------------------

        [Test]
        public void PitchInsideSoftLimit_PassesValidation()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    Vector3.zero, pitchDeg: 8f)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
            Assert.IsEmpty(r.Warnings);
        }

        [Test]
        public void PitchPastSoftLimit_WarnsButValid()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    Vector3.zero, pitchDeg: 19f)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsTrue(r.IsValid, r.ToString());
            Assert.That(r.Warnings, Has.Some.Contains("stall margin"));
        }

        [Test]
        public void PitchPastHardLimit_FailsValidation()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    Vector3.zero, pitchDeg: 25f)
                .Build();
            BlueprintValidationResult r = BlueprintValidator.Validate(plan);
            Assert.IsFalse(r.IsValid);
            Assert.That(r.Errors, Has.Some.Contains("hard limit"));
        }

        [Test]
        public void NegativePitch_SymmetricLimit()
        {
            // Tail elevators want -1° — should pass with no warning.
            // -19° is in the warning range. -25° is over the hard limit.
            BlueprintPlan ok = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero, new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    Vector3.zero, pitchDeg: -1f)
                .Build();
            Assert.IsTrue(BlueprintValidator.Validate(ok).IsValid);

            BlueprintPlan over = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Aero, new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    Vector3.zero, pitchDeg: -25f)
                .Build();
            Assert.IsFalse(BlueprintValidator.Validate(over).IsValid);
        }
    }
}
