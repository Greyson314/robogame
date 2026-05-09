using NUnit.Framework;
using Robogame.Block;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlockConnectivity"/>'s leaf-vs-host
    /// rule. The actual placement gating lives in
    /// <c>Robogame.Gameplay.BlockEditor</c> and is exercised by
    /// in-engine play; these tests just lock down the data layer.
    /// </summary>
    public sealed class BlockConnectivityTests
    {
        [Test]
        public void NullDefinition_IsNotLeaf()
        {
            Assert.IsFalse(BlockConnectivity.IsLeaf(null));
        }

        [Test]
        public void Cube_IsNotLeaf()
        {
            Assert.IsFalse(BlockConnectivity.IsLeafId(BlockIds.Cube),
                "Structural cubes must remain hostable on every face — they're the chassis backbone.");
        }

        [Test]
        public void Cpu_IsNotLeaf()
        {
            Assert.IsFalse(BlockConnectivity.IsLeafId(BlockIds.Cpu),
                "CPU is the chassis root; making it a leaf would brick every blueprint.");
        }

        [TestCase(BlockIds.Aero)]
        [TestCase(BlockIds.AeroFin)]
        [TestCase(BlockIds.Thruster)]
        [TestCase(BlockIds.Rudder)]
        [TestCase(BlockIds.Rotor)]
        [TestCase(BlockIds.Weapon)]
        [TestCase(BlockIds.Cannon)]
        [TestCase(BlockIds.BombBay)]
        [TestCase(BlockIds.Hook)]
        [TestCase(BlockIds.Mace)]
        [TestCase(BlockIds.Wheel)]
        [TestCase(BlockIds.WheelSteer)]
        [TestCase(BlockIds.Rope)]
        public void SpecialtyBlocks_AreLeaf(string id)
        {
            Assert.IsTrue(BlockConnectivity.IsLeafId(id),
                $"'{id}' should be a leaf (no other block can attach to it). Update BlockConnectivity if a new specialty block lands.");
        }
    }
}
