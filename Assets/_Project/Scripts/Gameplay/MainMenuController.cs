using Robogame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Sheet no. 01 — home. The main menu drawn as a page from the
    /// inventor's notebook: menu column on the left third, the player's
    /// current blueprint inked as fig. 1 on the right
    /// (<see cref="BotInkDiagram"/>), drafting title block bottom-right.
    /// Three actions: <b>Begin</b> (ink-wipe to the garage),
    /// <b>Settings</b>, <b>Quit</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Motion follows the Ink &amp; Motion kit: a staged entrance (paper →
    /// grid → title → underline draw → splat stamps → buttons → diagram →
    /// footer) built entirely from <see cref="UiTween"/> delays, skippable
    /// by any input; hover makes the diagram answer through
    /// <see cref="BotInkDiagram.SetFocus"/>; Begin plays the D–F–A
    /// flourish and leaves through <see cref="PageWipe"/>.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: sheet 01 layout + entrance choreography.
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name to load when Begin is pressed. Must be in Build Settings.")]
        [SerializeField] private string _startScene = "Garage";

        [Tooltip("Game title shown at the top of the menu.")]
        [SerializeField] private string _title = "Robogame";

        [Tooltip("Optional tagline shown beneath the title.")]
        [SerializeField] private string _tagline = "A Bestiary of Contraptions";

        private BotInkDiagram _diagram;
        private float _entranceEndsAt;

        private void Awake()
        {
            EnsureEventSystem();
            BuildPanel();
        }

        private void Update()
        {
            // Any input completes the entrance instantly — nobody waits on
            // choreography twice.
            if (_entranceEndsAt <= 0f || Time.unscaledTime >= _entranceEndsAt) return;
            bool key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            bool mouse = Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame);
            if (key || mouse)
            {
                UiTween.CompleteAll();
                _entranceEndsAt = 0f;
            }
        }

        // -----------------------------------------------------------------
        // EventSystem (shared with other HUDs)
        // -----------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            EventSystem es = Object.FindAnyObjectByType<EventSystem>();
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

        // -----------------------------------------------------------------
        // Panel construction
        // -----------------------------------------------------------------

        private void BuildPanel()
        {
            var canvasGO = new GameObject("MainMenuCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // ---- paper ground + drafting grid --------------------------
            CanvasGroup paper = Wrap(canvasGO.transform, "Paper");
            FillParent((RectTransform)paper.transform);
            var paperImg = paper.gameObject.AddComponent<Image>();
            paperImg.sprite = InkKit.Paper;
            paperImg.color = Color.white;
            Enter(paper, 0f, 0.25f);

            CanvasGroup grid = Wrap(canvasGO.transform, "Grid");
            FillParent((RectTransform)grid.transform);
            var gridImg = grid.gameObject.AddComponent<Image>();
            gridImg.sprite = InkKit.GridTile;
            gridImg.type = Image.Type.Tiled;
            gridImg.color = UguiPalette.GridLine;
            gridImg.raycastTarget = false;
            Enter(grid, 0.08f, 0.30f);

            CanvasGroup regs = Wrap(canvasGO.transform, "RegMarks");
            FillParent((RectTransform)regs.transform);
            AddRegMark(regs.transform, new Vector2(0f, 0f), new Vector2(34f, 34f));
            AddRegMark(regs.transform, new Vector2(1f, 0f), new Vector2(-34f, 34f));
            AddRegMark(regs.transform, new Vector2(0f, 1f), new Vector2(34f, -34f));
            AddRegMark(regs.transform, new Vector2(1f, 1f), new Vector2(-34f, -34f));
            Enter(regs, 0.12f, 0.20f);

            // ---- the diagram (built early so buttons can focus it) -----
            _diagram = BotInkDiagram.Build(canvasGO.transform);
            CanvasGroup[] dg = _diagram.EntranceGroups;
            if (dg != null && dg.Length == 3)
            {
                Enter(dg[0], 0.43f, 0.50f);
                Enter(dg[1], 0.95f, 0.40f);
                Enter(dg[2], 1.15f, 0.40f);
            }

            // ---- title column -------------------------------------------
            CanvasGroup title = Wrap(canvasGO.transform, "Title");
            var titleRt = TopLeft((RectTransform)title.transform, 140f, 208f, 760f, 120f);
            var titleText = UguiKit.AddText(title.transform, _title, InkKit.Display, 96, FontStyle.Normal,
                HudStyles.TextPrimary, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            Enter(title, 0.16f, 0.26f, titleRt);

            var underlineGo = UguiKit.NewChild("TitleUnderline", canvasGO.transform);
            var underlineRt = TopLeft((RectTransform)underlineGo.transform, 140f, 331f, 588f, 13f);
            underlineRt.localRotation = Quaternion.Euler(0f, 0f, -0.5f);
            var underlineImg = underlineGo.AddComponent<Image>();
            underlineImg.sprite = InkKit.Underline;
            underlineImg.color = UguiPalette.Ink;
            underlineImg.raycastTarget = false;
            underlineImg.type = Image.Type.Filled;
            underlineImg.fillMethod = Image.FillMethod.Horizontal;
            underlineImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            underlineImg.fillAmount = 0f;
            UiTween.Fill(underlineImg, 1f, UiMotion.Reduced ? 0.15f : 0.34f, UiMotion.Ease.Draw,
                UiMotion.Reduced ? 0f : 0.30f);
            NoteEntrance(0.30f + 0.34f);

            AddSplat(canvasGO.transform, new Vector2(700f, 330f), 16f, 0.66f);
            AddSplat(canvasGO.transform, new Vector2(726f, 342f), 9f, 0.73f);

            if (!string.IsNullOrEmpty(_tagline))
            {
                CanvasGroup tag = Wrap(canvasGO.transform, "Tagline");
                var tagRt = TopLeft((RectTransform)tag.transform, 142f, 372f, 620f, 40f);
                UguiKit.AddText(tag.transform, _tagline, InkKit.Annotation, 25, FontStyle.Italic,
                    HudStyles.TextMuted, TextAnchor.MiddleLeft,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    raycastTarget: false, horizontalOverflow: true);
                Enter(tag, 0.48f, 0.24f, tagRt);
            }

            // ---- menu ----------------------------------------------------
            BuildPrimaryRow(canvasGO.transform, "Begin", "— to the workshop", 512f, 0.56f,
                BotInkDiagram.Focus.Pilot, HandleStart, UiCues.Confirm);
            BuildGhostRow(canvasGO.transform, "Settings", "— calibrate the instruments", 642f, 0.64f,
                BotInkDiagram.Focus.Works, HandleSettings,
                WashColor(UguiPalette.Accent, 0.62f), WashColor(UguiPalette.Accent, 1f), AudioCue.UiClick);
            BuildGhostRow(canvasGO.transform, "Quit", "— close the notebook", 712f, 0.72f,
                BotInkDiagram.Focus.Rest, HandleExit,
                WashColor(UguiPalette.Vermilion, 0.35f), WashColor(UguiPalette.Vermilion, 0.8f), AudioCue.UiBack);

            // ---- footer --------------------------------------------------
            CanvasGroup hint = Wrap(canvasGO.transform, "EscHint");
            var hintRt = (RectTransform)hint.transform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0f, 0f);
            hintRt.pivot = new Vector2(0f, 0f);
            hintRt.sizeDelta = new Vector2(420f, 30f);
            hintRt.anchoredPosition = new Vector2(56f, 64f);
            UguiKit.AddText(hint.transform, "Esc — Open Settings", InkKit.Display, 19, FontStyle.Normal,
                HudStyles.TextMuted, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            Enter(hint, 1.04f, 0.24f);

            CanvasGroup flavor = Wrap(canvasGO.transform, "Flavor");
            var flavorRt = (RectTransform)flavor.transform;
            flavorRt.anchorMin = flavorRt.anchorMax = new Vector2(0f, 0f);
            flavorRt.pivot = new Vector2(0f, 0f);
            flavorRt.sizeDelta = new Vector2(360f, 26f);
            flavorRt.anchoredPosition = new Vector2(84f, 30f);
            // Mirror writing — flavor lines only. TRACE[DOC:research/ui-design-handoff]
            Text flavorText = UguiKit.AddText(flavor.transform, "the frame remembers every fall",
                InkKit.Annotation, 16, FontStyle.Italic, HudStyles.TextMuted, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            flavorText.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            Color fc = flavorText.color; fc.a = 0.6f; flavorText.color = fc;
            Enter(flavor, 1.09f, 0.24f);

            string version = string.IsNullOrEmpty(Application.version) ? "dev" : Application.version;
            GameObject tblock = SheetTitleBlock.Build(canvasGO.transform,
                _title, "— a bestiary of contraptions",
                "no. 01 — home",
                $"v{version} · {Application.platform}", "drawn by mutedtuple");
            Enter(tblock.GetComponent<CanvasGroup>(), 1.14f, 0.28f);
        }

        // -----------------------------------------------------------------
        // Menu rows
        // -----------------------------------------------------------------

        private void BuildPrimaryRow(Transform parent, string label, string annotation, float yFromTop,
            float delay, BotInkDiagram.Focus focus, System.Action onClick, System.Action clickVoice)
        {
            CanvasGroup row = Wrap(parent, $"Row_{label}");
            var rowRt = TopLeft((RectTransform)row.transform, 140f, yFromTop, 760f, 92f);

            var btnGo = UguiKit.NewChild($"Btn_{label}", row.transform);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0f, 0.5f);
            btnRt.pivot = new Vector2(0f, 0.5f);
            btnRt.sizeDelta = new Vector2(400f, 86f);
            btnRt.anchoredPosition = Vector2.zero;

            var faceGo = UguiKit.NewChild("Face", btnGo.transform);
            var faceRt = (RectTransform)faceGo.transform;
            FillParent(faceRt);
            faceRt.localRotation = Quaternion.Euler(0f, 0f, -0.7f);
            var faceImg = faceGo.AddComponent<Image>();
            faceImg.sprite = InkKit.BrushBlob;

            var labelText = UguiKit.AddText(faceGo.transform, label, InkKit.Display, 34, FontStyle.Normal,
                UguiPalette.CreamText, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            labelText.raycastTarget = false;

            CanvasGroup annot = BuildAnnotation(row.transform, annotation, 424f + 24f);

            btnGo.AddComponent<InkButton>()
                .WithFace(faceRt)
                .WithFaceTint(faceImg, UguiPalette.Ink, UguiPalette.InkHover, UguiPalette.InkPressed)
                // The blob leans toward the cursor and straightens from its
                // resting -0.7° tilt — attention before the stamp.
                .WithHoverPose(1.02f, -0.25f)
                .WithAnnotation(annot, (RectTransform)annot.transform)
                .WithClickVoice(clickVoice)
                .OnClick(onClick)
                .HoverChanged += on => _diagram.SetFocus(on ? focus : BotInkDiagram.Focus.None);

            Enter(row, delay, 0.22f, rowRt);
        }

        private void BuildGhostRow(Transform parent, string label, string annotation, float yFromTop,
            float delay, BotInkDiagram.Focus focus, System.Action onClick,
            Color washIdle, Color washHover, AudioCue clickCue)
        {
            CanvasGroup row = Wrap(parent, $"Row_{label}");
            var rowRt = TopLeft((RectTransform)row.transform, 140f, yFromTop, 760f, 60f);

            var btnGo = UguiKit.NewChild($"Btn_{label}", row.transform);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0f, 0.5f);
            btnRt.pivot = new Vector2(0f, 0.5f);
            btnRt.sizeDelta = new Vector2(300f, 56f);
            btnRt.anchoredPosition = Vector2.zero;
            // Invisible raycast face so the whole rect is clickable.
            var hit = btnGo.AddComponent<Image>();
            hit.color = Color.clear;

            var labelText = UguiKit.AddText(btnGo.transform, label, InkKit.Display, 29, FontStyle.Normal,
                HudStyles.TextPrimary, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Vector2(10f, 8f), Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            labelText.raycastTarget = false;

            // Wash underline behind the label's lower half; hover finishes
            // the swipe (fill 0.94 → 1) and deepens the tint.
            var washGo = UguiKit.NewChild("Wash", btnGo.transform);
            var washRt = (RectTransform)washGo.transform;
            washRt.anchorMin = new Vector2(0f, 0f);
            washRt.anchorMax = new Vector2(0f, 0f);
            washRt.pivot = new Vector2(0f, 0f);
            washRt.sizeDelta = new Vector2(214f, 13f);
            washRt.anchoredPosition = new Vector2(6f, 2f);
            var washImg = washGo.AddComponent<Image>();
            washImg.sprite = InkKit.WashFill;
            washImg.raycastTarget = false;

            CanvasGroup annot = BuildAnnotation(row.transform, annotation, 324f + 24f);

            btnGo.AddComponent<InkButton>()
                .WithFace(btnRt)
                .WithWash(washImg, washIdle, washHover)
                .WithAnnotation(annot, (RectTransform)annot.transform)
                .WithCues(AudioCue.UiHover, clickCue)
                .OnClick(onClick)
                .HoverChanged += on => _diagram.SetFocus(on ? focus : BotInkDiagram.Focus.None);

            Enter(row, delay, 0.22f, rowRt);
        }

        private static CanvasGroup BuildAnnotation(Transform row, string text, float x)
        {
            var go = UguiKit.NewChild("Annot", row);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(340f, 34f);
            rt.anchoredPosition = new Vector2(x, 0f);
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            UguiKit.AddText(go.transform, text, InkKit.Annotation, 20, FontStyle.Italic,
                HudStyles.TextMuted, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);
            return cg;
        }

        // -----------------------------------------------------------------
        // Entrance choreography helpers
        // -----------------------------------------------------------------

        /// <summary>Fade a group in on the entrance schedule; optional 9-px rise. Reduced motion = one quick fade.</summary>
        private void Enter(CanvasGroup cg, float delay, float dur, RectTransform rise = null)
        {
            if (UiMotion.Reduced) { delay = 0f; dur = 0.15f; rise = null; }
            cg.alpha = 0f;
            UiTween.Alpha(cg, 1f, dur, UiMotion.Ease.Settle, delay);
            if (rise != null)
            {
                Vector2 rest = rise.anchoredPosition;
                rise.anchoredPosition = rest + new Vector2(0f, -9f);
                UiTween.Move(rise, rest, dur, UiMotion.Ease.Settle, delay);
            }
            NoteEntrance(delay + dur);
        }

        private void NoteEntrance(float endsIn)
            => _entranceEndsAt = Mathf.Max(_entranceEndsAt, Time.unscaledTime + endsIn);

        private static CanvasGroup Wrap(Transform parent, string name)
            => UguiKit.NewChild(name, parent).AddComponent<CanvasGroup>();

        private static Color WashColor(Color c, float a) => new(c.r, c.g, c.b, a);

        private static RectTransform TopLeft(RectTransform rt, float x, float yFromTop, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            return rt;
        }

        private static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddRegMark(Transform parent, Vector2 anchor, Vector2 inset)
        {
            var go = UguiKit.NewChild("RegMark", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(16f, 16f);
            rt.anchoredPosition = inset;
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.RegMark;
            img.color = new Color(UguiPalette.Ink.r, UguiPalette.Ink.g, UguiPalette.Ink.b, 0.45f);
            img.raycastTarget = false;
        }

        /// <summary>Vermilion splat that STAMPS in (scale 1.5 → 1) off the underline's end.</summary>
        private void AddSplat(Transform parent, Vector2 posFromTopLeft, float size, float delay)
        {
            var go = UguiKit.NewChild("Splat", parent);
            var rt = TopLeft((RectTransform)go.transform, posFromTopLeft.x, posFromTopLeft.y, size, size);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.Splat;
            img.color = UguiPalette.Vermilion;
            img.raycastTarget = false;
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            if (UiMotion.Reduced)
            {
                UiTween.Alpha(cg, 1f, 0.15f);
                NoteEntrance(0.15f);
                return;
            }
            rt.localScale = new Vector3(UiMotion.StampFromScale, UiMotion.StampFromScale, 1f);
            UiTween.Alpha(cg, 1f, 0.08f, UiMotion.Ease.Settle, delay);
            UiTween.Scale(rt, 1f, 0.15f, UiMotion.Ease.Settle, delay);
            NoteEntrance(delay + 0.15f);
        }

        // -----------------------------------------------------------------
        // Button handlers
        // -----------------------------------------------------------------

        private void HandleStart()
        {
            if (string.IsNullOrEmpty(_startScene))
            {
                Debug.LogWarning("[Robogame] MainMenu: _startScene is empty.");
                return;
            }
            // TRACE[DOC:research/ui-design-handoff-motion]: sheet numbering — 02 is the garage.
            PageWipe.To(_startScene, "no. 02", "The Garage");
        }

        private static void HandleSettings()
        {
            // SettingsHud lives on the persistent Bootstrap; reach it via
            // FindAnyObjectByType so the MainMenu controller doesn't need a
            // serialised reference (Bootstrap is in a different scene).
            SettingsHud hud = Object.FindAnyObjectByType<SettingsHud>();
            if (hud == null)
            {
                Debug.LogWarning("[Robogame] MainMenu: no SettingsHud in scene. Did Bootstrap.unity load before MainMenu?");
                return;
            }
            hud.Open();
        }

        private static void HandleExit()
        {
#if UNITY_EDITOR
            // In editor, Application.Quit doesn't exit the editor — flip
            // playmode off instead so the button has visible effect.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
