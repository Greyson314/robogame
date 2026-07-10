using Robogame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Full-screen edge frame + "BUILD MODE" tag shown only while build
    /// mode is active. The single strongest signifier of which garage
    /// state the player is in — before this, build vs. hangar read only
    /// from small HUD differences and the player couldn't always tell
    /// which mode they were in (session 138 UX pass).
    /// </summary>
    /// <remarks>
    /// Pure chrome: every graphic has <c>raycastTarget = false</c> so the
    /// frame can never eat a click aimed at the world or another HUD.
    /// Sorting order sits BELOW the build panels (hotbar 9x range) so the
    /// frame underlines them rather than covering them. Wired by
    /// <see cref="GarageController.EnsureBuildModeWired"/>, same lifecycle
    /// as the other build-HUD components.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildModeFrame : MonoBehaviour
    {
        private const float BarThickness = 4f;

        [SerializeField] private BuildModeController _buildMode;

        private GameObject _root;

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode != null) { _buildMode.Entered -= HandleEntered; _buildMode.Exited -= HandleExited; }
                _buildMode = value;
                if (_buildMode != null) { _buildMode.Entered += HandleEntered; _buildMode.Exited += HandleExited; }
                SetVisible(_buildMode != null && _buildMode.IsActive);
            }
        }

        private void Awake()
        {
            BuildCanvas();
            SetVisible(_buildMode != null && _buildMode.IsActive);
            if (_buildMode != null) { _buildMode.Entered += HandleEntered; _buildMode.Exited += HandleExited; }
        }

        private void OnDestroy()
        {
            if (_buildMode != null) { _buildMode.Entered -= HandleEntered; _buildMode.Exited -= HandleExited; }
        }

        private void HandleEntered() => SetVisible(true);
        private void HandleExited()  => SetVisible(false);

        private void SetVisible(bool v)
        {
            if (_root != null && _root.activeSelf != v) _root.SetActive(v);
        }

        private void BuildCanvas()
        {
            _root = new GameObject("BuildModeFrame");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the build HUD canvases (edit button 95, variant panel 96)
            // so the frame reads as the room's wallpaper, not a lid.
            canvas.sortingOrder = 90;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster on purpose — chrome only, never clickable.

            Color accent = UguiPalette.Accent;
            accent.a = 0.85f;

            // Four hairline bars hugging the screen edges.
            AddBar("Top",    anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f), size: new Vector2(0f, BarThickness), accent);
            AddBar("Bottom", anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f), size: new Vector2(0f, BarThickness), accent);
            AddBar("Left",   anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 0.5f), size: new Vector2(BarThickness, 0f), accent);
            AddBar("Right",  anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(1f, 0.5f), size: new Vector2(BarThickness, 0f), accent);

            // Corner tag — top-left, clear of the mirror banner (top-center),
            // variant panel (top-right) and the CenterOverlay legend (mid-left).
            var tag = new GameObject("Tag", typeof(RectTransform));
            tag.transform.SetParent(_root.transform, worldPositionStays: false);
            var trt = tag.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(150f, 26f);
            trt.anchoredPosition = new Vector2(BarThickness + 8f, -(BarThickness + 8f));
            var tagBg = tag.AddComponent<Image>();
            tagBg.color = accent;
            tagBg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(tag.transform, worldPositionStays: false);
            var xrt = textGo.GetComponent<RectTransform>();
            xrt.anchorMin = Vector2.zero;
            xrt.anchorMax = Vector2.one;
            xrt.offsetMin = new Vector2(8f, 0f);
            xrt.offsetMax = new Vector2(-8f, 0f);
            var text = textGo.AddComponent<Text>();
            // Plain ASCII — glyph coverage in InkKit.Display is not
            // guaranteed for dingbats, and a tofu box would undermine
            // the one label whose job is legibility.
            text.text = "BUILD MODE";
            text.font = InkKit.Display;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = UguiPalette.CreamText;
            text.raycastTarget = false;
        }

        private void AddBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
    }
}
