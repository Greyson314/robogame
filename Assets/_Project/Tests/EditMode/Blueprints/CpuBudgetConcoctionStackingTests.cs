using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Session-141 tests for <see cref="CpuBudget.EffectiveCpuCost"/> on the
    /// newly-concoctable ammo-configurable turrets (Weapon/SMG id, Cannon):
    /// the ammo-multiplier surcharge and the concoction surcharge must
    /// STACK (both apply), and the concoction surcharge must be computed on
    /// the block's BASE cost, not the ammo-scaled price, so the two knobs
    /// price independently — doubling a clip shouldn't quietly double the
    /// price of the payload chemistry riding on top of it.
    /// </summary>
    public sealed class CpuBudgetConcoctionStackingTests
    {
        private const string LibraryAssetPath =
            "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";
        private const string TestConcoctionId = "cpu-budget-stacking-test";

        private BlockDefinitionLibrary _lib;
        private BlockDefinition _weaponDef;
        private Concoction _concoction;

        [SetUp]
        public void SetUp()
        {
            _lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (_lib == null) Assert.Inconclusive("BlockDefinitionLibrary not found; run Build Everything.");
            _weaponDef = _lib.Get(BlockIds.Weapon);
            Assume.That(_weaponDef, Is.Not.Null);
            Assume.That(_weaponDef.CpuCost, Is.GreaterThan(0));

            // All-max recipe: predictable 1.5x-base surcharge (see
            // ConcoctionCpuSurchargeV2Tests for the calibration anchor).
            _concoction = new Concoction(TestConcoctionId, "Stack Test", 1f, 1f, 1f, 1f, 1f);
            ConcoctionRegistry.Register(_concoction);
        }

        [TearDown]
        public void TearDown()
        {
            // Static registry (INV: statics survive domain reload) — must
            // not leak this test fixture's concoction into other suites.
            ConcoctionRegistry.Clear();
        }

        private ChassisBlueprint.Entry WeaponEntry(float ammoConfig, string concoctionId) =>
            new ChassisBlueprint.Entry(BlockIds.Weapon, Vector3Int.zero,
                Vector3Int.up, Vector3.zero, 0f, ammoConfig, concoctionId);

        [Test]
        public void EffectiveCpuCost_AmmoConfiguredAndConcocted_StacksBothSurcharges()
        {
            int baseCost = _weaponDef.CpuCost;
            ChassisBlueprint.Entry entry = WeaponEntry(2f, TestConcoctionId);

            int expected = WeaponAmmoDefaults.CpuCostFor(baseCost, 2f) + _concoction.CpuSurcharge(baseCost);
            Assert.AreEqual(expected, CpuBudget.EffectiveCpuCost(entry, _weaponDef),
                "Ammo surcharge and concoction surcharge must both apply, additively.");
        }

        [Test]
        public void EffectiveCpuCost_AmmoConfiguredNoConcoction_OnlyAmmoSurcharge()
        {
            // Sanity anchor: without a concoction, stacking must not
            // phantom-add a surcharge from nowhere.
            int baseCost = _weaponDef.CpuCost;
            ChassisBlueprint.Entry entry = WeaponEntry(2f, string.Empty);

            int expected = WeaponAmmoDefaults.CpuCostFor(baseCost, 2f);
            Assert.AreEqual(expected, CpuBudget.EffectiveCpuCost(entry, _weaponDef));
        }

        [Test]
        public void EffectiveCpuCost_ConcoctionSurcharge_ComputedOnBaseCost_NotAmmoScaledPrice()
        {
            // Same concoction, two different ammo configs: the SURCHARGE
            // portion (total minus the ammo-scaled price) must be identical
            // in both cases, because it's priced off the block's base cost.
            // If the surcharge were computed on the ammo-scaled price
            // instead, doubling the clip would also inflate the chemistry
            // surcharge, which is the regression this test exists to catch.
            int baseCost = _weaponDef.CpuCost;

            ChassisBlueprint.Entry untouched = WeaponEntry(1f, TestConcoctionId);
            ChassisBlueprint.Entry doubled = WeaponEntry(2f, TestConcoctionId);

            int surchargeAtUntouched = CpuBudget.EffectiveCpuCost(untouched, _weaponDef)
                                      - WeaponAmmoDefaults.CpuCostFor(baseCost, 1f);
            int surchargeAtDoubled = CpuBudget.EffectiveCpuCost(doubled, _weaponDef)
                                    - WeaponAmmoDefaults.CpuCostFor(baseCost, 2f);

            Assert.AreEqual(surchargeAtUntouched, surchargeAtDoubled,
                "Concoction surcharge must not scale with the ammo multiplier.");
            Assert.AreEqual(_concoction.CpuSurcharge(baseCost), surchargeAtUntouched);
        }
    }
}
