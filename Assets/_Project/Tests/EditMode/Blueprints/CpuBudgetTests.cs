using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-logic tests for <see cref="CpuBudget.TrimToFit"/> — the
    /// connectivity-preserving strip that enforces the CPU cap at match
    /// start. Uses the real authored library so CPU costs match shipping
    /// values; the budget shape itself (BudgetPerCpuBlock / CPU block) is asserted via the
    /// resulting cap rather than hard-coded counts, so a cost retune doesn't
    /// silently rot the test.
    /// </summary>
    public sealed class CpuBudgetTests
    {
        private const string LibraryAssetPath =
            "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        private BlockDefinitionLibrary _lib;

        [SetUp]
        public void SetUp()
        {
            _lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (_lib == null) Assert.Inconclusive("BlockDefinitionLibrary not found; run Build Everything.");
        }

        // A straight z-line of weapon cells anchored on a CPU at the origin.
        // Position adjacency keeps the whole line connected; TrimToFit only
        // cares about position + CpuCost + Category, so this is a valid
        // connectivity fixture without needing placement-rule-legal hosts.
        private ChassisBlueprint.Entry[] LineOfWeapons(int weaponCount)
        {
            var entries = new List<ChassisBlueprint.Entry>
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
            };
            for (int z = 1; z <= weaponCount; z++)
                entries.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 0, z)));
            return entries.ToArray();
        }

        [Test]
        public void OverBudget_StripsToFit_KeepsCpuAndStaysConnected()
        {
            int weaponCost = _lib.Get(BlockIds.Weapon).CpuCost;
            Assume.That(weaponCost, Is.GreaterThan(0));
            // Enough weapons to blow well past one CPU block's budget.
            int count = CpuBudget.BudgetPerCpuBlock / weaponCost + 5;
            ChassisBlueprint.Entry[] entries = LineOfWeapons(count);

            Assert.IsTrue(CpuBudget.IsOverBudget(entries, _lib), "Fixture should start over budget.");

            ChassisBlueprint.Entry[] trimmed = CpuBudget.TrimToFit(entries, _lib, out int removed);

            Assert.Greater(removed, 0, "An over-budget chassis must lose blocks.");
            Assert.LessOrEqual(CpuBudget.UsedCpu(trimmed, _lib), CpuBudget.Capacity(trimmed, _lib),
                "Trimmed chassis must fit within its CPU cap.");
            Assert.IsTrue(trimmed.Any(e => e.BlockId == BlockIds.Cpu),
                "The CPU block must never be stripped — it supplies the budget.");

            // The survivors must be the cells NEAREST the CPU (lowest z): the
            // strip peels from the far end inward, so the kept weapons form a
            // contiguous z-run from 1 upward — i.e. still connected to the CPU.
            int[] keptZ = trimmed.Where(e => e.BlockId == BlockIds.Weapon)
                                  .Select(e => e.Position.z).OrderBy(z => z).ToArray();
            for (int i = 0; i < keptZ.Length; i++)
                Assert.AreEqual(i + 1, keptZ[i],
                    "Kept weapons must be the contiguous near-CPU run (connectivity preserved).");
        }

        [Test]
        public void UnderBudget_ReturnsUnchanged()
        {
            ChassisBlueprint.Entry[] entries = LineOfWeapons(1);
            Assume.That(CpuBudget.IsOverBudget(entries, _lib), Is.False);

            ChassisBlueprint.Entry[] trimmed = CpuBudget.TrimToFit(entries, _lib, out int removed);

            Assert.AreEqual(0, removed, "A within-budget chassis loses nothing.");
            Assert.AreSame(entries, trimmed, "Within budget should return the input array untouched.");
        }

        [Test]
        public void NoCpuBlock_NotStripped()
        {
            // A CPU-less pile (cap 0) is invalid for other reasons; TrimToFit
            // must not strip it to nothing and hide that.
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 0, 1)),
            };

            ChassisBlueprint.Entry[] trimmed = CpuBudget.TrimToFit(entries, _lib, out int removed);

            Assert.AreEqual(0, removed, "No CPU block → no budget source → don't strip.");
            Assert.AreEqual(entries.Length, trimmed.Length);
        }
    }
}
