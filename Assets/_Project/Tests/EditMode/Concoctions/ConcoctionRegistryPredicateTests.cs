using NUnit.Framework;
using Robogame.Block;

namespace Robogame.Tests.EditMode.Concoctions
{
    /// <summary>
    /// Session-141 tests for <see cref="ConcoctionRegistry.IsConcoctableBlock"/>:
    /// the single predicate shared by the variant-panel dropdown, the CPU
    /// surcharge, and fire-time application widened this session to include
    /// the ammo-configurable turrets (Weapon/SMG id, Cannon) alongside the
    /// pre-141 splash pair (BombBay, Mortar).
    /// </summary>
    public sealed class ConcoctionRegistryPredicateTests
    {
        [Test]
        public void IsConcoctableBlock_AmmoConfigurableTurrets_AreTrue()
        {
            Assert.IsTrue(ConcoctionRegistry.IsConcoctableBlock(BlockIds.Weapon),
                "Session 141: SMG-id turret is now concoctable, stacking with its ammo surcharge.");
            Assert.IsTrue(ConcoctionRegistry.IsConcoctableBlock(BlockIds.Cannon),
                "Session 141: Cannon is now concoctable, stacking with its ammo surcharge.");
        }

        [Test]
        public void IsConcoctableBlock_SplashWeapons_AreTrue()
        {
            Assert.IsTrue(ConcoctionRegistry.IsConcoctableBlock(BlockIds.BombBay));
            Assert.IsTrue(ConcoctionRegistry.IsConcoctableBlock(BlockIds.Mortar));
        }

        [Test]
        public void IsConcoctableBlock_NonWeaponBlocks_AreFalse()
        {
            Assert.IsFalse(ConcoctionRegistry.IsConcoctableBlock(BlockIds.Cube));
            Assert.IsFalse(ConcoctionRegistry.IsConcoctableBlock(BlockIds.Aero));
        }
    }
}
