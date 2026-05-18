using Unity.Netcode;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// One block's post-damage state on the wire (NETCODE_PLAN §7b). 5
    /// bytes. <see cref="BlockIndex"/> is an index into the
    /// <em>canonically-sorted</em> <c>blueprint.Entries</c> array — NOT a
    /// NetworkObjectId — so it resolves to the same physical block on every
    /// peer (invariant #2 / NETCODE_PLAN §6). Batched per network tick into
    /// a single ClientRpc by <see cref="NetworkBlockGrid"/>.
    /// </summary>
    public struct BlockHitEvent : INetworkSerializable
    {
        /// <summary>Index into the canonical blueprint entry ordering.</summary>
        public ushort BlockIndex;

        /// <summary>Block HP after the hit, ceil-rounded. 0 = destroyed.</summary>
        public ushort HpAfter;

        /// <summary>Reserved hit-flag bits (crit / splash / structural).
        /// Phase 1 sends 0; the field pins the wire layout for Phase 4.</summary>
        public byte HitFlags;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref BlockIndex);
            serializer.SerializeValue(ref HpAfter);
            serializer.SerializeValue(ref HitFlags);
        }
    }
}
