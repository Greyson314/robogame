using Robogame.Combat;
using Robogame.Core;
using Robogame.Input;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Server-authoritative combat sibling (NETCODE_PLAN §9 / §13). Two
    /// responsibilities on top of Phase-1's "silence client firers" base:
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>(1) FireCommand validation.</b> The owner sends a
    /// <see cref="FireCommandServerRpc"/> on rising-edge + periodic re-fire
    /// while held; the server cross-checks the cooldown via
    /// <see cref="FireCooldownTable"/> and increments
    /// <see cref="RejectedFireCount"/> on a breach. Aim-bounds validation is
    /// a stubbed always-pass for now (owner's input is in look-input space,
    /// <see cref="FireCommand.AimDir"/> is world-space muzzle-forward — the
    /// reconciliation belongs in Phase 3 alongside CSP). The aim fields are
    /// still on the wire so Phase 3+ can flip aim-validation on without an
    /// RPC churn.
    /// </para>
    /// <para>
    /// <b>(2) Cosmetic tracer fan-out.</b> Server subscribes to
    /// <see cref="ProjectileWorld.Spawned"/> filtered to its own owner's
    /// shots, packs a <see cref="ProjectileSpawnPayload"/>, and broadcasts
    /// via <see cref="ProjectileSpawnEventClientRpc"/>. Non-server clients
    /// (owner included — their firers are disabled) reconstruct a zero-damage
    /// projectile through their local <see cref="ProjectileWorld"/> so they
    /// see the tracer + muzzle flash + audio at server-echo latency. This is
    /// what closes the "ugly" gap Phase 1 explicitly accepted.
    /// </para>
    /// <para>
    /// <b>FireCommand honest scope.</b> The existing fire path (server's
    /// <see cref="NetworkInputSource"/> feeding the chassis's own firers) is
    /// what actually triggers projectile spawn. The
    /// <see cref="FireCommandServerRpc"/> is an <em>observation</em> channel:
    /// it surfaces the cooldown check and increments a counter for telemetry;
    /// it does not currently gate the fire (the per-block <c>_nextFireTime</c>
    /// on the firer already enforces the rate limit). Phase 4+ can promote
    /// this to a true gate once per-block fire commands replace the held-input
    /// fire model.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobotCombat : NetworkBehaviour
    {
        // Shortest legitimate fire interval across current weapon types
        // (SMG at 12 fires/sec is the floor). Phase 4+ refines to per-block.
        private const float MinFireInterval = 1f / 12f;

        // Owner re-fires FireCommandServerRpc at SMG cadence while held so
        // the server's cooldown table gets a TryAccept per intended shot.
        // (Less often → cooldown never triggers; more often → legit play
        // gets false-rejected.)
        private const float OwnerHeldFireRpcInterval = MinFireInterval;

        private NetworkRobot _net;

        // Server-side state.
        private readonly FireCooldownTable _cooldown = new();
        private bool _serverSubscribed;

        // Owner-side state (non-server owner only).
        private IInputSource _ownerInput;
        private float _nextOwnerFireRpcTime;

        /// <summary>Server-only counter. Increments on a rejected
        /// <see cref="FireCommandServerRpc"/>. Reads zero on non-server peers.</summary>
        public int RejectedFireCount => _cooldown.RejectedCount;

        private void Awake() => _net = GetComponent<NetworkRobot>();

        public override void OnNetworkSpawn() => _net.WhenBuilt(OnChassisBuilt);

        public override void OnNetworkDespawn()
        {
            if (_serverSubscribed)
            {
                ProjectileWorld.Spawned -= OnServerProjectileSpawned;
                _serverSubscribed = false;
            }
            _ownerInput = null;
            _cooldown.Reset();
        }

        private void OnChassisBuilt(NetworkRobot _)
        {
            if (IsServer)
            {
                // Server: subscribe to projectile spawns so we can fan out
                // cosmetic events to every non-server peer. The Spawned event
                // is global; we filter to our own owner inside the handler.
                ProjectileWorld.Spawned += OnServerProjectileSpawned;
                _serverSubscribed = true;
                return;
            }

            // Every non-server copy: silence the firers so the client cannot
            // spawn an authoritative projectile or compute a hit. (Phase-1
            // behaviour, unchanged.)
            foreach (ProjectileGun gun in GetComponentsInChildren<ProjectileGun>(true))
                gun.enabled = false;
            foreach (CannonBlock cannon in GetComponentsInChildren<CannonBlock>(true))
                cannon.enabled = false;
            foreach (BombBayBlock bay in GetComponentsInChildren<BombBayBlock>(true))
                bay.enabled = false;

            // Owner non-server: cache the local input source so Update can
            // dispatch FireCommandServerRpc on rising-edge / held fire.
            if (IsOwner)
                _ownerInput = GetComponentInChildren<IInputSource>(includeInactive: true);
        }

        // -----------------------------------------------------------------
        // Owner-side: dispatch FireCommandServerRpc while fire is intended
        // -----------------------------------------------------------------

        private void Update()
        {
            if (!IsOwner || IsServer || _ownerInput == null) return;

            // While the owner holds fire, send commands at the SMG fire rate
            // so the server validates one per intended shot. Released → reset
            // the cadence gate so the next press fires immediately.
            if (_ownerInput.FireHeld)
            {
                if (Time.time >= _nextOwnerFireRpcTime)
                {
                    FireCommand cmd = new FireCommand
                    {
                        Tick = (uint)NetworkManager.LocalTime.Tick,
                        AimDir = Vector3.forward,        // Phase-3 wires real aim
                        MuzzlePos = transform.position,  // ditto
                    };
                    FireCommandServerRpc(cmd);
                    _nextOwnerFireRpcTime = Time.time + OwnerHeldFireRpcInterval;
                }
            }
            else
            {
                _nextOwnerFireRpcTime = 0f;
            }
        }

        [ServerRpc]
        private void FireCommandServerRpc(FireCommand cmd)
        {
            // Phase-2 validation: cooldown only. Aim-bounds is stubbed.
            Robogame.Robots.Robot owner = _net.Handle != null ? _net.Handle.Robot : null;
            if (owner == null || owner.IsDestroyed) return;

            // One coarse chassis-wide key today (Vector3Int.zero). The
            // per-position helper is here for the per-block Phase 4 refactor.
            if (!_cooldown.TryAccept(Vector3Int.zero, Time.time, MinFireInterval))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning(
                    $"[NetworkRobotCombat] Rejected FireCommand from client " +
                    $"{OwnerClientId} (cooldown breach, count={_cooldown.RejectedCount}).");
#endif
            }
        }

        // -----------------------------------------------------------------
        // Server-side: fan out cosmetic ProjectileSpawnEvent on own spawns
        // -----------------------------------------------------------------

        private void OnServerProjectileSpawned(ProjectileSpec spec)
        {
            // Filter to this robot's shots. ProjectileWorld.Spawned is a
            // global event — every NetworkRobotCombat subscribes; the owner-
            // equality check fans out only the relevant subset.
            if (_net.Handle == null || spec.Owner != _net.Handle.Robot) return;

            ProjectileSpawnPayload payload = new ProjectileSpawnPayload
            {
                Kind = spec.Kind,
                Origin = spec.Origin,
                InitialVelocity = spec.InitialVelocity,
                GravityWorld = spec.GravityWorld,
                MaxLifetime = spec.MaxLifetime,
                CastRadius = spec.CastRadius,
                VisualMeshDiameter = spec.VisualMeshDiameter,
                VisualTint = spec.VisualTint,
                HitMask = spec.HitMask.value,
            };
            ProjectileSpawnEventClientRpc(payload);
        }

        [ClientRpc]
        private void ProjectileSpawnEventClientRpc(ProjectileSpawnPayload payload)
        {
            // Server already owns the real projectile (visible to the host
            // by rendering its own scene). Skip the cosmetic there.
            if (IsServer) return;

            // Cosmetic spec: zero damage, no splash. Owner = local Robot ref
            // so the cosmetic tracer's own-chassis filter matches the server.
            Robogame.Robots.Robot ownerRobot = _net.Handle != null ? _net.Handle.Robot : null;
            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = payload.Kind,
                Origin = payload.Origin,
                InitialVelocity = payload.InitialVelocity,
                GravityWorld = payload.GravityWorld,
                MaxLifetime = payload.MaxLifetime,
                CastRadius = payload.CastRadius,
                Damage = 0f,
                SplashRings = null,
                SplashRadius = 0f,
                HitMask = payload.HitMask,
                Owner = ownerRobot,
                ShowTrail = payload.Kind == ProjectileKind.SmgPellet,
                ShowMesh = payload.Kind != ProjectileKind.SmgPellet,
                VisualTint = payload.VisualTint,
                VisualMeshDiameter = payload.VisualMeshDiameter,
                ImpactAudioOverride = AudioCue.ProjectileImpact,
            };
            ProjectileWorld.Spawn(in spec);

            // Muzzle flash + audio at the muzzle so the owner sees their
            // shot (their local firers are disabled) and observers hear it
            // alongside the visual.
            Vector3 dir = payload.InitialVelocity.sqrMagnitude > 1e-5f
                ? payload.InitialVelocity.normalized
                : Vector3.forward;
            float flashScale = payload.Kind == ProjectileKind.Cannonball ? 2.0f : 1.0f;
            VfxSpawner.Spawn(VfxKind.MuzzleFlash, payload.Origin, dir, flashScale);
            AudioCue fireCue = payload.Kind switch
            {
                ProjectileKind.Cannonball => AudioCue.WeaponFireCannon,
                _                         => AudioCue.WeaponFire,
            };
            AudioRouter.PlayOneShot(fireCue, payload.Origin);
        }
    }
}
