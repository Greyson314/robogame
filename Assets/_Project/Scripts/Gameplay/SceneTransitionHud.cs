using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Drives the Pass A garage ⇄ arena scene transitions via a real UGUI
    /// canvas (so Input System UI integration suppresses player actions
    /// like fire while clicking the button).
    /// </summary>
    /// <remarks>
    /// On <c>Awake</c> this component lazily builds an <see cref="EventSystem"/>
    /// (with <see cref="InputSystemUIInputModule"/>), a <see cref="Canvas"/>
    /// in <c>ScreenSpaceOverlay</c>, and a single <see cref="Button"/> labelled
    /// for the current <see cref="GameStateController.State"/>. Pass B will
    /// replace this with a proper authored Canvas prefab.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SceneTransitionHud : MonoBehaviour
    {
        [SerializeField] private Vector2 _buttonSize = new Vector2(220f, 64f);
        [SerializeField] private Vector2 _margin = new Vector2(28f, 28f);
        [SerializeField] private int _fontSize = 22;

        private Button _button;
        private Text _label;
        private GameState _lastState = GameState.Bootstrap;

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
            RefreshLabel();
        }

        private void Update()
        {
            // Cheap: only restyle when the state changes.
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            if (state.State == _lastState) return;
            _lastState = state.State;
            RefreshLabel();
        }

        // -----------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            EventSystem es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
            // The legacy StandaloneInputModule conflicts with the new Input
            // System's UI module; replace if present.
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildCanvas()
        {
            // Canvas root.
            var canvasGO = new GameObject("SceneTransitionCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Button.
            var btnGO = new GameObject("LaunchButton");
            btnGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);
            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.92f);
            _button = btnGO.AddComponent<Button>();
            _button.targetGraphic = img;

            ColorBlock cols = _button.colors;
            cols.normalColor = new Color(1f, 1f, 1f, 1f);
            cols.highlightedColor = new Color(0.95f, 0.55f, 0.10f, 1f); // hazard orange tint
            cols.pressedColor = new Color(0.7f, 0.4f, 0.05f, 1f);
            cols.selectedColor = cols.highlightedColor;
            _button.colors = cols;

            var rt = btnGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = _buttonSize;
            rt.anchoredPosition = new Vector2(-_margin.x, _margin.y);

            // Label child.
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
            _label = labelGO.AddComponent<Text>();
            _label.alignment = TextAnchor.MiddleCenter;
            _label.fontSize = _fontSize;
            _label.color = Color.white;
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _button.onClick.AddListener(HandleClick);
        }

        private void RefreshLabel()
        {
            if (_label == null || _button == null) return;
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                _button.gameObject.SetActive(false);
                return;
            }

            switch (state.State)
            {
                case GameState.Garage:
                    _label.text = "Launch ▶";
                    _button.gameObject.SetActive(true);
                    break;
                case GameState.Arena:
                    _label.text = "◀ Garage";
                    _button.gameObject.SetActive(true);
                    break;
                default:
                    _button.gameObject.SetActive(false);
                    break;
            }
        }

        private void HandleClick()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;

            switch (state.State)
            {
                case GameState.Garage:
                    var garage = FindAnyObjectByType<GarageController>();
                    if (garage != null) garage.Launch();
                    else state.EnterArena();
                    break;
                case GameState.Arena:
                    var arena = FindAnyObjectByType<ArenaController>();
                    if (arena != null) arena.Return();
                    else state.EnterGarage();
                    break;
            }
        }
    }
}
