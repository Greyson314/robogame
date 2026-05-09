using System;
using Robogame.Block;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode mirror toggle (Robocraft-style). Holds the on/off state
    /// + active mirror axis, listens for the toggle hotkey, and shows a
    /// HUD banner so the player always knows whether placements will be
    /// duplicated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State only — the actual mirror placement is implemented in
    /// <see cref="BlockEditor"/> using <see cref="BlockMirror"/>. Lives on
    /// the same GameObject as the rest of the build-mode singletons (see
    /// <see cref="GarageController.EnsureBuildModeWired"/>).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildMirrorMode : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildMode;
        [Tooltip("Hotkey that toggles mirror on/off while build mode is active.")]
        [SerializeField] private Key _toggleKey = Key.M;
        [Tooltip("Hotkey that cycles mirror axis (X → Z → X) while build mode is active.")]
        [SerializeField] private Key _cycleAxisKey = Key.B;

        public bool Enabled { get; private set; }
        public MirrorAxis Axis { get; private set; } = MirrorAxis.X;

        /// <summary>Raised whenever Enabled or Axis changes — ghost preview rebuilds on this.</summary>
        public event Action Changed;

        private GameObject _hudRoot;
        private Text _hudText;
        private bool _subscribed;

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                Unsubscribe();
                _buildMode = value;
                Subscribe();
                UpdateHudVisibility();
            }
        }

        public void Toggle() { Enabled = !Enabled; UpdateHudText(); Changed?.Invoke(); }

        public void SetAxis(MirrorAxis axis)
        {
            if (Axis == axis) return;
            Axis = axis;
            UpdateHudText();
            Changed?.Invoke();
        }

        private void Awake()
        {
            BuildHud();
            UpdateHudText();
            Subscribe();
            UpdateHudVisibility();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || _buildMode == null) return;
            _buildMode.Entered += UpdateHudVisibility;
            _buildMode.Exited  += UpdateHudVisibility;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _buildMode == null) return;
            _buildMode.Entered -= UpdateHudVisibility;
            _buildMode.Exited  -= UpdateHudVisibility;
            _subscribed = false;
        }

        private void Update()
        {
            // Hotkeys are build-mode-gated so M/B in the arena don't fire mirror toggles.
            if (_buildMode == null || !_buildMode.IsActive) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb[_toggleKey].wasPressedThisFrame) Toggle();
            if (kb[_cycleAxisKey].wasPressedThisFrame)
            {
                SetAxis(Axis == MirrorAxis.X ? MirrorAxis.Z : MirrorAxis.X);
            }
        }

        private void UpdateHudVisibility()
        {
            if (_hudRoot == null) return;
            _hudRoot.SetActive(_buildMode != null && _buildMode.IsActive);
        }

        private void UpdateHudText()
        {
            if (_hudText == null) return;
            string axisLabel = Axis == MirrorAxis.X ? "X" : "Z";
            _hudText.text = Enabled
                ? $"MIRROR  {axisLabel}   [M off  /  B axis]"
                : "MIRROR  off   [M on  /  B axis]";
            _hudText.color = Enabled
                ? new Color(0.95f, 0.55f, 0.10f, 1f)
                : new Color(1f, 1f, 1f, 0.6f);
        }

        // -----------------------------------------------------------------
        // HUD — top-centre banner. Fades out when build mode is inactive.
        // -----------------------------------------------------------------

        private void BuildHud()
        {
            _hudRoot = new GameObject("BuildMirrorHud");
            _hudRoot.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            _hudRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _hudRoot.AddComponent<GraphicRaycaster>();

            var panel = NewChild("Panel", _hudRoot.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(360f, 32f);
            prt.anchoredPosition = new Vector2(0f, -12f);
            panel.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.85f);

            _hudText = AddText(panel.transform);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static Text AddText(Transform parent)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 13;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }
    }
}
