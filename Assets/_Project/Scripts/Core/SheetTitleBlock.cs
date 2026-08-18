using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// The drafting title block — the bordered project/sheet/version grid
    /// a real blueprint carries in its bottom-right corner. One per screen;
    /// the sheet number is the project's screen-naming convention
    /// (no. 01 — home, no. 02 — the garage, …), which is what turns the
    /// scattered menus into pages of one notebook.
    /// </summary>
    // TRACE[DOC:research/ui-design-handoff-motion]: sheet-number convention.
    public static class SheetTitleBlock
    {
        private const float Width = 430f;
        private const float RowHeight = 41f;
        private const float LabelColWidth = 108f;

        /// <summary>
        /// Build the three-row block anchored to the parent's bottom-right
        /// (56 / 40 reference-px inset). Returns the root for entrance
        /// wiring (a CanvasGroup is attached, starting at alpha 1).
        /// </summary>
        public static GameObject Build(Transform parent,
            string projectValue, string projectItalic,
            string sheetValue, string versionValue, string versionItalic)
        {
            GameObject root = UguiKit.NewChild("SheetTitleBlock", parent);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(Width, RowHeight * 3f);
            rt.anchoredPosition = new Vector2(-56f, 40f);
            root.AddComponent<CanvasGroup>();

            // Paper backing so diagram strokes underneath dim to legible.
            var bg = UguiKit.NewChild("Bg", root.transform);
            Fill(bg);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = UguiPalette.PanelBg;
            bgImg.raycastTarget = false;

            // Outer border + inner rules, all frame-line ink.
            AddRule(root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), 1.6f);   // bottom
            AddRule(root.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), 1.6f);   // top
            AddVRule(root.transform, 0f, 1.6f);                                        // left
            AddVRule(root.transform, 1f, 1.6f);                                        // right
            AddRule(root.transform, new Vector2(0f, 1f / 3f), new Vector2(1f, 1f / 3f), 1f);
            AddRule(root.transform, new Vector2(0f, 2f / 3f), new Vector2(1f, 2f / 3f), 1f);
            AddVRuleAtPx(root.transform, LabelColWidth, 1f);

            Row(root.transform, 2, "project", projectValue, projectItalic);
            Row(root.transform, 1, "sheet", sheetValue, null);
            Row(root.transform, 0, "version", versionValue, versionItalic);
            return root;
        }

        private static void Row(Transform parent, int rowFromBottom, string label, string value, string italicSuffix)
        {
            float y0 = rowFromBottom * RowHeight;

            var k = UguiKit.AddText(parent, label.ToUpperInvariant(), InkKit.Annotation, 11, FontStyle.Normal,
                UguiPalette.TextDim, TextAnchor.MiddleLeft,
                anchorMin: Vector2.zero, anchorMax: Vector2.zero,
                offsetMin: new Vector2(12f, y0), offsetMax: new Vector2(LabelColWidth - 4f, y0 + RowHeight),
                raycastTarget: false);
            k.text = label.ToUpperInvariant();

            // Legacy Text can't mix styles inline; the italic tail is its own
            // Text so annotations keep the Space Mono italic voice.
            var v = UguiKit.AddText(parent, value, InkKit.Display, 17, FontStyle.Normal,
                HudStyles.TextPrimary, TextAnchor.MiddleLeft,
                anchorMin: Vector2.zero, anchorMax: Vector2.zero,
                offsetMin: new Vector2(LabelColWidth + 12f, y0), offsetMax: new Vector2(Width - 8f, y0 + RowHeight),
                raycastTarget: false, horizontalOverflow: true);

            if (!string.IsNullOrEmpty(italicSuffix))
            {
                float valueWidth = v.preferredWidth;
                UguiKit.AddText(parent, italicSuffix, InkKit.Annotation, 13, FontStyle.Italic,
                    UguiPalette.TextDim, TextAnchor.MiddleLeft,
                    anchorMin: Vector2.zero, anchorMax: Vector2.zero,
                    offsetMin: new Vector2(LabelColWidth + 18f + valueWidth, y0), offsetMax: new Vector2(Width - 6f, y0 + RowHeight),
                    raycastTarget: false, horizontalOverflow: true);
            }
        }

        private static void Fill(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddRule(Transform parent, Vector2 anchorMin, Vector2 anchorMax, float thickness)
        {
            var go = UguiKit.NewChild("Rule", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = UguiPalette.FrameLine;
            img.raycastTarget = false;
        }

        private static void AddVRule(Transform parent, float anchorX, float thickness)
        {
            var go = UguiKit.NewChild("Rule", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(anchorX, 0f);
            rt.anchorMax = new Vector2(anchorX, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(thickness, 0f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = UguiPalette.FrameLine;
            img.raycastTarget = false;
        }

        private static void AddVRuleAtPx(Transform parent, float xPx, float thickness)
        {
            var go = UguiKit.NewChild("Rule", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(thickness, 0f);
            rt.anchoredPosition = new Vector2(xPx, 0f);
            var img = go.AddComponent<Image>();
            img.color = UguiPalette.FrameLine;
            img.raycastTarget = false;
        }
    }
}
