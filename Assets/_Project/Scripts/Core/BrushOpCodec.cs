using System;

namespace Robogame.Core
{
    /// <summary>
    /// Binary encode / decode for <see cref="BrushOp"/> and
    /// <see cref="BrushOpBatch"/>. Phase 6 wire contract per
    /// TERRAFORMING_PLAN § 10.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire format is fixed-size, little-endian, no headers — the
    /// surrounding transport (a ClientRpc, a log entry, a tcp frame)
    /// supplies framing. A <see cref="BrushOp"/> is exactly
    /// <see cref="EncodedOpSize"/> bytes (17); a <see cref="BrushOpBatch"/>
    /// is <see cref="EncodedBatchHeaderSize"/> bytes of header (6) plus
    /// <see cref="EncodedOpSize"/> per op.
    /// </para>
    /// <para>
    /// The codec deliberately avoids managed BinaryReader / BinaryWriter
    /// — both allocate on construction and copy through stream wrappers.
    /// Direct <c>byte[]</c> + offset arithmetic is zero-alloc and matches
    /// the netcode hot-path requirements from PHYSICS_PLAN § 1 (no per-
    /// frame allocations).
    /// </para>
    /// </remarks>
    public static class BrushOpCodec
    {
        /// <summary>Bytes per encoded <see cref="BrushOp"/>: 1 + 2 + 6 + 6 + 2 = 17.</summary>
        public const int EncodedOpSize = 17;

        /// <summary>Bytes per <see cref="BrushOpBatch"/> header (zoneId + tick + count): 2 + 2 + 2 = 6.</summary>
        public const int EncodedBatchHeaderSize = 6;

        /// <summary>Total wire size of a batch carrying <paramref name="opCount"/> ops.</summary>
        public static int EncodedBatchSize(int opCount) => EncodedBatchHeaderSize + opCount * EncodedOpSize;

        // -----------------------------------------------------------------
        // BrushOp
        // -----------------------------------------------------------------

        /// <summary>
        /// Write a single <see cref="BrushOp"/> to <paramref name="buffer"/>
        /// starting at <paramref name="offset"/>. Returns the number of
        /// bytes written (always <see cref="EncodedOpSize"/>). Throws if
        /// the buffer can't hold the op.
        /// </summary>
        public static int EncodeOp(in BrushOp op, byte[] buffer, int offset)
        {
            EnsureRoom(buffer, offset, EncodedOpSize, nameof(EncodeOp));
            buffer[offset + 0] = (byte)op.kind;
            WriteUShort(buffer, offset + 1, op.serverTick);
            WriteShort(buffer, offset + 3, op.p0.x);
            WriteShort(buffer, offset + 5, op.p0.y);
            WriteShort(buffer, offset + 7, op.p0.z);
            WriteShort(buffer, offset + 9, op.p1.x);
            WriteShort(buffer, offset + 11, op.p1.y);
            WriteShort(buffer, offset + 13, op.p1.z);
            WriteUShort(buffer, offset + 15, op.radiusFixed);
            return EncodedOpSize;
        }

        /// <summary>
        /// Read a single <see cref="BrushOp"/> from <paramref name="buffer"/>
        /// starting at <paramref name="offset"/>. Returns bytes consumed
        /// (always <see cref="EncodedOpSize"/>).
        /// </summary>
        public static int DecodeOp(byte[] buffer, int offset, out BrushOp op)
        {
            EnsureRoom(buffer, offset, EncodedOpSize, nameof(DecodeOp));
            op = new BrushOp
            {
                kind = (BrushKind)buffer[offset + 0],
                serverTick = ReadUShort(buffer, offset + 1),
                p0 = new Vector3Fixed(
                    ReadShort(buffer, offset + 3),
                    ReadShort(buffer, offset + 5),
                    ReadShort(buffer, offset + 7)),
                p1 = new Vector3Fixed(
                    ReadShort(buffer, offset + 9),
                    ReadShort(buffer, offset + 11),
                    ReadShort(buffer, offset + 13)),
                radiusFixed = ReadUShort(buffer, offset + 15),
            };
            return EncodedOpSize;
        }

        // -----------------------------------------------------------------
        // BrushOpBatch
        // -----------------------------------------------------------------

        /// <summary>
        /// Encode a <see cref="BrushOpBatch"/> into <paramref name="buffer"/>.
        /// Returns bytes written. Throws if <c>batch.ops</c> is null, has
        /// more than <see cref="BrushOpBatch.MaxOpsPerBatch"/> entries, or
        /// the buffer is too small.
        /// </summary>
        public static int EncodeBatch(in BrushOpBatch batch, byte[] buffer, int offset)
        {
            if (batch.ops == null) throw new ArgumentNullException(nameof(batch), "BrushOpBatch.ops is null");
            int count = batch.ops.Length;
            if (count > BrushOpBatch.MaxOpsPerBatch)
                throw new ArgumentOutOfRangeException(nameof(batch),
                    $"BrushOpBatch has {count} ops; max {BrushOpBatch.MaxOpsPerBatch}");

            int total = EncodedBatchSize(count);
            EnsureRoom(buffer, offset, total, nameof(EncodeBatch));

            WriteUShort(buffer, offset + 0, batch.digZoneId);
            WriteUShort(buffer, offset + 2, batch.serverTick);
            WriteUShort(buffer, offset + 4, (ushort)count);
            int write = offset + EncodedBatchHeaderSize;
            for (int i = 0; i < count; i++)
            {
                EncodeOp(batch.ops[i], buffer, write);
                write += EncodedOpSize;
            }
            return total;
        }

        /// <summary>
        /// Decode a <see cref="BrushOpBatch"/> from <paramref name="buffer"/>.
        /// Allocates a new <c>ops</c> array sized to the encoded count.
        /// Returns bytes consumed.
        /// </summary>
        public static int DecodeBatch(byte[] buffer, int offset, out BrushOpBatch batch)
        {
            EnsureRoom(buffer, offset, EncodedBatchHeaderSize, nameof(DecodeBatch));
            ushort zoneId = ReadUShort(buffer, offset + 0);
            ushort tick = ReadUShort(buffer, offset + 2);
            ushort count = ReadUShort(buffer, offset + 4);
            if (count > BrushOpBatch.MaxOpsPerBatch)
                throw new InvalidOperationException(
                    $"Decoded BrushOpBatch op count {count} exceeds max {BrushOpBatch.MaxOpsPerBatch}");

            int total = EncodedBatchSize(count);
            EnsureRoom(buffer, offset, total, nameof(DecodeBatch));

            BrushOp[] ops = new BrushOp[count];
            int read = offset + EncodedBatchHeaderSize;
            for (int i = 0; i < count; i++)
            {
                DecodeOp(buffer, read, out ops[i]);
                read += EncodedOpSize;
            }

            batch = new BrushOpBatch { digZoneId = zoneId, serverTick = tick, ops = ops };
            return total;
        }

        // -----------------------------------------------------------------
        // Low-level read/write — little-endian, branch-free.
        // -----------------------------------------------------------------

        private static void WriteShort(byte[] b, int off, short v)
        {
            b[off + 0] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        private static short ReadShort(byte[] b, int off)
            => (short)(b[off + 0] | (b[off + 1] << 8));

        private static void WriteUShort(byte[] b, int off, ushort v)
        {
            b[off + 0] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        private static ushort ReadUShort(byte[] b, int off)
            => (ushort)(b[off + 0] | (b[off + 1] << 8));

        private static void EnsureRoom(byte[] buffer, int offset, int needed, string caller)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer), $"{caller}: buffer is null");
            if (offset < 0 || offset + needed > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"{caller}: need {needed} bytes at offset {offset}, buffer length {buffer.Length}");
        }
    }
}
