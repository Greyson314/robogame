using System.Collections.Generic;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
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
    /// <see cref="FireCooldownTable"/> and the aim delta (deliberately
    /// loose at <see cref="MaxAimDeltaDeg"/> degrees per accepted command
    /// — catches teleporting-aim hacks without false-positives for skilled
    /// tracking; Phase 6 lag-comp can tighten with a server-side aim-at-
    /// time-T record). Both cooldown- and aim-rejections increment
    /// <see cref="RejectedFireCount"/>. Owner-side aim is sampled from
    /// <see cref="RobotDrive.AimPoint"/>, the same point the chassis
    /// firers use locally.
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

        /// <summary>Max permitted angle between consecutive accepted aim
        /// directions in degrees. Phase 4 anti-cheat hardening — rejects
        /// only impossible-aim-jump cases. At ~12 fires/sec a human tracking
        /// across the full FOV shifts &lt; 60°/interval, so 90° is a safety
        /// margin with effectively zero false positives. Phase 6 lag-comp
        /// can tighten this once aim-at-time-T is a server-side record.</summary>
        public const float MaxAimDeltaDeg = 90f;

        private NetworkRobot _net;

        // Server-side state.
        private readonly FireCooldownTable _cooldown = new();
        private bool _serverSubscribed;
        private Vector3 _lastValidatedAimDir;
        private bool _haveLastValidatedAim;
        private int _aimRejectedCount;
        // Phase 6 lag-comp (telemetry-only — NETCODE_PLAN §9 variant C).
        private LagCompHistory _lagComp;
        private readonly List<(ulong, RobotBoundsSnapshot)> _lagCompQuery = new(16);
        private int _lagCompTelemetryHits;

        // Owner-side state (non-server owner only).
        private IInputSource _ownerInput;
        private RobotDrive _ownerDrive;
        private float _nextOwnerFireRpcTime;

        /// <summary>Server-only counter. Increments on a rejected
        /// <see cref="FireCommandServerRpc"/> — cooldown OR aim-bounds.
        /// Reads zero on non-server peers.</summary>
        public int RejectedFireCount => _cooldown.RejectedCount + _aimRejectedCount;

        /// <summary>Phase-6 telemetry counter — number of FireCommands where
        /// the bounding-volume lag-comp check confirmed a hit against any
        /// remote robot at the shooter's claimed tick. Currently
        /// observational only (no damage applied); diagnoses "I shot them,
        /// why didn't it land?" complaints under high RTT.</summary>
        public int LagCompTelemetryHitCount => _lagCompTelemetryHits;

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
                // Phase 6: attach lag-comp history with the chassis bounding
                // sphere. Radius is half-block-padded from the furthest
                // blueprint cell — the chassis is rigid, so this is correct
                // for the lifetime of the robot (block detachment only
                // shrinks the convex hull, and over-cover is the safe side
                // for variant C bounding-volume tests).
                _lagComp = GetComponent<LagCompHistory>();
                if (_lagComp == null) _lagComp = gameObject.AddComponent<LagCompHistory>();
                _lagComp.SetChassisBounds(ComputeChassisRadius());
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
            {
                _ownerInput = GetComponentInChildren<IInputSource>(includeInactive: true);
                _ownerDrive = GetComponentInChildren<RobotDrive>(includeInactive: true);
            }
        }

        // Chassis sphere radius — distance from chassis origin to the
        // furthest blueprint cell, plus half a cell for block-edge cover.
        // Run once at chassis build; not per-tick.
        private float ComputeChassisRadius()
        {
            if (_net == null || _net.Handle == null || _net.Handle.Blueprint == null)
                return 1f;
            ChassisBlueprint.Entry[] entries = _net.Handle.Blueprint.Entries;
            float maxDistSq = 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                Vector3 p = entries[i].Position;
                float d = p.sqrMagnitude;
                if (d > maxDistSq) maxDistSq = d;
            }
            return Mathf.Sqrt(maxDistSq) + 0.5f;
        }

        // -----------------------------------------------------------------
        // Server-side: sample chassis pose every physics tick for lag-comp
        // -----------------------------------------------------------------

        private void FixedUpdate()
        {
            if (!IsServer || _lagComp == null) return;
            uint tick = NetworkManager.Singleton != null
                ? (uint)NetworkManager.Singleton.LocalTime.Tick
                : 0u;
            _lagComp.Sample(transform.position, tick);
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
                        AimDir = ComputeOwnerAimDir(),
                        MuzzlePos = transform.position,
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

        private Vector3 ComputeOwnerAimDir()
        {
            // Sample the same aim point the chassis firers locally use, so
            // the server's bounds check sees a vector that's coherent with
            // the player's intent. Falls back to chassis-forward if the
            // drive isn't built yet (early-spawn frame) or if the aim point
            // degenerates onto the muzzle.
            if (_ownerDrive == null) return transform.forward;
            Vector3 delta = _ownerDrive.AimPoint - transform.position;
            return delta.sqrMagnitude > 1e-6f ? delta.normalized : transform.forward;
        }

        [ServerRpc]
        private void FireCommandServerRpc(FireCommand cmd)
        {
            // Phase 4 validation order: wire-packet checks (aim) first —
            // these are stateless and worth running even pre-chassis-spawn
            // so a client spamming bogus aim packets logs as rejected.
            // Then the gameplay-state check (owner null / destroyed) and
            // finally the cooldown. Both reject paths increment
            // RejectedFireCount.
            if (!ValidateAim(in cmd))
            {
                _aimRejectedCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning(
                    $"[NetworkRobotCombat] Rejected FireCommand from client " +
                    $"{OwnerClientId} (aim delta > {MaxAimDeltaDeg}°, count={_aimRejectedCount}).");
#endif
                return;
            }

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
                return;
            }

            // Phase 6 — lag-comp telemetry (variant C, observational only).
            RunLagCompTelemetry(in cmd);
        }

        // Phase 6 — bounding-volume rewind, observational only (no damage).
        // Slow projectile weapons (SMG ≈ 80 m/s) keep their leadable feel;
        // ProjectileWorld's live sweep stays authoritative. This logs when
        // lag-comp would have called a hit the live sweep is about to miss —
        // a diagnostic for "I shot them, why didn't it land?" at high RTT.
        private void RunLagCompTelemetry(in FireCommand cmd)
        {
            // Host's own shots have zero RTT — rewinding would only re-derive
            // the live state. Skip to keep the telemetry log focused on the
            // remote-client cases where lag-comp is meaningful.
            if (NetworkManager.Singleton != null &&
                OwnerClientId == NetworkManager.Singleton.LocalClientId) return;

            _lagCompQuery.Clear();
            LagCompRegistry.QueryAll(cmd.Tick, _lagCompQuery);
            if (_lagCompQuery.Count == 0) return;

            Vector3 origin = cmd.MuzzlePos;
            Vector3 dir = cmd.AimDir.sqrMagnitude > 1e-6f ? cmd.AimDir.normalized : Vector3.forward;

            ulong ownId = _net != null && _net.NetworkObject != null
                ? _net.NetworkObject.NetworkObjectId
                : ulong.MaxValue;

            ulong bestId = 0;
            float bestT = float.MaxValue;
            for (int i = 0; i < _lagCompQuery.Count; i++)
            {
                (ulong id, RobotBoundsSnapshot snap) = _lagCompQuery[i];
                if (id == ownId) continue;
                if (snap.Radius <= 0f) continue;
                if (TryRaySphere(origin, dir, snap.Pos, snap.Radius, out float t) &&
                    t < bestT && t <= MaxLagCompRangeMetres)
                {
                    bestT = t;
                    bestId = id;
                }
            }
            if (bestT < float.MaxValue)
            {
                _lagCompTelemetryHits++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log(
                    $"[NetworkRobotCombat] Lag-comp telemetry: shooter " +
                    $"{OwnerClientId}, target NetworkObject {bestId} at " +
                    $"t={bestT:F2}m on tick {cmd.Tick}. " +
                    $"(Observational; no damage applied. count={_lagCompTelemetryHits}).");
#endif
            }
        }

        // Conservative upper bound for ray-vs-sphere intersection distance.
        // SMG pellets at 80 m/s × MaxLifetime cap < 800 m; anything beyond
        // is geometrically implausible.
        private const float MaxLagCompRangeMetres = 800f;

        private static bool TryRaySphere(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float t)
        {
            // Standard analytic ray-vs-sphere. Returns the nearest non-negative
            // intersection parameter, or false if the ray points away or misses.
            t = 0f;
            Vector3 L = center - origin;
            float tca = Vector3.Dot(L, dir);
            if (tca < 0f) return false;
            float d2 = Vector3.Dot(L, L) - tca * tca;
            float r2 = radius * radius;
            if (d2 > r2) return false;
            float thc = Mathf.Sqrt(r2 - d2);
            float t0 = tca - thc;
            t = t0 < 0f ? tca + thc : t0;
            return t >= 0f;
        }

        // Internal hook for tests — bypasses NGO RPC routing so EditMode
        // tests can drive the validator directly. Exposed via the public
        // ServerValidateAim entry point below.
        private bool ValidateAim(in FireCommand cmd)
        {
            // Degenerate / zero AimDir: accept (we don't want a bad sample
            // from an early frame to trigger a reject cascade).
            float sq = cmd.AimDir.sqrMagnitude;
            if (sq < 1e-6f) return true;

            Vector3 cur = cmd.AimDir.normalized;
            if (!_haveLastValidatedAim)
            {
                _lastValidatedAimDir = cur;
                _haveLastValidatedAim = true;
                return true;
            }

            float angle = Vector3.Angle(_lastValidatedAimDir, cur);
            if (angle > MaxAimDeltaDeg) return false;

            _lastValidatedAimDir = cur;
            return true;
        }

        /// <summary>Server-side validator entry point exposed for tests.
        /// Bypasses NGO routing — returns true if <paramref name="cmd"/>
        /// would have passed the aim-bounds gate and updates the last-
        /// validated direction; false otherwise (RejectedFireCount remains
        /// unchanged unless you call through <see cref="ServerProcessFireCommand"/>
        /// instead).</summary>
        public bool ServerValidateAim(in FireCommand cmd) => ValidateAim(in cmd);

        /// <summary>Server-side wrapper around the full RPC body — exists so
        /// EditMode tests can run the validator + counter increment path
        /// without standing up a NetworkManager. Wire-packet validation
        /// (aim) runs first regardless of chassis state, matching the
        /// FireCommandServerRpc ordering exactly.</summary>
        public void ServerProcessFireCommand(in FireCommand cmd)
        {
            if (!ValidateAim(in cmd)) { _aimRejectedCount++; return; }
            Robogame.Robots.Robot owner = _net != null && _net.Handle != null ? _net.Handle.Robot : null;
            if (owner == null || owner.IsDestroyed) return;
            _cooldown.TryAccept(Vector3Int.zero, Time.time, MinFireInterval);
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
