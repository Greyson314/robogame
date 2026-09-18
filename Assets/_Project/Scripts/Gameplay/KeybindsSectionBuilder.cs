using System.Collections.Generic;
using Robogame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Builds the static keybinds reference section at the bottom of
    /// <see cref="SettingsHud"/>'s panel. Extracted from SettingsHud
    /// (F-052): the section is a static reference table with stub
    /// <see cref="Tweakables.Spec"/> entries just to ride the panel's
    /// search/foldout pipeline — it needs only the content parent and the
    /// group-header helpers SettingsHud already owns, none of the
    /// Tweakable-row-building responsibility.
    /// </summary>
    // TRACE[F-052]: static keybinds table split out of SettingsHud's god
    // class; zero behaviour change (CHG-033).
    internal static class KeybindsSectionBuilder
    {
        /// <summary>
        /// Builds the "Keybinds" foldout group and its rows under
        /// <paramref name="content"/>, exactly as SettingsHud's own
        /// BuildKeybindsSection/AddKeybindRow used to.
        /// </summary>
        /// <param name="content">SettingsHud's content parent (_content.transform).</param>
        /// <param name="uiFont">SettingsHud.UIFont.</param>
        /// <param name="textColor">SettingsHud.s_textColor.</param>
        /// <param name="groupColor">SettingsHud.s_groupColor.</param>
        /// <param name="devSurfacesVisible">Tweakables.DevSurfacesVisible.</param>
        /// <param name="addFoldoutGroupHeader">SettingsHud.AddFoldoutGroupHeader.</param>
        /// <param name="applyGroupExpansion">SettingsHud.ApplyGroupExpansion.</param>
        /// <param name="allRows">SettingsHud._allRows.</param>
        public static void Build(
            Transform content,
            Font uiFont,
            Color textColor,
            Color groupColor,
            bool devSurfacesVisible,
            System.Func<string, SettingsHud.GroupSection> addFoldoutGroupHeader,
            System.Action<SettingsHud.GroupSection> applyGroupExpansion,
            List<SettingsHud.RowEntry> allRows)
        {
            // Use a foldout group so keybinds nest into the same UI grammar
            // as Tweakables groups. We mark it collapsed-by-default AFTER
            // adding rows: ApplyGroupExpansion only walks Rows, so an
            // empty Rows list at construction time wouldn't hide newly
            // appended rows.
            SettingsHud.GroupSection g = addFoldoutGroupHeader("Keybinds");

            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Pitch / throttle",         "W / S");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Steer / roll",             "A / D");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Vertical (jump / climb)",  "Space");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Fire primary",             "Mouse 1");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Aim down sights",          "Mouse 2 (hold)");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Camera zoom (orbit)",      "Mouse wheel");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Release grapples",         "R");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Respawn player",           "K");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Begin combat (warmup → live)", "`  (backtick)");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Toggle settings",          "Esc");
            if (devSurfacesVisible)
                AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Toggle dev HUD",       "F1");

            // Build-mode keys were previously undocumented anywhere in the
            // game (R = rotate had NO on-screen hint at all) — 169.
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: place / remove",        "Mouse 1 / Mouse 2");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: rotate before placing", "R");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: copy block settings",   "Mouse 3");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: tuning mode (re-tune a placed block)", "T");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: move mode (pick up + re-place)",       "V");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: mirror mode / mirror axis", "M / B");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: centers overlay (mass / lift / thrust)", "G");
            AddKeybindRow(g, content, uiFont, textColor, groupColor, allRows, "Build: hotbar slots / category", "1–9, Q / E");

            g.Expanded = false;
            applyGroupExpansion(g);
        }

        private static void AddKeybindRow(
            SettingsHud.GroupSection g,
            Transform content,
            Font uiFont,
            Color textColor,
            Color groupColor,
            List<SettingsHud.RowEntry> allRows,
            string action,
            string keys)
        {
            var rowGO = UguiKit.NewChild($"Key_{action}", content);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;
            rowGO.AddComponent<Image>().color = new Color(UguiPalette.Ink.r, UguiPalette.Ink.g, UguiPalette.Ink.b, 0.025f);

            var actionGO = UguiKit.NewChild("Action", rowGO.transform);
            var actionRT = actionGO.GetComponent<RectTransform>();
            actionRT.anchorMin = new Vector2(0f, 0f);
            actionRT.anchorMax = new Vector2(0.6f, 1f);
            actionRT.offsetMin = new Vector2(20f, 0f);
            actionRT.offsetMax = new Vector2(-8f, 0f);
            var actionText = actionGO.AddComponent<Text>();
            actionText.text = action;
            actionText.font = uiFont;
            actionText.fontSize = 16;
            actionText.color = textColor;
            actionText.alignment = TextAnchor.MiddleLeft;
            actionText.verticalOverflow = VerticalWrapMode.Overflow;

            var keyGO = UguiKit.NewChild("Keys", rowGO.transform);
            var keyRT = keyGO.GetComponent<RectTransform>();
            keyRT.anchorMin = new Vector2(0.6f, 0f);
            keyRT.anchorMax = new Vector2(1f, 1f);
            keyRT.offsetMin = new Vector2(8f, 0f);
            keyRT.offsetMax = new Vector2(-12f, 0f);
            var keyText = keyGO.AddComponent<Text>();
            keyText.text = keys;
            keyText.font = uiFont;
            keyText.fontSize = 16;
            keyText.fontStyle = FontStyle.Bold;
            keyText.color = groupColor;
            keyText.alignment = TextAnchor.MiddleRight;
            keyText.verticalOverflow = VerticalWrapMode.Overflow;

            // Stub spec so the row shares the visibility / search pipeline
            // even though there's nothing to tweak. Keys field carries the
            // bound shortcut so the search filter matches it.
            var stubSpec = new Tweakables.Spec("__keybind__" + action, "Keybinds", action + " — " + keys, 0f, 0f, 1f, Tweakables.SpecKind.Bool);
            var entry = new SettingsHud.RowEntry { Spec = stubSpec, Row = rowGO };
            g.Rows.Add(entry);
            allRows.Add(entry);
        }
    }
}
