using NUnit.Framework;
using Robogame.Voxel;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Phase 2d machine gate: DigZoneFormat write/read round-trip is
    /// byte-identical and content-hash-stable. Plus tamper detection —
    /// flipping any payload byte makes the read throw.
    /// </summary>
    public sealed class DigZoneFormatTests
    {
        private static DigZoneSnapshot MakeSnapshot(Vector3Int gridSize, int chunkSizeCells, float cellSize, int seed)
        {
            int dim = chunkSizeCells + 1;
            int sdfBytes = dim * dim * dim;
            int chunkCount = gridSize.x * gridSize.y * gridSize.z;
            var chunks = new DigZoneSnapshot.Chunk[chunkCount];

            int idx = 0;
            var rng = new System.Random(seed);
            for (int cz = 0; cz < gridSize.z; cz++)
            for (int cy = 0; cy < gridSize.y; cy++)
            for (int cx = 0; cx < gridSize.x; cx++)
            {
                var sdf = new byte[sdfBytes];
                for (int i = 0; i < sdfBytes; i++) sdf[i] = (byte)rng.Next(0, 256);
                chunks[idx++] = new DigZoneSnapshot.Chunk(new Vector3Int(cx, cy, cz), sdf);
            }

            // ContentHash field on DigZoneSnapshot isn't used by Write; it
            // recomputes from the payload. Passing zeros here is fine.
            return new DigZoneSnapshot(gridSize, chunkSizeCells, cellSize, new byte[DigZoneFormat.ContentHashSize], chunks);
        }

        // ------------------------------------------------------------------
        // Round-trip
        // ------------------------------------------------------------------

        [Test]
        public void Write_Read_SmallGrid_RoundTripByteIdentical()
        {
            DigZoneSnapshot original = MakeSnapshot(new Vector3Int(2, 1, 1), chunkSizeCells: 4, cellSize: 0.5f, seed: 42);

            byte[] bytes = WriteSnapshotDirectly(original);
            DigZoneSnapshot decoded = DigZoneFormat.Read(bytes);

            Assert.AreEqual(original.ChunkGridSize, decoded.ChunkGridSize);
            Assert.AreEqual(original.ChunkSizeCells, decoded.ChunkSizeCells);
            Assert.AreEqual(original.CellSize, decoded.CellSize);
            Assert.AreEqual(original.Chunks.Length, decoded.Chunks.Length);

            for (int i = 0; i < original.Chunks.Length; i++)
            {
                Assert.AreEqual(original.Chunks[i].ChunkCoord, decoded.Chunks[i].ChunkCoord);
                CollectionAssert.AreEqual(original.Chunks[i].Sdf, decoded.Chunks[i].Sdf,
                    $"Chunk {i} SDF bytes don't match after round-trip.");
            }
        }

        [Test]
        public void Write_TwoRunsSameInput_ProducesIdenticalBytes()
        {
            // Content hash stability — the same SDF data baked twice gives
            // the same bytes (which means the same hash). This is what the
            // Phase 6 netcode handshake will rely on to detect tampering.
            DigZoneSnapshot a = MakeSnapshot(new Vector3Int(2, 1, 1), 4, 0.5f, seed: 1);

            byte[] bytesA = WriteSnapshotDirectly(a);
            byte[] bytesB = WriteSnapshotDirectly(a);

            CollectionAssert.AreEqual(bytesA, bytesB, "Re-baking the same snapshot must produce byte-identical output.");
        }

        [Test]
        public void Write_DifferentInputs_ProduceDifferentHashes()
        {
            DigZoneSnapshot a = MakeSnapshot(new Vector3Int(2, 1, 1), 4, 0.5f, seed: 1);
            DigZoneSnapshot b = MakeSnapshot(new Vector3Int(2, 1, 1), 4, 0.5f, seed: 2);

            byte[] bytesA = WriteSnapshotDirectly(a);
            byte[] bytesB = WriteSnapshotDirectly(b);

            DigZoneSnapshot decodedA = DigZoneFormat.Read(bytesA);
            DigZoneSnapshot decodedB = DigZoneFormat.Read(bytesB);

            // Different SDF inputs → different hashes (with overwhelming probability for SHA-256).
            CollectionAssert.AreNotEqual(decodedA.ContentHash, decodedB.ContentHash);
        }

        // ------------------------------------------------------------------
        // Tamper detection
        // ------------------------------------------------------------------

        [Test]
        public void Read_TamperedPayloadByte_ThrowsInvalidDataException()
        {
            DigZoneSnapshot original = MakeSnapshot(new Vector3Int(2, 1, 1), 4, 0.5f, seed: 7);
            byte[] bytes = WriteSnapshotDirectly(original);

            // Flip a byte in the payload (anywhere past the 68-byte header).
            int tamperIdx = DigZoneFormat.HeaderSize + 20;
            bytes[tamperIdx] ^= 0xFF;

            Assert.Throws<System.IO.InvalidDataException>(() => DigZoneFormat.Read(bytes),
                "Tampering a payload byte must trip the content-hash verification.");
        }

        [Test]
        public void Read_BadMagic_ThrowsInvalidDataException()
        {
            DigZoneSnapshot original = MakeSnapshot(new Vector3Int(1, 1, 1), 4, 0.5f, seed: 9);
            byte[] bytes = WriteSnapshotDirectly(original);

            bytes[0] = (byte)'X';   // corrupt magic
            Assert.Throws<System.IO.InvalidDataException>(() => DigZoneFormat.Read(bytes));
        }

        [Test]
        public void Read_TruncatedBuffer_ThrowsInvalidDataException()
        {
            DigZoneSnapshot original = MakeSnapshot(new Vector3Int(1, 1, 1), 4, 0.5f, seed: 11);
            byte[] bytes = WriteSnapshotDirectly(original);

            byte[] truncated = new byte[bytes.Length - 10];
            System.Buffer.BlockCopy(bytes, 0, truncated, 0, truncated.Length);

            Assert.Throws<System.IO.InvalidDataException>(() => DigZoneFormat.Read(truncated));
        }

        // ------------------------------------------------------------------
        // Helpers
        //
        // DigZoneFormat.Write takes a live DigZone, but for EditMode-only
        // tests we want to skip the MonoBehaviour. We replicate the format
        // logic against a snapshot directly so the format math is testable
        // without spinning up a zone.
        // ------------------------------------------------------------------

        private static byte[] WriteSnapshotDirectly(DigZoneSnapshot s)
        {
            int dim = s.ChunkSizeCells + 1;
            int sdfBytesPerChunk = dim * dim * dim;

            using var payloadStream = new System.IO.MemoryStream();
            using var pw = new System.IO.BinaryWriter(payloadStream);
            foreach (DigZoneSnapshot.Chunk c in s.Chunks)
            {
                pw.Write(c.ChunkCoord.x); pw.Write(c.ChunkCoord.y); pw.Write(c.ChunkCoord.z);
                pw.Write(c.Sdf, 0, sdfBytesPerChunk);
            }
            pw.Flush();
            byte[] payload = payloadStream.ToArray();

            byte[] hash;
            using (var sha = System.Security.Cryptography.SHA256.Create()) hash = sha.ComputeHash(payload);

            using var outStream = new System.IO.MemoryStream();
            using var w = new System.IO.BinaryWriter(outStream);
            w.Write(DigZoneFormat.MagicBytes);
            w.Write(DigZoneFormat.Version);
            w.Write(s.ChunkGridSize.x); w.Write(s.ChunkGridSize.y); w.Write(s.ChunkGridSize.z);
            w.Write(s.ChunkSizeCells);
            w.Write(s.CellSize);
            w.Write(hash);
            w.Write(DigZoneFormat.HeaderSize);
            w.Write(payload);
            return outStream.ToArray();
        }
    }
}
