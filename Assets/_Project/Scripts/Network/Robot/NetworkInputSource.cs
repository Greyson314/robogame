using Robogame.Input;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>One tick of owner intent on the wire.</summary>
    public struct InputCommand : INetworkSerializable
    {
        public Vector2 Move;
        public Vector2 Look;
        public float Vertical;
        public bool FireHeld;
        public bool FirePressed;
        public bool ReloadPressed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Look);
            serializer.SerializeValue(ref Vertical);
            serializer.SerializeValue(ref FireHeld);
            serializer.SerializeValue(ref FirePressed);
            serializer.SerializeValue(ref ReloadPressed);
        }
    }

    /// <summary>
    /// An <see cref="IInputSource"/> driven by wire commands instead of the
    /// keyboard. On the server's copy of a remote player's robot this is the
    /// component <c>PlayerController</c> resolves, so <c>RobotDrive</c> and
    /// every movement subsystem run unchanged — the existing interface makes
    /// the network path a drop-in (NETCODE_PLAN §5).
    /// </summary>
    /// <remarks>
    /// Added to the chassis root by <c>NetworkRobot</c> before
    /// <c>ChassisAssembler</c> runs (Bot builds only — owner builds keep
    /// their local <c>PlayerInputHandler</c>), so
    /// <c>PlayerController.Awake</c>'s <c>GetComponent&lt;IInputSource&gt;</c>
    /// picks it up. <see cref="NetworkRobotMovement"/> calls
    /// <see cref="Apply"/> on the server when an owner input RPC lands.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetworkInputSource : MonoBehaviour, IInputSource
    {
        private InputCommand _cmd;

        public Vector2 Move => _cmd.Move;
        public Vector2 Look => _cmd.Look;
        public float Vertical => _cmd.Vertical;
        public bool FireHeld => _cmd.FireHeld;
        public bool FirePressed => _cmd.FirePressed;
        public bool ReloadPressed => _cmd.ReloadPressed;

        /// <summary>Server-side: install the latest received owner command.</summary>
        public void Apply(in InputCommand cmd) => _cmd = cmd;
    }
}
