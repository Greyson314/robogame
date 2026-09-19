using System;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// The Laboratory's one-shot decorative and widget UGUI construction —
    /// the "night workshop" scene under <see cref="LabController"/>'s
    /// <c>_root</c> canvas (ground, panel screws/header/well/switchboard
    /// levers/name field/actions, specimen vial). Extracted from
    /// LabController (F-053): pure layout scaffolding with zero Concoction-
    /// editing logic. LabController.BuildCanvas still owns the canvas root
    /// and the panel shell (shadow/face/border/sheen/pool/catchlight) —
    /// only the named methods below moved — and wires the callbacks these
    /// builders need back to its own editor-state methods; it never hands
    /// this class its private editor fields. RefreshList/BuildNewRow/
    /// BuildRowShell stay on LabController (they rebuild against live
    /// editor state on every list refresh, not once at Awake), as do the
    /// shared UGUI primitives (Stretch/NewChild/AddImage/AddText/
    /// StyleButton) and the two fonts (UIFont/AnnoFont), widened from
    /// private to internal there since both classes use them (F-053's
    /// "only what they alone use" -- these are used elsewhere too).
    /// </summary>
    // TRACE[F-053]: one-shot Lab canvas construction split out of
    // LabController's god class; zero behaviour change (CHG-035).
    internal static class LabCanvasBuilder
    {
        // -----------------------------------------------------------------
        // Ground: soot ground, blotches, 2.5D fog banks.
        // -----------------------------------------------------------------

        /// <summary>
        /// Builds the "Ground" layer under <paramref name="root"/> and fills
        /// <paramref name="fog"/> (LabController's fixed length-3 _fog
        /// array) with the built fog-bank images, exactly as
        /// LabController's own BuildGround used to.
        /// </summary>
        public static void BuildGround(Transform root, Image[] fog)
        {
            var ground = LabController.NewChild("Ground", root);
            LabController.Stretch(ground.GetComponent<RectTransform>());
            var g = ground.AddComponent<Image>();
            g.sprite = LabKit.Ground;
            g.color = Color.white;      // eats clicks behind the panel

            // Uneven soot blotches (indigo cool / brass warm / pooled black).
            PlaceBlotch(ground.transform, 0.18f, 0.22f, 500f, 340f, LabKit.IndigoWash(0.07f));
            PlaceBlotch(ground.transform, 0.84f, 0.84f, 620f, 420f, LabKit.Brass(0.05f));
            PlaceBlotch(ground.transform, 0.70f, 0.10f, 400f, 300f, LabKit.Shade(0.35f));

            // 2.5D fog banks (no drafting grid / registration marks here —
            // the night workshop keeps its haze, not the blueprint chrome).
            // Far → near: smaller/dimmer/cooler back, bigger/brighter front.
            fog[0] = BuildFogBank(ground.transform, LabKit.FogA, new Vector2(1500f, 460f),
                new Vector2(LabController.s_fogBaseX[0], LabController.s_fogY[0]), LabKit.IndigoWash(0.10f));
            fog[1] = BuildFogBank(ground.transform, LabKit.FogB, new Vector2(1900f, 560f),
                new Vector2(LabController.s_fogBaseX[1], LabController.s_fogY[1]), LabKit.Bone(0.055f));
            fog[2] = BuildFogBank(ground.transform, LabKit.FogA, new Vector2(2400f, 680f),
                new Vector2(LabController.s_fogBaseX[2], LabController.s_fogY[2]), LabKit.Bone(0.085f));
        }

        private static Image BuildFogBank(Transform parent, Sprite sprite, Vector2 size, Vector2 pos, Color tint)
        {
            var img = LabController.AddImage(parent, sprite, tint, raycast: false);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        private static void PlaceBlotch(Transform parent, float ax, float ay, float w, float h, Color color)
        {
            var img = LabController.AddImage(parent, LabKit.Glow, color, raycast: false);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
        }

        // -----------------------------------------------------------------
        // Screws.
        // -----------------------------------------------------------------

        // Four brass screw heads, each slot at its own lazy angle.
        public static void BuildScrews(Transform panel)
        {
            BuildScrew(panel, 0f, 1f, 40f);
            BuildScrew(panel, 1f, 1f, -15f);
            BuildScrew(panel, 0f, 0f, 75f);
            BuildScrew(panel, 1f, 0f, 10f);
        }

        private static void BuildScrew(Transform panel, float ax, float ay, float slotAngle)
        {
            float sx = ax > 0.5f ? -1f : 1f;
            float sy = ay > 0.5f ? -1f : 1f;
            var screw = LabController.AddImage(panel, LabKit.BrassKnob, Color.white, raycast: false);
            var rt = screw.rectTransform;
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(10f, 10f);
            rt.anchoredPosition = new Vector2(10f * sx, 10f * sy);
            var slot = LabController.AddImage(screw.transform, null, LabKit.BrassSlot, raycast: false);
            var sRT = slot.rectTransform;
            sRT.anchorMin = new Vector2(0.5f, 0.5f); sRT.anchorMax = new Vector2(0.5f, 0.5f);
            sRT.sizeDelta = new Vector2(8f, 1.5f);
            sRT.localRotation = Quaternion.Euler(0f, 0f, slotAngle);
        }

        // -----------------------------------------------------------------
        // Header: title, rule, Close button.
        // -----------------------------------------------------------------

        public static void BuildHeader(Transform panel, UnityAction onClose)
        {
            LabController.AddText(panel, "The Laboratory", new Vector2(36f, -76f), new Vector2(-160f, -26f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 34, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            // Header rule.
            var rule = LabController.AddImage(panel, null, LabKit.Bone(0.18f), raycast: false);
            var rRT = rule.rectTransform;
            rRT.anchorMin = new Vector2(0f, 1f); rRT.anchorMax = new Vector2(1f, 1f);
            rRT.pivot = new Vector2(0.5f, 1f);
            rRT.offsetMin = new Vector2(36f, -84f);
            rRT.offsetMax = new Vector2(-36f, -83f);

            // Close: transparent with a hairline bone border; hover → accent.
            var close = LabController.NewChild("Btn_Close", panel);
            var cRT = close.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(1f, 1f);
            cRT.sizeDelta = new Vector2(88f, 32f);
            cRT.anchoredPosition = new Vector2(-36f, -38f);
            var cBorder = close.AddComponent<Image>();
            cBorder.sprite = LabKit.Border;
            cBorder.type = Image.Type.Sliced;
            var cBtn = close.AddComponent<Button>();
            cBtn.targetGraphic = cBorder;
            LabController.StyleButton(cBtn, LabKit.Bone(0.35f), LabKit.Accent, LabKit.AccentGlow);
            cBtn.onClick.AddListener(onClose);
            LabController.AddText(close.transform, "Close", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                15, FontStyle.Normal, TextAnchor.MiddleCenter, LabKit.Bone());
        }

        // -----------------------------------------------------------------
        // Journal: the concoctions well shell (RefreshList fills it).
        // -----------------------------------------------------------------

        // Left column: the concoctions well — a recessed shelf of saved jars.
        // Returns the "Content" GameObject LabController stores as
        // _listContent; RefreshList/BuildNewRow/BuildRowShell (staying on
        // LabController) keep rebuilding its children against live editor
        // state, so only this one-shot shell construction moved.
        public static GameObject BuildJournal(Transform panel)
        {
            LabController.AddText(panel, "Concoctions", new Vector2(36f, -128f), new Vector2(276f, -104f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            var well = LabController.NewChild("Well", panel);
            var wRT = well.GetComponent<RectTransform>();
            wRT.anchorMin = new Vector2(0f, 0f); wRT.anchorMax = new Vector2(0f, 1f);
            wRT.pivot = new Vector2(0f, 1f);
            wRT.offsetMin = new Vector2(36f, 40f);
            wRT.offsetMax = new Vector2(276f, -136f);
            var wellBg = well.AddComponent<Image>();
            wellBg.color = LabKit.Shade(0.34f);
            var wellBorder = LabController.AddImage(well.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            LabController.Stretch(wellBorder.rectTransform);
            wellBorder.type = Image.Type.Sliced;
            // Lighter bottom lip + inset top shade = recessed read.
            var lip = LabController.AddImage(well.transform, null, LabKit.Bone(0.14f), raycast: false);
            lip.rectTransform.anchorMin = new Vector2(0f, 0f); lip.rectTransform.anchorMax = new Vector2(1f, 0f);
            lip.rectTransform.sizeDelta = new Vector2(0f, 1f);
            var inset = LabController.AddImage(well.transform, LabKit.FadeV, LabKit.Shade(0.55f), raycast: false);
            inset.rectTransform.anchorMin = new Vector2(0f, 1f); inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.pivot = new Vector2(0.5f, 1f);
            inset.rectTransform.sizeDelta = new Vector2(0f, 12f);
            // FadeV is opaque-top; flip so the shade hugs the well's top edge.
            inset.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var viewport = LabController.NewChild("ListViewport", well.transform);
            LabController.Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();
            GameObject listContent = LabController.NewChild("Content", viewport.transform);
            var crt = listContent.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 1f);
            return listContent;
        }

        // -----------------------------------------------------------------
        // Switchboard: five levers, name field, CPU readout, Save/Delete.
        // -----------------------------------------------------------------

        /// <summary>Widget references LabController assigns to its own fields after BuildSwitchboard returns.</summary>
        public readonly struct SwitchboardResult
        {
            public readonly Slider DmgSlider, SizeSlider, KbSlider, SpeedSlider, SpreadSlider;
            public readonly InputField NameField;
            public readonly Text CpuReadout;
            public readonly Text DeleteLabel;
            public readonly GameObject DeleteStrike;

            public SwitchboardResult(Slider dmgSlider, Slider sizeSlider, Slider kbSlider, Slider speedSlider, Slider spreadSlider,
                InputField nameField, Text cpuReadout, Text deleteLabel, GameObject deleteStrike)
            {
                DmgSlider = dmgSlider; SizeSlider = sizeSlider; KbSlider = kbSlider;
                SpeedSlider = speedSlider; SpreadSlider = spreadSlider;
                NameField = nameField; CpuReadout = cpuReadout;
                DeleteLabel = deleteLabel; DeleteStrike = deleteStrike;
            }
        }

        // Centre column: the switchboard — raised plate with five levers,
        // the label field and the Save / Delete actions. The five onChanged
        // callbacks, onNameEdited, onSave and onDelete are LabController's
        // own editor-state methods (OnDmgChanged.../OnNameEdited/Save/
        // DeleteCurrent); setActiveSlider is its SetActiveSlider. None of
        // this class's own state is involved -- it only wires them.
        public static SwitchboardResult BuildSwitchboard(
            Transform panel,
            Dictionary<Slider, LabController.SliderVisual> sliderVisuals,
            Action<Slider> setActiveSlider,
            UnityAction<float> onDmgChanged, UnityAction<float> onSizeChanged, UnityAction<float> onKbChanged,
            UnityAction<float> onSpeedChanged, UnityAction<float> onSpreadChanged,
            UnityAction<string> onNameEdited, UnityAction onSave, UnityAction onDelete)
        {
            var col = LabController.NewChild("Switchboard", panel);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(308f, 26f);
            rt.offsetMax = new Vector2(772f, -106f);

            // Raised plate: drop shadow, top-lit face, top-lighter border.
            var plate = LabController.NewChild("Plate", col.transform);
            var pRT = plate.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0f, 1f); pRT.anchorMax = new Vector2(1f, 1f);
            pRT.pivot = new Vector2(0.5f, 1f);
            pRT.sizeDelta = new Vector2(0f, 286f);
            pRT.anchoredPosition = Vector2.zero;
            var plateShadow = LabController.AddImage(plate.transform, LabKit.Glow, LabKit.Shade(0.6f), raycast: false);
            LabController.Stretch(plateShadow.rectTransform);
            plateShadow.rectTransform.offsetMin = new Vector2(-34f, -58f);
            plateShadow.rectTransform.offsetMax = new Vector2(34f, 14f);
            var plateFace = LabController.AddImage(plate.transform, LabKit.Plate, Color.white, raycast: false);
            LabController.Stretch(plateFace.rectTransform);
            var plateBorder = LabController.AddImage(plate.transform, LabKit.Border, LabKit.Bone(0.12f), raycast: false);
            LabController.Stretch(plateBorder.rectTransform);
            plateBorder.type = Image.Type.Sliced;
            var plateTopLight = LabController.AddImage(plate.transform, null, LabKit.Bone(0.22f), raycast: false);
            plateTopLight.rectTransform.anchorMin = new Vector2(0f, 1f);
            plateTopLight.rectTransform.anchorMax = new Vector2(1f, 1f);
            plateTopLight.rectTransform.pivot = new Vector2(0.5f, 1f);
            plateTopLight.rectTransform.sizeDelta = new Vector2(0f, 1f);

            Slider dmgSlider    = BuildSliderRow(plate.transform, "Damage",    0, onDmgChanged, sliderVisuals, setActiveSlider);
            Slider sizeSlider   = BuildSliderRow(plate.transform, "Size",      1, onSizeChanged, sliderVisuals, setActiveSlider);
            Slider kbSlider     = BuildSliderRow(plate.transform, "Knockback", 2, onKbChanged, sliderVisuals, setActiveSlider);
            Slider speedSlider  = BuildSliderRow(plate.transform, "Speed",     3, onSpeedChanged, sliderVisuals, setActiveSlider);
            Slider spreadSlider = BuildSliderRow(plate.transform, "Spread",    4, onSpreadChanged, sliderVisuals, setActiveSlider);

            // Sunken name field.
            InputField nameField = BuildNameField(col.transform, new Vector2(0f, -306f), new Vector2(300f, 38f));
            nameField.onValueChanged.AddListener(onNameEdited);

            // Multiplier / CPU-surcharge annotation (kept from 141 — the
            // handoff computes this string and invites surfacing it).
            Text cpuReadout = LabController.AddText(col.transform, "", new Vector2(0f, -372f), new Vector2(0f, -352f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 11, FontStyle.Italic, TextAnchor.MiddleLeft, LabKit.Bone(0.5f));
            cpuReadout.font = LabController.AnnoFont;

            (Text deleteLabel, GameObject deleteStrike) = BuildActions(col.transform, onSave, onDelete);

            return new SwitchboardResult(dmgSlider, sizeSlider, kbSlider, speedSlider, spreadSlider,
                nameField, cpuReadout, deleteLabel, deleteStrike);
        }

        private static Slider BuildSliderRow(
            Transform plate, string label, int index, UnityAction<float> onChanged,
            Dictionary<Slider, LabController.SliderVisual> sliderVisuals, Action<Slider> setActiveSlider)
        {
            var row = LabController.NewChild($"Row_{label}", plate);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(20f, -22f - index * 52f - 34f);
            rt.offsetMax = new Vector2(-20f, -22f - index * 52f);

            LabController.AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(96f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            // Readout, right-aligned tabular (annotation face is monospaced).
            Text readout = LabController.AddText(row.transform, "50%", new Vector2(-58f, 0f), new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleRight, LabKit.Bone(0.88f));
            readout.font = LabController.AnnoFont;

            // Track host: full-height hit area between label and readout.
            var host = LabController.NewChild("Slider", row.transform);
            var hRT = host.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0f); hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = new Vector2(112f, 0f);
            hRT.offsetMax = new Vector2(-74f, 0f);
            // An invisible full-size graphic so the whole 34px band drags.
            var hitArea = host.AddComponent<Image>();
            hitArea.color = Color.clear;

            // Brass ruled bar (with its own dark rim) at the vertical centre.
            var barRim = LabController.AddImage(host.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            barRim.type = Image.Type.Sliced;
            barRim.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            barRim.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            barRim.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var bar = LabController.AddImage(host.transform, LabKit.BrassBar, Color.white, raycast: false);
            bar.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bar.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(-2f, 4f);

            // Faint ink tick marks every 10%, riding above the bar.
            var ticks = LabController.AddImage(host.transform, LabKit.Ticks, LabKit.Bone(0.35f), raycast: false);
            ticks.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            ticks.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            ticks.rectTransform.sizeDelta = new Vector2(0f, 6f);
            ticks.rectTransform.anchoredPosition = new Vector2(0f, 9f);

            // Fill strip (slider-driven) over the brass bar.
            var fillArea = LabController.NewChild("Fill Area", host.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.5f); faRT.anchorMax = new Vector2(1f, 0.5f);
            faRT.sizeDelta = new Vector2(0f, 4f);
            var fill = LabController.NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = LabKit.Bone(0.75f);
            fillImg.raycastTarget = false;

            // Pip handle with a hidden accent glow for the drag state.
            // Zero-height slide area: the Slider re-stretches the handle to
            // the area's full cross-axis every frame, so the only way to a
            // 9px round pip is a 0px-tall area + the handle's own sizeDelta.
            var handleArea = LabController.NewChild("Handle Slide Area", host.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0.5f); haRT.anchorMax = new Vector2(1f, 0.5f);
            haRT.offsetMin = new Vector2(5f, 0f); haRT.offsetMax = new Vector2(-5f, 0f);
            var handle = LabController.NewChild("Handle", handleArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            // Pin to the vertical centre — the Slider only drives the X
            // anchors, and a stretched Y turns the pip into a lozenge.
            handleRT.anchorMin = new Vector2(0.5f, 0.5f);
            handleRT.anchorMax = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(9f, 9f);
            var pipGlow = LabController.AddImage(handle.transform, LabKit.Glow, LabKit.AccentGlow, raycast: false);
            pipGlow.rectTransform.sizeDelta = new Vector2(26f, 26f);
            pipGlow.gameObject.SetActive(false);
            var pip = handle.AddComponent<Image>();
            pip.sprite = LabKit.Circle;
            pip.color = LabKit.Bone();

            var slider = host.AddComponent<Slider>();
            slider.targetGraphic = hitArea;
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fillRT;
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = Concoction.DefaultPct;
            slider.onValueChanged.AddListener(onChanged);

            sliderVisuals[slider] = new LabController.SliderVisual { Fill = fillImg, Pip = pip, PipGlow = pipGlow.gameObject, Readout = readout };

            // Press-and-hold accent: the dragged lever, its fill and its
            // readout go galvanic until release.
            var trigger = host.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => setActiveSlider(slider));
            trigger.triggers.Add(down);
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => setActiveSlider(null));
            trigger.triggers.Add(up);
            return slider;
        }

        private static InputField BuildNameField(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = LabController.NewChild("NameField", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = LabKit.Shade(0.35f);
            var fBorder = LabController.AddImage(go.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            LabController.Stretch(fBorder.rectTransform);
            fBorder.type = Image.Type.Sliced;
            var inset = LabController.AddImage(go.transform, LabKit.FadeV, LabKit.Shade(0.45f), raycast: false);
            inset.rectTransform.anchorMin = new Vector2(0f, 1f);
            inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.pivot = new Vector2(0.5f, 1f);
            inset.rectTransform.sizeDelta = new Vector2(0f, 6f);
            inset.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var textGo = LabController.NewChild("Text", go.transform);
            LabController.Stretch(textGo.GetComponent<RectTransform>(), 12f);
            var text = textGo.AddComponent<Text>();
            text.font = LabController.UIFont; text.fontSize = 19; text.color = LabKit.Bone();
            text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var placeholderGo = LabController.NewChild("Placeholder", go.transform);
            LabController.Stretch(placeholderGo.GetComponent<RectTransform>(), 12f);
            var placeholder = placeholderGo.AddComponent<Text>();
            placeholder.font = LabController.UIFont; placeholder.fontSize = 19; placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = LabKit.Bone(0.4f); placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.verticalOverflow = VerticalWrapMode.Overflow;
            placeholder.text = "the mix names itself…";

            var field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 32;
            return field;
        }

        private static (Text deleteLabel, GameObject deleteStrike) BuildActions(Transform col, UnityAction onSave, UnityAction onDelete)
        {
            // Save: a bone brushstroke blob with ink text — the one light
            // shape on the dark bench (physical, per the elevation language).
            var save = LabController.NewChild("Btn_Save", col);
            var sRT = save.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0f, 1f); sRT.anchorMax = new Vector2(0f, 1f);
            sRT.pivot = new Vector2(0f, 1f);
            sRT.sizeDelta = new Vector2(118f, 42f);
            sRT.anchoredPosition = new Vector2(0f, -388f);
            var saveShadow = LabController.AddImage(save.transform, LabKit.Glow, LabKit.Shade(0.5f), raycast: false);
            LabController.Stretch(saveShadow.rectTransform);
            saveShadow.rectTransform.offsetMin = new Vector2(-14f, -20f);
            saveShadow.rectTransform.offsetMax = new Vector2(14f, 6f);
            var saveImg = LabController.AddImage(save.transform, InkKit.BrushBlob, Color.white, raycast: true);
            LabController.Stretch(saveImg.rectTransform);
            var saveBtn = save.AddComponent<Button>();
            saveBtn.targetGraphic = saveImg;
            LabController.StyleButton(saveBtn, LabKit.Bone(), Color.white, LabKit.Bone(0.82f));
            saveBtn.onClick.AddListener(onSave);
            LabController.AddText(save.transform, "Save", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                17, FontStyle.Normal, TextAnchor.MiddleCenter, UguiPalette.Ink);

            // Delete: text-only, destructive; arming strikes it through in
            // the rationed vermilion.
            var del = LabController.NewChild("Btn_Delete", col);
            var dRT = del.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0f, 1f); dRT.anchorMax = new Vector2(0f, 1f);
            dRT.pivot = new Vector2(0f, 1f);
            dRT.sizeDelta = new Vector2(80f, 42f);
            dRT.anchoredPosition = new Vector2(136f, -388f);
            // Invisible hit area — the label itself is raycast-off like all
            // AddText output.
            var delHit = del.AddComponent<Image>();
            delHit.color = Color.clear;
            Text deleteLabel = LabController.AddText(del.transform, "Delete", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                16, FontStyle.Normal, TextAnchor.MiddleCenter, LabKit.Bone(0.6f));
            var strike = LabController.AddImage(del.transform, null, UguiPalette.Vermilion, raycast: false);
            strike.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            strike.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            strike.rectTransform.sizeDelta = new Vector2(64f, 2.5f);
            strike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
            GameObject deleteStrike = strike.gameObject;
            deleteStrike.SetActive(false);
            var delBtn = del.AddComponent<Button>();
            delBtn.targetGraphic = deleteLabel;
            delBtn.transition = Selectable.Transition.None;
            delBtn.onClick.AddListener(onDelete);

            return (deleteLabel, deleteStrike);
        }

        // -----------------------------------------------------------------
        // Vial: cork, rolled lip, glass tube, live liquid, bubbles.
        // -----------------------------------------------------------------

        /// <summary>Widget references LabController assigns to its own fields after BuildVial returns.</summary>
        public readonly struct VialResult
        {
            public readonly RectTransform VialRoot;
            public readonly Image Liquid, LiquidGlowOverlay, Surface, TubeTint, VialOuterGlow;
            public readonly RectTransform LiquidRT, SurfaceRT;

            public VialResult(RectTransform vialRoot, Image liquid, Image liquidGlowOverlay, Image surface,
                Image tubeTint, Image vialOuterGlow, RectTransform liquidRT, RectTransform surfaceRT)
            {
                VialRoot = vialRoot; Liquid = liquid; LiquidGlowOverlay = liquidGlowOverlay; Surface = surface;
                TubeTint = tubeTint; VialOuterGlow = vialOuterGlow; LiquidRT = liquidRT; SurfaceRT = surfaceRT;
            }
        }

        // Right column: the specimen vial — cork, rolled lip, glass tube,
        // live liquid, bubbles, ground shadow, wax seal, fig. 1 caption.
        // bubbles: LabController's fixed length-3 _bubbles array, filled in
        // place (Update, staying there, owns the per-frame rise/fade).
        public static VialResult BuildVial(Transform panel, Image[] bubbles)
        {
            var colCenterX = 804f + 110f; // right column 804..1024

            var vial = LabController.NewChild("Vial", panel);
            RectTransform vialRoot = vial.GetComponent<RectTransform>();
            vialRoot.anchorMin = new Vector2(0f, 1f); vialRoot.anchorMax = new Vector2(0f, 1f);
            vialRoot.pivot = new Vector2(0.5f, 0.5f);
            vialRoot.sizeDelta = new Vector2(70f, 230f);
            vialRoot.anchoredPosition = new Vector2(colCenterX, -128f - 115f);

            // Outer glow (liquid-coloured) behind everything.
            Image vialOuterGlow = LabController.AddImage(vial.transform, LabKit.Glow, LabKit.Shade(0f), raycast: false);
            LabController.Stretch(vialOuterGlow.rectTransform);
            vialOuterGlow.rectTransform.offsetMin = new Vector2(-45f, -35f);
            vialOuterGlow.rectTransform.offsetMax = new Vector2(45f, 15f);

            // Ground shadow ellipse.
            var shadow = LabController.AddImage(vial.transform, LabKit.Glow, LabKit.Shade(0.55f), raycast: false);
            shadow.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            shadow.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            shadow.rectTransform.sizeDelta = new Vector2(64f, 14f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            // Tube region: 36 wide, from below the lip to the bottom.
            var tube = LabController.NewChild("Tube", vial.transform);
            var tRT = tube.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0.5f, 0f); tRT.anchorMax = new Vector2(0.5f, 1f);
            tRT.pivot = new Vector2(0.5f, 0f);
            tRT.sizeDelta = new Vector2(36f, 0f);
            tRT.offsetMin = new Vector2(-18f, 4f);
            tRT.offsetMax = new Vector2(18f, -24f);

            // Inner colour tint (the liquid haze inside the glass).
            Image tubeTint = LabController.AddImage(tube.transform, LabKit.TubeFill, LabKit.Shade(0f), raycast: false);
            LabController.Stretch(tubeTint.rectTransform);

            // Masked liquid stack.
            var maskGo = LabController.NewChild("LiquidMask", tube.transform);
            LabController.Stretch(maskGo.GetComponent<RectTransform>());
            var maskImg = maskGo.AddComponent<Image>();
            maskImg.sprite = LabKit.TubeFill;
            maskImg.raycastTarget = false;
            var mask = maskGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var liquid = LabController.NewChild("Liquid", maskGo.transform);
            RectTransform liquidRT = liquid.GetComponent<RectTransform>();
            liquidRT.anchorMin = new Vector2(0f, 0f); liquidRT.anchorMax = new Vector2(1f, 0f);
            liquidRT.pivot = new Vector2(0.5f, 0f);
            liquidRT.sizeDelta = new Vector2(0f, 100f);
            Image liquidImg = liquid.AddComponent<Image>();
            liquidImg.color = Color.gray;
            liquidImg.raycastTarget = false;
            // Glow-toned top: FadeV is opaque-top, exactly the handoff's
            // glow→solid vertical gradient when laid over the solid fill.
            Image liquidGlowOverlay = LabController.AddImage(liquid.transform, LabKit.FadeV, LabKit.Shade(0f), raycast: false);
            LabController.Stretch(liquidGlowOverlay.rectTransform);

            // Bright surface ellipse riding the fill line.
            var surface = LabController.NewChild("Surface", maskGo.transform);
            RectTransform surfaceRT = surface.GetComponent<RectTransform>();
            surfaceRT.anchorMin = new Vector2(0.5f, 0f); surfaceRT.anchorMax = new Vector2(0.5f, 0f);
            surfaceRT.pivot = new Vector2(0.5f, 0.5f);
            surfaceRT.sizeDelta = new Vector2(32f, 8f);
            Image surfaceImg = surface.AddComponent<Image>();
            surfaceImg.sprite = LabKit.Glow;
            surfaceImg.raycastTarget = false;

            // Bubbles (animated in Update).
            for (int i = 0; i < bubbles.Length; i++)
            {
                float size = i == 0 ? 6f : 4f;
                var b = LabController.AddImage(maskGo.transform, LabKit.Ring, LabKit.Bone(0.7f), raycast: false);
                b.rectTransform.anchorMin = new Vector2(LabController.s_bubbleX[i], 0f);
                b.rectTransform.anchorMax = new Vector2(LabController.s_bubbleX[i], 0f);
                b.rectTransform.sizeDelta = new Vector2(size, size);
                b.rectTransform.anchoredPosition = new Vector2(0f, 10f);
                bubbles[i] = b;
            }

            // Vertical glass highlight streak.
            var streak = LabController.AddImage(tube.transform, LabKit.Glow, LabKit.Bone(0.22f), raycast: false);
            streak.rectTransform.anchorMin = new Vector2(0f, 0f);
            streak.rectTransform.anchorMax = new Vector2(0f, 1f);
            streak.rectTransform.pivot = new Vector2(0f, 0.5f);
            streak.rectTransform.offsetMin = new Vector2(5f, 12f);
            streak.rectTransform.offsetMax = new Vector2(13f, -8f);

            // Glass wall on top of the liquid.
            var wall = LabController.AddImage(tube.transform, LabKit.TubeOutline, LabKit.Bone(0.5f), raycast: false);
            LabController.Stretch(wall.rectTransform);

            // Rolled lip.
            var lip = LabController.NewChild("Lip", vial.transform);
            var lipRT = lip.GetComponent<RectTransform>();
            lipRT.anchorMin = new Vector2(0.5f, 1f); lipRT.anchorMax = new Vector2(0.5f, 1f);
            lipRT.pivot = new Vector2(0.5f, 1f);
            lipRT.sizeDelta = new Vector2(44f, 7f);
            lipRT.anchoredPosition = new Vector2(0f, -18f);
            var lipBg = lip.AddComponent<Image>();
            lipBg.color = LabKit.Bone(0.14f);
            lipBg.raycastTarget = false;
            var lipEdge = LabController.AddImage(lip.transform, LabKit.Border, LabKit.Bone(0.5f), raycast: false);
            LabController.Stretch(lipEdge.rectTransform);
            lipEdge.type = Image.Type.Sliced;

            // Cork.
            var cork = LabController.AddImage(vial.transform, LabKit.Cork, Color.white, raycast: false);
            cork.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            cork.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            cork.rectTransform.pivot = new Vector2(0.5f, 1f);
            cork.rectTransform.sizeDelta = new Vector2(36f, 20f);
            cork.rectTransform.anchoredPosition = new Vector2(0f, 0f);

            // Wax seal — the screen's one vermilion mark.
            var seal = LabController.AddImage(vial.transform, InkKit.WaxSeal, Color.white, raycast: false);
            seal.rectTransform.anchorMin = new Vector2(1f, 0f);
            seal.rectTransform.anchorMax = new Vector2(1f, 0f);
            seal.rectTransform.sizeDelta = new Vector2(22f, 22f);
            seal.rectTransform.anchoredPosition = new Vector2(-5f, 62f); // kissing the tube's right wall
            seal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);

            // fig. 1 caption.
            Text fig = LabController.AddText(panel, "fig. 1", new Vector2(804f, -394f), new Vector2(1024f, -372f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 13, FontStyle.Italic, TextAnchor.MiddleCenter, LabKit.Bone(0.5f));
            fig.font = LabController.AnnoFont;

            return new VialResult(vialRoot, liquidImg, liquidGlowOverlay, surfaceImg, tubeTint, vialOuterGlow, liquidRT, surfaceRT);
        }
    }
}
