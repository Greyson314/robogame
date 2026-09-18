// =============================================================================
// SettingsHudKeybindsSectionTests — PlayMode (CHG-033, F-052)
//
// WHAT THIS PINS
//   SettingsHud.BuildKeybindsSection + AddKeybindRow build a static
//   "Keybinds" reference section at the bottom of the settings panel: a
//   foldout GroupSection header ("Group_Keybinds") followed by one row per
//   bound key ("Key_<action>"), each a GameObject with an "Action" Text
//   child (left) and a "Keys" Text child (right, bold, group colour). The
//   section is collapsed by default (Expanded = false after construction)
//   and its rows ride the same search/foldout pipeline as the Tweakable
//   rows via a stub Tweakables.Spec.
//
//   This test reaches SettingsHud's private fields/types by reflection
//   (BindingFlags.NonPublic | Instance) because SettingsHud exposes no
//   public surface for panel internals — CHG-033's extraction only widens
//   GroupSection/RowEntry from `private` to `internal` (a visibility-only
//   change), so this same reflection (NonPublic matches internal members
//   too) keeps working unchanged after the extraction.
//
// RED-FIRST
//   Green on today's code (commit 1, extraction not yet applied). Written
//   before the extraction so it pins today's construction order, text and
//   default-collapsed state. It would go RED under a mutation such as
//   changing the "Fire primary" row's key text from "Mouse 1" to
//   "Mouse Left" (the DumpKeybindRows assertion checks each row's action
//   AND key text verbatim, in order) — that mutation is NOT committed here,
//   only described, per the CHG-033 brief.
//
// AFTER THE EXTRACTION (commit 2)
//   Same test, same assertions, same hierarchy dump — proving
//   KeybindsSectionBuilder reproduces BuildKeybindsSection/AddKeybindRow
//   field-for-field.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Robogame.Tests.PlayMode.Gameplay
{
    public sealed class SettingsHudKeybindsSectionTests
    {
        private GameObject _hostGo;

        // The exact (action, keys) pairs BuildKeybindsSection adds, in
        // order, copied from SettingsHud.cs. The last two lines are dev-
        // only (Tweakables.DevSurfacesVisible) rows.
        private static readonly (string action, string keys)[] s_expectedAlways =
        {
            ("Pitch / throttle",         "W / S"),
            ("Steer / roll",             "A / D"),
            ("Vertical (jump / climb)",  "Space"),
            ("Fire primary",             "Mouse 1"),
            ("Aim down sights",          "Mouse 2 (hold)"),
            ("Camera zoom (orbit)",      "Mouse wheel"),
            ("Release grapples",         "R"),
            ("Respawn player",           "K"),
            ("Begin combat (warmup → live)", "`  (backtick)"),
            ("Toggle settings",          "Esc"),
        };
        private static readonly (string action, string keys) s_expectedDevOnly =
            ("Toggle dev HUD", "F1");
        private static readonly (string action, string keys)[] s_expectedBuildRows =
        {
            ("Build: place / remove",        "Mouse 1 / Mouse 2"),
            ("Build: rotate before placing", "R"),
            ("Build: copy block settings",   "Mouse 3"),
            ("Build: tuning mode (re-tune a placed block)", "T"),
            ("Build: move mode (pick up + re-place)",       "V"),
            ("Build: mirror mode / mirror axis", "M / B"),
            ("Build: centers overlay (mass / lift / thrust)", "G"),
            ("Build: hotbar slots / category", "1–9, Q / E"),
        };

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) Object.Destroy(_hostGo);
        }

        private static List<(string action, string keys)> ExpectedRows()
        {
            var list = new List<(string, string)>(s_expectedAlways);
            if (Tweakables.DevSurfacesVisible) list.Add(s_expectedDevOnly);
            list.AddRange(s_expectedBuildRows);
            return list;
        }

        // -- reflection helpers ---------------------------------------------

        private static object GetPrivate(object instance, string fieldName)
        {
            FieldInfo fi = instance.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"expected a field named \"{fieldName}\" on {instance.GetType()}.");
            return fi.GetValue(instance);
        }

        private static void InvokePrivate(object instance, string methodName, params object[] args)
        {
            MethodInfo mi = instance.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, $"expected a method named \"{methodName}\" on {instance.GetType()}.");
            mi.Invoke(instance, args);
        }

        // Walks _content's children in sibling order and returns every
        // "Key_*" row as (rowName, action text, keys text, active).
        private static List<(string rowName, string action, string keys, bool active)> DumpKeybindRows(Transform content)
        {
            var rows = new List<(string, string, string, bool)>();
            for (int i = 0; i < content.childCount; i++)
            {
                Transform child = content.GetChild(i);
                if (!child.name.StartsWith("Key_")) continue;
                Text actionText = child.Find("Action")?.GetComponent<Text>();
                Text keysText = child.Find("Keys")?.GetComponent<Text>();
                Assert.IsNotNull(actionText, $"{child.name}: missing Action text child.");
                Assert.IsNotNull(keysText, $"{child.name}: missing Keys text child.");
                rows.Add((child.name, actionText.text, keysText.text, child.gameObject.activeSelf));
            }
            return rows;
        }

        private static string FormatDump(List<(string rowName, string action, string keys, bool active)> rows, GameObject header)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CHG-033] Keybinds section hierarchy dump");
            sb.AppendLine($"  header: {(header != null ? header.name : "<null>")} active={(header != null ? header.activeSelf.ToString() : "n/a")}");
            sb.AppendLine($"  row count: {rows.Count}");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine($"  [{i}] {r.rowName} | Action=\"{r.action}\" | Keys=\"{r.keys}\" | active={r.active}");
            }
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator BuildKeybindsSection_PinsRowOrderTextAndDefaultCollapsedState()
        {
            _hostGo = new GameObject("SettingsHudHost");
            SettingsHud hud = _hostGo.AddComponent<SettingsHud>();
            yield return null;

            var content = (GameObject)GetPrivate(hud, "_content");
            Assert.IsNotNull(content, "SettingsHud._content was not built.");

            GameObject header = content.transform.Find("Group_Keybinds")?.gameObject;
            Assert.IsNotNull(header, "expected a \"Group_Keybinds\" foldout header under _content.");

            List<(string rowName, string action, string keys, bool active)> rows = DumpKeybindRows(content.transform);
            string dump = FormatDump(rows, header);
            Debug.Log(dump); // written to the test-run log for the before/after diff, per the CHG-033 brief

            List<(string action, string keys)> expected = ExpectedRows();
            Assert.AreEqual(expected.Count, rows.Count, "keybinds row count drifted.\n" + dump);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].action, rows[i].action, $"row {i} action text drifted.\n" + dump);
                Assert.AreEqual(expected[i].keys, rows[i].keys, $"row {i} keys text drifted.\n" + dump);
                Assert.AreEqual($"Key_{expected[i].action}", rows[i].rowName, $"row {i} GameObject name drifted.\n" + dump);
                // Collapsed by default: BuildKeybindsSection sets
                // g.Expanded = false AFTER adding rows, then calls
                // ApplyGroupExpansion, which SetActive(false)s every row.
                Assert.IsFalse(rows[i].active, $"row {i} ({rows[i].rowName}) should start collapsed (inactive).\n" + dump);
            }

            // The GroupSection itself: reached via the private _groups
            // dictionary (NonPublic reflection — see file header).
            object groups = GetPrivate(hud, "_groups");
            var groupsDict = (System.Collections.IDictionary)groups;
            Assert.IsTrue(groupsDict.Contains("Keybinds"), "no GroupSection registered under \"Keybinds\".");
            object keybindsSection = groupsDict["Keybinds"];
            var expandedField = keybindsSection.GetType().GetField("Expanded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(expandedField);
            Assert.IsFalse((bool)expandedField.GetValue(keybindsSection), "Keybinds GroupSection.Expanded should be false by default.");
            var rowsField = keybindsSection.GetType().GetField("Rows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var sectionRows = (System.Collections.ICollection)rowsField.GetValue(keybindsSection);
            Assert.AreEqual(expected.Count, sectionRows.Count, "GroupSection.Rows count drifted.\n" + dump);
        }

        [UnityTest]
        public IEnumerator SearchFilter_ShowsOnlyKeybindRowsMatchingKeyLabel_IgnoringFoldState()
        {
            _hostGo = new GameObject("SettingsHudHost2");
            SettingsHud hud = _hostGo.AddComponent<SettingsHud>();
            yield return null;

            var content = (GameObject)GetPrivate(hud, "_content");

            // "backtick" appears only in the "Begin combat" row's key text
            // ("`  (backtick)") — a unique needle across every row in the
            // panel (Tweakable rows included), so a false-positive match
            // would mean the filter is over- or under-matching.
            InvokePrivate(hud, "OnSearchChanged", "backtick");
            yield return null;

            List<(string rowName, string action, string keys, bool active)> rows = DumpKeybindRows(content.transform);
            string dump = FormatDump(rows, content.transform.Find("Group_Keybinds")?.gameObject);
            Debug.Log("[CHG-033] search=\"backtick\"\n" + dump);

            foreach (var r in rows)
            {
                bool shouldMatch = r.action == "Begin combat (warmup → live)";
                Assert.AreEqual(shouldMatch, r.active,
                    $"row {r.rowName} active={r.active}, expected {shouldMatch} while searching \"backtick\".\n" + dump);
            }

            // Header stays visible because at least one row matched.
            GameObject header = content.transform.Find("Group_Keybinds")?.gameObject;
            Assert.IsTrue(header.activeSelf, "Keybinds header should stay visible: one row matched the search.\n" + dump);

            // Clearing the search restores the collapsed state (search
            // ignores fold state; clearing search re-applies it).
            InvokePrivate(hud, "OnSearchChanged", "");
            yield return null;
            rows = DumpKeybindRows(content.transform);
            foreach (var r in rows)
                Assert.IsFalse(r.active, $"row {r.rowName} should re-collapse once the search is cleared.");
        }
    }
}
