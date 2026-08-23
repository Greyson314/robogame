using System;
using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode "Move part" toggle (session 169). When on, a left-click
    /// picks the block under the cursor UP (with all its per-instance
    /// settings) and the next left-click on a valid face re-places it
    /// there via <see cref="BuildSession.TryMove"/> — atomic, with
    /// rollback, so a bad drop can never delete a tuned part. Right-click
    /// cancels the carry.
    /// </summary>
    /// <remarks>
    /// Thin adapter in the <see cref="BuildEditMode"/> mold: state + a
    /// clickable HUD button + a <c>V</c> hotkey; the pick/drop behaviour
    /// lives in <see cref="BlockEditor"/>, which reads
    /// <see cref="Enabled"/> each frame. Mutually exclusive with tune
    /// mode — both claim the left click, so enabling one disables the
    /// other. Wired in <see cref="GarageController"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildMoveMode : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildMode;
        [Tooltip("Hotkey that toggles Move-part mode while build mode is active.")]
        [SerializeField] private Key _toggleKey = Key.V;

        public bool Enabled { get; private set; }

        /// <summary>Raised whenever <see cref="Enabled"/> changes.</summary>
        public event Action<bool> Changed;

        private GameObject _hudRoot;
        private Image _buttonImage;
        private Text _buttonText;
        private GameObject _hintRoot;
        private Text _hintText;
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
            if (enabled)
            {
                // Exclusive with tune mode — both interpret the left click.
                BuildEditMode tune = FindAnyObjectByType<BuildEditMode>();
                if (tune != null && tune.Enabled) tune.SetEnabled(false);
            }
            UpdateButtonVisual();
            AudioRouter.PlayUI(enabled ? AudioCue.UiClick : AudioCue.UiBack);
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

        private void HandleBuildExited()
        {
            SetEnabled(false);
            UpdateHudVisibility();
        }

        private void Update()
        {
            if (_buildMode == null || !_buildMode.IsActive) return;
            bool typing = UguiNav.IsTextInputFocused();
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
                _buttonText.text = Enabled ? "Move Part: on   [V]" : "Move Part   [V]";
            if (_buttonImage != null)
                _buttonImage.color = Enabled ? UguiPalette.Accent : UguiPalette.ButtonIdle;
            if (_buttonText != null)
                _buttonText.color = Enabled ? UguiPalette.CreamText : UguiPalette.Ink;
            if (_hintRoot != null) _hintRoot.SetActive(Enabled);
        }

        // -----------------------------------------------------------------
        // HUD — clickable button under the Tuning Mode button.
        // -----------------------------------------------------------------

        private void BuildHud()
        {
            _hudRoot = new GameObject("BuildMoveModeHud");
            _hudRoot.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            var scaler = _hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _hudRoot.AddComponent<GraphicRaycaster>();

            var go = NewChild("MoveButton", _hudRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(200f, 30f);
            // Under the Tuning Mode button (that one sits at -50, 30 high).
            rt.anchoredPosition = new Vector2(0f, -86f);
            _buttonImage = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = _buttonImage;
            btn.onClick.AddListener(Toggle);

            _buttonText = AddText(go.transform);

            // How-to line, shown only while the mode is on. Shares the
            // -122 hint slot with tune mode's hint — the modes are
            // mutually exclusive, so only one hint is ever visible.
            _hintRoot = NewChild("MoveHint", _hudRoot.transform);
            var hrt = _hintRoot.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 1f);
            hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(660f, 22f);
            hrt.anchoredPosition = new Vector2(0f, -122f);
            var hintBg = _hintRoot.AddComponent<Image>();
            hintBg.color = UguiPalette.Backdrop;
            hintBg.raycastTarget = false;
            _hintText = AddText(_hintRoot.transform);
            _hintText.fontSize = 12;
            _hintText.fontStyle = FontStyle.Normal;
            _hintText.color = UguiPalette.Text;
            _hintText.raycastTarget = false;
            _hintText.text = "Click a part to pick it up (settings kept)  •  click a face to drop  •  right-click cancels  •  V exits";
            _hintRoot.SetActive(false);
        }

        private static GameObject NewChild(string name, Transform parent)
            => Robogame.Core.UguiKit.NewChild(name, parent);

        private static Text AddText(Transform parent)
            => Robogame.Core.UguiKit.AddText(parent, "", Robogame.Core.InkKit.Display, 13,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
    }
}
