// =============================================================================
// WeaponAmmoDefaultsTests — EditMode
//
// What this suite covers
// -----------------------
// The ammo-multiplier contract for per-instance weapon config: the 0-sentinel
// default, clip scaling, the asymmetric CPU pricing curve (full linear above
// 1×, shallower discount below so gun-stacking can't buy free burst DPS), and
// the mass scale. These three curves are the tradeoff the feature IS — if the
// pricing drifts from the clip you actually get, the garage spend bar lies.
// =============================================================================

using NUnit.Framework;
using Robogame.Block;

namespace Robogame.Tests.EditMode.Combat
{
    public sealed class WeaponAmmoDefaultsTests
    {
        [Test]
        public void ResolveMultiplier_ZeroSentinel_IsDefault()
        {
            Assert.AreEqual(1f, WeaponAmmoDefaults.ResolveMultiplier(0f), 0.0001f,
                "BlockConfig 0 must mean 'untouched = 1×' so existing blueprints are unaffected.");
        }

        [Test]
        public void ResolveMultiplier_ClampsToSliderRange()
        {
            Assert.AreEqual(WeaponAmmoDefaults.MinMultiplier, WeaponAmmoDefaults.ResolveMultiplier(0.1f), 0.0001f);
            Assert.AreEqual(WeaponAmmoDefaults.MaxMultiplier, WeaponAmmoDefaults.ResolveMultiplier(99f), 0.0001f);
        }

        [Test]
        public void ClipFor_ScalesAndFloorsAtOne()
        {
            Assert.AreEqual(30, WeaponAmmoDefaults.ClipFor(30, 0f), "Untouched = authored clip.");
            Assert.AreEqual(60, WeaponAmmoDefaults.ClipFor(30, 2f));
            Assert.AreEqual(15, WeaponAmmoDefaults.ClipFor(30, 0.5f));
            Assert.AreEqual(1, WeaponAmmoDefaults.ClipFor(1, 0.5f), "Clip can never round to zero rounds.");
        }

        [Test]
        public void CpuCostFor_UntouchedPaysSticker_FreeStaysFree()
        {
            Assert.AreEqual(20, WeaponAmmoDefaults.CpuCostFor(20, 0f),
                "Config 0 must cost exactly the authored CpuCost (INV-5 zero-cost default).");
            Assert.AreEqual(0, WeaponAmmoDefaults.CpuCostFor(0, 2.5f),
                "An authored-free weapon stays free at any multiplier.");
        }

        [Test]
        public void CpuCostFor_LinearAboveOne_ShallowBelow()
        {
            // Above 1×: linear — 2× ammo = 2× price.
            Assert.AreEqual(40, WeaponAmmoDefaults.CpuCostFor(20, 2f));
            // Below 1×: 0.5 + 0.5m — half ammo pays 75%, NOT 50%. A full
            // linear discount would make stacking extra half-ammo guns
            // strictly better burst DPS per CPU than one full-ammo gun.
            Assert.AreEqual(15, WeaponAmmoDefaults.CpuCostFor(20, 0.5f));
        }

        [Test]
        public void MassScaleFor_OnlyAmmoFractionScales()
        {
            Assert.AreEqual(1f, WeaponAmmoDefaults.MassScaleFor(0f), 0.0001f,
                "Untouched = sticker mass — existing chassis stay byte-identical.");
            // 2.5× ammo at 0.4 ammo-mass fraction → 1 + 0.4·1.5 = 1.6× mass.
            Assert.AreEqual(1.6f, WeaponAmmoDefaults.MassScaleFor(2.5f), 0.0001f);
            // 0.5× ammo → 1 − 0.4·0.5 = 0.8× mass.
            Assert.AreEqual(0.8f, WeaponAmmoDefaults.MassScaleFor(0.5f), 0.0001f);
        }

        [Test]
        public void IsAmmoConfigurable_TurretsOnly()
        {
            Assert.IsTrue(WeaponAmmoDefaults.IsAmmoConfigurable(BlockIds.Weapon));
            Assert.IsTrue(WeaponAmmoDefaults.IsAmmoConfigurable(BlockIds.Cannon));
            // Explosives keep their variant slot for the concoction chooser.
            Assert.IsFalse(WeaponAmmoDefaults.IsAmmoConfigurable(BlockIds.Mortar));
            Assert.IsFalse(WeaponAmmoDefaults.IsAmmoConfigurable(BlockIds.BombBay));
            Assert.IsFalse(WeaponAmmoDefaults.IsAmmoConfigurable(BlockIds.Cube));
        }
    }
}
