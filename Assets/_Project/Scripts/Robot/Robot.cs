using System;
using System.Collections;
using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Robots
{
    /// <summary>
    /// Root component on a robot GameObject. Owns the rigidbody and the block grid,
    /// tracks aggregate stats, runs connectivity checks when blocks die, and raises
    /// a single <see cref="Destroyed"/> event when the robot is no longer functional.
    /// </summary>
    /// <remarks>
    /// "No longer functional" matches the Robocraft rule of thumb: the CPU is gone,
    /// or enough mass has been shed that the robot can't fight (default 75%).
    /// Connectivity: when a block is removed, every block must still be reachable
    /// from the CPU through the 6-axis block graph; orphans are detached as debris.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class Robot : MonoBehaviour
    {
        [Tooltip("Fraction of starting block mass that, once lost, marks the robot destroyed. " +
                 "Robocraft used roughly 75%.")]
        [SerializeField, Range(0.1f, 1f)] private float _massLossDestroyThreshold = 0.75f;

        [Tooltip("If true, blocks that lose connectivity to the CPU are detached as physics " +
                 "debris instead of being silently destroyed.")]
        [SerializeField] private bool _spawnDetachedDebris = true;

        [Tooltip("Seconds before detached debris despawns.")]
        [SerializeField, Min(0f)] private float _debrisLifetime = 4f;

        private Rigidbody _rb;
        private BlockGrid _grid;

        public Rigidbody Rigidbody => _rb;
        public BlockGrid Grid => _grid;

        public int TotalCpu { get; private set; }
        public float TotalBlockMass { get; private set; }
        public int BlockCount { get; private set; }

        public float InitialBlockMass { get; private set; }
        public int InitialBlockCount { get; private set; }
        public BlockBehaviour CpuBlock { get; private set; }

        public bool IsDestroyed { get; private set; }

        /// <summary>
        /// Scrap currency carried by this chassis. Incremented when a
        /// <c>ScrapPickup</c> is collected; decremented to zero when a
        /// <c>ScrapDepot</c> banks the load. Drives the scrap-based match
        /// score: kills produce drops, drops get carried, depots bank.
        /// </summary>
        public int ScrapHeld { get; private set; }

        /// <summary>
        /// Maximum scrap this chassis can carry at once. Above the cap,
        /// pickups stay in the world — forcing the player to deposit
        /// before resuming hoarding. Set by <c>MatchConfig.ScrapCarryCapacity</c>
        /// via <see cref="ConfigureScrap"/>; default falls back to a
        /// sensible standalone value so tests don't need a MatchConfig.
        /// </summary>
        public int ScrapCarryCapacity { get; private set; } = 8;

        /// <summary>True if <see cref="ScrapHeld"/> is at <see cref="ScrapCarryCapacity"/>.</summary>
        public bool IsScrapFull => ScrapHeld >= ScrapCarryCapacity;

        /// <summary>
        /// Team this chassis belongs to. <see cref="TeamId.None"/> = neutral
        /// (training dummies, props). Set by the gameplay layer at
        /// registration time; consumed by friendly-fire filters
        /// (<c>ProjectileWorld</c>) and the depot grinder hazard
        /// (<c>ScrapDepot</c>).
        /// </summary>
        public TeamId Team { get; private set; } = TeamId.None;

        /// <summary>
        /// Push a team identity onto this chassis. Called by
        /// <c>ArenaController.RegisterChassis</c> right after the per-side
        /// registry entry lands so server-authoritative team data flows
        /// through one path. Idempotent — safe to call repeatedly with the
        /// same value.
        /// </summary>
        public void ConfigureTeam(TeamId team) { Team = team; }

        /// <summary>
        /// Raised after <see cref="ScrapHeld"/> changes (positive or
        /// negative delta). Args: this Robot, the signed delta. Negative
        /// deltas indicate deposits or losses. Subscribers (HUD, match
        /// score, etc.) read the totals off the Robot directly.
        /// </summary>
        public event Action<Robot, int> ScrapAwarded;

        /// <summary>
        /// Apply the match's scrap tuning to this chassis. Called by
        /// <c>ArenaController</c> after the Robot is spawned and the
        /// MatchController is built — keeps tuning in one place
        /// (MatchConfig) without giving every Robot a backref to the
        /// match controller.
        /// </summary>
        public void ConfigureScrap(int carryCapacity)
        {
            if (carryCapacity > 0) ScrapCarryCapacity = carryCapacity;
        }

        /// <summary>
        /// Movement-speed multiplier driven by carried scrap. Drives the
        /// "haul slows you down" gameplay — encourages the player to
        /// commit to a depot run rather than sit on a giant hoard.
        /// Stepped curve per <c>docs/SCRAP_LOOP_PLAN.md §3</c>:
        /// 0–2 = 1.00, 3–5 = 0.95, 6–9 = 0.85, 10+ = 0.70. Const-time;
        /// safe to call per-tick.
        /// </summary>
        public float CarryWeightMoveMultiplier => ComputeMoveMultiplier(ScrapHeld);

        /// <summary>Static carry-weight curve. Exposed for tests + diagnostic HUDs.</summary>
        public static float ComputeMoveMultiplier(int scrapHeld)
        {
            if (scrapHeld <= 2) return 1.00f;
            if (scrapHeld <= 5) return 0.95f;
            if (scrapHeld <= 9) return 0.85f;
            return 0.70f;
        }

        /// <summary>
        /// Try to award <paramref name="amount"/> scrap. Respects
        /// <see cref="ScrapCarryCapacity"/>: if the chassis is already
        /// full, returns 0; if a partial fit is available, awards exactly
        /// as much as remains under the cap and returns that amount.
        /// Returns the amount actually awarded.
        /// </summary>
        /// <remarks>
        /// Pickups call this and only despawn themselves if the return
        /// value equals their pickup value — partial pickups bank what
        /// fits and leave the remainder in the world.
        /// </remarks>
        public int TryAwardScrap(int amount)
        {
            if (amount <= 0) return 0;
            int room = ScrapCarryCapacity - ScrapHeld;
            if (room <= 0) return 0;
            int awarded = amount < room ? amount : room;
            ScrapHeld += awarded;
            ScrapAwarded?.Invoke(this, awarded);
            return awarded;
        }

        /// <summary>
        /// Drain all carried scrap, returning the amount banked. Used by
        /// <c>ScrapDepot</c> when the chassis enters its trigger. Fires
        /// <see cref="ScrapAwarded"/> with the negative delta so HUDs
        /// update without polling.
        /// </summary>
        public int DepositScrap()
        {
            int amount = ScrapHeld;
            if (amount <= 0) return 0;
            ScrapHeld = 0;
            ScrapAwarded?.Invoke(this, -amount);
            return amount;
        }

        /// <summary>
        /// The blueprint this chassis was built from, stashed by
        /// <c>ChassisFactory.Build</c> at spawn time. Consumed by repair-style
        /// systems (e.g. <c>RepairPad</c>) that need to know which cells are
        /// "supposed to be there" so they can re-place destroyed blocks.
        /// </summary>
        /// <remarks>
        /// Reference, not a copy: the same ScriptableObject the player edited
        /// in the garage. Repair callers must not mutate it. Null on chassis
        /// built outside the factory pipeline (legacy editor scaffolds, tests).
        /// </remarks>
        public Block.ChassisBlueprint Blueprint { get; set; }

        /// <summary>
        /// The block definition library that paired with <see cref="Blueprint"/>
        /// at spawn. Repair systems need this to resolve a blueprint entry's
        /// <c>BlockId</c> back to a definition for re-placement.
        /// </summary>
        public Block.BlockDefinitionLibrary Library { get; set; }

        /// <summary>Raised exactly once when the robot transitions to destroyed.</summary>
        public event Action<Robot> Destroyed;

        /// <summary>Static: raised whenever a robot is recreated via <see cref="RebuildByName"/>.</summary>
        public static event Action<Robot> Rebuilt;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grid = GetComponent<BlockGrid>();
        }

        private void OnEnable()
        {
            if (_grid != null)
            {
                _grid.BlockPlaced += HandleBlockPlaced;
                _grid.BlockRemoving += HandleBlockRemoving;
            }
        }

        private void OnDisable()
        {
            if (_grid != null)
            {
                _grid.BlockPlaced -= HandleBlockPlaced;
                _grid.BlockRemoving -= HandleBlockRemoving;
            }
        }

        private void Start()
        {
            RecalculateAggregates();
            InitialBlockMass = TotalBlockMass;
            InitialBlockCount = BlockCount;
            CpuBlock = FindCpuBlock();
            CaptureTemplate();
        }

        // -----------------------------------------------------------------
        // Snapshot / runtime rebuild
        // -----------------------------------------------------------------

        // Static registry: name -> hidden, disabled clone captured at Start.
        // The shell can fully self-destruct on death; rebuild instantiates a
        // fresh copy from this template so callers don't need any setup code.
        private static readonly Dictionary<string, GameObject> s_templates = new();

        private string _registryKey;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _isTemplate;

        private void CaptureTemplate()
        {
            if (_isTemplate) return; // never re-snapshot a template

            _registryKey = name;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            // Drop any prior template for this key.
            if (s_templates.TryGetValue(_registryKey, out GameObject prior) && prior != null)
            {
                Destroy(prior);
            }

            // Trick: temporarily deactivate this GameObject, instantiate the clone
            // (which inherits inactive state — so its components never run Awake/
            // OnEnable and therefore never touch the shared InputActionAsset map),
            // then reactivate the original. The clone stays cold storage.
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(false);

            GameObject template = Instantiate(gameObject);
            template.name = $"__Template_{_registryKey}";
            template.hideFlags = HideFlags.HideAndDontSave;

            // Mark the template so RebuildByName produces an active copy without
            // recursively snapshotting itself.
            Robot templateRobot = template.GetComponent<Robot>();
            if (templateRobot != null)
            {
                templateRobot._isTemplate = true;
                templateRobot._registryKey = _registryKey;
            }

            s_templates[_registryKey] = template;

            if (wasActive) gameObject.SetActive(true);
        }

        /// <summary>
        /// Spawn a fresh copy of the robot named <paramref name="key"/> from the
        /// template captured at its first <see cref="Start"/>. Destroys whatever
        /// scene object currently bears that name.
        /// </summary>
        public static Robot RebuildByName(string key)
        {
            if (!s_templates.TryGetValue(key, out GameObject template) || template == null)
            {
                Debug.LogWarning($"[Robogame] No template stored for robot '{key}'.");
                return null;
            }

            // Tear down any existing instance.
            GameObject existing = GameObject.Find(key);
            if (existing != null) Destroy(existing);

            GameObject fresh = Instantiate(template);
            fresh.name = key;
            fresh.hideFlags = HideFlags.None;
            fresh.SetActive(true);
            Robot freshRobot = fresh.GetComponent<Robot>();
            Rebuilt?.Invoke(freshRobot);
            return freshRobot;
        }

        // -----------------------------------------------------------------
        // Aggregates
        // -----------------------------------------------------------------

        /// <summary>
        /// Pull the optional COM offset from a sibling <see cref="Movement.RobotDrive"/>
        /// (if any). Default zero — pure mass-weighted COM. Robots → Movement
        /// is an existing asmdef edge, so the type reference is fine.
        /// </summary>
        private Vector3 ResolveCenterOfMassOffset()
        {
            Movement.RobotDrive drive = GetComponent<Movement.RobotDrive>();
            return drive != null ? drive.GetCenterOfMassOffset() : Vector3.zero;
        }

        /// <summary>
        /// Re-baseline <see cref="InitialBlockMass"/> and
        /// <see cref="InitialBlockCount"/> against the current grid state.
        /// Called by the repair pad after a successful gradual rebuild so
        /// the mass-loss destroy threshold doesn't fire on the next chip
        /// of damage. <see cref="CpuBlock"/> is also re-resolved in case
        /// the original CPU was lost and the rebuild dropped a fresh one.
        /// </summary>
        public void ResetInitialAggregates()
        {
            RecalculateAggregates();
            InitialBlockMass = TotalBlockMass;
            InitialBlockCount = BlockCount;
            CpuBlock = FindCpuBlock();
        }

        /// <summary>Recompute mass, CPU, count, COM, and inertia tensor from the grid; sync to the rigidbody.</summary>
        public void RecalculateAggregates()
        {
            using var _scope = Robogame.Core.PerfMarkers.RobotRecalcAggregates.Auto();
            if (_grid == null) return;

            int cpu = 0;
            float mass = 0f;
            int count = 0;
            Vector3 weightedPos = Vector3.zero;

            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                cpu += b.Definition.CpuCost;
                mass += b.Definition.Mass;
                count++;
                // Mass-weighted accumulation against the chassis-local
                // grid origin. _grid.Blocks keys are integer cell coords;
                // converting to chassis-local metres is a multiply by
                // CellSize, which we do once at the end (cheaper than
                // per-block).
                weightedPos += (Vector3)kvp.Key * b.Definition.Mass;
            }

            TotalCpu = cpu;
            TotalBlockMass = mass;
            BlockCount = count;

            if (_rb == null || mass <= 0f) return;

            _rb.mass = mass;

            // Mass-weighted COM in chassis-local metres. Apply the optional
            // tuning offset (RobotDrive.CenterOfMassOffset) so a ground
            // vehicle can keep the legacy "pull COM down 0.5" tip-resistance.
            float cellSize = _grid.CellSize;
            Vector3 com = (weightedPos / mass) * cellSize + ResolveCenterOfMassOffset();

            // Explicit COM + inertia tensor management (PHYSICS_PLAN-aligned
            // determinism; session-25 latent fix). PhysX auto-computes the
            // inertia tensor from the collider distribution about the
            // centerOfMass, which:
            //   1. introduces frame mismatch when centerOfMass is overridden
            //      to a constant that doesn't match the collider distribution,
            //   2. silently changes when foils get adopted off the chassis
            //      (their colliders move to a kinematic hub at scene root).
            // Computing a diagonal inertia tensor from the block grid bakes
            // the canonical chassis frame into the rigidbody — angular
            // axes stay decoupled, foil adoption doesn't shift the tensor,
            // and the same input produces the same response across machines
            // (deterministic, MP-safe).
            _rb.automaticCenterOfMass = false;
            _rb.automaticInertiaTensor = false;
            _rb.centerOfMass = com;
            _rb.inertiaTensor = ComputeDiagonalInertiaTensor(com, cellSize);
            _rb.inertiaTensorRotation = Quaternion.identity;
        }

        // Diagonal inertia tensor in chassis-local frame, computed from the
        // block grid via the parallel-axis theorem. Each block is treated
        // as a uniform-density cube of side = cellSize, contributing
        // (1/6)·m·s² to its own diagonal plus m·d² for offset from COM
        // along the perpendicular axes. Returns positive non-zero values
        // (PhysX rejects zero on any diagonal).
        private Vector3 ComputeDiagonalInertiaTensor(Vector3 com, float cellSize)
        {
            float ixx = 0f, iyy = 0f, izz = 0f;
            float selfTerm = (cellSize * cellSize) / 6f; // for a uniform cube
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                float m = b.Definition.Mass;
                Vector3 r = (Vector3)kvp.Key * cellSize - com;
                ixx += m * (selfTerm + r.y * r.y + r.z * r.z);
                iyy += m * (selfTerm + r.x * r.x + r.z * r.z);
                izz += m * (selfTerm + r.x * r.x + r.y * r.y);
            }
            // PhysX disallows non-positive entries on the diagonal. Single-
            // block chassis would otherwise hit that floor.
            return new Vector3(
                Mathf.Max(0.001f, ixx),
                Mathf.Max(0.001f, iyy),
                Mathf.Max(0.001f, izz));
        }

        private BlockBehaviour FindCpuBlock()
        {
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b != null && b.Definition != null && b.Definition.Category == BlockCategory.Cpu)
                {
                    return b;
                }
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Damage convenience
        // -----------------------------------------------------------------

        /// <summary>Apply ring-falloff splash damage centred on the world-space hit point.</summary>
        public void ApplySplashDamage(Vector3 worldPos, IReadOnlyList<float> ringDamage)
        {
            if (_grid == null) return;
            Vector3Int gridPos = _grid.WorldToGrid(worldPos);
            _grid.ApplySplashDamage(gridPos, ringDamage);
        }

        // -----------------------------------------------------------------
        // Block lifecycle
        // -----------------------------------------------------------------

        private void HandleBlockPlaced(BlockBehaviour block)
        {
            // Cheap; recalc keeps mass/CPU honest if blocks are added at runtime.
            RecalculateAggregates();
        }

        private void HandleBlockRemoving(BlockBehaviour block)
        {
            if (IsDestroyed) return;

            bool removingCpu = block != null && block == CpuBlock;

            // Adjust aggregates (the block is still in the grid dictionary right now;
            // deduct manually instead of recounting to keep this O(1)).
            if (block != null && block.Definition != null)
            {
                TotalCpu = Mathf.Max(0, TotalCpu - block.Definition.CpuCost);
                TotalBlockMass = Mathf.Max(0f, TotalBlockMass - block.Definition.Mass);
                BlockCount = Mathf.Max(0, BlockCount - 1);
                if (_rb != null && TotalBlockMass > 0f) _rb.mass = TotalBlockMass;
            }

            if (removingCpu)
            {
                CpuBlock = null;
                MarkDestroyed("CPU destroyed");
                return;
            }

            // Run connectivity from the CPU after this block is gone. Defer to next
            // frame so the grid finishes removing this block from its dictionary.
            if (CpuBlock != null && _spawnDetachedDebris && isActiveAndEnabled)
            {
                StartCoroutine(RunConnectivityNextFrame());
            }

            CheckMassThreshold();
        }

        private IEnumerator RunConnectivityNextFrame()
        {
            yield return null;
            if (IsDestroyed || CpuBlock == null) yield break;

            List<BlockBehaviour> orphans = _grid.FindDisconnectedFrom(CpuBlock.GridPosition);
            if (orphans.Count == 0) yield break;

            foreach (BlockBehaviour orphan in orphans)
            {
                if (orphan == null) continue;
                DetachAsDebris(orphan);
            }

            // Detached blocks no longer count toward our mass.
            RecalculateAggregates();
            CheckMassThreshold();
        }

        private void DetachAsDebris(BlockBehaviour block)
        {
            Vector3Int pos = block.GridPosition;
            BlockBehaviour detached = _grid.DetachBlock(pos);
            if (detached == null) return;

            Transform t = detached.transform;
            Vector3 worldPos = t.position;
            Quaternion worldRot = t.rotation;
            Vector3 worldScale = t.lossyScale;

            // Dust puff at the detach point. Single one-shot per block
            // detached; the spawner caps the concurrent count so a wreck
            // that sheds 30 blocks in one frame doesn't turn into a
            // particle storm.
            Robogame.Core.VfxSpawner.Spawn(
                Robogame.Core.VfxKind.DebrisDust,
                worldPos,
                Quaternion.identity);

            // Audio: same per-block cadence as the dust puff. The
            // AudioRouter's voice pool caps simultaneous voices at 24,
            // so a chassis shedding 30 blocks in one frame gets ~24
            // crunches and the rest are dropped — acceptable for a
            // crackling collapse, intentionally not a single big boom
            // (the chassis-destroyed boom can come in a future cue).
            Robogame.Core.AudioRouter.PlayOneShot(
                Robogame.Core.AudioCue.BlockDestroyed,
                worldPos);

            t.SetParent(null, worldPositionStays: true);
            t.position = worldPos;
            t.rotation = worldRot;
            t.localScale = worldScale;

            Rigidbody rb = detached.gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = detached.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, detached.Definition != null ? detached.Definition.Mass : 1f);
            // Inherit some of the parent's velocity for plausibility.
            if (_rb != null)
            {
                rb.linearVelocity = _rb.GetPointVelocity(worldPos);
                rb.angularVelocity = _rb.angularVelocity;
            }

            if (_debrisLifetime > 0f)
            {
                Destroy(detached.gameObject, _debrisLifetime);
            }
        }

        private void CheckMassThreshold()
        {
            if (IsDestroyed || InitialBlockMass <= 0f) return;
            float lossFraction = 1f - (TotalBlockMass / InitialBlockMass);
            if (lossFraction >= _massLossDestroyThreshold)
            {
                MarkDestroyed($"Lost {lossFraction:P0} of mass");
            }
        }

        private void MarkDestroyed(string reason)
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            Debug.Log($"[Robogame] Robot '{name}' destroyed: {reason}", this);
            Destroyed?.Invoke(this);
            ExplodeRemainingBlocks();
        }

        /// <summary>
        /// Detach every surviving block as physics debris, then dispose of the
        /// (now-empty) robot shell. Called once on destruction so the wreck
        /// actually falls apart instead of rolling around looking healthy.
        /// </summary>
        private void ExplodeRemainingBlocks()
        {
            if (_grid == null) return;

            var positions = new List<Vector3Int>(_grid.Blocks.Keys);
            foreach (Vector3Int pos in positions)
            {
                if (_grid.TryGetBlock(pos, out BlockBehaviour b) && b != null)
                {
                    DetachAsDebris(b);
                }
            }

            // Disable control + physics on the empty shell, then clean it up.
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
            // Hide immediately so we don't sit there as a single bare collider.
            gameObject.SetActive(false);
            Destroy(gameObject, 0.1f);
        }
    }
}

