using Robogame.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Enforces server-authoritative combat (NETCODE_PLAN §9 / §13). The
    /// hard invariant: <b>clients never spawn an authoritative projectile.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase-1 fire path is server-authoritative <em>by reuse</em>, not by
    /// a new code path. The owner's <c>FireHeld</c>/<c>FirePressed</c> is
    /// already replicated to the server inside the Step-7
    /// <see cref="InputCommand"/>; the server's copy of the robot resolves
    /// its <see cref="NetworkInputSource"/> as the weapon
    /// <c>IInputSource</c>, so the existing <c>ProjectileGun</c> /
    /// <c>CannonBlock</c> / <c>BombBayBlock</c> fire on the server with real
    /// damage, and that damage replicates through
    /// <see cref="NetworkBlockGrid"/> (Step 6). Gameplay combat code is
    /// untouched (NETCODE_PLAN §5).
    /// </para>
    /// <para>
    /// This component's concrete job is the §9 invariant: on every
    /// non-server copy (owner <em>and</em> remote) it disables the weapon
    /// firers so no client ever spawns a damaging projectile or runs hit
    /// detection. The server alone owns projectiles and damage.
    /// </para>
    /// <para>
    /// <b>Deliberately deferred (honest scope):</b> the explicit validated
    /// <c>FireCommand</c> ServerRpc (cooldown / aim-bounds checks — §9
    /// step 2 / §13) and the cosmetic <c>ProjectileSpawnEvent</c> ClientRpc
    /// that paints tracers on observer clients both require a sanctioned
    /// projectile-spawn observation hook on <c>ProjectileWorld</c> — a
    /// Combat-tier change outside this pass's "don't touch gameplay" remit.
    /// Phase-1 consequence: remote players' shots may not draw a client-side
    /// tracer ("ugly", per the Phase-1 exit criterion), but damage and block
    /// destruction DO replicate correctly via NetworkBlockGrid. Wiring the
    /// tracer + validated FireCommand is the first combat task of the next
    /// netcode phase.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobotCombat : NetworkBehaviour
    {
        private NetworkRobot _net;

        private void Awake() => _net = GetComponent<NetworkRobot>();

        public override void OnNetworkSpawn() => _net.WhenBuilt(OnChassisBuilt);

        private void OnChassisBuilt(NetworkRobot _)
        {
            // The server (incl. host) keeps its firers — it owns the
            // authoritative projectiles and damage.
            if (IsServer) return;

            // Every client copy: silence the firers so the client cannot
            // spawn an authoritative projectile or compute a hit. The
            // owner still sees its shots indirectly — the server fires and
            // block damage/destruction replicates back via NetworkBlockGrid.
            foreach (ProjectileGun gun in GetComponentsInChildren<ProjectileGun>(true))
                gun.enabled = false;
            foreach (CannonBlock cannon in GetComponentsInChildren<CannonBlock>(true))
                cannon.enabled = false;
            foreach (BombBayBlock bay in GetComponentsInChildren<BombBayBlock>(true))
                bay.enabled = false;
        }
    }
}
