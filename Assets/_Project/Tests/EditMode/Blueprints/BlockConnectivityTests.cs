using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Classification-integrity tests (ADR-0008). The SO flags on the
    /// shipped BlockDefinition assets are the ONLY source for leaf /
    /// side-mount / top-mount / drive-need / companion classification —
    /// the old hardcoded fallback lists are gone — so these tests walk
    /// the real library and pin the authored data. A wizard regression
    /// (or a hand edit the wizard later re-stamps away) fails loudly
    /// here instead of silently changing placement rules.
    /// </summary>
    public sealed class BlockConnectivityTests
    {
        private const string LibraryPath =
            "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        private static BlockDefinition Def(string id)
        {
            var lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryPath);
            Assert.IsNotNull(lib, $"Missing {LibraryPath} — run Robogame → Build Everything.");
            BlockDefinition def = lib.Get(id);
            Assert.IsNotNull(def, $"Library has no definition for '{id}'.");
            return def;
        }

        [Test]
        public void NullDefinition_IsNotLeaf()
        {
            Assert.IsFalse(BlockConnectivity.IsLeaf(null));
        }

        [TestCase(BlockIds.Cube)]
        [TestCase(BlockIds.Cpu)]
        [TestCase(BlockIds.Counterweight)]
        [TestCase(BlockIds.Feather)]
        public void StructuralBlocks_AreNotLeaf(string id)
        {
            Assert.IsFalse(BlockConnectivity.IsLeaf(Def(id)),
                $"'{id}' must remain hostable on every face — structure is the chassis backbone.");
        }

        [TestCase(BlockIds.Aero)]
        [TestCase(BlockIds.AeroFin)]
        [TestCase(BlockIds.Wing)]
        [TestCase(BlockIds.Thruster)]
        [TestCase(BlockIds.Rudder)]
        [TestCase(BlockIds.Rotor)]
        [TestCase(BlockIds.Weapon)]
        [TestCase(BlockIds.Cannon)]
        [TestCase(BlockIds.BombBay)]
        [TestCase(BlockIds.Mortar)]
        [TestCase(BlockIds.Hook)]
        [TestCase(BlockIds.Mace)]
        [TestCase(BlockIds.Magnet)]
        [TestCase(BlockIds.GrappleMagnet)]
        [TestCase(BlockIds.Wheel)]
        [TestCase(BlockIds.WheelSteer)]
        [TestCase(BlockIds.Rope)]
        [TestCase(BlockIds.HoverBlade)]
        [TestCase(BlockIds.Drill)]
        [TestCase(BlockIds.Pogo)]
        [TestCase(BlockIds.Gyro)]
        public void SpecialtyBlocks_AreLeaf(string id)
        {
            // Pogo/Gyro/Drill were the review-found gap: unclassified, so
            // structure could stack on a pogo foot (2.5 m stilt included).
            Assert.IsTrue(BlockConnectivity.IsLeaf(Def(id)),
                $"'{id}' should be a leaf (no other block can attach to it). Author the flag in BlockDefinitionWizard.");
        }

        [TestCase(BlockIds.Wheel)]
        [TestCase(BlockIds.WheelSteer)]
        public void Wheels_AreSideMountOnly(string id)
        {
            Assert.IsTrue(BlockConnectivity.RequiresSideMount(Def(id)),
                $"'{id}' stems are horizontal — top/bottom mounts must be rejected.");
        }

        [Test]
        public void Mortar_IsTopMountOnly()
        {
            Assert.IsTrue(BlockConnectivity.RequiresTopMount(Def(BlockIds.Mortar)),
                "The mortar tube fires upward — side/bottom mounts are nonsensical.");
        }

        [Test]
        public void DriveNeeds_MatchSubsystemContract()
        {
            // ChassisAssembler unions these to decide which chassis-level
            // subsystems to add — a wrong value silently strips a chassis
            // of its drive (the LOG-132 mortar failure class).
            Assert.AreEqual(DriveNeed.Ground, Def(BlockIds.Wheel).DriveSubsystemNeed);
            Assert.AreEqual(DriveNeed.Ground, Def(BlockIds.WheelSteer).DriveSubsystemNeed);
            Assert.AreEqual(DriveNeed.Flight, Def(BlockIds.Aero).DriveSubsystemNeed);
            Assert.AreEqual(DriveNeed.Flight, Def(BlockIds.AeroFin).DriveSubsystemNeed);
            Assert.AreEqual(DriveNeed.Flight, Def(BlockIds.Wing).DriveSubsystemNeed,
                "Wing-only chassis must count as Flight (Plane control scheme + aero binder) — the pre-ADR-0008 id list missed it.");
            Assert.AreEqual(DriveNeed.Hover, Def(BlockIds.HoverBlade).DriveSubsystemNeed);
            Assert.AreEqual(DriveNeed.None, Def(BlockIds.Rudder).DriveSubsystemNeed,
                "A rudder alone must not grant plane-control authority (ADR-0008).");
            Assert.AreEqual(DriveNeed.None, Def(BlockIds.Thruster).DriveSubsystemNeed);
        }

        [Test]
        public void Rotor_DeclaresMechanismCubeCompanion()
        {
            BlockDefinition rotor = Def(BlockIds.Rotor);
            Assert.IsTrue(rotor.HasCompanion);
            Assert.AreEqual(BlockIds.Cube, rotor.CompanionBlockId);
            CollectionAssert.AreEquivalent(
                new[] { BlockIds.Aero, BlockIds.AeroFin, BlockIds.Rope },
                (System.Collections.ICollection)rotor.CompanionLateralAttachIds,
                "The rotor's companion cube accepts exactly the blade/rope ring laterally.");
        }

        [Test]
        public void Tips_AreExactlyHookMaceMagnet()
        {
            Assert.IsTrue(BlockIds.IsTipId(BlockIds.Hook));
            Assert.IsTrue(BlockIds.IsTipId(BlockIds.Mace));
            Assert.IsTrue(BlockIds.IsTipId(BlockIds.Magnet));
            Assert.IsFalse(BlockIds.IsTipId(BlockIds.GrappleMagnet),
                "GrappleMagnet is a turret weapon firing a tethered projectile — NOT a rope tip.");
            Assert.IsFalse(BlockIds.IsTipId(BlockIds.Rope));
        }
    }
}
