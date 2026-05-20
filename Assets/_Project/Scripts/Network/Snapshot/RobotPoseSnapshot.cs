using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Snapshot
{
    /// <summary>
    /// One server tick of a robot's authoritative physics state, sent to the
    /// owning client so it can reconcile its predicted local Rigidbody
    /// (NETCODE_PLAN §6 Bucket C, §8). Sized to fit one MTU comfortably even
    /// at 16-robot fan-out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The struct stores Rigidbody pose + linear / angular velocity plus the
    /// server's last-processed owner-command tick. The owner uses
    /// <see cref="LastProcessedCommandTick"/> to know which subset of its
    /// circular command buffer to replay on top of the snapshot.
    /// </para>
    /// <para>
    /// Phase 3 sends snapshots reliably for simplicity (NGO's reliable channel
    /// preserves ordering). The bandwidth-conscious migration to unreliable
    /// + sequence-numbered snapshots is a Phase-4 task.
    /// </para>
    /// </remarks>
    public struct RobotPoseSnapshot : INetworkSerializable
    {
        public int ServerTick;
        public int LastProcessedCommandTick;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref LastProcessedCommandTick);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref LinearVelocity);
            serializer.SerializeValue(ref AngularVelocity);
        }
    }
}
