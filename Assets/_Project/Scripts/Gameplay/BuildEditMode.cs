using System;
using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode "Edit Block" toggle. When on, a left-click selects the
    /// block under the cursor and binds it to the variant panel so the
    /// player can retune its sliders in place — no delete, no orphaning.
    /// When off, left/right-click place / remove as normal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State + a clickable HUD button (plus an <c>E</c> hotkey). The actual
    /// select-and-bind behaviour lives in <see cref="BlockEditor"/>, which
    /// reads <see cref="Enabled"/> each frame; this component is the thin
    /// adapter, same split as <see cref="BuildMirrorMode"/>. Wired in
    /// <see cref="GarageController.EnsureBuildModeWired"/>.
    /// </para>
    /// <para>
    /// Replaces the session-125 middle-click instance-edit, which the player
    /// found fiddly. Middle-click reverts to a plain eyedropper (copy a
    /// block's type + settings onto the next placement); editing an existing
    /// block is now an explicit, discoverable mode behind this button.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildEditMode : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildMode;
        [Tooltip("Hotkey that toggles edit-block mode while build mode is active.")]
        [SerializeField] private Key _toggleKey = Key.E;

        public bool Enabled { get; private set; }

        /// <summary>Raised whenever <see cref="Enabled"/> changes (button label + editor react).</summary>
        public event Action<bool> Changed;

        private GameObject _hudRoot;
        private Image _buttonImage;
        private Text _buttonText;
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

        public void Toggle() => SetEnabled(!Enabled);

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            UpdateButtonVisual();
            Changed?.Invoke(Enabled);
        }

        private void Awake()
        {
            BuildHud();
            UpdateButtonVisual();
            Subscribe();
            UpdateHudVisibility();
        }

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || _buildMode == null) return;
            _buildMode.Entered += UpdateHudVisibility;
            _buildMode.Exited  += HandleBuildExited;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _buildMode == null) return;
            _buildMode.Entered -= UpdateHudVisibility;
            _buildMode.Exited  -= HandleBuildExited;
            _subscribed = false;
        }

        // Leaving build mode always drops edit mode so re-entering starts in
        // plain placement.
        private void HandleBuildExited()
        {
            SetEnabled(false);
            UpdateHudVisibility();
        }

        private void Update()
        {
            if (_buildMode == null || !_buildMode.IsActive) return;
            // Don't eat the keystroke while a text field is focused.
            bool typing = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null;
            Keyboard kb = Keyboard.current;
            if (kb != null && !typing && kb[_toggleKey].wasPressedThisFrame) Toggle();
        }

        private void UpdateHudVisibility()
        {
            if (_hudRoot == null) return;
            _hudRoot.SetActive(_buildMode != null && _buildMode.IsActive);
        }

        private void UpdateButtonVisual()
        {
            if (_buttonText != null)
                _buttonText.text = Enabled ? "EDIT: ON   [E]" : "EDIT BLOCK   [E]";
            if (_buttonImage != null)
                _buttonImage.color = Enabled ? UguiPalette.Accent : UguiPalette.ButtonIdle;
            if (_buttonText != null)
                _buttonText.color = Enabled ? Color.black : Color.white;
        }

        // -----------------------------------------------------------------
        // HUD — clickable button, top-centre under the mirror banner.
        // -----------------------------------------------------------------

        private void BuildHud()
        {
            _hudRoot = new GameObject("BuildEditModeHud");
            _hudRoot.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            _hudRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _hudRoot.AddComponent<GraphicRaycaster>();

            var go = NewChild("EditButton", _hudRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(200f, 30f);
            // Sits just below the mirror banner (that panel is 32 high at -12).
            rt.anchoredPosition = new Vector2(0f, -50f);
            _buttonImage = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = _buttonImage;
            btn.onClick.AddListener(Toggle);

            _buttonText = AddText(go.transform);
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
            t.font = Robogame.Core.InkKit.Display;
            t.fontSize = 13;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }
    }
}
