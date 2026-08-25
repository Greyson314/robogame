// =============================================================================
// BuildSessionApplyToPlacedTests — EditMode (LOG-172 explicit Apply verb)
//
// INVARIANTS COVERED
//   • ApplyVariantCachesToPlacedBlocks writes the caches onto EVERY placed
//     block of the id (and only that id), returns the count, and syncs the
//     blueprint so save/launch see the values.
//   • With a tune-mode instance BOUND it is a no-op returning 0 — the
//     span-isolation contract (implicit all-blocks propagation stays
//     retired; the bound flow owns per-instance writes) is not weakened by
//     the button's existence.
//   • SeedVariantCachesFromPlacedBlock copies a placed block's tune into
//     the caches (so the panel shows / applies real values, not sentinel
//     zeros) and reports false when nothing of the id is placed.
//
// PATTERN
//   MakeDef/MakeBlock reflection stubs mirror BuildSessionInstanceEditTests.
//   Grid blocks are injected into BlockGrid's private _blocks dictionary —
//   PlaceBlock needs the full placement pipeline, and these tests only need
//   membership.
// =============================================================================

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    public sealed class BuildSessionApplyToPlacedTests
    {
        private readonly List<GameObject> _spawned = new();
        private GameObject _gridGo;
        private BlockGrid _grid;
        private ChassisBlueprint _blueprint;
        private BuildSession _session;

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, 100f);
            return def;
        }

        private BlockBehaviour AddBlockToGrid(string blockId, Vector3Int cell)
        {
            var go = new GameObject($"TestBlock_{blockId}_{cell}");
            _spawned.Add(go);
            BlockBehaviour bb = go.AddComponent<BlockBehaviour>();
            var initMethod = typeof(BlockBehaviour).GetMethod(
                "Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(initMethod,
                "BlockBehaviour.Initialize not found — signature drifted? These tests " +
                "must build placed-block stubs the same way BlockGrid does.");
            initMethod.Invoke(bb, new object[]
            {
                MakeDef(blockId), cell, Vector3.zero, Vector3Int.up, 0f, 0
            });

            var blocksField = typeof(BlockGrid).GetField(
                "_blocks", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(blocksField, "BlockGrid._blocks not found — field renamed?");
            var dict = (Dictionary<Vector3Int, BlockBehaviour>)blocksField.GetValue(_grid);
            dict[cell] = bb;
            return bb;
        }

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("TestGrid");
            _grid = _gridGo.AddComponent<BlockGrid>();
            _blueprint = ScriptableObject.CreateInstance<ChassisBlueprint>();
            _session = new BuildSession();
            _session.Bind(_grid, _blueprint, library: null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_blueprint != null) Object.DestroyImmediate(_blueprint);
        }

        /// <summary>
        /// The Apply button's contract: every placed block of the id gets
        /// the cached config, other block types are untouched, and the
        /// blueprint entries carry the value immediately.
        ///
        /// WHY: this is the fix for the pogo-power report — a tuned value
        /// that reaches the caches but not the placed blocks + blueprint is
        /// exactly the silent no-op the player hit. Save and launch read
        /// the ENTRIES, so blueprint sync is part of the contract, not a
        /// nicety.
        /// </summary>
        [Test]
        public void ApplyVariantCaches_UnboundWithPlacedBlocks_WritesAllOfIdAndSyncsBlueprint()
        {
            BlockBehaviour pogoA = AddBlockToGrid(BlockIds.Pogo, new Vector3Int(0, 0, 0));
            BlockBehaviour pogoB = AddBlockToGrid(BlockIds.Pogo, new Vector3Int(1, 0, 0));
            BlockBehaviour cube = AddBlockToGrid(BlockIds.Cube, new Vector3Int(2, 0, 0));

            _session.SetVariantConfig(BlockIds.Pogo, 4f);
            int written = _session.ApplyVariantCachesToPlacedBlocks(BlockIds.Pogo);

            Assert.AreEqual(2, written,
                "Apply must report exactly the number of placed blocks of the id it wrote.");
            Assert.AreEqual(4f, pogoA.ConfigValue, 1e-4f,
                "Every placed pogo must receive the cached config on Apply.");
            Assert.AreEqual(4f, pogoB.ConfigValue, 1e-4f,
                "Every placed pogo must receive the cached config on Apply.");
            Assert.AreEqual(0f, cube.ConfigValue, 1e-4f,
                "Blocks of OTHER types must not be touched by an Apply for the pogo id.");

            bool foundPogoEntry = false;
            foreach (ChassisBlueprint.Entry e in _blueprint.Entries)
            {
                if (e.BlockId != BlockIds.Pogo) continue;
                foundPogoEntry = true;
                Assert.AreEqual(4f, e.BlockConfig, 1e-4f,
                    "Apply must sync the blueprint entry — save/launch read entries, " +
                    "and an unsynced entry reverts the tune on the next rebuild.");
            }
            Assert.IsTrue(foundPogoEntry,
                "Blueprint must contain the pogo entries after Apply's SyncBlueprint.");
        }

        /// <summary>
        /// With a tune-mode instance bound, Apply is a no-op. The bound
        /// flow is per-instance BY DESIGN (span-isolation: one foil's edit
        /// must never rewrite every foil), and the button is hidden in that
        /// mode — this guards the backend against a stale click anyway.
        /// </summary>
        [Test]
        public void ApplyVariantCaches_WhenInstanceBound_NoOpReturnsZero()
        {
            BlockBehaviour pogoA = AddBlockToGrid(BlockIds.Pogo, new Vector3Int(0, 0, 0));
            BlockBehaviour pogoB = AddBlockToGrid(BlockIds.Pogo, new Vector3Int(1, 0, 0));
            _session.SetEditingInstance(pogoA);
            _session.SetVariantConfig(BlockIds.Pogo, 4f);

            int written = _session.ApplyVariantCachesToPlacedBlocks(BlockIds.Pogo);

            Assert.AreEqual(0, written,
                "Apply must refuse to blanket-write while an instance is bound — " +
                "per-instance tune mode owns writes in that state (span-isolation).");
            Assert.AreEqual(0f, pogoB.ConfigValue, 1e-4f,
                "The non-bound sibling must be untouched by an Apply issued while bound.");
        }

        /// <summary>
        /// Selecting a block type in the unbound panel seeds the caches
        /// from a placed block, so the sliders (and a subsequent Apply)
        /// operate on the bot's REAL values. Without the seed, untouched
        /// cache fields hold sentinel zeros and Apply would wipe existing
        /// per-bot tunes with defaults.
        /// </summary>
        [Test]
        public void SeedVariantCaches_FromPlacedBlock_CopiesTuneAndReportsPresence()
        {
            Assert.IsFalse(_session.SeedVariantCachesFromPlacedBlock(BlockIds.Pogo),
                "Seeding must report false when nothing of the id is placed — the " +
                "next-placement dial flow must keep its cache untouched.");

            BlockBehaviour pogo = AddBlockToGrid(BlockIds.Pogo, new Vector3Int(0, 0, 0));
            pogo.ConfigValue = 2.5f;

            Assert.IsTrue(_session.SeedVariantCachesFromPlacedBlock(BlockIds.Pogo),
                "Seeding must report true once a block of the id is placed.");
            Assert.AreEqual(2.5f, _session.GetVariantConfig(BlockIds.Pogo), 1e-4f,
                "The cache must hold the placed block's config after seeding — this is " +
                "what makes the panel show (and Apply push) the bot's real tune.");
        }
    }
}
