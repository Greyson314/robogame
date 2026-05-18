using System;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// One-way Core signal: the local player's networked robot finished
    /// building. Gameplay (e.g. <c>ArenaController</c>) subscribes to bind
    /// the camera / HUDs to it, without referencing <c>Robogame.Network</c>
    /// (asmdef contract — same reason <see cref="INetworkContext"/> lives
    /// in Core). The Network module raises it; gameplay listens.
    /// </summary>
    public static class NetworkPlayerBridge
    {
        /// <summary>Raised on the owning client when its networked robot's
        /// chassis is assembled. Argument is the robot root GameObject.</summary>
        public static event Action<GameObject> LocalOwnerRobotReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LocalOwnerRobotReady = null;

        /// <summary>Called by the Network module (owner side, post-build).</summary>
        public static void RaiseLocalOwnerRobotReady(GameObject robotRoot)
            => LocalOwnerRobotReady?.Invoke(robotRoot);
    }
}
