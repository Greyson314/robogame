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

        [Test]
        public void MirrorTransform_Teeter_SharesPitchParity()
        {
            // Teeter (chord-axis tilt, session 123) uses the same
            // mount-frame sign convention as pitch: if it didn't negate
            // when up flips, a mirrored dihedral wing pair would form a
            // Z instead of a V.
            var t = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Aero,
                position: new Vector3Int(2, 1, 3),
                up: new Vector3Int(1, 0, 0),
                dims: new Vector3(4f, 0.08f, 0.9f),
                pitch: 5f);
            source.Teeter = 10f;

            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);

            Assert.AreEqual(-10f, mirrored.Teeter, 1e-4f,
                "Side-mount teeter must negate under X-mirror, same parity as pitch.");

            // Top mount: up doesn't flip → teeter preserved.
            ChassisBlueprint.Entry topSource = new ChassisBlueprint.Entry(
                BlockIds.Aero, new Vector3Int(2, 1, 0), new Vector3Int(0, 1, 0), Vector3.zero, 4f);
            topSource.Teeter = 7f;
            ChassisBlueprint.Entry topMirrored = BlueprintEntryTransform.Apply(t, topSource);
            Assert.AreEqual(7f, topMirrored.Teeter, 1e-4f,
                "Top-mount teeter is preserved under X-mirror — up doesn't flip.");
        }

        [Test]
        public void MirrorTransform_CarriesBlockConfigAndConcoction_StraightAcross()
        {
            // BlockConfig (thrust/RPM) and ConcoctionId are orientation-free:
            // a mirrored thruster keeps its tuned thrust, a mirrored bomb its
            // concoction. This is the regression the session-124 Apply fix
            // closes — the 5-arg ctor used to default both to 0/"".
            var t = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Thruster, new Vector3Int(2, 1, 0), new Vector3Int(1, 0, 0));
            source.BlockConfig = 4200f;
            source.ConcoctionId = "concoction.test";

            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);

            Assert.AreEqual(4200f, mirrored.BlockConfig, 1e-4f,
                "Mirrored block must keep its server-authoritative config (thrust/RPM).");
            Assert.AreEqual("concoction.test", mirrored.ConcoctionId,
                "Mirrored block must keep its authored concoction.");
        }

        [Test]
        public void MirrorTransform_CarriesYaw_ThroughApply()
        {
            // Yaw used to be dropped by Apply's 5-arg ctor (defaulted 0).
            var t = new MirrorTransform(MirrorAxis.X);
            ChassisBlueprint.Entry source = new ChassisBlueprint.Entry(
                BlockIds.Thruster, new Vector3Int(2, 1, 0), new Vector3Int(1, 0, 0));
            source.Yaw = 90;

            ChassisBlueprint.Entry mirrored = BlueprintEntryTransform.Apply(t, source);

            // up=+X is non-polar; X-mirror adds no base offset → yaw' = -90 ≡ 270.
            Assert.AreEqual(270, mirrored.Yaw,
                "Side-mount yaw must reflect, not copy, through Apply.");
        }

        // -----------------------------------------------------------------
        // BlockMirror.MirrorYaw — geometric reflection rule (session 124).
        // yaw' = baseOffset - yaw, where baseOffset is 180° iff the mirror
        // flips OrientationFromUp's deterministic forward-seed axis (+Z for
        // ordinary mounts, +X for polar ±Z mounts), else 0°.
        // -----------------------------------------------------------------

        [Test]
        public void MirrorYaw_NonPolarUp_X_NegatesYaw()
        {
            // up=±X or ±Y under X-mirror: seed is +Z, which X-mirror leaves
            // intact → baseOffset 0 → yaw' = -yaw.
            Assert.AreEqual(270, BlockMirror.MirrorYaw(90, new Vector3Int(1, 0, 0), MirrorAxis.X),
                "Side-mount (up=+X) yaw negates under X-mirror.");
            Assert.AreEqual(270, BlockMirror.MirrorYaw(90, new Vector3Int(0, 1, 0), MirrorAxis.X),
                "Top-mount (up=+Y) yaw negates under X-mirror.");
            Assert.AreEqual(90, BlockMirror.MirrorYaw(270, new Vector3Int(0, 1, 0), MirrorAxis.X));
            Assert.AreEqual(180, BlockMirror.MirrorYaw(180, new Vector3Int(1, 0, 0), MirrorAxis.X),
                "180° is its own negation.");
            Assert.AreEqual(0, BlockMirror.MirrorYaw(0, new Vector3Int(1, 0, 0), MirrorAxis.X));
        }

        [Test]
        public void MirrorYaw_PolarUp_X_AddsBaseOffset()
        {
            // up=±Z under X-mirror: seed is +X, which X-mirror flips →
            // baseOffset 180 → yaw' = 180 - yaw.
            Assert.AreEqual(180, BlockMirror.MirrorYaw(0, new Vector3Int(0, 0, 1), MirrorAxis.X));
            Assert.AreEqual(90, BlockMirror.MirrorYaw(90, new Vector3Int(0, 0, 1), MirrorAxis.X),
                "Polar mount yaw=90 maps to 90 under X-mirror (180-90).");
            Assert.AreEqual(270, BlockMirror.MirrorYaw(270, new Vector3Int(0, 0, -1), MirrorAxis.X));
        }

        [Test]
        public void MirrorYaw_NonPolarUp_Z_AddsBaseOffset()
        {
            // up=±X or ±Y under Z-mirror: seed is +Z, which Z-mirror flips →
            // baseOffset 180 → yaw' = 180 - yaw.
            Assert.AreEqual(90, BlockMirror.MirrorYaw(90, new Vector3Int(1, 0, 0), MirrorAxis.Z));
            Assert.AreEqual(180, BlockMirror.MirrorYaw(0, new Vector3Int(0, 1, 0), MirrorAxis.Z));
        }

        [Test]
        public void MirrorYaw_PolarUp_Z_NegatesYaw()
        {
            // up=±Z under Z-mirror: seed is +X, which Z-mirror leaves intact
            // → baseOffset 0 → yaw' = -yaw.
            Assert.AreEqual(270, BlockMirror.MirrorYaw(90, new Vector3Int(0, 0, 1), MirrorAxis.Z));
            Assert.AreEqual(180, BlockMirror.MirrorYaw(180, new Vector3Int(0, 0, 1), MirrorAxis.Z));
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
