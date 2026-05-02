using Robogame.Block;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Per-chassis buoyancy. Walks the chassis's <see cref="BlockGrid"/>
    /// every <see cref="FixedUpdate"/>, computes how deep each block sits
    /// below the active <see cref="WaterVolume"/>'s surface, and applies a
    /// proportional upward force at the block's world position. Drag is
    /// applied to the chassis Rigidbody as a whole, scaled by the average
    /// submerged fraction across all blocks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buoyancy model is intentionally cheap and stable rather than
    /// physically perfect:
    /// <list type="bullet">
    ///   <item><description>Each block is treated as a unit cube of side <see cref="BlockGrid.CellSize"/> m.</description></item>
    ///   <item><description>Submerged depth is clamped to <c>[0, cellSize]</c> — blocks never count as "more than fully" wet.</description></item>
    ///   <item><description>Force = ρ · V_submerged · g, applied at block centre (not at the centroid of the wet portion). For Pass-A sandbox feel this is plenty; replace with a multi-sample integration if we ever need realistic capsizing.</description></item>
    ///   <item><description>Drag is applied via <see cref="Rigidbody.linearDamping"/> and <see cref="Rigidbody.angularDamping"/> instead of an explicit damping force, so it tracks Unity's solver exactly. Original Rigidbody damping values are restored when no block is wet.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Lookup: the controller resolves <see cref="WaterVolume.Active"/>
    /// each FixedUpdate. If there's no water in the scene this component
    /// silently no-ops, so it's safe to leave attached on chassis spawned
    /// in non-water scenes (the WaterArenaController only adds it on entry).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class BuoyancyController : MonoBehaviour
    {
        private Rigidbody _rb;
        private BlockGrid _grid;
        private float _baseLinearDamping;
        private float _baseAngularDamping;
        private bool _dampingOverridden;

        // Active controllers, exposed so the visual water mesh can ask
        // "where is anything currently touching me?" without us having to
        // re-walk Physics each frame. Populated in OnEnable / OnDisable.
        private static readonly System.Collections.Generic.HashSet<BuoyancyController> s_active
            = new System.Collections.Generic.HashSet<BuoyancyController>();
        public static System.Collections.Generic.IReadOnlyCollection<BuoyancyController> Active => s_active;

        // World-space XZ positions of blocks that straddled the waterline
        // during the most recent FixedUpdate. "Straddle" = depth in (0, cell):
        // a block fully submerged or fully dry doesn't produce surface foam,
        // only the ones cutting through the surface do. Re-used between
        // frames so the visual mesh always has *something* to read even if
        // a render frame falls between physics ticks.
        private readonly System.Collections.Generic.List<Vector2> _surfaceContacts
            = new System.Collections.Generic.List<Vector2>();
        public System.Collections.Generic.IReadOnlyList<Vector2> SurfaceContacts => _surfaceContacts;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grid = GetComponent<BlockGrid>();
            _baseLinearDamping  = _rb.linearDamping;
            _baseAngularDamping = _rb.angularDamping;
        }

        private void OnEnable()
        {
            s_active.Add(this);
        }

        private void OnDisable()
        {
            s_active.Remove(this);
            _surfaceContacts.Clear();
            // Don't leave the chassis with our overridden damping if this
            // component is removed mid-arena (e.g. on a respawn).
            RestoreDamping();
        }

        private void FixedUpdate()
        {
            _surfaceContacts.Clear();
            WaterVolume water = WaterVolume.Active;
            if (water == null || _grid == null || _rb == null || _grid.Count == 0)
            {
                RestoreDamping();
                return;
            }

            float cell = _grid.CellSize;
            // V_block = cell^3, but we factor out cell separately below to
            // multiply by submergedDepth (in metres) rather than fraction.
            // Force = ρ · A · depth · g · displacement, where A = cell^2
            // and `displacement` is the hollow-shell fraction of the cube
            // that actually pushes water aside (so heavy chassis sink unless
            // they have enough cross-section to lift their own mass).
            float crossSection = cell * cell;
            float gAccel = water.Gravity;
            float density = water.Density;
            float displacement = water.Displacement;
            // Sample at scene-stable time (not Time.fixedTime alone) so the
            // visual mesh animator (Phase 2) and the buoyancy sampler
            // share the same clock.
            float time = Time.timeSinceLevelLoad;

            float submergedFractionSum = 0f;
            int blockCount = 0;

            // Iterate every authored block and accumulate buoyancy.
            // Using the dictionary directly keeps allocation-free.
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null) continue;

                Vector3 worldCentre = _grid.GridToWorld(kvp.Key);
                // Per-block wave-aware surface height. Each block sees its
                // own slice of the surface, so a long hull straddling a
                // crest/trough rocks naturally instead of bobbing rigidly.
                float surfaceY = WaterSurface.SampleHeight(water, worldCentre.x, worldCentre.z, time);
                // World-space block bottom = centre - half-cell · world-up.
                // We accept the small error from rotated chassis (the wing-on-its-side case)
                // because the cubic approximation already drops orientation detail.
                float bottomY = worldCentre.y - 0.5f * cell;
                float depth = surfaceY - bottomY;
                if (depth <= 0f) continue; // block is fully above the surface

                float wetMetres = Mathf.Min(depth, cell);
                float fSubmerged = wetMetres / cell;
                submergedFractionSum += fSubmerged;
                blockCount++;

                // Surface contact: block is partially in the water (not fully
                // submerged, not dry). Recorded for the visual mesh's foam
                // wake — see WaterMeshAnimator. Cheap: just the world XZ.
                if (fSubmerged > 0.05f && fSubmerged < 0.95f)
                {
                    _surfaceContacts.Add(new Vector2(worldCentre.x, worldCentre.z));
                }

                // F = ρ · V_submerged · g · displacement
                float forceMag = density * crossSection * wetMetres * gAccel * displacement;
                _rb.AddForceAtPosition(Vector3.up * forceMag, worldCentre, ForceMode.Force);
            }

            ApplyDrag(water, submergedFractionSum, blockCount);
        }

        /// <summary>
        /// Scale the rigid-body damping by how much of the chassis is wet.
        /// Surface-skimming rovers (only the bottom row of blocks
        /// submerged) get light drag; a sunken hull is heavily damped.
        /// </summary>
        private void ApplyDrag(WaterVolume water, float submergedFractionSum, int blockCount)
        {
            if (blockCount == 0)
            {
                RestoreDamping();
                return;
            }

            float avg = submergedFractionSum / blockCount;
            // Blend FROM the chassis's authored damping TO water's damping
            // by the average submerged fraction. 0 → unchanged, 1 → fully water-damped.
            _rb.linearDamping  = Mathf.Lerp(_baseLinearDamping,  water.LinearDrag,  avg);
            _rb.angularDamping = Mathf.Lerp(_baseAngularDamping, water.AngularDrag, avg);
            _dampingOverridden = true;
        }

        private void RestoreDamping()
        {
            if (!_dampingOverridden || _rb == null) return;
            _rb.linearDamping  = _baseLinearDamping;
            _rb.angularDamping = _baseAngularDamping;
            _dampingOverridden = false;
        }
    }
}
