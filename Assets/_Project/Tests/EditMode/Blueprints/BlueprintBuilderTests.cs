using System;
using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlueprintBuilder"/>, <see cref="BlueprintValidator"/>,
    /// and <see cref="BlueprintAsciiDump"/>. No Unity scene state, no
    /// AssetDatabase — these run in EditMode against the data-only API.
    /// </summary>
    public sealed class BlueprintBuilderTests
    {
        // -----------------------------------------------------------------
        // Builder placements
        // -----------------------------------------------------------------

        [Test]
        public void Block_AddsSingleEntryAtCoord()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Build();
            Assert.AreEqual(1, plan.Entries.Length);
            Assert.AreEqual(BlockIds.Cpu, plan.Entries[0].BlockId);
            Assert.AreEqual(new Vector3Int(0, 0, 0), plan.Entries[0].Position);
            Assert.AreEqual(Vector3Int.up, plan.Entries[0].EffectiveUp);
        }

        [Test]
        public void Row_FillsInclusiveAxisAlignedLine()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Row(BlockIds.Cube, new Vector3Int(0, 0, -2), new Vector3Int(0, 0, 2))
                .Build();
            Assert.AreEqual(5, plan.Entries.Length); // -2, -1, 0, 1, 2 inclusive
        }

        [Test]
        public void Row_ThrowsOnNonAxisAlignedEndpoints()
        {
            BlueprintBuilder b = BlueprintBuilder.Create("X", ChassisKind.Ground);
            Assert.Throws<ArgumentException>(() =>
                b.Row(BlockIds.Cube, new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 0)));
        }

        [Test]
        public void Box_FillsRectangularRegionInclusive()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Box(BlockIds.Cube, new Vector3Int(-1, 0, -1), new Vector3Int(1, 0, 1))
                .Build();
            Assert.AreEqual(9, plan.Entries.Length); // 3×1×3
        }

        // -----------------------------------------------------------------
        // Mirror helpers
        // -----------------------------------------------------------------

        [Test]
        public void MirrorX_DoublesNonZeroXEntries()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Aero, 1, 0, 0))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.Position == new Vector3Int( 1, 0, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.Position == new Vector3Int(-1, 0, 0)));
        }

        [Test]
        public void MirrorX_DoesNotDoubleCenterCells()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Cpu, 0, 0, 0))
                .Build();
            Assert.AreEqual(1, plan.Entries.Length);
        }

        [Test]
        public void MirrorX_FlipsMountUpVector()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Weapon, new Vector3Int(2, 0, 0), new Vector3Int(1, 0, 0)))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            ChassisBlueprint.Entry posSide = Array.Find(plan.Entries, e => e.Position.x == 2);
            ChassisBlueprint.Entry negSide = Array.Find(plan.Entries, e => e.Position.x == -2);
            Assert.AreEqual(new Vector3Int( 1, 0, 0), posSide.Up);
            Assert.AreEqual(new Vector3Int(-1, 0, 0), negSide.Up);
        }

        // -----------------------------------------------------------------
        // RotorWithFoils
        // -----------------------------------------------------------------

        [Test]
        public void RotorWithFoils_PlacesRotorMechanismCubeAndFourFoils()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .RotorWithFoils(new Vector3Int(0, 1, 0))
                .Build();
            // 1 rotor + 1 mechanism cube + 4 foils = 6 entries.
            Assert.AreEqual(6, plan.Entries.Length);
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Rotor && e.Position == new Vector3Int(0, 1, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Cube  && e.Position == new Vector3Int(0, 2, 0)));
            // Four foils ringed around the mechanism cell at y=2.
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int( 1, 2, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int(-1, 2, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int( 0, 2, 1)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int( 0, 2,-1)));
        }

        [Test]
        public void RotorWithFoils_HorizontalSpinAxisRingsLaterally()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .RotorWithFoils(new Vector3Int(0, 0, 0), spinAxis: new Vector3Int(1, 0, 0))
                .Build();
            // Mechanism cell offset by +X.
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Cube && e.Position == new Vector3Int(1, 0, 0)));
            // Foils ring around (1,0,0) in the YZ plane.
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int(1,  1,  0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int(1, -1,  0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int(1,  0,  1)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Aero && e.Position == new Vector3Int(1,  0, -1)));
        }

        [Test]
        public void RotorBare_PlacesRotorOnly()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .RotorBare(new Vector3Int(0, 1, 0))
                .Build();
            Assert.AreEqual(1, plan.Entries.Length);
            Assert.AreEqual(BlockIds.Rotor, plan.Entries[0].BlockId);
        }

        // -----------------------------------------------------------------
        // BuildValidated
        // -----------------------------------------------------------------

        [Test]
        public void BuildValidated_ThrowsWhenBlueprintLacksCpu()
        {
            BlueprintBuilder b = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cube, 0, 0, 0);
            Assert.Throws<InvalidOperationException>(() => b.BuildValidated());
        }

        [Test]
        public void BuildValidated_PassesForMinimalCpuOnlyChassis()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .BuildValidated();
            Assert.AreEqual(1, plan.Entries.Length);
        }

        // -----------------------------------------------------------------
        // RopeWithHook / RopeWithMace
        // -----------------------------------------------------------------

        [Test]
        public void RopeWithHook_PlacesRopeAndHookBelow()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .RopeWithHook(new Vector3Int(0, 0, 0))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Rope && e.Position == new Vector3Int(0, 0, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Hook && e.Position == new Vector3Int(0,-1, 0)));
        }

        [Test]
        public void RopeWithMace_PlacesRopeAndMaceBelow()
        {
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .RopeWithMace(new Vector3Int(0, 0, 0))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Rope && e.Position == new Vector3Int(0, 0, 0)));
            Assert.IsTrue(Array.Exists(plan.Entries, e => e.BlockId == BlockIds.Mace && e.Position == new Vector3Int(0,-1, 0)));
        }

        [Test]
        public void Chassis_WithRopeWithHook_PassesValidation()
        {
            // CPU + cube above, rope hanging off cube, hook on rope tip.
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .RopeWithHook(new Vector3Int(0, -1, 0))
                .BuildValidated();
            Assert.AreEqual(3, plan.Entries.Length);
        }
    }
}
