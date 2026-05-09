using Robogame.Block;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Per-frame request to the build-mode ghost renderer. Encodes
    /// "what the player would place if they clicked right now" plus the
    /// optional mirrored counterpart.
    /// </summary>
    public readonly struct GhostRequest
    {
        public readonly bool HasTarget;
        public readonly BlockDefinition Definition;
        public readonly Vector3 Dims;
        public readonly float PitchDeg;
        public readonly Vector3Int Cell;
        public readonly Vector3Int Up;
        public readonly bool Valid;

        public readonly bool ShowMirror;
        public readonly Vector3Int MirrorCell;
        public readonly Vector3Int MirrorUp;
        public readonly float MirrorPitchDeg;
        public readonly bool MirrorValid;

        public readonly Transform ChassisRoot;
        public readonly BlockGrid Grid;

        public GhostRequest(
            bool hasTarget,
            BlockDefinition definition,
            Vector3 dims,
            float pitchDeg,
            Vector3Int cell,
            Vector3Int up,
            bool valid,
            bool showMirror,
            Vector3Int mirrorCell,
            Vector3Int mirrorUp,
            float mirrorPitchDeg,
            bool mirrorValid,
            Transform chassisRoot,
            BlockGrid grid)
        {
            HasTarget = hasTarget;
            Definition = definition;
            Dims = dims;
            PitchDeg = pitchDeg;
            Cell = cell;
            Up = up;
            Valid = valid;
            ShowMirror = showMirror;
            MirrorCell = mirrorCell;
            MirrorUp = mirrorUp;
            MirrorPitchDeg = mirrorPitchDeg;
            MirrorValid = mirrorValid;
            ChassisRoot = chassisRoot;
            Grid = grid;
        }
    }

    /// <summary>
    /// Owns the build-mode placement ghost(s): one for the primary
    /// targeted cell and an optional mirror counterpart when mirror
    /// mode is on. Pure presentation — driven by the editor each frame
    /// via <see cref="Render"/>; no targeting / placement logic of its
    /// own.
    /// </summary>
    /// <remarks>
    /// Ghost shape is rebuilt only when block id / dims / cell / up /
    /// pitch change. Per-frame work otherwise is just transform pose +
    /// material swap when validity flips.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockGhostRenderer : MonoBehaviour
    {
        // Ghost asset cache. Materials are reused across rebuilds; only
        // the shape changes when the block id / dims / cell / up / pitch
        // shifts.
        private GameObject _ghost;
        private Material _matValid;
        private Material _matInvalid;
        private string _builtForId;
        private Vector3 _builtForDims;
        private Vector3Int _builtForCell;
        private Vector3Int _builtForUp;
        private float _builtForPitch;
        private bool _showingValid = true;

        private GameObject _mirrorGhost;
        private bool _mirrorBuilt;
        private string _mirrorBuiltForId;
        private Vector3 _mirrorBuiltForDims;
        private Vector3Int _mirrorBuiltForCell;
        private Vector3Int _mirrorBuiltForUp;
        private float _mirrorBuiltForPitch;
        private bool _mirrorShowingValid = true;

        private void OnDisable()
        {
            DestroyAll();
        }

        /// <summary>
        /// Build-mode driver calls this every Update with the current
        /// targeting state. The renderer figures out whether to rebuild
        /// meshes (id / dims / pose changed) or just re-pose + swap
        /// materials.
        /// </summary>
        public void Render(in GhostRequest req)
        {
            EnsureMaterials();

            if (!req.HasTarget || req.Definition == null || req.Grid == null || req.ChassisRoot == null)
            {
                if (_ghost != null) _ghost.SetActive(false);
                if (_mirrorGhost != null) _mirrorGhost.SetActive(false);
                return;
            }

            EnsurePrimaryShape(req);
            PoseGhost(_ghost, req.Cell, req.Up, req.Grid, req.ChassisRoot);
            if (req.Valid != _showingValid)
            {
                BlockGhostFactory.ApplyMaterial(_ghost, req.Valid ? _matValid : _matInvalid);
                _showingValid = req.Valid;
            }

            if (req.ShowMirror)
            {
                EnsureMirrorShape(req);
                PoseGhost(_mirrorGhost, req.MirrorCell, req.MirrorUp, req.Grid, req.ChassisRoot);
                if (req.MirrorValid != _mirrorShowingValid)
                {
                    BlockGhostFactory.ApplyMaterial(_mirrorGhost, req.MirrorValid ? _matValid : _matInvalid);
                    _mirrorShowingValid = req.MirrorValid;
                }
            }
            else if (_mirrorGhost != null)
            {
                _mirrorGhost.SetActive(false);
            }
        }

        /// <summary>Hide and destroy both ghost objects. Call when build mode exits.</summary>
        public void Clear() => DestroyAll();

        // -----------------------------------------------------------------

        private void EnsurePrimaryShape(in GhostRequest req)
        {
            string id = req.Definition.Id;
            if (_ghost != null
                && _builtForId == id
                && _builtForDims == req.Dims
                && _builtForCell == req.Cell
                && _builtForUp == req.Up
                && Mathf.Approximately(_builtForPitch, req.PitchDeg))
            {
                _ghost.SetActive(true);
                return;
            }

            if (_ghost != null) Object.Destroy(_ghost);
            _ghost = BlockGhostFactory.Build(req.Definition, _matValid, req.Dims, req.Cell, req.Up, req.PitchDeg);
            _ghost.transform.SetParent(transform, worldPositionStays: false);
            _builtForId = id;
            _builtForDims = req.Dims;
            _builtForCell = req.Cell;
            _builtForUp = req.Up;
            _builtForPitch = req.PitchDeg;
            // Force material swap on the next validity check — fresh
            // ghost was authored with _matValid, so showingValid starts
            // true.
            _showingValid = true;
        }

        private void EnsureMirrorShape(in GhostRequest req)
        {
            string id = req.Definition.Id;
            if (_mirrorBuilt
                && _mirrorGhost != null
                && _mirrorBuiltForId == id
                && _mirrorBuiltForDims == req.Dims
                && _mirrorBuiltForCell == req.MirrorCell
                && _mirrorBuiltForUp == req.MirrorUp
                && Mathf.Approximately(_mirrorBuiltForPitch, req.MirrorPitchDeg))
            {
                _mirrorGhost.SetActive(true);
                return;
            }

            if (_mirrorGhost != null) Object.Destroy(_mirrorGhost);
            _mirrorGhost = BlockGhostFactory.Build(req.Definition, _matValid, req.Dims, req.MirrorCell, req.MirrorUp, req.MirrorPitchDeg);
            _mirrorGhost.transform.SetParent(transform, worldPositionStays: false);
            _mirrorBuilt = true;
            _mirrorBuiltForId = id;
            _mirrorBuiltForDims = req.Dims;
            _mirrorBuiltForCell = req.MirrorCell;
            _mirrorBuiltForUp = req.MirrorUp;
            _mirrorBuiltForPitch = req.MirrorPitchDeg;
            _mirrorShowingValid = true;
        }

        private static void PoseGhost(GameObject ghost, Vector3Int cell, Vector3Int up, BlockGrid grid, Transform chassisRoot)
        {
            if (ghost == null) return;
            ghost.SetActive(true);
            ghost.transform.position = grid.GridToWorld(cell);
            // Apply the per-cell rotation the placed block would get,
            // then the chassis world rotation. Without this, the ghost
            // stays axis-aligned and a foil/rotor preview wouldn't
            // reflect the mount face.
            ghost.transform.rotation = chassisRoot.rotation * BlockGrid.OrientationFromUp(up);
            // The ghost factory authored shapes at unit-cell scale, so
            // multiply by the grid's cell size with a tiny inflation
            // to avoid z-fighting with adjacent solid blocks.
            ghost.transform.localScale = Vector3.one * (grid.CellSize * 1.01f);
        }

        private void EnsureMaterials()
        {
            if (_matValid == null) _matValid = MakeMat(new Color(0.30f, 0.95f, 0.30f, 0.45f));
            if (_matInvalid == null) _matInvalid = MakeMat(new Color(0.95f, 0.25f, 0.20f, 0.45f));
        }

        private static Material MakeMat(Color c)
        {
            // URP/Unlit if available, else built-in Unlit. Both support _BaseColor / _Color.
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "Mat_BlockGhost" };
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

        private void DestroyAll()
        {
            if (_ghost != null) Object.Destroy(_ghost);
            if (_mirrorGhost != null) Object.Destroy(_mirrorGhost);
            if (_matValid != null) Object.Destroy(_matValid);
            if (_matInvalid != null) Object.Destroy(_matInvalid);
            _ghost = null;
            _mirrorGhost = null;
            _mirrorBuilt = false;
            _builtForId = null;
            _mirrorBuiltForId = null;
            _matValid = _matInvalid = null;
        }
    }
}
