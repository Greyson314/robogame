// =============================================================================
// StyleButtonTests — EditMode (CHG-027, F-054)
//
// WHAT THIS COVERS
//   LabController.StyleButton(Button, normal, hover, pressed) is the
//   de-duplicated ColorBlock styler shared by the New-row button
//   (BuildNewRow), the header Close button (BuildHeader) and the Save
//   button (BuildActions) — three of the four F-054 call sites that share
//   this exact 3-colour shape (get colors, set normal/highlighted/pressed,
//   write back). Values below are the Save button's old literals
//   (LabController.cs BuildActions, before the CHG-027 edit): LabKit.
//   Bone() / Color.white / LabKit.Bone(0.82f).
//
// SCOPE NOTE — the row-select button (RefreshList) is not this test's job
//   That fourth call site ALSO sets ColorBlock.selectedColor = normalColor
//   after calling StyleButton — a per-site addition, not part of the
//   shared 3-colour shape the other three sites use, so it stays a direct
//   assignment at that call site (CHG-027 spec: "if a call site differs in
//   a way a parameter cannot carry cleanly, leave that site as it is").
//   This test instead asserts StyleButton itself leaves selectedColor and
//   disabledColor at the Button's own defaults, i.e. untouched.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class StyleButtonTests
    {
        private GameObject _buttonGO;

        [TearDown]
        public void TearDown()
        {
            if (_buttonGO != null) Object.DestroyImmediate(_buttonGO);
        }

        [Test]
        public void StyleButton_SetsNormalHoverPressed_LeavesSelectedAndDisabledDefault()
        {
            MethodInfo method = typeof(LabController).GetMethod(
                "StyleButton", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "LabController.StyleButton not found — has it been extracted from BuildNewRow / BuildHeader / BuildActions yet (F-054)?");

            _buttonGO = new GameObject("ThrowawayButton", typeof(RectTransform), typeof(Button));
            var button = _buttonGO.GetComponent<Button>();
            ColorBlock before = button.colors;

            // Same literals as the Save button in LabController.BuildActions
            // (before the CHG-027 edit): sc.normalColor = LabKit.Bone();
            // sc.highlightedColor = Color.white; sc.pressedColor = LabKit.Bone(0.82f).
            Color normal = LabKit.Bone();
            Color hover = Color.white;
            Color pressed = LabKit.Bone(0.82f);

            method.Invoke(null, new object[] { button, normal, hover, pressed });

            ColorBlock after = button.colors;
            Assert.AreEqual(normal, after.normalColor, "normalColor was not set.");
            Assert.AreEqual(hover, after.highlightedColor, "highlightedColor was not set.");
            Assert.AreEqual(pressed, after.pressedColor, "pressedColor was not set.");

            // Untouched by StyleButton — same as the Button's pre-call defaults.
            Assert.AreEqual(before.selectedColor, after.selectedColor, "selectedColor changed; StyleButton should leave it at the Button default.");
            Assert.AreEqual(before.disabledColor, after.disabledColor, "disabledColor changed; StyleButton should leave it at the Button default.");
            Assert.AreEqual(before.colorMultiplier, after.colorMultiplier, "colorMultiplier changed; StyleButton should leave it at the Button default.");
            Assert.AreEqual(before.fadeDuration, after.fadeDuration, "fadeDuration changed; StyleButton should leave it at the Button default.");
        }
    }
}
