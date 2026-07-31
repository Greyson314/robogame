using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pin the schema-side "does this block have variant config?" query
    /// against the shipped assets (ADR-0008 — the SO flag is the only
    /// source; the old hardcoded list is gone). The build hotbar's VAR
    /// badge and the variant panel's visibility both flow from
    /// <see cref="BlockVariants"/> — a wizard regression that drops the
    /// flag silently strips a block of its sliders in the editor.
    /// </summary>
    public sealed class BlockVariantsTests
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

        [TestCase(BlockIds.Aero)]
        [TestCase(BlockIds.AeroFin)]
        [TestCase(BlockIds.Wing)]
        [TestCase(BlockIds.Rope)]
        [TestCase(BlockIds.Rotor)]
        [TestCase(BlockIds.HoverBlade)]
        [TestCase(BlockIds.Spring)]
        [TestCase(BlockIds.ModuleEmp)]
        [TestCase(BlockIds.ModuleBlink)]
        [TestCase(BlockIds.ModuleShield)]
        [TestCase(BlockIds.ModuleSmoke)]
        [TestCase(BlockIds.ModuleInvis)]
        [TestCase(BlockIds.ModuleMines)]
        [TestCase(BlockIds.ModuleRepair)]
        [TestCase(BlockIds.BombBay)]
        [TestCase(BlockIds.Mortar)]
        [TestCase(BlockIds.Weapon)]
        [TestCase(BlockIds.Cannon)]
        [TestCase(BlockIds.Pogo)]
        public void VariableBlocks_HaveVariantConfig(string id)
        {
            // ModuleMines + ModuleRepair are the drift fix: the old list's
            // comment said every module qualifies, but both were missing —
            // their power sliders were unreachable.
            Assert.IsTrue(BlockVariants.HasVariantConfig(Def(id)),
                $"'{id}' should expose variant config. Author the flag in BlockDefinitionWizard.");
        }

        [TestCase(BlockIds.Cube)]
        [TestCase(BlockIds.Cpu)]
        [TestCase(BlockIds.Wheel)]
        [TestCase(BlockIds.Thruster)]
        public void FixedBlocks_HaveNoVariantConfig(string id)
        {
            Assert.IsFalse(BlockVariants.HasVariantConfig(Def(id)));
        }

        [Test]
        public void DefaultConstructedDefinition_IsNotVariable()
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            Assert.IsFalse(BlockVariants.HasVariantConfig(def),
                "No authored flag → no variant config; there is no id fallback any more.");
            Object.DestroyImmediate(def);
        }
    }
}
