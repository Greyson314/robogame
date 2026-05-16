using NUnit.Framework;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Phase 6 machine gate (encode/decode half): BrushOp + BrushOpBatch
    /// round-trip byte-identical through <see cref="BrushOpCodec"/>.
    /// Pins the wire format so a future refactor can't silently shift
    /// bytes and desync clients.
    /// </summary>
    public sealed class BrushOpCodecTests
    {
        // ------------------------------------------------------------------
        // BrushOp
        // ------------------------------------------------------------------

        [Test]
        public void EncodeOp_ProducesExactly17Bytes()
        {
            BrushOp op = MakeSphereOp();
            byte[] buf = new byte[BrushOpCodec.EncodedOpSize];
            int written = BrushOpCodec.EncodeOp(op, buf, 0);
            Assert.AreEqual(17, written, "Encoded BrushOp must occupy exactly 17 bytes.");
            Assert.AreEqual(17, BrushOpCodec.EncodedOpSize);
        }

        [Test]
        public void EncodeOp_DecodeOp_RoundTripsByteIdentical()
        {
            BrushOp original = MakeCapsuleOp();
            byte[] buf = new byte[BrushOpCodec.EncodedOpSize];
            BrushOpCodec.EncodeOp(original, buf, 0);

            int read = BrushOpCodec.DecodeOp(buf, 0, out BrushOp decoded);

            Assert.AreEqual(BrushOpCodec.EncodedOpSize, read);
            Assert.AreEqual(original, decoded,
                "Decoded BrushOp must be byte-identical to the encoded source.");
        }

        [Test]
        public void EncodeOp_AtOffset_DoesNotTrampleAdjacentBytes()
        {
            // Encode into the middle of a buffer; assert the bytes BEFORE
            // and AFTER the encoded region are untouched.
            BrushOp op = MakeSphereOp();
            byte[] buf = new byte[BrushOpCodec.EncodedOpSize + 4];
            // Pre-fill with a sentinel pattern.
            for (int i = 0; i < buf.Length; i++) buf[i] = 0xAB;

            BrushOpCodec.EncodeOp(op, buf, 2);

            Assert.AreEqual(0xAB, buf[0]);
            Assert.AreEqual(0xAB, buf[1]);
            Assert.AreEqual(0xAB, buf[buf.Length - 1]);
        }

        // ------------------------------------------------------------------
        // BrushOpBatch
        // ------------------------------------------------------------------

        [Test]
        public void EncodeBatch_DecodeBatch_RoundTripsByteIdentical_EmptyOps()
        {
            BrushOpBatch original = new BrushOpBatch
            {
                digZoneId = 7,
                serverTick = 1234,
                ops = new BrushOp[0],
            };
            byte[] buf = new byte[BrushOpCodec.EncodedBatchSize(0)];
            BrushOpCodec.EncodeBatch(original, buf, 0);
            BrushOpCodec.DecodeBatch(buf, 0, out BrushOpBatch decoded);

            Assert.AreEqual(original.digZoneId, decoded.digZoneId);
            Assert.AreEqual(original.serverTick, decoded.serverTick);
            Assert.AreEqual(0, decoded.ops.Length);
        }

        [Test]
        public void EncodeBatch_DecodeBatch_RoundTripsByteIdentical_MultipleOps()
        {
            BrushOpBatch original = new BrushOpBatch
            {
                digZoneId = 42,
                serverTick = ushort.MaxValue,
                ops = new[] { MakeSphereOp(), MakeCapsuleOp(), MakeSphereOp() },
            };
            byte[] buf = new byte[BrushOpCodec.EncodedBatchSize(original.ops.Length)];
            int written = BrushOpCodec.EncodeBatch(original, buf, 0);
            int read = BrushOpCodec.DecodeBatch(buf, 0, out BrushOpBatch decoded);

            Assert.AreEqual(written, read, "Encode + decode must consume identical byte counts.");
            Assert.AreEqual(original.digZoneId, decoded.digZoneId);
            Assert.AreEqual(original.serverTick, decoded.serverTick);
            Assert.AreEqual(original.ops.Length, decoded.ops.Length);
            for (int i = 0; i < original.ops.Length; i++)
                Assert.AreEqual(original.ops[i], decoded.ops[i],
                    $"Op {i} mismatched after batch round-trip.");
        }

        [Test]
        public void DecodeBatch_RejectsCountAboveMaxOpsPerBatch()
        {
            // Hand-craft a buffer whose count header is MaxOpsPerBatch + 1.
            byte[] buf = new byte[BrushOpCodec.EncodedBatchHeaderSize];
            buf[0] = 1; buf[1] = 0; // zoneId = 1
            buf[2] = 0; buf[3] = 0; // tick = 0
            buf[4] = (byte)((BrushOpBatch.MaxOpsPerBatch + 1) & 0xFF);
            buf[5] = (byte)((BrushOpBatch.MaxOpsPerBatch + 1) >> 8);

            Assert.Throws<System.InvalidOperationException>(
                () => BrushOpCodec.DecodeBatch(buf, 0, out _),
                "DecodeBatch must reject counts above MaxOpsPerBatch as malformed input.");
        }

        // ------------------------------------------------------------------
        // Bandwidth synthesis — Phase 6 machine gate's "< 16 kbps per
        // drilling client" assertion, simulated.
        // ------------------------------------------------------------------

        [Test]
        public void BandwidthSynthesis_TenMinDrillTrace_StaysUnderSixteenKbpsPerClient()
        {
            // Per TERRAFORMING_PLAN § 4: continuous drilling at 30 Hz tick
            // rate emits 1 op per tick (capsule sweep from prev to current
            // tip). A 10-minute synthetic trace is 600s × 30 ticks/s =
            // 18000 ticks, each producing one BrushOp wrapped in a
            // single-op BrushOpBatch.
            const int hz = 30;
            const int seconds = 600;
            const int totalTicks = hz * seconds;
            int batchBytes = BrushOpCodec.EncodedBatchSize(1);
            long totalBytes = (long)totalTicks * batchBytes;
            double seconds_d = seconds;
            double bytesPerSecond = totalBytes / seconds_d;
            double kbps = bytesPerSecond * 8.0 / 1000.0;

            Assert.Less(kbps, 16.0,
                $"Synthesized 10-min drill trace ({totalBytes} bytes / {seconds}s = {kbps:F2} kbps) " +
                $"must fit inside the < 16 kbps/client target from TERRAFORMING_PLAN § 11.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static BrushOp MakeSphereOp() => new BrushOp
        {
            kind = BrushKind.SphereSubtract,
            serverTick = 100,
            p0 = Vector3Fixed.FromVector3(new Vector3(1.5f, 2.5f, 3.5f)),
            p1 = Vector3Fixed.FromVector3(new Vector3(1.5f, 2.5f, 3.5f)),
            radiusFixed = (ushort)(2.0f * Vector3Fixed.UnitsPerMeter),
        };

        private static BrushOp MakeCapsuleOp() => new BrushOp
        {
            kind = BrushKind.CapsuleSubtract,
            serverTick = 250,
            p0 = Vector3Fixed.FromVector3(new Vector3(-4.0f, 0.5f, 8.0f)),
            p1 = Vector3Fixed.FromVector3(new Vector3(-3.0f, 0.5f, 8.5f)),
            radiusFixed = (ushort)(1.5f * Vector3Fixed.UnitsPerMeter),
        };
    }
}
