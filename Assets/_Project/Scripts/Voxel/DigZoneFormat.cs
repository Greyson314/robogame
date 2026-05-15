using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Binary serialisation for a <see cref="DigZone"/>'s initial SDF state.
    /// The on-disk format is a `.dig` (loaded as a Unity `TextAsset` via its
    /// `.bytes` property) — a fixed-layout header followed by sequential
    /// per-chunk SDF payloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Header layout (68 bytes):
    /// <list type="bullet">
    ///   <item><c>magic</c> (8 bytes): the ASCII <c>"ROBODIG\0"</c> sentinel.</item>
    ///   <item><c>version</c> (4 bytes, uint32): <c>1</c> in this rev.</item>
    ///   <item><c>chunkGridSize</c> (12 bytes, 3 × int32): X, Y, Z chunk grid extent.</item>
    ///   <item><c>chunkSizeCells</c> (4 bytes, int32).</item>
    ///   <item><c>cellSize</c> (4 bytes, float32).</item>
    ///   <item><c>contentHash</c> (32 bytes): SHA-256 of the payload region.</item>
    ///   <item><c>payloadOffset</c> (4 bytes, int32): always 68 in v1.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Payload layout: <c>chunkGridSize.x × y × z</c> chunks in z-major
    /// order, each chunk being three int32 chunk-coords followed by
    /// <c>(chunkSizeCells + 1)³</c> sbyte SDF samples (z-major). The chunk
    /// SDFs are the OWN region only — apron data is recomputed by the
    /// runtime, never stored.
    /// </para>
    /// <para>
    /// The content hash is Phase 6 netcode-relevant: a client and server
    /// loading the same authored arena must compute the same hash from
    /// the asset bytes, so a tampered or mismatched <c>.dig</c> fails
    /// the connection handshake.
    /// </para>
    /// </remarks>
    public static class DigZoneFormat
    {
        public const uint Version = 1;
        public const int HeaderSize = 68;
        public const int ContentHashSize = 32;
        public static readonly byte[] MagicBytes = System.Text.Encoding.ASCII.GetBytes("ROBODIG\0");

        /// <summary>Serialise a fully-initialised <see cref="DigZone"/> to a <c>.dig</c> byte buffer.</summary>
        public static byte[] Write(DigZone zone)
        {
            if (zone == null) throw new System.ArgumentNullException(nameof(zone));
            if (zone.ChunkCount == 0)
                throw new System.InvalidOperationException("DigZone is not initialised — call EnsureInitialised first.");

            int dim = zone.ChunkSizeCells + 1;
            int sdfBytesPerChunk = dim * dim * dim;

            // Build the payload first so we can hash it before writing the header.
            using var payloadStream = new MemoryStream();
            using var payloadWriter = new BinaryWriter(payloadStream);

            int nx = zone.ChunkGridSize.x, ny = zone.ChunkGridSize.y, nz = zone.ChunkGridSize.z;
            for (int cz = 0; cz < nz; cz++)
            for (int cy = 0; cy < ny; cy++)
            for (int cx = 0; cx < nx; cx++)
            {
                DigChunk chunk = zone.GetChunk(cx, cy, cz);
                if (chunk == null)
                    throw new System.InvalidOperationException($"Chunk ({cx},{cy},{cz}) is null at bake time.");

                payloadWriter.Write(chunk.ChunkCoord.x);
                payloadWriter.Write(chunk.ChunkCoord.y);
                payloadWriter.Write(chunk.ChunkCoord.z);

                NativeArray<sbyte> sdf = chunk.Sdf;
                if (sdf.Length != sdfBytesPerChunk)
                    throw new System.InvalidOperationException(
                        $"Chunk ({cx},{cy},{cz}) SDF length {sdf.Length} != expected {sdfBytesPerChunk}.");

                // Write sbyte data as bytes. Reinterpret keeps it zero-copy.
                NativeArray<byte> asBytes = sdf.Reinterpret<byte>(sizeof(sbyte));
                for (int i = 0; i < asBytes.Length; i++) payloadWriter.Write(asBytes[i]);
            }
            payloadWriter.Flush();
            byte[] payloadBytes = payloadStream.ToArray();

            byte[] contentHash;
            using (SHA256 sha = SHA256.Create())
                contentHash = sha.ComputeHash(payloadBytes);

            using var outputStream = new MemoryStream();
            using var w = new BinaryWriter(outputStream);
            w.Write(MagicBytes);                       // 8
            w.Write(Version);                          // 4
            w.Write(zone.ChunkGridSize.x);             // 4
            w.Write(zone.ChunkGridSize.y);             // 4
            w.Write(zone.ChunkGridSize.z);             // 4
            w.Write(zone.ChunkSizeCells);              // 4
            w.Write(zone.CellSize);                    // 4
            w.Write(contentHash);                      // 32
            w.Write(HeaderSize);                       // 4 (payload offset)
            w.Write(payloadBytes);
            return outputStream.ToArray();
        }

        /// <summary>
        /// Parse a <c>.dig</c> byte buffer into a <see cref="DigZoneSnapshot"/>.
        /// Validates magic + version; the caller is responsible for
        /// validating the parsed dimensions against the consumer DigZone's
        /// configuration.
        /// </summary>
        public static DigZoneSnapshot Read(byte[] data)
        {
            if (data == null) throw new System.ArgumentNullException(nameof(data));
            if (data.Length < HeaderSize)
                throw new System.IO.InvalidDataException(
                    $"Buffer length {data.Length} is shorter than the header ({HeaderSize}).");

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);

            byte[] magic = r.ReadBytes(8);
            for (int i = 0; i < MagicBytes.Length; i++)
                if (magic[i] != MagicBytes[i])
                    throw new System.IO.InvalidDataException("Magic bytes do not match — not a .dig file.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new System.IO.InvalidDataException($"Unsupported .dig version {version} (expected {Version}).");

            var gridSize = new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            int chunkSizeCells = r.ReadInt32();
            float cellSize = r.ReadSingle();
            byte[] contentHash = r.ReadBytes(ContentHashSize);
            int payloadOffset = r.ReadInt32();
            if (payloadOffset != HeaderSize)
                throw new System.IO.InvalidDataException(
                    $"Unexpected payload offset {payloadOffset} (expected {HeaderSize}).");

            int dim = chunkSizeCells + 1;
            int sdfBytesPerChunk = dim * dim * dim;
            int chunkCount = gridSize.x * gridSize.y * gridSize.z;
            int expectedTotal = HeaderSize + chunkCount * (12 + sdfBytesPerChunk);
            if (data.Length != expectedTotal)
                throw new System.IO.InvalidDataException(
                    $"Buffer length {data.Length} doesn't match expected {expectedTotal} for {chunkCount} chunks.");

            var chunks = new DigZoneSnapshot.Chunk[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                var coord = new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                byte[] sdfBytes = r.ReadBytes(sdfBytesPerChunk);
                chunks[i] = new DigZoneSnapshot.Chunk(coord, sdfBytes);
            }

            // Re-hash the payload region to verify integrity.
            int payloadLen = data.Length - HeaderSize;
            byte[] payloadCopy = new byte[payloadLen];
            System.Buffer.BlockCopy(data, HeaderSize, payloadCopy, 0, payloadLen);
            byte[] verifyHash;
            using (SHA256 sha = SHA256.Create()) verifyHash = sha.ComputeHash(payloadCopy);
            bool hashMatches = true;
            for (int i = 0; i < ContentHashSize; i++)
                if (verifyHash[i] != contentHash[i]) { hashMatches = false; break; }
            if (!hashMatches)
                throw new System.IO.InvalidDataException("Content hash mismatch — .dig payload is corrupt or tampered.");

            return new DigZoneSnapshot(gridSize, chunkSizeCells, cellSize, contentHash, chunks);
        }
    }
}
