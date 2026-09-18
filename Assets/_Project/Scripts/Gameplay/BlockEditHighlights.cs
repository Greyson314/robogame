using UnityEngine;
using Robogame.Block;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Instance-edit and tune-mode hover highlight shells for
    /// <see cref="BlockEditor"/> (session 125 instance-edit highlight,
    /// F-050 shared shell builder, F-049 extraction). Self-contained: owns
    /// its own highlight GameObjects and materials and needs only the
    /// grid's cell size for the no-renderer fallback — BlockEditor keeps
    /// the targeting/placement decisions (which block, if any, is bound or
    /// hovered) and hands this class the resolved block.
    /// </summary>
    public sealed class BlockEditHighlights
    {
        // -----------------------------------------------------------------
        // Instance-edit highlight (session 125)
        // -----------------------------------------------------------------

        private GameObject _instanceHighlight;
        private static Material s_highlightMat;

        // Build a highlight shell: a cube primitive with its collider
        // stripped, the given material assigned, and shadows disabled.
        // Shared by the bound-instance highlight and the tune-mode hover
        // highlight (F-050).
        private static GameObject MakeHighlightShell(Material material)
        {
            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Collider col = shell.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var mr = shell.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return shell;
        }

        // Translucent box around the edited block so the player can see which
        // instance their sliders are driving. A bounding cube (not a shape
        // match) is enough to answer "which one"; parented to the block so it
        // tracks any reparent (rotor-adopted foils) and dies with the block.
        public void HighlightInstance(BlockBehaviour block, float cellSize)
        {
            if (_instanceHighlight != null) { Object.Destroy(_instanceHighlight); _instanceHighlight = null; }
            if (block == null) return;

            if (s_highlightMat == null)
                s_highlightMat = Robogame.Core.RuntimeMaterials.UnlitTransparent(new Color(1f, 0.62f, 0.10f, 0.22f));

            _instanceHighlight = MakeHighlightShell(s_highlightMat);
            _instanceHighlight.name = "InstanceEditHighlight";
            FitShellToBlock(_instanceHighlight, block, cellSize);
        }

        // Drop the instance-edit highlight. Safe to call when nothing is bound.
        public void ClearInstanceHighlight()
        {
            if (_instanceHighlight != null)
            {
                Object.Destroy(_instanceHighlight);
                _instanceHighlight = null;
            }
        }

        // Fit a highlight shell to the block's full RENDERED bounds — a
        // wing glows across its whole span, a rotor across its disc. The
        // old cell-sized shell read as a faint box at the mount point,
        // not a glow on the part (session 138 playtest). World-axis AABB
        // is fine for a glow read; the chassis is parked in build mode.
        // Parented afterwards (preserving world pose) so it tracks the
        // block and dies with it. Allocation only on hover/bind changes,
        // never per frame.
        private void FitShellToBlock(GameObject shell, BlockBehaviour block, float cellSize)
        {
            Transform t = shell.transform;
            t.SetParent(null, worldPositionStays: false);
            Bounds b = default;
            bool has = false;
            Renderer[] rends = block.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null) continue;
                // Never measure our own shells — a shell inside the bounds
                // pass would inflate itself by 8% per refit.
                GameObject go = r.gameObject;
                if (go == shell || go == _instanceHighlight || go == _hoverHighlight) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) b = new Bounds(block.transform.position, Vector3.one * cellSize);
            t.SetPositionAndRotation(b.center, Quaternion.identity);
            // 8% swell + a small absolute pad so thin parts (wing sheets)
            // still get a visible halo instead of a coplanar z-fight.
            t.localScale = b.size * 1.08f + Vector3.one * 0.08f;
            t.SetParent(block.transform, worldPositionStays: true);
        }

        // -----------------------------------------------------------------
        // Tune-mode hover highlight — a fainter shell than the bound-
        // instance one, marking the tunable block under the cursor as
        // clickable. One shell object reused across hovers (invariant #6:
        // no per-frame allocations); it only reparents when the hovered
        // block changes.
        // -----------------------------------------------------------------

        private GameObject _hoverHighlight;
        private BlockBehaviour _hoverBlock;
        // Per-editor material instance (not the static shared one) so the
        // pulse below can animate alpha without touching other shells.
        private Material _hoverMat;
        private static readonly Color s_hoverBase = new Color(1f, 0.62f, 0.10f, 0.14f);

        // BlockEditor resolves which block (if any) is the hover target —
        // that decision needs targeting state this class doesn't own — and
        // hands the result in here each frame.
        public void DriveHover(BlockBehaviour target, float cellSize)
        {
            PulseHoverHighlight();

            if (target == _hoverBlock && (target == null || _hoverHighlight != null)) return;
            _hoverBlock = target;
            if (target == null)
            {
                HideHover();
                return;
            }

            if (_hoverMat == null)
                _hoverMat = Robogame.Core.RuntimeMaterials.UnlitTransparent(s_hoverBase);
            if (_hoverHighlight == null)
            {
                _hoverHighlight = MakeHighlightShell(_hoverMat);
                _hoverHighlight.name = "TuneHoverHighlight";
            }
            FitShellToBlock(_hoverHighlight, target, cellSize);
            _hoverHighlight.SetActive(true);
        }

        // Slow alpha breathe on the hover shell so tunable parts read as
        // "glowing" rather than faintly boxed. One material color write
        // per frame while tune mode is on — no allocation.
        private void PulseHoverHighlight()
        {
            if (_hoverMat == null || _hoverHighlight == null || !_hoverHighlight.activeSelf) return;
            Color c = s_hoverBase;
            c.a = 0.12f + 0.12f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
            _hoverMat.color = c;
        }

        public void HideHover()
        {
            _hoverBlock = null;
            if (_hoverHighlight == null) return;
            // Detach so a later host-block destroy can't take the reusable
            // shell down with it.
            _hoverHighlight.transform.SetParent(null, worldPositionStays: false);
            _hoverHighlight.SetActive(false);
        }
    }
}
