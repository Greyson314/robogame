using System;
using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode "Tune part" toggle. When on, a left-click selects the
    /// block under the cursor and binds it to the variant panel so the
    /// player can retune its sliders in place — no delete, no orphaning.
    /// When off, left/right-click place / remove as normal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State + a clickable HUD button (plus a <c>T</c> hotkey). The actual
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
    /// <para>
    /// Tune mode is also the build flow's "cursor state": while on, the
    /// free-fly cam's cursor lock is suspended (<see
    /// cref="Robogame.Player.BuildFreeCam.ExternalCursorHold"/>) so the
    /// player drags variant sliders with a live cursor instead of the
    /// two-hand Alt chord. Placement keeps the locked-cursor reticle.
    /// Escape backs out of Tune mode before it opens the pause menu —
    /// see <see cref="PauseMenuHud"/>'s one-level-at-a-time ladder.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildEditMode : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildMode;
        [Tooltip("Hotkey that toggles Tune-part mode while build mode is active. " +
                 "T, not E — E is the free-cam's fly-up key, so the old E binding " +
                 "jolted the camera on every toggle.")]
        [SerializeField] private Key _toggleKey = Key.T;

        public bool Enabled { get; private set; }

        /// <summary>Raised whenever <see cref="Enabled"/> changes (button label + editor react).</summary>
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
            UpdateButtonVisual();
            ApplyCursorHold(enabled);
            // Mode flips read better with an ear cue — one place for it so
            // the E key, the HUD button, and the Escape back-out all sound
            // the same.
            AudioRouter.PlayUI(enabled ? AudioCue.UiClick : AudioCue.UiBack);
            Changed?.Invoke(Enabled);
        }

        // Tune mode lends the cursor to the HUD for its whole duration —
        // the variant panel is the point of the mode. Routed through the
        // free-cam so its click-to-relock / Alt paths stay coherent.
        private static void ApplyCursorHold(bool held)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            var freeCam = cam.GetComponent<Robogame.Player.BuildFreeCam>();
            if (freeCam != null) freeCam.ExternalCursorHold = held;
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
                _buttonText.text = Enabled ? "Tuning: on   [T]" : "Tune part   [T]";
            if (_buttonImage != null)
                _buttonImage.color = Enabled ? UguiPalette.Accent : UguiPalette.ButtonIdle;
            // Cream text on the accent (on) state, ink on the cream idle
            // button — white-on-cream was unreadable.
            if (_buttonText != null)
                _buttonText.color = Enabled ? UguiPalette.CreamText : UguiPalette.Ink;
            // The how-to hint only earns its pixels while the mode is on.
            if (_hintRoot != null) _hintRoot.SetActive(Enabled);
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
            // Match the Settings/Pause scaling so the HUD isn't tiny above 1080p.
            var scaler = _hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
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

            // One-line how-to under the button, shown only while tuning:
            // the mode is useless if the player doesn't know the next click
            // is a selection, not a placement.
            _hintRoot = NewChild("TuneHint", _hudRoot.transform);
            var hrt = _hintRoot.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 1f);
            hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(420f, 22f);
            hrt.anchoredPosition = new Vector2(0f, -82f);
            var hintBg = _hintRoot.AddComponent<Image>();
            hintBg.color = UguiPalette.Backdrop;
            hintBg.raycastTarget = false;
            _hintText = AddText(_hintRoot.transform);
            _hintText.fontSize = 12;
            _hintText.fontStyle = FontStyle.Normal;
            _hintText.color = UguiPalette.CreamText;
            _hintText.raycastTarget = false;
            _hintText.text = "Click a glowing part to tune it  •  T or Esc exits";
            _hintRoot.SetActive(false);
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
