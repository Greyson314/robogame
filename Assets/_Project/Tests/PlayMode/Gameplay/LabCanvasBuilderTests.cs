// =============================================================================
// LabCanvasBuilderTests — PlayMode (CHG-035, F-053)
//
// WHAT THIS PINS
//   LabController.BuildCanvas builds the Lab's "night workshop" UGUI scene
//   under _root at Awake: Ground (soot + blotches + fog banks), Panel
//   (shadow/face/border/sheen/pool/catchlight + four screws), Btn_Close,
//   Well (the concoctions journal shell), Switchboard (Plate with five
//   slider rows, NameField, Btn_Save, Btn_Delete) and Vial (tube, liquid,
//   bubbles, cork, seal). This test dumps that whole hierarchy (names,
//   sibling order, RectTransform anchors/sizes, Image/Text colours and
//   strings, Button/Slider/InputField wiring counts) to the log for a
//   before/after diff, and asserts the landmark structural facts a
//   refactor could silently drop: which nodes exist, where, and that each
//   interactive widget still carries exactly the one listener it always
//   did.
//
// RED-FIRST / GREEN-ON-OLD-CODE
//   This is a pinning test for a zero-behaviour-change refactor (CHG-035,
//   F-053: LabCanvasBuilder extracts LabController's one-shot decorative
//   construction). Per the brief it is GREEN on today's LabController (one
//   big class) and must stay GREEN, with a byte-identical dump, once
//   BuildGround/BuildScrews/BuildHeader/BuildJournal/BuildSwitchboard/
//   BuildSliderRow/BuildNameField/BuildActions/BuildVial move into
//   LabCanvasBuilder. The full-fidelity check is external: diff this
//   test's dump section between the TestRun log at the base commit and the
//   TestRun log after the extraction (see the CHG-035 report for the exact
//   diff command). The in-test asserts below are a second, independent
//   check that would fail on a mutation such as a dropped listener, a
//   renamed/reparented node, or a missing slider row.
// =============================================================================

using System.Collections;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Robogame.Tests.PlayMode.Gameplay
{
    public sealed class LabCanvasBuilderTests
    {
        private GameObject _hostGo;

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) Object.Destroy(_hostGo);
        }

        // -- reflection helpers ---------------------------------------------

        private static object GetPrivate(object instance, string fieldName)
        {
            FieldInfo fi = instance.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"expected a field named \"{fieldName}\" on {instance.GetType()}.");
            return fi.GetValue(instance);
        }

        // Counts a UnityEvent's runtime (AddListener) subscribers via the
        // same private InvokableCallList Unity's own UI uses internally.
        // GetPersistentEventCount() only sees editor-serialized listeners
        // (always 0 for code-built UI), so this is the only way to prove a
        // .AddListener call survived the extraction. Fails loudly (does not
        // swallow) if Unity's internal layout ever changes.
        private static int CountEventListeners(UnityEventBase evt)
        {
            Assert.IsNotNull(evt, "event to count listeners on was null.");
            FieldInfo callsField = typeof(UnityEventBase).GetField("m_Calls", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(callsField, "UnityEventBase.m_Calls reflection target missing -- Unity internals changed.");
            object calls = callsField.GetValue(evt);
            Assert.IsNotNull(calls, "UnityEventBase.m_Calls was null.");
            FieldInfo runtimeCallsField = calls.GetType().GetField("m_RuntimeCalls", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(runtimeCallsField, "InvokableCallList.m_RuntimeCalls reflection target missing -- Unity internals changed.");
            object runtimeCalls = runtimeCallsField.GetValue(calls);
            Assert.IsNotNull(runtimeCalls, "InvokableCallList.m_RuntimeCalls was null.");
            return ((IList)runtimeCalls).Count;
        }

        // -- hierarchy dump ---------------------------------------------------

        private static void DumpNode(Transform t, StringBuilder sb, int depth)
        {
            var rt = t as RectTransform;
            sb.Append(' ', depth * 2);
            sb.Append(t.name).Append(" active=").Append(t.gameObject.activeSelf)
              .Append(" sibling=").Append(t.GetSiblingIndex());
            if (rt != null)
            {
                sb.Append(" anchorMin=").Append(rt.anchorMin).Append(" anchorMax=").Append(rt.anchorMax)
                  .Append(" pivot=").Append(rt.pivot).Append(" sizeDelta=").Append(rt.sizeDelta)
                  .Append(" anchoredPosition=").Append(rt.anchoredPosition);
            }
            if (t.localScale != Vector3.one) sb.Append(" localScale=").Append(t.localScale);
            if (t.localRotation != Quaternion.identity) sb.Append(" localRotationZ=").Append(t.localRotation.eulerAngles.z);

            var img = t.GetComponent<Image>();
            if (img != null)
                sb.Append(" Image[sprite=").Append(img.sprite != null ? img.sprite.name : "<null>")
                  .Append(" color=").Append(img.color).Append(" type=").Append(img.type)
                  .Append(" raycast=").Append(img.raycastTarget).Append(']');

            var txt = t.GetComponent<Text>();
            if (txt != null)
                sb.Append(" Text[\"").Append(txt.text).Append("\" font=").Append(txt.font != null ? txt.font.name : "<null>")
                  .Append(" size=").Append(txt.fontSize).Append(" style=").Append(txt.fontStyle)
                  .Append(" color=").Append(txt.color).Append(" align=").Append(txt.alignment).Append(']');

            var btn = t.GetComponent<Button>();
            if (btn != null)
                sb.Append(" Button[onClickListeners=").Append(CountEventListeners(btn.onClick))
                  .Append(" transition=").Append(btn.transition).Append(']');

            var sld = t.GetComponent<Slider>();
            if (sld != null)
                sb.Append(" Slider[min=").Append(sld.minValue).Append(" max=").Append(sld.maxValue)
                  .Append(" value=").Append(sld.value)
                  .Append(" onValueChangedListeners=").Append(CountEventListeners(sld.onValueChanged)).Append(']');

            var input = t.GetComponent<InputField>();
            if (input != null)
                sb.Append(" InputField[charLimit=").Append(input.characterLimit)
                  .Append(" onValueChangedListeners=").Append(CountEventListeners(input.onValueChanged)).Append(']');

            var mask = t.GetComponent<Mask>();
            if (mask != null) sb.Append(" Mask[showMaskGraphic=").Append(mask.showMaskGraphic).Append(']');

            if (t.GetComponent<RectMask2D>() != null) sb.Append(" RectMask2D");

            var trig = t.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trig != null) sb.Append(" EventTrigger[entries=").Append(trig.triggers.Count).Append(']');

            var canvas = t.GetComponent<Canvas>();
            if (canvas != null)
                sb.Append(" Canvas[renderMode=").Append(canvas.renderMode).Append(" sortingOrder=").Append(canvas.sortingOrder).Append(']');

            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++)
                DumpNode(t.GetChild(i), sb, depth + 1);
        }

        [UnityTest]
        public IEnumerator BuildCanvas_PinsTheFullLabHierarchy()
        {
            _hostGo = new GameObject("LabControllerHost");
            LabController lab = _hostGo.AddComponent<LabController>();
            yield return null;

            GameObject root = (GameObject)GetPrivate(lab, "_root");
            Assert.IsNotNull(root, "LabController._root was not built by Awake/BuildCanvas.");
            Assert.AreEqual("LabCanvas", root.name, "the canvas root's name drifted.");

            var sb = new StringBuilder();
            sb.Append("[CHG-035] Lab canvas hierarchy dump\n");
            DumpNode(root.transform, sb, 0);
            string dump = sb.ToString();
            Debug.Log(dump); // captured for the before/after TestRun-log diff, per the CHG-035 brief

            // Landmark structural facts -- enough to fail if the extraction
            // drops, renames or reparents anything load-bearing. Full
            // fidelity is the before/after log-dump diff (see the report).
            Assert.IsNotNull(root.transform.Find("Ground"), "missing Ground.\n" + dump);
            Transform panel = root.transform.Find("Panel");
            Assert.IsNotNull(panel, "missing Panel.\n" + dump);
            Assert.IsNotNull(panel.Find("Btn_Close"), "missing Panel/Btn_Close.\n" + dump);
            Assert.IsNotNull(panel.Find("Well"), "missing Panel/Well.\n" + dump);
            Assert.IsNotNull(panel.Find("Vial"), "missing Panel/Vial.\n" + dump);
            Transform switchboard = panel.Find("Switchboard");
            Assert.IsNotNull(switchboard, "missing Panel/Switchboard.\n" + dump);
            Assert.IsNotNull(switchboard.Find("Plate"), "missing Switchboard/Plate.\n" + dump);
            Assert.IsNotNull(switchboard.Find("NameField"), "missing Switchboard/NameField.\n" + dump);
            Assert.IsNotNull(switchboard.Find("Btn_Save"), "missing Switchboard/Btn_Save.\n" + dump);
            Assert.IsNotNull(switchboard.Find("Btn_Delete"), "missing Switchboard/Btn_Delete.\n" + dump);

            // The five slider rows, each wired to exactly one onValueChanged
            // listener (LabController.OnDmgChanged/OnSizeChanged/...).
            foreach (string label in new[] { "Damage", "Size", "Knockback", "Speed", "Spread" })
            {
                Transform row = switchboard.Find($"Plate/Row_{label}");
                Assert.IsNotNull(row, $"missing slider row '{label}'.\n{dump}");
                Slider slider = row.Find("Slider")?.GetComponent<Slider>();
                Assert.IsNotNull(slider, $"row '{label}' has no Slider component.\n{dump}");
                Assert.AreEqual(1, CountEventListeners(slider.onValueChanged),
                    $"row '{label}' slider onValueChanged listener count drifted.\n{dump}");
            }

            InputField nameField = switchboard.Find("NameField")?.GetComponent<InputField>();
            Assert.IsNotNull(nameField, "NameField has no InputField component.\n" + dump);
            Assert.AreEqual(1, CountEventListeners(nameField.onValueChanged),
                "NameField onValueChanged listener count drifted (LabController.OnNameEdited).\n" + dump);

            Button saveBtn = switchboard.Find("Btn_Save")?.GetComponent<Button>();
            Assert.IsNotNull(saveBtn, "Btn_Save has no Button component.\n" + dump);
            Assert.AreEqual(1, CountEventListeners(saveBtn.onClick),
                "Btn_Save onClick listener count drifted (LabController.Save).\n" + dump);

            Button deleteBtn = switchboard.Find("Btn_Delete")?.GetComponent<Button>();
            Assert.IsNotNull(deleteBtn, "Btn_Delete has no Button component.\n" + dump);
            Assert.AreEqual(1, CountEventListeners(deleteBtn.onClick),
                "Btn_Delete onClick listener count drifted (LabController.DeleteCurrent).\n" + dump);

            Button closeBtn = panel.Find("Btn_Close")?.GetComponent<Button>();
            Assert.IsNotNull(closeBtn, "Btn_Close has no Button component.\n" + dump);
            Assert.AreEqual(1, CountEventListeners(closeBtn.onClick),
                "Btn_Close onClick listener count drifted (LabController.SetOpen(false)).\n" + dump);
        }
    }
}
