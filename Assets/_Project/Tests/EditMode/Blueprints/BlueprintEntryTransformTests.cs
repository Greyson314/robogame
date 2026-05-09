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
        public void MirrorTransform_X_FlipsCellUpAndPitch_WhenUpHasXComponent()
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
            Assert.AreEqual(-5f, mirrored.Pitch, 1e-4f,
                "Side-mount wing pitch must negate under X-mirror — chord-axis rotation lands on same world axis on both sides.");
        }

        [Test]
        public void MirrorTransform_Z_FlipsZAxisAndNegatesPitch_WhenUpHasZComponent()
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
            Assert.AreEqual(2f, mirrored.Pitch, 1e-4f,
                "Front-mount wing pitch must negate under Z-mirror.");
        }

        [Test]
        public void MirrorTransform_PreservesPitch_WhenUpDoesNotFlipUnderAxis()
        {
            // Top-mounted wing (up=+Y) under X-mirror: up is unchanged,
            // pitch is preserved. Same for under Z-mirror.
            var tX = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Aero,
                position: new Vector3Int(2, 1, 0),
                up: new Vector3Int(0, 1, 0),
                dims: Vector3.zero,
                pitch: 4f);
            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(tX, source);
            Assert.AreEqual(new Vector3Int(0, 1, 0), mirrored.Up);
            Assert.AreEqual(4f, mirrored.Pitch, 1e-4f,
                "Top-mount wing pitch is preserved under X-mirror — up doesn't flip.");
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
        public void MirrorPitch_NegatesWhenUpFlipsAcrossAxis()
        {
            // up=+X under MirrorAxis.X → up flips to -X → pitch negates.
            Assert.AreEqual(-18f, BlockMirror.MirrorPitch(18f, new Vector3Int(1, 0, 0), MirrorAxis.X), 1e-4f);
            // up=+Z under MirrorAxis.Z → up flips to -Z → pitch negates.
            Assert.AreEqual(  3f, BlockMirror.MirrorPitch(-3f, new Vector3Int(0, 0, 1), MirrorAxis.Z), 1e-4f);
        }

        [Test]
        public void MirrorPitch_PreservesWhenUpStaysSameUnderAxis()
        {
            // up=+Y under MirrorAxis.X → up unchanged → pitch preserved.
            Assert.AreEqual(18f, BlockMirror.MirrorPitch(18f, new Vector3Int(0, 1, 0), MirrorAxis.X), 1e-4f);
            // up=+Y under MirrorAxis.Z → up unchanged → pitch preserved.
            Assert.AreEqual(18f, BlockMirror.MirrorPitch(18f, new Vector3Int(0, 1, 0), MirrorAxis.Z), 1e-4f);
            // up=+Z under MirrorAxis.X → up unchanged (z-component, not x) → preserved.
            Assert.AreEqual(5f,  BlockMirror.MirrorPitch(5f,  new Vector3Int(0, 0, 1), MirrorAxis.X), 1e-4f);
        }
    }
}
