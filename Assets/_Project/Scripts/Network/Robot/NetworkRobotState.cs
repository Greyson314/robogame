using Robogame.Block;
using Unity.Netcode;
using UnityEngine;
using GameRobot = Robogame.Robots.Robot;

namespace Robogame.Network.Robot
{
    /// <summary>Coarse damage state replicated for every robot. Visual
    /// damage / death reads this; it never carries per-graze HP.</summary>
    public enum RobotHealthTier : byte
    {
        Full = 0,     // mass fraction > 0.75
        Cracked = 1,  // 0.50 < fraction <= 0.75
        Critical = 2, // 0    < fraction <= 0.50
        Dead = 3,     // robot destroyed
    }

    /// <summary>
    /// Replicates aggregate alive/dead + a four-tier health band
    /// (NETCODE_PLAN §6 Bucket D / §7 open-question). The server recomputes
    /// the tier from the authoritative <c>Robot</c> mass on each block
    /// removal and writes the <see cref="NetworkVariable{T}"/> <em>only on a
    /// tier boundary crossing</em> — a graze that doesn't change the band
    /// costs zero wire traffic. Per-block destruction itself rides
    /// <c>NetworkBlockGrid</c> (Step 6); this is the cheap summary any
    /// joining client / HUD / AI reads without replaying every hit.
    /// </summary>
    /// <remarks>
    /// Thresholds (0.75 / 0.50 / dead) are an architect decision
    /// (handoff §5.2) baked as constants — changing them is a wire-contract
    /// change. The server hooks gameplay events via
    /// <see cref="NetworkRobot.WhenBuilt"/> because the chassis does not
    /// exist at <c>OnNetworkSpawn</c> time.
    /// </remarks>
    [RequireComponent(typeof(NetworkRobot))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobotState : NetworkBehaviour
    {
        private const float FullThreshold = 0.75f;
        private const float CrackedThreshold = 0.50f;

        private readonly NetworkVariable<byte> _tier =
            new((byte)RobotHealthTier.Full);
        private readonly NetworkVariable<bool> _isAlive =
            new(true);

        private NetworkRobot _net;
        private GameRobot _robot;
        private BlockGrid _grid;
        private bool _hooked;

        /// <summary>Replicated health band. Authoritative on the server,
        /// read-only mirror on clients.</summary>
        public RobotHealthTier Tier => (RobotHealthTier)_tier.Value;

        /// <summary>Replicated alive flag (false once the robot dies).</summary>
        public bool IsAlive => _isAlive.Value;

        /// <summary>Fires on every machine when the replicated tier changes
        /// — the hook a future damage-VFX / HUD consumer subscribes to.</summary>
        public event System.Action<RobotHealthTier> TierChanged;

        private void Awake() => _net = GetComponent<NetworkRobot>();

        public override void OnNetworkSpawn()
        {
            _tier.OnValueChanged += HandleTierReplicated;
            // Server owns the computation; it hooks once the chassis exists.
            if (IsServer) _net.WhenBuilt(_ => HookServer());
        }

        public override void OnNetworkDespawn()
        {
            _tier.OnValueChanged -= HandleTierReplicated;
            if (_hooked && _grid != null) _grid.BlockRemoving -= HandleBlockRemoving;
            if (_hooked && _robot != null) _robot.Destroyed -= HandleRobotDestroyed;
            _hooked = false;
        }

        private void HookServer()
        {
            if (_hooked || _net.Handle == null) return;
            _robot = _net.Handle.Robot;
            _grid = _net.Handle.Grid;
            if (_robot == null || _grid == null) return;

            _grid.BlockRemoving += HandleBlockRemoving;
            _robot.Destroyed += HandleRobotDestroyed;
            _hooked = true;
            RecomputeTier(); // baseline (= Full unless spawned pre-damaged)
        }

        // Robot's own BlockRemoving handler runs first (subscribed earlier
        // in its OnEnable), so TotalBlockMass is already decremented here.
        private void HandleBlockRemoving(BlockBehaviour _) => RecomputeTier();

        private void HandleRobotDestroyed(GameRobot _)
        {
            if (!IsServer) return;
            _isAlive.Value = false;
            SetTier(RobotHealthTier.Dead);
        }

        private void RecomputeTier()
        {
            if (!IsServer || _robot == null) return;
            if (_robot.IsDestroyed) { SetTier(RobotHealthTier.Dead); return; }

            float frac = _robot.InitialBlockMass > 0f
                ? _robot.TotalBlockMass / _robot.InitialBlockMass
                : 0f;

            RobotHealthTier next =
                frac > FullThreshold ? RobotHealthTier.Full :
                frac > CrackedThreshold ? RobotHealthTier.Cracked :
                frac > 0f ? RobotHealthTier.Critical :
                RobotHealthTier.Dead;

            SetTier(next);
        }

        // Writes only on a real boundary crossing — no per-graze traffic.
        private void SetTier(RobotHealthTier next)
        {
            if (_tier.Value == (byte)next) return;
            _tier.Value = (byte)next;
        }

        private void HandleTierReplicated(byte _, byte now)
            => TierChanged?.Invoke((RobotHealthTier)now);
    }
}
