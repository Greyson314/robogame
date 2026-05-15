using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// In-memory parsed contents of a `.dig` file. Plain data; no Unity
    /// dependencies beyond <see cref="Vector3Int"/>. Produced by
    /// <see cref="DigZoneFormat.Read"/>, consumed by <see cref="DigZone"/>'s
    /// loader path.
    /// </summary>
    public sealed class DigZoneSnapshot
    {
        public Vector3Int ChunkGridSize { get; }
        public int ChunkSizeCells { get; }
        public float CellSize { get; }
        public byte[] ContentHash { get; }
        public Chunk[] Chunks { get; }

        public DigZoneSnapshot(Vector3Int chunkGridSize, int chunkSizeCells, float cellSize, byte[] contentHash, Chunk[] chunks)
        {
            ChunkGridSize = chunkGridSize;
            ChunkSizeCells = chunkSizeCells;
            CellSize = cellSize;
            ContentHash = contentHash;
            Chunks = chunks;
        }

        /// <summary>One chunk's contribution to the snapshot — chunk coord + the raw SDF bytes for its own region.</summary>
        public readonly struct Chunk
        {
            public readonly Vector3Int ChunkCoord;
            public readonly byte[] Sdf;

            public Chunk(Vector3Int chunkCoord, byte[] sdf)
            {
                ChunkCoord = chunkCoord;
                Sdf = sdf;
            }
        }
    }
}
