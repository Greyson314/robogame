using System.IO;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Loads every shipped preset blueprint asset off disk and validates
    /// it. Run via Window → Test Runner → EditMode. Catches "the
    /// scaffolder authored a chassis that fails CPU connectivity" before
    /// the user notices it in-game.
    /// </summary>
    /// <remarks>
    /// Also writes ASCII dumps to <c>docs/blueprint-snapshots/</c> so the
    /// human reviewer can read what each preset looks like without
    /// opening Unity. The dump file is NOT a test fixture — it's an
    /// observable output for manual review.
    /// </remarks>
    public sealed class PresetBlueprintTests
    {
        private const string BlueprintFolder = "Assets/_Project/ScriptableObjects/Blueprints";
        private const string LibraryAssetPath = "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        // Repo-relative path for the snapshot file. Unity's Application.dataPath
        // ends in "/Assets" — go up one level to reach the project root.
        private static string SnapshotPath
            => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "docs", "blueprint-snapshots", "presets.md");

        // Every preset asset path the scaffolder produces. Add new entries
        // here when GameplayScaffolder ships a new blueprint preset. Shared with
        // ScriptedChassisBuilderTests so the two suites cannot drift apart (CHG-008).
        internal static string[] PresetPaths => new[]
        {
            BlueprintFolder + "/Blueprint_DefaultGround.asset",
            BlueprintFolder + "/Blueprint_DefaultPlane.asset",
            BlueprintFolder + "/Blueprint_DefaultGrappler.asset",
            BlueprintFolder + "/Blueprint_DefaultBoat.asset",
            BlueprintFolder + "/Blueprint_DefaultBomber.asset",
            BlueprintFolder + "/Blueprint_DefaultPropPlane.asset",
            BlueprintFolder + "/Blueprint_DefaultHelicopter.asset",
            BlueprintFolder + "/Blueprint_DefaultDrillBot.asset",
            BlueprintFolder + "/Blueprint_DefaultHoverTank.asset",
            BlueprintFolder + "/Blueprint_DefaultSpringBot.asset",
            BlueprintFolder + "/Blueprint_CombatDummy.asset",
            BlueprintFolder + "/Blueprint_StressRotorTower.asset",
            BlueprintFolder + "/Blueprint_StressRopeTower.asset",
            BlueprintFolder + "/Blueprint_ArchDummy.asset",
        };

        /// <summary>
        /// Guards the list above in one direction only, list → disk: a path
        /// that no longer exists on disk makes <see cref="Preset_PassesValidation"/>
        /// Inconclusive forever instead of failing, which hides the fact that
        /// a preset is no longer validated at all (session 61 retired the
        /// Buggy preset; the stale entry sat here as a permanent Inconclusive
        /// until the factory's 2026-09-16 suite run, F-016). A missing asset
        /// is a defect in this list or in the scaffolder, never noise.
        /// </summary>
        [Test]
        public void PresetPaths_AllExistOnDisk()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (string path in PresetPaths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    missing.Add(path);
            }
            Assert.That(missing, Is.Empty,
                "PresetPaths lists assets that do not exist. Either the scaffolder stopped producing them (remove the entry) or the asset was never committed (scaffold it via Robogame → Build Everything and commit it):\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// The other direction of the CHG-003 guard: every player-facing preset the
        /// scaffolder wires into GameStateController._presetBlueprints must be in
        /// <see cref="PresetPaths"/>, or it ships validated by nothing. The ten slot
        /// paths are hard-coded here because GameplayScaffolder's constants live in the
        /// Editor assembly; keep them in step with GameplayScaffolder.cs (the
        /// `presets.arraySize = 10` block, sessions 61/99/104 added Grappler, HoverTank,
        /// SpringBot). Red team, CHG-003 (F-022): Grappler and HoverTank were in neither
        /// test list.
        /// </summary>
        [Test]
        public void PresetPaths_CoverEveryScaffolderSlot()
        {
            string[] scaffolderSlots =
            {
                BlueprintFolder + "/Blueprint_DefaultGround.asset",      // slot 0
                BlueprintFolder + "/Blueprint_DefaultPlane.asset",       // slot 1
                BlueprintFolder + "/Blueprint_DefaultGrappler.asset",    // slot 2 (replaced Buggy, session 61)
                BlueprintFolder + "/Blueprint_DefaultBoat.asset",        // slot 3
                BlueprintFolder + "/Blueprint_DefaultBomber.asset",      // slot 4
                BlueprintFolder + "/Blueprint_DefaultPropPlane.asset",   // slot 5
                BlueprintFolder + "/Blueprint_DefaultHelicopter.asset",  // slot 6
                BlueprintFolder + "/Blueprint_DefaultDrillBot.asset",    // slot 7
                BlueprintFolder + "/Blueprint_DefaultHoverTank.asset",   // slot 8 (session 99)
                BlueprintFolder + "/Blueprint_DefaultSpringBot.asset",   // slot 9 (session 104)
            };
            var listed = new System.Collections.Generic.HashSet<string>(PresetPaths);
            var missing = new System.Collections.Generic.List<string>();
            foreach (string slot in scaffolderSlots)
                if (!listed.Contains(slot)) missing.Add(slot);
            Assert.That(missing, Is.Empty,
                "Scaffolder presets that no test validates (add them to PresetPaths):\n  " + string.Join("\n  ", missing));
        }

        [TestCaseSource(nameof(PresetPaths))]
        public void Preset_PassesValidation(string assetPath)
        {
            ChassisBlueprint bp = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(assetPath);
            if (bp == null)
            {
                Assert.Inconclusive($"Preset asset not found at {assetPath}. Run Robogame → Build Everything to scaffold.");
                return;
            }

            // Library-aware validation — same strict path the scaffolder
            // runs at write-time, so a hand-edited preset that drifted
            // from a player-buildable shape fails here too.
            BlockDefinitionLibrary lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            BlueprintValidationResult r = BlueprintValidator.Validate(plan, lib);
            Assert.IsTrue(r.IsValid, $"Validation failed for {bp.DisplayName}:\n{r}");
        }

        /// <summary>
        /// Generates a markdown snapshot of every preset's ASCII layout
        /// to <c>docs/blueprint-snapshots/presets.md</c>. Re-runs every
        /// time the test runs; check the diff into git after iteration
        /// passes so the blueprint shapes are visible in code review.
        /// </summary>
        [Test]
        public void DumpAllPresets_WritesAsciiSnapshot()
        {
            string dir = Path.GetDirectoryName(SnapshotPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (StreamWriter w = new StreamWriter(SnapshotPath, false))
            {
                w.WriteLine("# Blueprint snapshots");
                w.WriteLine();
                w.WriteLine("Auto-generated by `PresetBlueprintTests.DumpAllPresets_WritesAsciiSnapshot`.");
                w.WriteLine("Run Window → Test Runner → EditMode → DumpAllPresets to refresh.");
                w.WriteLine();
                int loaded = 0;
                foreach (string path in PresetPaths)
                {
                    ChassisBlueprint bp = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(path);
                    if (bp == null)
                    {
                        w.WriteLine($"## (missing: {path})");
                        w.WriteLine();
                        continue;
                    }
                    loaded++;
                    BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
                    BlueprintValidationResult r = BlueprintValidator.Validate(plan);
                    w.WriteLine("## " + bp.DisplayName);
                    w.WriteLine();
                    w.WriteLine("```");
                    w.Write(BlueprintAsciiDump.Dump(plan));
                    w.WriteLine("```");
                    w.WriteLine();
                    w.WriteLine("Validation: " + (r.IsValid ? "OK" : "FAILED"));
                    if (r.Errors.Count > 0 || r.Warnings.Count > 0)
                    {
                        w.WriteLine();
                        w.WriteLine("```");
                        w.Write(r.ToString());
                        w.WriteLine();
                        w.WriteLine("```");
                    }
                    w.WriteLine();
                }
                if (loaded == 0)
                    Assert.Inconclusive("No preset assets found. Scaffold first.");
            }
            UnityEngine.Debug.Log($"[PresetBlueprintTests] Wrote snapshot to {SnapshotPath}");
        }
    }
}
