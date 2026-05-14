using System;
using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Tools.Editor;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Exercises <see cref="ScriptedChassisBuilder"/>, which drives the same
    /// <c>BuildSession.TryPlace</c> verb the in-garage editor uses. These tests
    /// pin down "the scaffolder's authoring path produces a blueprint that
    /// passes the same validator a player save would" — the contract that
    /// makes default presets indistinguishable from user-built bots.
    /// </summary>
    public sealed class ScriptedChassisBuilderTests
    {
        private const string LibraryAssetPath = "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        private static BlockDefinitionLibrary LoadLibrary()
        {
            BlockDefinitionLibrary lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (lib == null)
                Assert.Inconclusive($"BlockDefinitionLibrary not found at {LibraryAssetPath}. Run Robogame → Build Everything to scaffold.");
            return lib;
        }

        // -----------------------------------------------------------------
        // Minimal CPU-only chassis — proves the temp BlockGrid + BuildSession
        // wiring works end-to-end on the smallest possible case.
        // -----------------------------------------------------------------

        [Test]
        public void Build_CpuOnly_ProducesSingleEntry()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                BlueprintPlan plan = sb.Build();
                Assert.AreEqual(1, plan.Entries.Length);
                Assert.AreEqual(BlockIds.Cpu, plan.Entries[0].BlockId);
                Assert.AreEqual(new Vector3Int(0, 0, 0), plan.Entries[0].Position);
            }
            finally { sb.Dispose(); }
        }

        // -----------------------------------------------------------------
        // Rules-engine enforcement — the script can't author placements
        // the player couldn't make. This is the core invariant of Option C.
        // -----------------------------------------------------------------

        [Test]
        public void Place_ThrowsWhenHostMissing()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Cube at (5, 0, 0) has no face-adjacent neighbour — rules
                // engine rejects with HostMissing.
                Assert.Throws<InvalidOperationException>(() =>
                    sb.Place(BlockIds.Cube, new Vector3Int(5, 0, 0), Vector3Int.right));
            }
            finally { sb.Dispose(); }
        }

        [Test]
        public void Place_ThrowsOnSecondCpu()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                Assert.Throws<InvalidOperationException>(() =>
                    sb.Place(BlockIds.Cpu, new Vector3Int(1, 0, 0), Vector3Int.right));
            }
            finally { sb.Dispose(); }
        }

        // -----------------------------------------------------------------
        // Auto-companion cascade — placing a rotor causes the mechanism
        // cube to land automatically. Same behaviour the player gets in
        // the garage; the scripted builder doesn't have to author it.
        // -----------------------------------------------------------------

        [Test]
        public void PlaceRotor_AutoPlacesMechanismCubeOnSpinAxisFace()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Rotor with default spin-axis (+Y) sits on top of CPU.
                // Auto-companion drops a cube at (0, 2, 0).
                sb.Place(BlockIds.Rotor, new Vector3Int(0, 1, 0), Vector3Int.up);
                BlueprintPlan plan = sb.Build();

                Assert.IsTrue(plan.Entries.Any(e =>
                    e.BlockId == BlockIds.Rotor && e.Position == new Vector3Int(0, 1, 0)),
                    "Rotor missing from blueprint.");
                Assert.IsTrue(plan.Entries.Any(e =>
                    e.BlockId == BlockIds.Cube && e.Position == new Vector3Int(0, 2, 0)),
                    "Auto-companion mechanism cube missing at (0, 2, 0).");
            }
            finally { sb.Dispose(); }
        }

        [Test]
        public void RotorWithFoilsHelper_ProducesValidatableChassis()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                sb.RotorWithFoils(new Vector3Int(0, 1, 0));
                BlueprintPlan plan = sb.Build();

                // CPU + rotor + auto-cube + 4 foils = 7 entries.
                Assert.AreEqual(7, plan.Entries.Length);

                // Full validation must pass — same rules a user-saved blueprint
                // would face on load. This is the round-trip Option C buys us.
                BlueprintValidationResult v = BlueprintValidator.Validate(plan, lib);
                Assert.IsTrue(v.IsValid, $"Scripted RotorWithFoils chassis failed validation:\n{v}");
            }
            finally { sb.Dispose(); }
        }

        // -----------------------------------------------------------------
        // Mirror — verifies the session's mirror plumbing produces matched
        // pairs across the X plane.
        // -----------------------------------------------------------------

        [Test]
        public void MirrorX_PlacesMatchedPairs()
        {
            var lib = LoadLibrary();
            var sb = ScriptedChassisBuilder.Create("X", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                Vector3Int rightStep = new Vector3Int(1, 0, 0);
                sb.MirrorX(b => b.Place(BlockIds.Cube, new Vector3Int(1, 0, 0), rightStep));
                BlueprintPlan plan = sb.Build();

                Assert.IsTrue(plan.Entries.Any(e =>
                    e.BlockId == BlockIds.Cube && e.Position == new Vector3Int( 1, 0, 0)));
                Assert.IsTrue(plan.Entries.Any(e =>
                    e.BlockId == BlockIds.Cube && e.Position == new Vector3Int(-1, 0, 0)));
            }
            finally { sb.Dispose(); }
        }

        // -----------------------------------------------------------------
        // Round-trip: every shipped preset can be re-derived from the same
        // build script and validates against the same library. This is the
        // "default presets are equivalent to player saves" property the
        // refactor exists to enforce.
        // -----------------------------------------------------------------

        [Test]
        public void EveryShippedPreset_PassesLibraryAwareValidation()
        {
            BlockDefinitionLibrary lib = LoadLibrary();
            string[] paths =
            {
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultGround.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultPlane.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultBuggy.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultBoat.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultBomber.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultPropPlane.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultHelicopter.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_CombatDummy.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_StressRotorTower.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_StressRopeTower.asset",
                "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_ArchDummy.asset",
            };
            int loaded = 0;
            foreach (string path in paths)
            {
                ChassisBlueprint bp = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(path);
                if (bp == null) continue;
                loaded++;
                BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
                BlueprintValidationResult v = BlueprintValidator.Validate(plan, lib);
                Assert.IsTrue(v.IsValid, $"Preset '{bp.DisplayName}' (at {path}) failed library-aware validation:\n{v}");
            }
            if (loaded == 0)
                Assert.Inconclusive("No preset assets found. Run Robogame → Build Everything to scaffold them first.");
        }
    }
}
