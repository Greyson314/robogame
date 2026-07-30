using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// Shared low-level uGUI construction helpers for the code-built HUD
    /// panels. One implementation of the "RectTransform child + legacy
    /// Text" idiom that was hand-copied (with four drifting AddText
    /// signatures) across eight Gameplay panels — a fix to canvas-child
    /// setup or font fallback now lands everywhere at once. Sits beside
    /// <see cref="HudStyles"/> (IMGUI) and <see cref="InkKit"/> (fonts).
    /// </summary>
    public static class UguiKit
    {
        /// <summary>New RectTransform child, parented without keeping world position.</summary>
        public static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        /// <summary>
        /// The full-parameter Text builder every panel-local AddText wrapper
        /// delegates to. Vertical overflow is always on (legacy Text
        /// truncates the whole line when a font's line box overruns a small
        /// rect — bit hard with Yuji Syuku's CJK metrics); horizontal
        /// overflow is opt-in for short static copy.
        /// </summary>
        public static Text AddText(
            Transform parent, string text, Font font, int size, FontStyle style,
            Color color, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
            bool raycastTarget = true, bool horizontalOverflow = false)
        {
            GameObject go = NewChild("Text", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = alignment;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (horizontalOverflow) t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = raycastTarget;
            return t;
        }
    }
}
