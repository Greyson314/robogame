using Robogame.Block;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Surfaces the build-mode placement-rule rejection reason as a
    /// small bottom-right HUD label so the player can see *why* a
    /// placement is illegal rather than just a red ghost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spot fix for §3a Bug 4 / §3.10 of
    /// <c>docs/BUILDING_ARCHITECTURE_REVIEW.md</c>: targeting and rules
    /// answer different questions, and the player has no way to debug
    /// the mismatch from inside the game. The label converts the
    /// <see cref="PlacementRules.PlacementError"/> enum into one
    /// short human-readable line + the cell coordinates the rule was
    /// evaluated against.
    /// </para>
    /// <para>
    /// Driven by <see cref="BlockEditor"/> per-frame; the editor
    /// already evaluates the rules engine to drive ghost colour and
    /// just needs to forward the result.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlacementFeedbackHud : MonoBehaviour
    {
        private GameObject _hudRoot;
        private Text _label;

        private void Awake()
        {
            BuildHud();
            SetVisible(false);
        }

        /// <summary>
        /// Update the label. <paramref name="error"/> =
        /// <see cref="PlacementRules.PlacementError.None"/> hides it;
        /// any failure renders the cell coords + a short reason line.
        /// </summary>
        public void Show(PlacementRules.PlacementError error, Vector3Int targetCell, Vector3Int hostCell)
        {
            if (error == PlacementRules.PlacementError.None)
            {
                SetVisible(false);
                return;
            }
            if (_label == null) return;
            _label.text = $"Can't place at {targetCell}: {DescribeError(error, hostCell)}";
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        private static string DescribeError(PlacementRules.PlacementError e, Vector3Int hostCell)
        {
            switch (e)
            {
                case PlacementRules.PlacementError.CellOccupied:
                    return "cell already has a block.";
                case PlacementRules.PlacementError.HostMissing:
                    return $"no host block at {hostCell} — orbit the camera and aim at a cube face.";
                case PlacementRules.PlacementError.HostNotCpuReachable:
                    return $"host at {hostCell} isn't connected to the CPU.";
                case PlacementRules.PlacementError.HostIsLeaf:
                    return $"host at {hostCell} is a leaf (wing/weapon/thruster) — nothing builds on it.";
                case PlacementRules.PlacementError.HostFaceRejectsBlockType:
                    return $"host at {hostCell} doesn't accept this block type on that face — try a different block (e.g. aero on a rotor mechanism, hook/mace below a rope).";
                case PlacementRules.PlacementError.InvalidMountFace:
                    return "this block can only mount on side faces, not top/bottom.";
                case PlacementRules.PlacementError.SecondCpu:
                    return "chassis already has a CPU.";
                case PlacementRules.PlacementError.WouldOverlapNeighbour:
                    return "swept volume overlaps a neighbour — try a smaller span / different cell.";
                case PlacementRules.PlacementError.WouldOrphanOnRemoval:
                    return "removal would orphan blocks from the CPU.";
                default:
                    return e.ToString();
            }
        }

        private void SetVisible(bool visible)
        {
            if (_hudRoot != null) _hudRoot.SetActive(visible);
        }

        private void BuildHud()
        {
            _hudRoot = new GameObject("PlacementFeedbackHud");
            _hudRoot.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96;
            _hudRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _hudRoot.AddComponent<GraphicRaycaster>();

            var panel = NewChild("Panel", _hudRoot.transform);
            var prt = panel.GetComponent<RectTransform>();
            // Bottom-right pinning, with margin from edges so the label
            // doesn't crowd the existing build-mode HUD elements.
            prt.anchorMin = new Vector2(1f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(1f, 0f);
            prt.sizeDelta = new Vector2(520f, 32f);
            prt.anchoredPosition = new Vector2(-12f, 12f);
            panel.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.85f);

            _label = AddText(panel.transform);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static Text AddText(Transform parent)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 13;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleRight;
            t.color = new Color(1f, 0.5f, 0.4f, 1f);
            return t;
        }
    }
}
