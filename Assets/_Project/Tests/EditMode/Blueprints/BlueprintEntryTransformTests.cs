using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pin the "every Entry field is addressed" contract for
    /// <see cref="IBlueprintEntryTransform"/> implementers. The interface
    /// itself is the compile-time guard; these tests cover the
    /// behavioural shape so a future "field handled but identity by
    /// mistake" regression surfaces.
    /// </summary>
    public sealed class BlueprintEntryTransformTests
    {
        // -----------------------------------------------------------------
        // MirrorTransform
        // -----------------------------------------------------------------

        [Test]
        public void MirrorTransform_X_FlipsCellAndUpButPreservesDimsAndPitch()
        {
            var t = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Aero,
                position: new Vector3Int(2, 1, 3),
                up: new Vector3Int(1, 0, 0),
                dims: new Vector3(4f, 0.08f, 0.9f),
                pitch: 5f);

            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);

            Assert.AreEqual(BlockIds.Aero, mirrored.BlockId);
            Assert.AreEqual(new Vector3Int(-2, 1, 3), mirrored.Position);
            Assert.AreEqual(new Vector3Int(-1, 0, 0), mirrored.Up);
            Assert.AreEqual(source.Dims, mirrored.Dims, "Dims is scalar — mirror is identity.");
            Assert.AreEqual(5f, mirrored.Pitch, 1e-4f,
                "Pitch passes through unchanged under MirrorAxis.X — symmetric trim depends on it.");
        }

        [Test]
        public void MirrorTransform_Z_FlipsZAxisOnly()
        {
            var t = new MirrorTransform(MirrorAxis.Z);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Aero,
                position: new Vector3Int(2, 1, 3),
                up: new Vector3Int(0, 0, 1),
                dims: new Vector3(4f, 0.08f, 0.9f),
                pitch: -2f);

            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);

            Assert.AreEqual(new Vector3Int(2, 1, -3), mirrored.Position);
            Assert.AreEqual(new Vector3Int(0, 0, -1), mirrored.Up);
            Assert.AreEqual(-2f, mirrored.Pitch, 1e-4f);
        }

        [Test]
        public void MirrorTransform_LegacyZeroUp_NormalisesToPlusY()
        {
            // Entries authored before the Up field existed have Up=zero;
            // EffectiveUp resolves to +Y. Mirror must read EffectiveUp
            // so the mirrored side gets an explicit +Y, not a zero.
            var t = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Cube, new Vector3Int(2, 0, 0));
            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);
            Assert.AreEqual(new Vector3Int(0, 1, 0), mirrored.Up,
                "Mirror of a zero-up entry must materialise +Y on the other side.");
        }

        // -----------------------------------------------------------------
        // BlockMirror.MirrorPitch
        // -----------------------------------------------------------------

        [Test]
        public void MirrorPitch_PreservesUnderBothAxes()
        {
            // Today's rule: pitch is preserved across both supported
            // mirror axes because OrientationFromUp lands the chord axis
            // at the same world direction on both sides. If a future
            // axis (free plane) breaks this, the rule lives in BlockMirror.
            Assert.AreEqual( 18f, BlockMirror.MirrorPitch( 18f, MirrorAxis.X), 1e-4f);
            Assert.AreEqual( 18f, BlockMirror.MirrorPitch( 18f, MirrorAxis.Z), 1e-4f);
            Assert.AreEqual(-3f,  BlockMirror.MirrorPitch(-3f,  MirrorAxis.X), 1e-4f);
        }
    }
}
