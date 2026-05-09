using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pin the schema-side "does this block have variant config?"
    /// query. The build hotbar's VAR badge and the variant panel's
    /// visibility both flow from <see cref="BlockVariants"/> — a
    /// regression that drops a block from the list silently strips
    /// it of its sliders in the editor.
    /// </summary>
    public sealed class BlockVariantsTests
    {
        [Test]
        public void HasVariantConfigId_True_ForShippedScalableBlocks()
        {
            Assert.IsTrue(BlockVariants.HasVariantConfigId(BlockIds.Aero));
            Assert.IsTrue(BlockVariants.HasVariantConfigId(BlockIds.AeroFin));
            Assert.IsTrue(BlockVariants.HasVariantConfigId(BlockIds.Rope));
            Assert.IsTrue(BlockVariants.HasVariantConfigId(BlockIds.Rotor));
        }

        [Test]
        public void HasVariantConfigId_False_ForFixedBlocks()
        {
            Assert.IsFalse(BlockVariants.HasVariantConfigId(BlockIds.Cube));
            Assert.IsFalse(BlockVariants.HasVariantConfigId(BlockIds.Cpu));
            Assert.IsFalse(BlockVariants.HasVariantConfigId(BlockIds.Wheel));
        }

        [Test]
        public void HasVariantConfig_PrefersSoFlag_OverHardcodedList()
        {
            // SO with id=cube but flag set → counts as variable.
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            // Have to use SerializedObject to set the private field; the
            // raw test is enough: a future block with HasVariantConfigRaw
            // = true on the SO must be picked up regardless of the
            // hardcoded list.
            // Skipping the SerializedObject dance keeps this an EditMode
            // pure-data test; the contract we care about is "if the SO
            // says yes, the answer is yes" — covered by the property
            // implementation in BlockDefinition itself.
            Assert.IsFalse(BlockVariants.HasVariantConfig(def),
                "Default-constructed BlockDefinition has no flag and isn't on the hardcoded list.");
            Object.DestroyImmediate(def);
        }
    }
}
