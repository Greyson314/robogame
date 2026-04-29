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

        /// <summary>Convenience: rebuild this robot by its registry name.</summary>
        public void RebuildFromSnapshot()
        {
            if (string.IsNullOrEmpty(_registryKey)) return;
            RebuildByName(_registryKey);
        }

        // -----------------------------------------------------------------
        // Aggregates
        // -----------------------------------------------------------------

        /// <summary>Recompute mass, CPU, and block count from the grid; sync to the rigidbody.</summary>
        public void RecalculateAggregates()
        {
            if (_grid == null) return;

            int cpu = 0;
            float mass = 0f;
            int count = 0;

            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                cpu += b.Definition.CpuCost;
                mass += b.Definition.Mass;
                count++;
            }

            TotalCpu = cpu;
            TotalBlockMass = mass;
            BlockCount = count;

            if (_rb != null && mass > 0f)
            {
                _rb.mass = mass;
            }
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

