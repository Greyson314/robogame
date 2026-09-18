// =============================================================================
// BuildRowShellTests — EditMode (CHG-027, F-051)
//
// WHAT THIS COVERS
//   SettingsHud.BuildRowShell(Transform, string, string, float) is the
//   de-duplicated row builder shared by AddActionRow, AddBisectToggleRow,
//   AddSliderRow and AddBoolRow: a row GameObject (LayoutElement height 44,
//   tinted Image background) with a single left-aligned label Text child.
//   Built into a throwaway Canvas so no scene / panel is needed — the
//   values asserted below are exactly what the four old inline blocks
//   produced (read from SettingsHud.cs before the CHG-027 edit): row name,
//   child count/names, LayoutElement height, Image colour, and the label
//   Text's RectTransform + font/size/colour/alignment/overflow.
//   labelWidth is the one value that differs per call site (360 / 420 /
//   220 / 560 across the four rows) — this test drives it as a parameter
//   the same way the real call sites do, using AddSliderRow's 220f.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class BuildRowShellTests
    {
        private GameObject _canvasGO;

        [TearDown]
        public void TearDown()
        {
            if (_canvasGO != null) Object.DestroyImmediate(_canvasGO);
        }

        [Test]
        public void BuildRowShell_MatchesOldInlineRowConstruction()
        {
            MethodInfo method = typeof(SettingsHud).GetMethod(
                "BuildRowShell", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "SettingsHud.BuildRowShell not found — has it been extracted from AddActionRow / AddBisectToggleRow / AddSliderRow / AddBoolRow yet (F-051)?");

            // Throwaway Canvas — BuildRowShell only needs a Transform parent
            // with a RectTransform, same as _content in the real panel.
            _canvasGO = new GameObject("ThrowawayCanvas", typeof(RectTransform), typeof(Canvas));
            Transform content = _canvasGO.transform;

            object boxedResult = method.Invoke(null, new object[] { content, "Row_Test", "Test Label", 220f });
            var result = ((GameObject row, Text label))boxedResult;

            GameObject row = result.row;
            Assert.IsNotNull(row, "BuildRowShell returned a null row.");
            Assert.AreEqual("Row_Test", row.name, "row name was not the 'name' argument (old code: NewChild(name/$\"...\", _content.transform)).");
            Assert.AreSame(content, row.transform.parent, "row was not parented under the given parent (old code: _content.transform).");

            // LayoutElement height 44 — identical across all four old call sites.
            var le = row.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "row has no LayoutElement.");
            Assert.AreEqual(44f, le.preferredHeight, "LayoutElement.preferredHeight was not 44 (old code: le.preferredHeight = 44f).");

            // Tinted background Image — identical Color across all four old call sites.
            var rowImg = row.GetComponent<Image>();
            Assert.IsNotNull(rowImg, "row has no background Image.");
            Color expectedRowColor = new Color(UguiPalette.Ink.r, UguiPalette.Ink.g, UguiPalette.Ink.b, 0.04f);
            Assert.AreEqual(expectedRowColor, rowImg.color, "row background Image colour changed from the old tinted-Ink value.");

            // Exactly one child: the Label.
            Assert.AreEqual(1, row.transform.childCount, "row has more than the single Label child.");
            Transform labelT = row.transform.GetChild(0);
            Assert.AreEqual("Label", labelT.name, "label child was not named 'Label'.");

            var labelRT = labelT.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(0f, 0f), labelRT.anchorMin, "Label anchorMin changed.");
            Assert.AreEqual(new Vector2(0f, 1f), labelRT.anchorMax, "Label anchorMax changed.");
            Assert.AreEqual(new Vector2(0f, 0.5f), labelRT.pivot, "Label pivot changed.");
            Assert.AreEqual(new Vector2(220f, 0f), labelRT.sizeDelta, "Label sizeDelta.x did not carry the labelWidth parameter.");
            Assert.AreEqual(new Vector2(12f, 0f), labelRT.anchoredPosition, "Label anchoredPosition changed.");

            Text labelText = labelT.GetComponent<Text>();
            Assert.IsNotNull(labelText, "Label child has no Text component.");
            Assert.AreSame(labelText, result.label, "returned label Text is not the same component on the Label child.");
            Assert.AreEqual("Test Label", labelText.text, "label text did not carry the 'label' argument.");
            Assert.AreEqual(InkKit.Display, labelText.font, "label font changed from UIFont (InkKit.Display).");
            Assert.AreEqual(18, labelText.fontSize, "label fontSize changed from 18.");
            Assert.AreEqual(UguiPalette.Text, labelText.color, "label colour changed from s_textColor (UguiPalette.Text).");
            Assert.AreEqual(TextAnchor.MiddleLeft, labelText.alignment, "label alignment changed from MiddleLeft.");
            Assert.AreEqual(VerticalWrapMode.Overflow, labelText.verticalOverflow, "label verticalOverflow changed from Overflow.");
        }
    }
}
