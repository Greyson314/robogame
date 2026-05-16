using System.Collections.Generic;
using Robogame.Core;
using Robogame.Robots;
using Robogame.Voxel;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Phase 5 visual demo: a surface-crawling AI that uses an
    /// <see cref="OccupancyGrid"/> + A* to chase the player across
    /// voxel terrain. As the player drills tunnels, the bot's path
    /// refresh picks them up and routes through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Minimal AI shape — the goal here is "Phase 5 visual playtest
    /// gate is green," not "ship a real combat enemy." The bot moves
    /// at a constant walking pace along the A* waypoint list, refreshes
    /// the path every <see cref="_pathRefreshInterval"/> seconds, and
    /// fails closed (stops) when no path exists (e.g., target is
    /// unreachable because the player hasn't dug a connecting tunnel
    /// yet). Movement is kinematic — no Rigidbody — so the bot doesn't
    /// participate in collision damage or compete with the chassis
    /// physics for solver attention.
    /// </para>
    /// <para>
    /// Spawning is the caller's responsibility (e.g.
    /// <c>EnvironmentBuilder</c>). The bot resolves its own DigZone via
    /// <see cref="DigField.ZoneAt"/> at spawn position; if the zone or
    /// target Robot can't be resolved, the bot retries on each
    /// <c>FixedUpdate</c> until both are present.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VoxelChaserBot : MonoBehaviour
    {
        [Tooltip("World-space walking speed in m/s.")]
        [SerializeField, Min(0.1f)] private float _walkSpeed = 2.0f;

        [Tooltip("Seconds between A* re-runs. Lower = more responsive to player movement / new tunnels; " +
                 "higher = cheaper. 0.5s keeps a 50-KB-grid A* well under 1ms even with linear-scan open list.")]
        [SerializeField, Min(0.05f)] private float _pathRefreshInterval = 0.5f;

        [Tooltip("Distance at which the bot considers a waypoint reached and advances to the next one (metres).")]
        [SerializeField, Min(0.1f)] private float _waypointReachedDistance = 0.8f;

        [Tooltip("If true, the bot is allowed to traverse OpenNoFloor cells (flying AI). " +
                 "Default false: ground bot, sticks to OpenWithFloor cells only.")]
        [SerializeField] private bool _allowFlying = false;

        [Tooltip("A* neighbour topology. Cardinal6 keeps ground-bot paths grid-aligned; " +
                 "Full26 lets the bot cut corners diagonally.")]
        [SerializeField] private OccupancyConnectivity _connectivity = OccupancyConnectivity.Cardinal6;

        private DigZone _zone;
        private OccupancyGrid _grid;
        private Transform _target;
        private readonly List<Vector3Int> _path = new(64);
        private int _pathIndex;
        private float _nextRefreshTime;
        // Track whether the bot currently has a valid path so we can fire
        // BotDetected exactly once at the no-path → path edge instead of
        // every refresh tick.
        private bool _hadPathLastRefresh;

        // Test-friendly setters — let PlayMode tests inject a specific
        // zone + target instead of relying on the scene-search heuristics
        // below.
        public void BindZone(DigZone zone)
        {
            _zone = zone;
            _grid = zone != null ? zone.OccupancyGrid : null;
        }

        public void BindTarget(Transform target) => _target = target;

        public bool HasPath => _path.Count > 0 && _pathIndex < _path.Count;
        public int PathLength => _path.Count;
        public Transform Target => _target;
        public DigZone Zone => _zone;
        public OccupancyGrid Grid => _grid;

        private void OnEnable()
        {
            if (_zone == null) ResolveZone();
            if (_target == null) ResolveTarget();
        }

        private void ResolveZone()
        {
            IDigZone iz = DigField.ZoneAt(transform.position);
            _zone = iz as DigZone;
            _grid = _zone != null ? _zone.OccupancyGrid : null;
        }

        private void ResolveTarget()
        {
            // Pick the first Robot in scene that isn't this bot. For the
            // 1-bot + 1-player Phase 5 demo this is sufficient; a real
            // AI service would let the bot's target be authored or
            // assigned by a director.
            Robot[] robots = FindObjectsByType<Robot>(FindObjectsSortMode.None);
            for (int i = 0; i < robots.Length; i++)
            {
                if (robots[i] == null) continue;
                if (robots[i].gameObject == gameObject) continue;
                _target = robots[i].transform;
                return;
            }
        }

        private void FixedUpdate()
        {
            if (_grid == null) { ResolveZone(); return; }
            if (_target == null) { ResolveTarget(); return; }

            if (Time.time >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.time + _pathRefreshInterval;
                RefreshPath();
            }

            FollowPath();
        }

        /// <summary>
        /// Re-run A* from the bot's current grid cell to the target's
        /// grid cell. Public for tests + future debug overlays.
        /// </summary>
        public bool RefreshPath()
        {
            if (_grid == null || _target == null) return false;
            Vector3Int from = _grid.WorldToGrid(transform.position);
            Vector3Int to = _grid.WorldToGrid(_target.position);
            bool found = _grid.TryFindPath(from, to, _connectivity, _allowFlying, _path);
            _pathIndex = 0;
            // BotDetected: fire on the no-path → path edge. This is the
            // gameplay moment — the bot just acquired a route to the
            // player, e.g., because the player drilled a connecting
            // tunnel. The cue is missing-by-default per AUDIO_PLAN's
            // declared-then-authored pipeline; the no-clip warning
            // surfaces it for the audio pass.
            if (found && !_hadPathLastRefresh)
            {
                AudioRouter.PlayOneShot(AudioCue.BotDetected, transform.position);
            }
            _hadPathLastRefresh = found;
            return found;
        }

        private void FollowPath()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count) return;
            Vector3 waypointWorld = _grid.GridToWorld(_path[_pathIndex]);
            Vector3 delta = waypointWorld - transform.position;
            float sqr = delta.sqrMagnitude;
            if (sqr < _waypointReachedDistance * _waypointReachedDistance)
            {
                AdvanceWaypoint();
                return;
            }
            // Step toward the waypoint at a fixed speed. Don't normalise
            // when the remaining distance is shorter than one step's
            // worth — otherwise the bot overshoots and jitters.
            float maxStep = _walkSpeed * Time.fixedDeltaTime;
            if (sqr <= maxStep * maxStep)
            {
                transform.position = waypointWorld;
                AdvanceWaypoint();
            }
            else
            {
                transform.position += delta.normalized * maxStep;
            }
        }

        // Footstep cue + minor dust puff on every other waypoint, so a
        // long path doesn't spam the audio mixer or VFX pool. The cue is
        // declared but unmapped per AUDIO_PLAN's missing-clip path; VFX
        // reuses DebrisDust at low scale until a dedicated kind lands.
        private void AdvanceWaypoint()
        {
            _pathIndex++;
            if ((_pathIndex & 1) == 0)
            {
                AudioRouter.PlayOneShot(AudioCue.BotStep, transform.position);
                VfxSpawner.Spawn(VfxKind.DebrisDust, transform.position, Quaternion.identity, scale: 0.25f);
            }
        }
    }
}
