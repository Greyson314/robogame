using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Per-chassis gate for the MK Toon outline pass. When a chassis is
    /// not "relevant" (not the local player's and not the local player's
    /// current target), every block renderer is swapped from its authored
    /// <c>+ Outline</c> material to the plain counterpart, so the
    /// expensive per-renderer outline pass simply doesn't run for it.
    /// The outline look itself is unchanged — only *who* gets it.
    /// </summary>
    /// <remarks>
    /// Added to the chassis root by <see cref="Gameplay"/>'s assembler;
    /// driven by <see cref="OutlineRelevanceManager"/>. Walks the
    /// <see cref="BlockGrid"/> (not <c>GetComponentsInChildren</c> on the
    /// transform) so rotor-adopted foils reparented to scene root are
    /// still covered, mirroring <c>FollowCamera</c>'s cache. Allocation-
    /// free in steady state; the cache only rebuilds on block place/remove
    /// (repair pad, combat loss), not per frame.
    /// </remarks>
    public sealed class ChassisOutlineController : MonoBehaviour
    {
        private struct Entry
        {
            public MeshRenderer Renderer;
            public Material Outline; // authored material (captured first sight)
            public Material Plain;   // registry plain, or Outline if none
        }

        private BlockGrid _grid;
        private readonly List<Entry> _entries = new(64);
        private readonly HashSet<MeshRenderer> _seen = new();
        private bool _outlined = true;   // default = current behaviour
        private bool _cacheBuilt;

        private void OnEnable()
        {
            _grid = GetComponentInChildren<BlockGrid>(includeInactive: true);
            if (_grid != null)
            {
                _grid.BlockPlaced += OnBlockChanged;
                _grid.BlockRemoving += OnBlockChanged;
            }
            RebuildCache();
        }

        private void OnDisable()
        {
            if (_grid != null)
            {
                _grid.BlockPlaced -= OnBlockChanged;
                _grid.BlockRemoving -= OnBlockChanged;
            }
        }

        private void OnBlockChanged(BlockBehaviour _) => RebuildCache();

        /// <summary>
        /// Rescan the chassis. New renderers capture their *current*
        /// shared material as the authored outline reference (valid: a
        /// freshly placed block still has its authored material — the
        /// BlockPlaced event fires after PlaceBlock's material apply and
        /// before any relevance swap re-touches it). Already-known
        /// renderers keep their stored authored reference so a swap to
        /// plain can't corrupt it. Then the current state is re-applied
        /// so newly placed blocks match.
        /// </summary>
        private void RebuildCache()
        {
            if (_grid == null) return;
            OutlineMaterialRegistry reg = OutlineMaterialRegistry.Instance;

            // Drop dead renderers.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Renderer == null)
                {
                    _seen.Remove(_entries[i].Renderer);
                    _entries.RemoveAt(i);
                }
            }

            foreach (BlockBehaviour b in _grid.Blocks.Values)
            {
                if (b == null) continue;
                var rends = b.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                for (int i = 0; i < rends.Length; i++)
                {
                    MeshRenderer r = rends[i];
                    if (r == null || _seen.Contains(r)) continue;
                    Material authored = r.sharedMaterial;
                    Material plain = reg != null ? reg.GetPlain(authored) : authored;
                    _entries.Add(new Entry { Renderer = r, Outline = authored, Plain = plain });
                    _seen.Add(r);
                }
            }

            _cacheBuilt = true;
            ApplyState();
        }

        /// <summary>
        /// Set whether this chassis renders its ink-line outline. State-
        /// guarded after the first build; cheap reference writes only.
        /// </summary>
        public void SetOutlined(bool outlined)
        {
            if (_cacheBuilt && outlined == _outlined) return;
            _outlined = outlined;
            ApplyState();
        }

        public bool IsOutlined => _outlined;

        private void ApplyState()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Renderer == null) continue;
                Material want = _outlined ? e.Outline : e.Plain;
                if (e.Renderer.sharedMaterial != want)
                    e.Renderer.sharedMaterial = want;
            }
        }
    }
}
