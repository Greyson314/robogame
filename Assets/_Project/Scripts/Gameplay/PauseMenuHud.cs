using Robogame.Core;
using Robogame.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Escape-owned pause menu: Resume / Settings / Return to Garage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sole owner of the Escape key. Before this class, Escape was
    /// double-booked — <c>FollowCamera</c> released the cursor AND
    /// <c>SettingsHud</c> toggled its full-screen panel on the same press,
    /// so "free my mouse" cost two Escapes and a settings flash.
    /// TRACE[LOG-128]. The ladder is now: Escape in settings → back to
    /// this menu; Escape here → resume (re-capturing the cursor if we
    /// took it); Escape while build-mode part tuning is on → exit tune
    /// mode; Escape in gameplay → open this menu.
    /// </para>
    /// <para>
    /// Self-bootstraps on a DontDestroyOnLoad root (same pattern as
    /// <c>FpsCounter</c> / <c>NetDevHud</c>) so no scene needs editing.
    /// Registers with <see cref="HudPointerGuard"/> as a modal while open,
    /// which suppresses cursor re-capture and camera drag underneath.
    /// Participates in the QoL.PauseOnSettings time-scale gate with the
    /// same semantics as <see cref="SettingsHud"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PauseMenuHud : MonoBehaviour
    {
        private static GameObject s_root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_root = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_root != null) return;
            s_root = new GameObject("[PauseMenu]");
            DontDestroyOnLoad(s_root);
            s_root.AddComponent<PauseMenuHud>();
        }

        private GameObject _canvasRoot;
        private Button _returnButton;
        private bool _open;
        // Whether the cursor was captured when the menu opened — Resume
        // hands it back to the FollowCamera only if we actually took it.
        private bool _relockOnResume;
        private SettingsHud _settings;

        private void Awake()
        {
            EnsureEventSystem();
            BuildPanel();
            _canvasRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            HudPointerGuard.SetModalOpen(this, false);
            // Never leave a scene paused behind us.
            Time.timeScale = 1f;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            SettingsHud settings = ResolveSettings();
            if (settings != null && settings.IsOpen)
            {
                // Back out one level: settings → pause menu (or → nothing
                // if settings was opened elsewhere, e.g. the main menu).
                settings.Close();
                AudioRouter.PlayUI(AudioCue.UiBack);
                // SettingsHud.SetOpen(false) resets timeScale; re-assert
                // our own gate if we're still open underneath.
                if (_open) ApplyPauseGate();
                return;
            }

            // Next rungs down the ladder: build-mode part tuning. First
            // Escape drops the bound part (the session event re-locks the
            // cursor + clears the highlight), the next one exits tune mode
            // — only then does the pause menu open. Lookups run only on
            // Escape-press frames, so no per-frame cost.
            if (!_open)
            {
                BuildEditMode tune = FindAnyObjectByType<BuildEditMode>();
                if (tune != null && tune.Enabled)
                {
                    var garage = FindAnyObjectByType<GarageController>();
                    BuildSession session = garage != null ? garage.BuildSession : null;
                    if (session != null && session.EditingInstance != null)
                    {
                        session.SetEditingInstance(null);
                        AudioRouter.PlayUI(AudioCue.UiBack);
                    }
                    else
                    {
                        tune.SetEnabled(false);
                    }
                    return;
                }
            }

            if (_open)
            {
                Resume();
            }
            else if (CanOpen())
            {
                Open();
            }
        }

        // -----------------------------------------------------------------
        // Open / close
        // -----------------------------------------------------------------

        private bool CanOpen()
        {
            // Gameplay states only. No GameStateController means a scene
            // was played directly in the editor — allow, it's a dev flow.
            GameStateController state = GameStateController.Instance;
            if (state != null && state.State == GameState.Bootstrap) return false;

            // The match-end overlay is its own modal with its own
            // "Return to Garage" — don't stack a second menu on it.
            MatchEndOverlay matchEnd = FindAnyObjectByType<MatchEndOverlay>();
            return matchEnd == null || !matchEnd.IsVisible;
        }

        private void Open()
        {
            _open = true;
            _relockOnResume = Cursor.lockState == CursorLockMode.Locked;
            ReleaseCursorForMenu();
            HudPointerGuard.SetModalOpen(this, true);
            RefreshReturnButton();
            _canvasRoot.SetActive(true);
            ApplyPauseGate();
            AudioRouter.PlayUI(AudioCue.UiClick);
        }

        private void Resume()
        {
            _open = false;
            _canvasRoot.SetActive(false);
            HudPointerGuard.SetModalOpen(this, false);
            Time.timeScale = 1f;
            if (_relockOnResume)
            {
                FollowCamera follow = ResolveFollowCamera();
                // Only the FollowCamera flow re-locks (it owns the capture
                // state machine). Build mode's free cam re-locks on its own
                // next click instead.
                if (follow != null && follow.isActiveAndEnabled) follow.ApplyCursorLock();
            }
            AudioRouter.PlayUI(AudioCue.UiBack);
        }

        private void ReleaseCursorForMenu()
        {
            FollowCamera follow = ResolveFollowCamera();
            if (follow != null && follow.isActiveAndEnabled)
            {
                // Routes through the camera so its relock watchdog stands
                // down (a bare lockState write lasts exactly one frame).
                follow.ReleaseCursor();
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // Time-scale gate — same QoL.PauseOnSettings semantics as
        // SettingsHud so singleplayer pause behaviour has one switch.
        private void ApplyPauseGate()
        {
            bool pauseOn = Tweakables.GetBool(Tweakables.SettingsPause);
            Time.timeScale = (_open && pauseOn) ? 0f : 1f;
        }

        private static FollowCamera ResolveFollowCamera()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.GetComponent<FollowCamera>() : null;
        }

        private SettingsHud ResolveSettings()
        {
            if (_settings == null) _settings = FindAnyObjectByType<SettingsHud>();
            return _settings;
        }

        // -----------------------------------------------------------------
        // Button handlers
        // -----------------------------------------------------------------

        private void HandleResumeClicked() => Resume();

        private void HandleSettingsClicked()
        {
            SettingsHud settings = ResolveSettings();
            if (settings == null) return;
            // Pause menu stays open underneath; the settings canvas
            // (sortingOrder 500) fully covers ours (400). Escape backs out
            // settings → pause menu → gameplay, one level per press.
            settings.Open();
            AudioRouter.PlayUI(AudioCue.UiClick);
        }

        private void HandleReturnClicked()
        {
            // Close cleanly BEFORE the transition so the next scene never
            // inherits a modal flag or a zeroed time scale.
            _open = false;
            _canvasRoot.SetActive(false);
            HudPointerGuard.SetModalOpen(this, false);
            Time.timeScale = 1f;
            AudioRouter.PlayUI(AudioCue.UiBack);

            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            switch (state.State)
            {
                case GameState.Arena:
                    var arena = FindAnyObjectByType<ArenaController>();
                    if (arena != null) arena.Return(); else state.EnterGarage();
                    break;
                case GameState.WaterArena:
                    var water = FindAnyObjectByType<WaterArenaController>();
                    if (water != null) water.Return(); else state.EnterGarage();
                    break;
                case GameState.PlanetArena:
                    var planet = FindAnyObjectByType<PlanetArenaController>();
                    if (planet != null) planet.Return(); else state.EnterGarage();
                    break;
            }
        }

        private void RefreshReturnButton()
        {
            if (_returnButton == null) return;
            GameStateController state = GameStateController.Instance;
            bool inArena = state != null
                && state.State is GameState.Arena or GameState.WaterArena or GameState.PlanetArena;
            _returnButton.gameObject.SetActive(inArena);
        }

        // -----------------------------------------------------------------
        // Panel construction (procedural UGUI, matches SettingsHud family)
        // -----------------------------------------------------------------

        private static Font UIFont => Robogame.Core.InkKit.Display;

        private static void EnsureEventSystem()
        {
            EventSystem es = FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            HudEventSystem.DisableKeyboardNavigation(es);
        }

        private void BuildPanel()
        {
            var canvasGO = new GameObject("PauseMenuCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above SceneTransitionHud (100) / BuildHotbar (95); below
            // SettingsHud (500) so Settings layers cleanly on top.
            canvas.sortingOrder = 400;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();
            _canvasRoot = canvasGO;

            // Full-screen dim. raycastTarget=true doubles as the click
            // blocker for everything underneath.
            var dim = NewChild("Dim", canvasGO.transform);
            FillParent(dim);
            dim.AddComponent<Image>().color = UguiPalette.ScrimDim;

            // Centered panel.
            var panel = NewChild("Panel", canvasGO.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(360f, 330f);
            panel.AddComponent<Image>().color = UguiPalette.PanelBg;

            // Accent strip along the panel top — same chrome family as the
            // scoreboard / stats panels.
            var accent = NewChild("AccentTop", panel.transform);
            var accentRT = accent.GetComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 1f);
            accentRT.anchorMax = new Vector2(1f, 1f);
            accentRT.pivot = new Vector2(0.5f, 1f);
            accentRT.sizeDelta = new Vector2(0f, 3f);
            accentRT.anchoredPosition = Vector2.zero;
            accent.AddComponent<Image>().color = UguiPalette.Accent;

            var title = NewChild("Title", panel.transform);
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 64f);
            titleRT.anchoredPosition = new Vector2(0f, -10f);
            var titleText = title.AddComponent<Text>();
            titleText.text = "Paused";
            titleText.font = UIFont;
            titleText.fontSize = 34;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            titleText.color = UguiPalette.Text;

            BuildButton(panel.transform, "ResumeButton", "Resume", row: 0, HandleResumeClicked);
            BuildButton(panel.transform, "SettingsButton", "Settings", row: 1, HandleSettingsClicked);
            _returnButton = BuildButton(panel.transform, "ReturnButton", "Return to Garage", row: 2, HandleReturnClicked);

            var hint = NewChild("Hint", panel.transform);
            var hintRT = hint.GetComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0f, 0f);
            hintRT.anchorMax = new Vector2(1f, 0f);
            hintRT.pivot = new Vector2(0.5f, 0f);
            hintRT.sizeDelta = new Vector2(0f, 26f);
            hintRT.anchoredPosition = new Vector2(0f, 8f);
            var hintText = hint.AddComponent<Text>();
            hintText.text = "hold Alt in-game for a quick cursor";
            hintText.font = UIFont;
            hintText.fontSize = 13;
            hintText.fontStyle = FontStyle.Italic;
            hintText.alignment = TextAnchor.MiddleCenter;
            hintText.verticalOverflow = VerticalWrapMode.Overflow;
            hintText.color = UguiPalette.TextDim;

            var group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 1f;
        }

        private Button BuildButton(Transform parent, string objName, string label, int row,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = NewChild(objName, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(280f, 48f);
            rt.anchoredPosition = new Vector2(0f, -84f - row * 58f);

            var img = go.AddComponent<Image>();
            img.color = UguiPalette.ButtonIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = UguiPalette.Accent;
            cols.pressedColor = UguiPalette.AccentPressed;
            cols.selectedColor = cols.highlightedColor;
            btn.colors = cols;

            var labelGO = NewChild("Label", go.transform);
            FillParent(labelGO);
            var text = labelGO.AddComponent<Text>();
            text.text = label;
            text.font = UIFont;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = UguiPalette.Text;

            btn.onClick.AddListener(onClick);
            return btn;
        }

        private static GameObject NewChild(string name, Transform parent)
            => Robogame.Core.UguiKit.NewChild(name, parent);

        private static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
