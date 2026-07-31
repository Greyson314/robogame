using Robogame.Input;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>One tick of owner intent on the wire. <see cref="Tick"/>
    /// stamps the command for Phase-3 CSP — the owner replays from the
    /// last-acked tick on each server snapshot, and the server records the
    /// last-processed tick per client so the owner knows what to replay.</summary>
    public struct InputCommand : INetworkSerializable
    {
        public int Tick;
        public Vector2 Move;
        public Vector2 Look;
        public float Vertical;
        public bool FireHeld;
        public bool FirePressed;
        public bool ReloadPressed;
        public bool FlipPressed;
        public bool HookReleasePressed;
        // Bit i (0..3) set = module slot i pressed this tick. One byte covers
        // the ModuleBudget.MaxModules ability bar (replaced the single
        // ModulePressed bool when modules went multi-slot, session 105).
        public byte ModuleMask;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Move);
            serializer.SerializeValue(ref Look);
            serializer.SerializeValue(ref Vertical);
            serializer.SerializeValue(ref FireHeld);
            serializer.SerializeValue(ref FirePressed);
            serializer.SerializeValue(ref ReloadPressed);
            serializer.SerializeValue(ref FlipPressed);
            serializer.SerializeValue(ref HookReleasePressed);
            serializer.SerializeValue(ref ModuleMask);
        }

        /// <summary>Pack an <see cref="IInputSource"/>'s slot presses into the mask.</summary>
        public static byte PackModuleMask(IInputSource src)
        {
            if (src == null) return 0;
            byte mask = 0;
            for (int i = 0; i < 4; i++)
                if (src.GetModulePressed(i)) mask |= (byte)(1 << i);
            return mask;
        }
    }

    /// <summary>
    /// An <see cref="IInputSource"/> driven by wire commands on the server's
    /// copy of a remote player's robot, and a replay-aware delegating bridge
    /// on the owning client's copy (Phase 3.5 CSP).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Server</b>: <see cref="Apply"/> installs the latest owner command;
    /// property reads return its fields. No <see cref="BindLive"/> call —
    /// the server has no local human input.
    /// </para>
    /// <para>
    /// <b>Owner client (Phase 3.5)</b>: <see cref="NetworkRobot"/> adds this
    /// component alongside <c>PlayerInputHandler</c> and calls
    /// <see cref="BindLive"/> with the handler. In normal play the
    /// properties delegate to the bound live source — chassis components
    /// (RobotDrive, ProjectileGun, etc.) read current input every frame.
    /// During CSP reconciliation, <see cref="NetworkRobotMovement"/> calls
    /// <see cref="EnterReplay"/> with each historical command before
    /// stepping <see cref="Physics.Simulate"/>, then
    /// <see cref="ExitReplay"/> to restore live delegation.
    /// </para>
    /// <para>
    /// <b>Non-owner remote client</b>: this component is not used (the chassis
    /// is kinematic + NetworkTransform-driven).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetworkInputSource : MonoBehaviour, IInputSource, IInputSourceWrapper
    {
        private InputCommand _cmd;
        private IInputSource _live;
        private bool _replayMode;

        /// <summary>Owner-client only — bind the live input source whose
        /// values the delegating properties read when not in replay.</summary>
        public void BindLive(IInputSource live)
        {
            _live = live;
            _replayMode = false;
        }

        /// <summary>Owner-client only — enter replay with <paramref name="cmd"/>
        /// as the locked values. Subsequent reads return cmd's fields until
        /// <see cref="ExitReplay"/>.</summary>
        public void EnterReplay(in InputCommand cmd)
        {
            _cmd = cmd;
            _replayMode = true;
        }

        /// <summary>Owner-client only — exit replay mode. Subsequent reads
        /// delegate back to the bound live source.</summary>
        public void ExitReplay() => _replayMode = false;

        /// <summary>Server-side — install the latest owner command. Reads
        /// always return its fields (server has no live source).</summary>
        public void Apply(in InputCommand cmd)
        {
            _cmd = cmd;
            _replayMode = true; // server always reads from _cmd
        }

        public Vector2 Move => UseCmd ? _cmd.Move : _live.Move;
        public Vector2 Look => UseCmd ? _cmd.Look : _live.Look;
        public float Vertical => UseCmd ? _cmd.Vertical : _live.Vertical;
        public bool FireHeld => UseCmd ? _cmd.FireHeld : _live.FireHeld;
        public bool FirePressed => UseCmd ? _cmd.FirePressed : _live.FirePressed;
        public bool ReloadPressed => UseCmd ? _cmd.ReloadPressed : _live.ReloadPressed;
        public bool FlipPressed => UseCmd ? _cmd.FlipPressed : _live.FlipPressed;
        public bool HookReleasePressed => UseCmd ? _cmd.HookReleasePressed : _live.HookReleasePressed;

        /// <summary>
        /// The bound live source (owner client) — lets consumers that
        /// gate on WHO drives the chassis (WeaponAmmoState's local-player
        /// audio check) unwrap through the delegation. Null on the server
        /// copy, which is exactly the "not the local player" answer.
        /// </summary>
        public IInputSource InnerSource => _live;

        public bool GetModulePressed(int slot)
        {
            if (slot < 0 || slot > 3) return false;
            return UseCmd ? (_cmd.ModuleMask & (1 << slot)) != 0 : _live.GetModulePressed(slot);
        }

        private bool UseCmd => _replayMode || _live == null;
    }
}
