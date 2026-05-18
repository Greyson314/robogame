using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Bucket-B per-match construction state (NETCODE_PLAN §6 / §7a): the
    /// data a peer needs to reconstruct one robot at spawn. The blueprint
    /// rides as a <see cref="Robogame.Block.BlueprintBlob"/> byte payload —
    /// the client decodes it and runs the <em>existing</em>
    /// <c>ChassisAssembler</c> path 1:1, so there is no parallel
    /// construction code (handoff §2.2).
    /// </summary>
    /// <remarks>
    /// Sent once via a post-spawn <c>ClientRpc</c> (not a managed
    /// <c>NetworkVariable</c>): Phase-1 v1 has no late-join
    /// (NETCODE_PLAN §10), so the replay-to-late-joiner property a
    /// NetworkVariable would buy is unused, and an RPC sidesteps NGO's
    /// managed-NetworkVariable version sensitivity. NGO guarantees an RPC
    /// targeting a NetworkObject is delivered after that object has
    /// spawned on the receiver, so ordering is safe.
    /// </remarks>
    public struct SpawnRobotPayload : INetworkSerializable
    {
        public ulong PlayerId;
        public byte TeamId;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
        public byte[] BlueprintBlob;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref TeamId);
            serializer.SerializeValue(ref SpawnPosition);
            serializer.SerializeValue(ref SpawnRotation);
            serializer.SerializeValue(ref BlueprintBlob);
        }
    }
}
