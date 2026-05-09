using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlockMirror"/>'s axis reflection
    /// helpers. Mirror-placement integration (which calls into BlockEditor /
    /// BlockGrid) is exercised in-engine, not here.
    /// </summary>
    public sealed class BlockMirrorTests
    {
        [Test]
        public void MirrorCell_X_FlipsXOnly()
        {
            Vector3Int m = BlockMirror.MirrorCell(new Vector3Int(2, 1, -3), MirrorAxis.X);
            Assert.AreEqual(new Vector3Int(-2, 1, -3), m);
        }

        [Test]
        public void MirrorCell_Z_FlipsZOnly()
        {
            Vector3Int m = BlockMirror.MirrorCell(new Vector3Int(2, 1, -3), MirrorAxis.Z);
            Assert.AreEqual(new Vector3Int(2, 1, 3), m);
        }

        [Test]
        public void MirrorUp_X_ReflectsX()
        {
            Vector3Int m = BlockMirror.MirrorUp(new Vector3Int(1, 0, 0), MirrorAxis.X);
            Assert.AreEqual(new Vector3Int(-1, 0, 0), m,
                "A wing on the +X face mirrors to a wing on the -X face with up=-X.");
        }

        [Test]
        public void MirrorUp_X_LeavesYZUntouched()
        {
            Assert.AreEqual(new Vector3Int(0, 1, 0),
                BlockMirror.MirrorUp(new Vector3Int(0, 1, 0), MirrorAxis.X),
                "Top-mounted block's up direction is unchanged by X-mirror.");
            Assert.AreEqual(new Vector3Int(0, 0, 1),
                BlockMirror.MirrorUp(new Vector3Int(0, 0, 1), MirrorAxis.X),
                "Front-mounted block's up direction is unchanged by X-mirror.");
        }

        [Test]
        public void MirrorUp_Z_ReflectsZ()
        {
            Vector3Int m = BlockMirror.MirrorUp(new Vector3Int(0, 0, 1), MirrorAxis.Z);
            Assert.AreEqual(new Vector3Int(0, 0, -1), m);
        }

        [Test]
        public void IsOnPlane_X_TrueAtZeroX()
        {
            Assert.IsTrue(BlockMirror.IsOnPlane(new Vector3Int(0, 5, -3), MirrorAxis.X));
            Assert.IsFalse(BlockMirror.IsOnPlane(new Vector3Int(1, 5, -3), MirrorAxis.X));
        }

        [Test]
        public void IsOnPlane_Z_TrueAtZeroZ()
        {
            Assert.IsTrue(BlockMirror.IsOnPlane(new Vector3Int(2, 5, 0), MirrorAxis.Z));
            Assert.IsFalse(BlockMirror.IsOnPlane(new Vector3Int(2, 5, 1), MirrorAxis.Z));
        }

        [Test]
        public void MirrorCell_OnPlane_IsIdempotent()
        {
            // Cells on the mirror plane should mirror to themselves —
            // the placement code uses this to skip the mirror copy.
            Vector3Int onPlane = new Vector3Int(0, 1, 2);
            Assert.AreEqual(onPlane, BlockMirror.MirrorCell(onPlane, MirrorAxis.X));
        }

        [Test]
        public void MirrorTwice_IsIdentity()
        {
            // Symmetry sanity: mirroring an X-mirrored cell again must
            // recover the original. Defends against off-by-one slips
            // in the mirror logic.
            Vector3Int orig = new Vector3Int(3, -1, 4);
            Vector3Int twice = BlockMirror.MirrorCell(BlockMirror.MirrorCell(orig, MirrorAxis.X), MirrorAxis.X);
            Assert.AreEqual(orig, twice);

            Vector3Int origUp = new Vector3Int(1, 0, 0);
            Vector3Int twiceUp = BlockMirror.MirrorUp(BlockMirror.MirrorUp(origUp, MirrorAxis.X), MirrorAxis.X);
            Assert.AreEqual(origUp, twiceUp);
        }
    }
}
