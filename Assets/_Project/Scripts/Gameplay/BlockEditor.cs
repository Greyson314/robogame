using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode block placement and removal. Active only while
    /// <see cref="BuildModeController.IsActive"/>; subscribes to its
    /// Entered/Exited events to show/hide the ghost preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Targeting model: raycast from the screen mouse → if the ray hits a
    /// <see cref="BlockBehaviour"/>, the target cell is the hit-block's
    /// grid position offset by the face-normal direction. Right-click
    /// targets the hit cell directly (for removal).
    /// </para>
    /// <para>
    /// On every successful place/remove, the <see cref="ChassisBlueprint"/>
    /// owned by <see cref="GameStateController"/> is rewritten from the
    /// live <see cref="BlockGrid"/> contents — so Save / Launch always
    /// see the current edits even if the player skips a manual save.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockEditor : MonoBehaviour
    {
        [Tooltip("Build-mode controller this editor follows.")]
        [SerializeField] private BuildModeController _buildMode;

        [Tooltip("Hotbar that selects which block ID is placed on left-click.")]
        [SerializeField] private BuildHotbar _hotbar;

        [Tooltip("Layer mask used by the targeting raycast. Default: everything.")]
        [SerializeField] private LayerMask _raycastMask = ~0;

        [Tooltip("Maximum picking distance.")]
        [SerializeField, Min(1f)] private float _raycastDistance = 100f;

        [Tooltip("Reference CPU budget contributed per CPU block on the chassis. " +
                 "Used only to compute the cap shown in the BuildHotbar readout — " +
                 "placements are NOT rejected when the cap is exceeded; the readout " +
                 "just turns hot to flag it.")]
        [SerializeField, Min(0)] private int _cpuBudgetPerCpu = 250;

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode == value) return;
                Unsubscribe();
                _buildMode = value;
                Subscribe();
            }
        }
        public BuildHotbar Hotbar { get => _hotbar; set => _hotbar = value; }

        /// <summary>Snapshot of CPU usage for HUD display.</summary>
        public readonly struct CpuUsage
        {
            public readonly int Used;
            public readonly int Cap;
            public CpuUsage(int used, int cap) { Used = used; Cap = cap; }
            public bool OverBudget => Used > Cap;
        }

        /// <summary>Live CPU usage of the chassis. Returns (0,0) if no grid.</summary>
        public CpuUsage GetCpuUsage()
        {
            if (_grid == null) return new CpuUsage(0, 0);
            int used = 0, cpus = 0;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                used += Mathf.Max(0, b.Definition.CpuCost);
                if (b.Definition.Category == BlockCategory.Cpu) cpus++;
            }
            return new CpuUsage(used, cpus * _cpuBudgetPerCpu);
        }

        // Targeting state -------------------------------------------------
        private BlockGrid _grid;
        private GameObject _ghost;
        private Material _ghostMatValid;
        private Material _ghostMatInvalid;
        private string _ghostBuiltForId;
        private bool _ghostShowingValid;

        private bool _hasTarget;
        private Vector3Int _targetPlaceCell;
        private Vector3Int _targetHitCell;
        private bool _validPlacement;

        private bool _subscribed;

        private static readonly Vector3Int[] s_neighbors =
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
        };

        private void OnEnable()
        {
            Subscribe();
            // If build mode is already active when we wake up, behave as if Entered just fired.
            if (_buildMode != null && _buildMode.IsActive) HandleEntered();
        }

        private void OnDisable()
        {
            Unsubscribe();
            DestroyGhost();
        }

        private void Subscribe()
        {
            if (_subscribed || _buildMode == null) return;
            _buildMode.Entered += HandleEntered;
            _buildMode.Exited  += HandleExited;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _buildMode == null) return;
            _buildMode.Entered -= HandleEntered;
            _buildMode.Exited  -= HandleExited;
            _subscribed = false;
        }

        private void HandleEntered()
        {
            // Re-resolve grid in case the chassis was respawned.
            _grid = _buildMode.Chassis != null ? _buildMode.Chassis.GetComponent<BlockGrid>() : null;
            EnsureGhost();
        }

        private void HandleExited()
        {
            _grid = null;
            DestroyGhost();
        }

        private void Update()
        {
            // Lazy-subscribe in case BuildMode was assigned after OnEnable ran.
            if (!_subscribed && _buildMode != null)
            {
                Subscribe();
                if (_buildMode.IsActive) HandleEntered();
            }
            if (_buildMode == null || !_buildMode.IsActive) return;
            if (_grid == null)
            {
                _grid = _buildMode.Chassis != null ? _buildMode.Chassis.GetComponent<BlockGrid>() : null;
                if (_grid == null) return;
            }
            EnsureGhost(); // safety net if HandleEntered never fired.

            UpdateTarget();
            UpdateGhost();
            HandleClicks();
        }

        // -----------------------------------------------------------------
        // Targeting
        // -----------------------------------------------------------------

        private void UpdateTarget()
        {
            _hasTarget = false;
            _validPlacement = false;

            Mouse mouse = Mouse.current;
            Camera cam = Camera.main;
            if (mouse == null || cam == null) return;

            // Ignore picks while the cursor is over the build HUD.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, _raycastDistance, _raycastMask, QueryTriggerInteraction.Ignore))
                return;

            // Only react to picks that land on blocks belonging to OUR chassis
            // (not on the ground, walls, podium, etc.).
            BlockBehaviour block = hit.collider != null
                ? hit.collider.GetComponentInParent<BlockBehaviour>()
                : null;
            if (block == null) return;
            if (_buildMode.Chassis == null || !block.transform.IsChildOf(_buildMode.Chassis)) return;

            _targetHitCell = block.GridPosition;

            // Convert hit normal to a grid step. World → local → round.
            Vector3 localN = _buildMode.Chassis.InverseTransformDirection(hit.normal);
            _targetPlaceCell = block.GridPosition + RoundToAxis(localN);

            _hasTarget = true;
            _validPlacement = IsValidPlacement(_targetPlaceCell);
        }

        private static Vector3Int RoundToAxis(Vector3 dir)
        {
            // Pick the axis with the largest absolute component.
            float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
            if (ax >= ay && ax >= az) return new Vector3Int(dir.x >= 0f ? 1 : -1, 0, 0);
            if (ay >= az)              return new Vector3Int(0, dir.y >= 0f ? 1 : -1, 0);
            return                            new Vector3Int(0, 0, dir.z >= 0f ? 1 : -1);
        }

        private bool IsValidPlacement(Vector3Int cell)
        {
            if (_grid == null) return false;
            if (_grid.Blocks.ContainsKey(cell)) return false;
            // Adjacency + connectivity: the new cell must touch at least one
            // block that is itself reachable from the CPU through the 6-axis
            // adjacency graph. In normal play this is identical to the old
            // "any neighbour" rule (every existing block is CPU-reachable by
            // induction), but it's the watertight version: a hand-edited or
            // corrupted blueprint that loaded with a disconnected island can't
            // be extended — you can only build off the CPU's component.
            HashSet<Vector3Int> reachable = BuildCpuReachableSet();
            bool adjacentToCpu = false;
            if (reachable != null)
            {
                for (int i = 0; i < s_neighbors.Length; i++)
                    if (reachable.Contains(cell + s_neighbors[i])) { adjacentToCpu = true; break; }
            }
            else
            {
                // No CPU yet (we only allow placement during edit, and a
                // chassis without a CPU is already broken — but be permissive
                // so the very first CPU can be dropped onto an empty grid).
                if (_grid.Blocks.Count == 0) adjacentToCpu = true;
                else
                {
                    for (int i = 0; i < s_neighbors.Length; i++)
                        if (_grid.Blocks.ContainsKey(cell + s_neighbors[i])) { adjacentToCpu = true; break; }
                }
            }
            if (!adjacentToCpu) return false;

            // CPU policy: we *track* total cost via GetCpuUsage / the
            // BuildHotbar readout, but we no longer reject placements that
            // exceed the cap. The 2nd-CPU rule below is a structural
            // constraint (chassis logic assumes one CPU), not a cost limit.
            BlockDefinition selected = GetSelectedDefinition();
            if (selected != null)
            {
                if (selected.Category == BlockCategory.Cpu && HasAnyCpu()) return false;
            }
            return true;
        }

        private BlockDefinition GetSelectedDefinition()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.Library == null || _hotbar == null) return null;
            return state.Library.Get(_hotbar.SelectedBlockId);
        }

        // -----------------------------------------------------------------
        // Ghost preview
        // -----------------------------------------------------------------

        private void EnsureGhost()
        {
            // Materials are reused across rebuilds; only the shape changes.
            if (_ghostMatValid == null)   _ghostMatValid   = MakeGhostMat(new Color(0.30f, 0.95f, 0.30f, 0.45f));
            if (_ghostMatInvalid == null) _ghostMatInvalid = MakeGhostMat(new Color(0.95f, 0.25f, 0.20f, 0.45f));

            string targetId = _hotbar != null ? _hotbar.SelectedBlockId : BlockIds.Cube;
            if (_ghost != null && _ghostBuiltForId == targetId) return;

            // Selection changed (or first build) — rebuild the ghost shape.
            if (_ghost != null) Object.Destroy(_ghost);

            BlockDefinition def = GetSelectedDefinition();
            _ghost = BlockGhostFactory.Build(def, _ghostMatValid);
            _ghost.SetActive(false);
            _ghostBuiltForId = targetId;
            _ghostShowingValid = true;
        }

        private static Material MakeGhostMat(Color c)
        {
            // URP/Unlit if available, else built-in Unlit. Both support _BaseColor / _Color.
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "Mat_BlockGhost" };

            // Make it transparent. URP/Unlit uses _Surface=1 + _Blend=0 for alpha blend.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // 1 = Transparent
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend",   0f); // 0 = Alpha
            if (m.HasProperty("_ZWrite"))  m.SetFloat("_ZWrite",  0f);
            m.renderQueue = 3000;
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
            return m;
        }

        private void DestroyGhost()
        {
            if (_ghost != null) Object.Destroy(_ghost);
            if (_ghostMatValid != null) Object.Destroy(_ghostMatValid);
            if (_ghostMatInvalid != null) Object.Destroy(_ghostMatInvalid);
            _ghost = null;
            _ghostBuiltForId = null;
            _ghostMatValid = _ghostMatInvalid = null;
        }

        private void UpdateGhost()
        {
            if (_ghost == null) return;
            if (!_hasTarget)
            {
                _ghost.SetActive(false);
                return;
            }
            _ghost.SetActive(true);
            _ghost.transform.position = _grid.GridToWorld(_targetPlaceCell);
            _ghost.transform.rotation = _buildMode.Chassis.rotation;
            // The ghost factory authored shapes at unit-cell scale, so we
            // just multiply by the grid's cell size (with a tiny inflation
            // so the ghost doesn't z-fight neighbouring solid blocks).
            _ghost.transform.localScale = Vector3.one * (_grid.CellSize * 1.01f);

            // Only swap materials when the validity actually flips — keeps
            // SRP batching happy on the ghost's renderers.
            if (_validPlacement != _ghostShowingValid)
            {
                BlockGhostFactory.ApplyMaterial(_ghost,
                    _validPlacement ? _ghostMatValid : _ghostMatInvalid);
                _ghostShowingValid = _validPlacement;
            }
        }

        // -----------------------------------------------------------------
        // Place / remove
        // -----------------------------------------------------------------

        private void HandleClicks()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !_hasTarget) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (mouse.leftButton.wasPressedThisFrame)  TryPlace();
            if (mouse.rightButton.wasPressedThisFrame) TryRemove();
        }

        private void TryPlace()
        {
            if (!_validPlacement) return;
            string id = _hotbar != null ? _hotbar.SelectedBlockId : BlockIds.Cube;

            GameStateController state = GameStateController.Instance;
            if (state == null || state.Library == null) return;
            BlockDefinition def = state.Library.Get(id);
            if (def == null)
            {
                Debug.LogWarning($"[Robogame] BlockEditor: unknown block id '{id}'.");
                return;
            }

            // Block placing a 2nd CPU.
            if (def.Category == BlockCategory.Cpu && HasAnyCpu())
            {
                Debug.Log("[Robogame] BlockEditor: chassis already has a CPU; placement skipped.");
                return;
            }

            if (_grid.PlaceBlock(def, _targetPlaceCell) != null)
                SyncBlueprintFromGrid();
        }

        private void TryRemove()
        {
            if (!_grid.Blocks.TryGetValue(_targetHitCell, out BlockBehaviour block) || block == null) return;

            // Per Pass-3 design: CPU is sacred while editing.
            if (block.Definition != null && block.Definition.Category == BlockCategory.Cpu)
            {
                Debug.Log("[Robogame] BlockEditor: CPU cannot be removed in build mode.");
                return;
            }

            // Connectivity: would removing this orphan any other block?
            if (WouldOrphanIfRemoved(_targetHitCell, out int orphanCount))
            {
                Debug.Log($"[Robogame] BlockEditor: removal blocked — would orphan {orphanCount} block(s).");
                return;
            }

            if (_grid.RemoveBlock(_targetHitCell))
                SyncBlueprintFromGrid();
        }

        /// <summary>
        /// Simulate removing <paramref name="cell"/> and BFS from the CPU. If any
        /// remaining block isn't reachable, the removal would orphan it.
        /// </summary>
        private bool WouldOrphanIfRemoved(Vector3Int cell, out int orphanCount)
        {
            orphanCount = 0;
            if (_grid == null) return false;

            // Locate the CPU. If no CPU (shouldn't happen — we forbid removing it),
            // fall back to letting the removal through.
            Vector3Int? cpuCell = null;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b != null && b.Definition != null && b.Definition.Category == BlockCategory.Cpu)
                { cpuCell = kvp.Key; break; }
            }
            if (cpuCell == null) return false;
            if (cpuCell.Value == cell) return false; // protected elsewhere

            // BFS from CPU over the live grid, treating `cell` as removed.
            var visited = new HashSet<Vector3Int> { cpuCell.Value };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(cpuCell.Value);
            while (queue.Count > 0)
            {
                Vector3Int c = queue.Dequeue();
                for (int i = 0; i < s_neighbors.Length; i++)
                {
                    Vector3Int n = c + s_neighbors[i];
                    if (n == cell) continue;                     // pretend it's gone
                    if (visited.Contains(n)) continue;
                    if (!_grid.Blocks.ContainsKey(n)) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }

            // Count any blocks not reached (excluding the cell-to-remove itself).
            int total = _grid.Blocks.Count - 1; // minus the one we're removing
            orphanCount = total - visited.Count;
            return orphanCount > 0;
        }

        /// <summary>
        /// BFS from the CPU over the live grid via 6-axis adjacency.
        /// Returns the set of CPU-reachable cells, or <c>null</c> if no CPU
        /// is present. Used by placement validation so new blocks must
        /// attach to the CPU's connected component (defends against loading
        /// a disconnected blueprint), and shareable with any other system
        /// that needs the same answer.
        /// </summary>
        private HashSet<Vector3Int> BuildCpuReachableSet()
        {
            if (_grid == null) return null;

            Vector3Int? cpuCell = null;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b != null && b.Definition != null && b.Definition.Category == BlockCategory.Cpu)
                { cpuCell = kvp.Key; break; }
            }
            if (cpuCell == null) return null;

            var visited = new HashSet<Vector3Int> { cpuCell.Value };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(cpuCell.Value);
            while (queue.Count > 0)
            {
                Vector3Int c = queue.Dequeue();
                for (int i = 0; i < s_neighbors.Length; i++)
                {
                    Vector3Int n = c + s_neighbors[i];
                    if (visited.Contains(n)) continue;
                    if (!_grid.Blocks.ContainsKey(n)) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
            return visited;
        }

        private bool HasAnyCpu()
        {
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b != null && b.Definition != null && b.Definition.Category == BlockCategory.Cpu)
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Blueprint sync
        // -----------------------------------------------------------------

        private void SyncBlueprintFromGrid()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.CurrentBlueprint == null || _grid == null) return;

            var list = new List<ChassisBlueprint.Entry>(_grid.Blocks.Count);
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                list.Add(new ChassisBlueprint.Entry(b.Definition.Id, kvp.Key));
            }
            state.CurrentBlueprint.SetEntries(list.ToArray());
        }
    }
}
