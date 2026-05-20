using Robogame.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// One owner-claimed fire attempt on the wire (NETCODE_PLAN §9). Carries
    /// enough state for the server to validate the request: when the owner
    /// thought they were firing (<see cref="Tick"/>), the world-space aim
    /// (<see cref="AimDir"/>), and the muzzle position (<see cref="MuzzlePos"/>).
    /// </summary>
    /// <remarks>
    /// Phase-2 validation uses cooldown only; aim-bounds is a stub always-pass
    /// because the owner's local input is in look-input (yaw/pitch) space while
    /// <see cref="AimDir"/> is world-space muzzle-forward — the reconciliation
    /// belongs in Phase 3 alongside CSP. The aim fields are still wired today
    /// so Phase 3+ can flip aim-validation on without an RPC-shape churn.
    /// </remarks>
    public struct FireCommand : INetworkSerializable
    {
        public uint Tick;
        public Vector3 AimDir;
        public Vector3 MuzzlePos;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref AimDir);
            serializer.SerializeValue(ref MuzzlePos);
        }
    }

    /// <summary>
    /// Cosmetic projectile-spawn echo from the server to every client
    /// (NETCODE_PLAN §9). Owner non-server clients use this to paint their own
    /// tracer (their local firers are disabled); remote clients use it to paint
    /// other players' tracers. <em>Damage is zero on cosmetic spawns — only
    /// the server's <see cref="ProjectileWorld"/> applies damage.</em>
    /// </summary>
    public struct ProjectileSpawnPayload : INetworkSerializable
    {
        public ProjectileKind Kind;
        public Vector3 Origin;
        public Vector3 InitialVelocity;
        public Vector3 GravityWorld;
        public float MaxLifetime;
        public float CastRadius;
        public float VisualMeshDiameter;
        public Color VisualTint;
        public int HitMask;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int kindInt = (int)Kind;
            serializer.SerializeValue(ref kindInt);
            Kind = (ProjectileKind)kindInt;
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref InitialVelocity);
            serializer.SerializeValue(ref GravityWorld);
            serializer.SerializeValue(ref MaxLifetime);
            serializer.SerializeValue(ref CastRadius);
            serializer.SerializeValue(ref VisualMeshDiameter);
            serializer.SerializeValue(ref VisualTint);
            serializer.SerializeValue(ref HitMask);
        }
    }
}
