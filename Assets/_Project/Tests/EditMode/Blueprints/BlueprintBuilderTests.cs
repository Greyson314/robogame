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

        [Test]
        public void MirrorX_PreservesDims()
        {
            // Regression: MirrorX used to drop Dims when reconstructing the
            // mirrored entry, so a span-4 wing on +X mirrored to a span-1
            // (default) stub on -X. Plane wings ended up lopsided.
            Vector3 wingDims = new Vector3(4f, 0.08f, 0.9f);
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0),
                    new Vector3Int(1, 0, 0),
                    wingDims))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            ChassisBlueprint.Entry posSide = Array.Find(plan.Entries, e => e.Position.x == 1);
            ChassisBlueprint.Entry negSide = Array.Find(plan.Entries, e => e.Position.x == -1);
            Assert.AreEqual(wingDims, posSide.Dims);
            Assert.AreEqual(wingDims, negSide.Dims,
                "Mirrored wing must keep the source's Dims — otherwise it lands as a default-span stub.");
        }

        [Test]
        public void MirrorX_NegatesPitch_WhenUpFlipsAcrossAxis()
        {
            // For a side-mounted wing (up=±X), MirrorAxis.X flips the
            // span direction. The chord-axis pitch rotation lands on
            // the same chassis-world direction on both sides, so the
            // SAME pitch sign tilts the tip OPPOSITE ways relative to
            // the chassis. To produce visually-symmetric tilt the
            // mirrored pitch must be negated.
            Vector3 wingDims = new Vector3(4f, 0.08f, 0.9f);
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0),
                    new Vector3Int(1, 0, 0),
                    wingDims,
                    pitchDeg: 2f))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            ChassisBlueprint.Entry posSide = Array.Find(plan.Entries, e => e.Position.x == 1);
            ChassisBlueprint.Entry negSide = Array.Find(plan.Entries, e => e.Position.x == -1);
            Assert.AreEqual(2f, posSide.Pitch, 1e-4f);
            Assert.AreEqual(-2f, negSide.Pitch, 1e-4f,
                "Mirrored side-mount wing must negate Pitch for symmetric tip-tilt.");
        }

        [Test]
        public void MirrorX_PreservesPitch_WhenUpHasNoXComponent()
        {
            // Top-mounted wing (up=+Y) under MirrorAxis.X: the up
            // doesn't flip, so the chord-axis pitch rotation lands on
            // the same chassis-world axis on both sides. Pitch is
            // preserved.
            BlueprintPlan plan = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .MirrorX(b => b.Block(BlockIds.Aero,
                    new Vector3Int(1, 0, 0),
                    new Vector3Int(0, 1, 0),
                    Vector3.zero,
                    pitchDeg: 3f))
                .Build();
            Assert.AreEqual(2, plan.Entries.Length);
            ChassisBlueprint.Entry posSide = Array.Find(plan.Entries, e => e.Position.x ==  1);
            ChassisBlueprint.Entry negSide = Array.Find(plan.Entries, e => e.Position.x == -1);
            Assert.AreEqual(3f, posSide.Pitch, 1e-4f);
            Assert.AreEqual(3f, negSide.Pitch, 1e-4f,
                "Mirrored top-mount wing must preserve Pitch — up doesn't flip under X-mirror.");
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
        // Rope+hook validation paths — exercise the rope-bridge virtual
        // edge in BlockGraph's BFS. Constructed via raw Entry arrays so
        // the test is independent of any specific authoring sugar (the
        // scripted authoring path that defaults use lives on
        // ScriptedChassisBuilder; see ScriptedChassisBuilderTests).
        // -----------------------------------------------------------------

        [Test]
        public void Chassis_WithRopeAndHookAtChainEnd_PassesValidation()
        {
            // CPU at origin; rope on CPU's +Y face; hook at the chain's
            // free end (DefaultLengthCells cells along mount-up). The
            // rope-bridge edge in BlockGraph's BFS reaches the hook from
            // the CPU without face-adjacent claims on the intermediate
            // cells.
            int len = RopeGeometry.DefaultLengthCells;
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Rope, new Vector3Int(0, 1, 0), Vector3Int.up, new Vector3(len, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Hook, new Vector3Int(0, 1 + len, 0), Vector3Int.up),
            };
            BlueprintPlan plan = new BlueprintPlan("X", ChassisKind.Ground, entries, rotorsGenerateLift: false);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsTrue(result.IsValid, $"Rope+hook validation failed:\n{result}");
        }

        [Test]
        public void Chassis_WithStrandedHook_FailsValidation()
        {
            // A hook placed at rope.cell + 2*up (instead of the chain's
            // true free end at rope.cell + N*up) is no longer reachable
            // from the CPU via either face-adjacency or the rope-bridge.
            // The validator must catch this — otherwise a half-snapped
            // rope+hook chassis would slip past placement rules at
            // blueprint-load time and fall apart at spawn.
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Rope, new Vector3Int(0, 1, 0), Vector3Int.up, new Vector3(4, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Hook, new Vector3Int(0, 3, 0)), // wrong: should be (0,5,0)
            };
            BlueprintPlan plan = new BlueprintPlan("X", ChassisKind.Ground, entries, rotorsGenerateLift: false);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsFalse(result.IsValid, "Hook at wrong-cell on the chain should fail connectivity validation.");
        }
    }
}
