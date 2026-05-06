using System.Collections.Generic;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode side panel that shows the "variable part" dimensions for
    /// the currently-selected block in the <see cref="BuildHotbar"/>.
    /// Aero / AeroFin: span / thickness / chord. Rope: segment count.
    /// Anything else: panel hides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-block-id "next placement" dims are cached here so the player can
    /// dial in a wing, place several, switch to a fin, dial that, and come
    /// back to the wing without losing the original setting. The cache
    /// resets each time build mode enters (see <see cref="HandleEntered"/>)
    /// so a fresh edit session starts from block defaults.
    /// </para>
    /// <para>
    /// <see cref="BlockEditor.TryPlace"/> reads the cached dims for the
    /// current block id and passes them to <see cref="BlockGrid.PlaceBlock"/>,
    /// which feeds them into <see cref="BlockBehaviour.Initialize"/>, which
    /// the per-block component (e.g. <see cref="AeroSurfaceBlock"/>) reads at
    /// rig setup. The blueprint serialiser carries them on save.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VariantConfigPanel : MonoBehaviour
    {
        [SerializeField] private BuildHotbar _hotbar;
        [SerializeField] private BuildModeController _buildMode;

        // "Next placement" cache. Vector3.zero entry = use block defaults.
        private readonly Dictionary<string, Vector3> _dimsByBlockId = new();

        // UGUI references (built in BuildCanvas / shown in OnSelected).
        private GameObject _root;
        private GameObject _foilSection;
        private GameObject _ropeSection;
        private Slider _spanSlider, _thicknessSlider, _chordSlider, _segmentSlider;
        private Text _spanValue, _thicknessValue, _chordValue, _segmentValue;
        private Text _titleText;

        private string _activeBlockId;
        private bool _suppressCallbacks;

        public BuildHotbar Hotbar
        {
            get => _hotbar;
            set
            {
                if (_hotbar != null) _hotbar.SelectedBlockChanged -= HandleSelectedBlockChanged;
                _hotbar = value;
                if (_hotbar != null) _hotbar.SelectedBlockChanged += HandleSelectedBlockChanged;
            }
        }

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode != null)
                {
                    _buildMode.Entered -= HandleEntered;
                    _buildMode.Exited -= HandleExited;
                }
                _buildMode = value;
                if (_buildMode != null)
                {
                    _buildMode.Entered += HandleEntered;
                    _buildMode.Exited += HandleExited;
                }
            }
        }

        /// <summary>
        /// Read the cached "next placement" dims for <paramref name="blockId"/>.
        /// Vector3.zero means "use block defaults" — callers should treat
        /// it that way and let the consuming block decide.
        /// </summary>
        public Vector3 GetDimsForBlock(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return Vector3.zero;
            _dimsByBlockId.TryGetValue(blockId, out Vector3 v);
            return v;
        }

        /// <summary>True when the block id participates in the variant config UI.</summary>
        public static bool IsVariableBlock(string id)
            => id == BlockIds.Aero || id == BlockIds.AeroFin || id == BlockIds.Rope;

        private void Awake()
        {
            BuildCanvas();
            if (_buildMode != null)
            {
                _buildMode.Entered += HandleEntered;
                _buildMode.Exited += HandleExited;
            }
            if (_hotbar != null)
            {
                _hotbar.SelectedBlockChanged += HandleSelectedBlockChanged;
            }
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_buildMode != null)
            {
                _buildMode.Entered -= HandleEntered;
                _buildMode.Exited -= HandleExited;
            }
            if (_hotbar != null)
            {
                _hotbar.SelectedBlockChanged -= HandleSelectedBlockChanged;
            }
        }

        private void HandleEntered()
        {
            // Fresh edit session starts from block defaults. Persist-across-
            // session would be confusing — the user might wonder why every
            // wing they place is suddenly thick.
            _dimsByBlockId.Clear();
            if (_hotbar != null) HandleSelectedBlockChanged(_hotbar.SelectedBlockId);
        }

        private void HandleExited()
        {
            SetVisible(false);
            _activeBlockId = null;
        }

        private void HandleSelectedBlockChanged(string blockId)
        {
            _activeBlockId = blockId;
            bool foil = blockId == BlockIds.Aero || blockId == BlockIds.AeroFin;
            bool rope = blockId == BlockIds.Rope;
            bool any = foil || rope;
            SetVisible(any);
            if (!any) return;

            if (_titleText != null)
            {
                _titleText.text = foil
                    ? (blockId == BlockIds.AeroFin ? "VARIANT — TAIL FIN" : "VARIANT — AERO WING")
                    : "VARIANT — ROPE";
            }
            _foilSection.SetActive(foil);
            _ropeSection.SetActive(rope);

            Vector3 cached = GetDimsForBlock(blockId);
            _suppressCallbacks = true;
            if (foil)
            {
                float span      = cached.x > 0f ? cached.x : AeroSurfaceBlock.DefaultSpan;
                float thickness = cached.y > 0f ? cached.y : AeroSurfaceBlock.DefaultThickness;
                float chord     = cached.z > 0f ? cached.z : AeroSurfaceBlock.DefaultChord;
                _spanSlider.value = span;
                _thicknessSlider.value = thickness;
                _chordSlider.value = chord;
                UpdateValueText(_spanValue, span, "F2");
                UpdateValueText(_thicknessValue, thickness, "F2");
                UpdateValueText(_chordValue, chord, "F2");
            }
            else if (rope)
            {
                int count = cached.x > 0f ? Mathf.RoundToInt(cached.x) : RopeBlock.DefaultSegmentCount;
                _segmentSlider.value = count;
                UpdateValueText(_segmentValue, count, "F0");
            }
            _suppressCallbacks = false;
        }

        // -----------------------------------------------------------------
        // Slider callbacks
        // -----------------------------------------------------------------

        private void OnSpanChanged(float v)        { if (!_suppressCallbacks) WriteFoilDim(0, v, _spanValue,      "F2"); }
        private void OnThicknessChanged(float v)   { if (!_suppressCallbacks) WriteFoilDim(1, v, _thicknessValue, "F2"); }
        private void OnChordChanged(float v)       { if (!_suppressCallbacks) WriteFoilDim(2, v, _chordValue,     "F2"); }
        private void OnSegmentCountChanged(float v)
        {
            if (_suppressCallbacks) return;
            int rounded = Mathf.RoundToInt(v);
            // Snap the slider to the integer value so the handle visibly
            // jumps between counts; otherwise the player sees a fractional
            // bar that doesn't reflect the actual stored value.
            _suppressCallbacks = true;
            _segmentSlider.value = rounded;
            _suppressCallbacks = false;

            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            Vector3 dims = GetDimsForBlock(id);
            dims.x = rounded;
            _dimsByBlockId[id] = dims;
            UpdateValueText(_segmentValue, rounded, "F0");
        }

        private void WriteFoilDim(int axis, float value, Text valueText, string fmt)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            Vector3 dims = GetDimsForBlock(id);
            if (axis == 0) dims.x = value;
            else if (axis == 1) dims.y = value;
            else dims.z = value;
            _dimsByBlockId[id] = dims;
            UpdateValueText(valueText, value, fmt);
        }

        private static void UpdateValueText(Text t, float v, string fmt)
        {
            if (t != null) t.text = v.ToString(fmt);
        }

        // -----------------------------------------------------------------
        // Presets (polish feature)
        // -----------------------------------------------------------------

        private void ApplyFoilPreset(float span, float thickness, float chord)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _dimsByBlockId[id] = new Vector3(span, thickness, chord);
            HandleSelectedBlockChanged(id);
        }

        // -----------------------------------------------------------------
        // UGUI build
        // -----------------------------------------------------------------

        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void BuildCanvas()
        {
            _root = new GameObject("VariantConfigCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96;
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _root.AddComponent<GraphicRaycaster>();

            // Panel — top-right, doesn't fight the bottom-centred hotbar.
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.sizeDelta = new Vector2(340f, 320f);
            prt.anchoredPosition = new Vector2(-24f, -24f);
            panel.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.93f);

            _titleText = AddText(panel.transform, "VARIANT", new Vector2(12f, -12f), new Vector2(-12f, -36f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 18, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft,
                color: new Color(0.95f, 0.55f, 0.10f, 1f));

            // Foil section (Span / Thickness / Chord) + presets.
            _foilSection = NewChild("Foil", panel.transform);
            var fsRT = _foilSection.GetComponent<RectTransform>();
            fsRT.anchorMin = new Vector2(0f, 0f);
            fsRT.anchorMax = new Vector2(1f, 1f);
            fsRT.offsetMin = new Vector2(12f, 12f);
            fsRT.offsetMax = new Vector2(-12f, -40f);

            _spanSlider      = BuildLabeledSlider(_foilSection.transform, "Span (m)",      0,
                AeroSurfaceBlock.MinSpan, AeroSurfaceBlock.MaxSpan, AeroSurfaceBlock.DefaultSpan,
                OnSpanChanged, out _spanValue);
            _thicknessSlider = BuildLabeledSlider(_foilSection.transform, "Thickness (m)", 1,
                AeroSurfaceBlock.MinThickness, AeroSurfaceBlock.MaxThickness, AeroSurfaceBlock.DefaultThickness,
                OnThicknessChanged, out _thicknessValue);
            _chordSlider     = BuildLabeledSlider(_foilSection.transform, "Chord (m)",     2,
                AeroSurfaceBlock.MinChord, AeroSurfaceBlock.MaxChord, AeroSurfaceBlock.DefaultChord,
                OnChordChanged, out _chordValue);

            // Preset buttons row — one-click sizing for common profiles.
            // POLISH FEATURE #1: Foil presets.
            var presetRow = NewChild("Presets", _foilSection.transform);
            var prRT = presetRow.GetComponent<RectTransform>();
            prRT.anchorMin = new Vector2(0f, 0f);
            prRT.anchorMax = new Vector2(1f, 0f);
            prRT.pivot = new Vector2(0.5f, 0f);
            prRT.sizeDelta = new Vector2(0f, 32f);
            prRT.anchoredPosition = new Vector2(0f, 4f);

            AddPresetButton(presetRow.transform, "Wing",       0,    () => ApplyFoilPreset(2.40f, 0.10f, 1.40f));
            AddPresetButton(presetRow.transform, "Stabilizer", 1,    () => ApplyFoilPreset(1.20f, 0.08f, 0.80f));
            AddPresetButton(presetRow.transform, "Blade",      2,    () => ApplyFoilPreset(1.00f, 0.06f, 0.45f));
            AddPresetButton(presetRow.transform, "Default",    3,    () => ApplyFoilPreset(
                AeroSurfaceBlock.DefaultSpan, AeroSurfaceBlock.DefaultThickness, AeroSurfaceBlock.DefaultChord));

            // Rope section (segment count only).
            _ropeSection = NewChild("Rope", panel.transform);
            var rsRT = _ropeSection.GetComponent<RectTransform>();
            rsRT.anchorMin = new Vector2(0f, 0f);
            rsRT.anchorMax = new Vector2(1f, 1f);
            rsRT.offsetMin = new Vector2(12f, 12f);
            rsRT.offsetMax = new Vector2(-12f, -40f);

            _segmentSlider = BuildLabeledSlider(_ropeSection.transform, "Segments", 0,
                RopeBlock.MinSegmentCount, RopeBlock.MaxSegmentCount, RopeBlock.DefaultSegmentCount,
                OnSegmentCountChanged, out _segmentValue);

            _foilSection.SetActive(false);
            _ropeSection.SetActive(false);
        }

        private void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        // Labeled slider row at vertical slot `index` (0 = top of section,
        // grows downward at 56px steps).
        private Slider BuildLabeledSlider(Transform parent, string label, int index,
            float min, float max, float def, UnityEngine.Events.UnityAction<float> onChanged, out Text valueText)
        {
            var row = NewChild($"Row_{label}", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);
            rt.anchoredPosition = new Vector2(0f, -index * 56f);

            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(140f, 0f),
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                size: 14, style: FontStyle.Normal, anchor: TextAnchor.MiddleLeft, color: Color.white);

            valueText = AddText(row.transform, def.ToString("F2"), new Vector2(-8f, 0f), new Vector2(-8f, 0f),
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                size: 14, style: FontStyle.Bold, anchor: TextAnchor.MiddleRight,
                color: new Color(0.95f, 0.55f, 0.10f, 1f));
            // Manual width on the value text — anchor stretches need a sizeDelta override.
            var vtRT = valueText.rectTransform;
            vtRT.sizeDelta = new Vector2(60f, 0f);
            vtRT.anchoredPosition = new Vector2(-8f, 0f);

            // Slider inside its own host so we can stretch it horizontally
            // between the label and the value readout.
            var sliderHost = NewChild("Slider", row.transform);
            var srt = sliderHost.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(120f, -10f);
            srt.offsetMax = new Vector2(-72f, 10f);

            var bg = NewChild("Background", sliderHost.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f);
            bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            var fillArea = NewChild("Fill Area", sliderHost.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.4f);
            faRT.anchorMax = new Vector2(1f, 0.6f);
            faRT.offsetMin = new Vector2(8f, 0f);
            faRT.offsetMax = new Vector2(-8f, 0f);
            var fill = NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fill.AddComponent<Image>().color = new Color(0.95f, 0.55f, 0.10f, 1f);

            var handleArea = NewChild("Handle Slide Area", sliderHost.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0f);
            haRT.anchorMax = new Vector2(1f, 1f);
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(16f, 22f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = sliderHost.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = def;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private void AddPresetButton(Transform parent, string label, int index, System.Action onClick)
        {
            const float btnW = 70f;
            const float btnGap = 6f;
            const float startX = 0f;

            var go = NewChild($"Preset_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.highlightedColor = new Color(0.95f, 0.55f, 0.10f, 1f);
            cols.pressedColor = new Color(0.7f, 0.4f, 0.05f, 1f);
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(btnW, 0f);
            rt.anchoredPosition = new Vector2(startX + index * (btnW + btnGap), 0f);

            var t = AddText(go.transform, label, Vector2.zero, Vector2.zero,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 13, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: Color.white);
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static Text AddText(Transform parent, string text, Vector2 offsetMin, Vector2 offsetMax,
            Vector2 anchorMin, Vector2 anchorMax, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = UIFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            return t;
        }
    }
}
