using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// The kit's one pressable: pointer feel (hover wash, press stamp),
    /// cue routing, and the optional hover annotation — for both button
    /// styles of the ink UI (solid primary blob, ghost wash-underline).
    /// Replaces the per-panel <see cref="Button"/> + ColorBlock wiring
    /// (ui-direction.md open question #1), with motion included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Press = the Stamp verb pressing INTO the paper:
    /// <see cref="UiMotion.PressScale"/> + a 1-reference-px dip on the face,
    /// <see cref="UiMotion.Stroke"/> ease-settle both ways, fully
    /// retargetable (a fast hover-off mid-press never snaps).
    /// </para>
    /// <para>
    /// Built procedurally like every panel: <c>AddComponent</c> then the
    /// Configure* calls — no serialized fields, so the AddComponent-runs-
    /// OnEnable footgun doesn't apply.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: press/hover/cue contract.
    [DisallowMultipleComponent]
    public sealed class InkButton : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private RectTransform _face;
        private Vector2 _faceRestPos;
        private float _faceRestRotZ;
        private float _hoverScale = 1f;
        private float _hoverRotZ = float.NaN;   // NaN = no rotation on hover

        private Graphic _faceGraphic;              // primary style
        private Color _faceIdle, _faceHover, _facePressed;
        private bool _hasFaceTint;

        private Image _wash;                       // ghost style
        private Color _washIdle, _washHover;
        private float _washIdleFill = 0.94f;
        private bool _hasWash;

        private CanvasGroup _annot;                // optional hover annotation
        private RectTransform _annotRt;
        private Vector2 _annotRestPos;

        private AudioCue _hoverCue = AudioCue.UiHover;
        private AudioCue _clickCue = AudioCue.UiClick;
        private Action _clickVoice;                // overrides _clickCue (e.g. UiCues.Confirm)
        private Action _onClick;

        /// <summary>Raised on hover enter/exit — the home diagram listens to answer the menu.</summary>
        public event Action<bool> HoverChanged;

        private bool _hover, _pressed;

        // -----------------------------------------------------------------
        // Setup (call once, right after AddComponent)
        // -----------------------------------------------------------------

        /// <summary>The RectTransform the press stamp animates. Its rest pose is captured now.</summary>
        public InkButton WithFace(RectTransform face)
        {
            _face = face;
            _faceRestPos = face.anchoredPosition;
            _faceRestRotZ = face.localEulerAngles.z;
            return this;
        }

        /// <summary>
        /// Hover pose for hero buttons: the face grows toward the cursor
        /// and (optionally) straightens from its resting tilt — attention
        /// before the stamp. Press still dips to
        /// <see cref="UiMotion.PressScale"/>, so the down-beat reads
        /// stronger from up here.
        /// </summary>
        public InkButton WithHoverPose(float scale, float rotZ = float.NaN)
        {
            _hoverScale = scale;
            _hoverRotZ = rotZ;
            return this;
        }

        /// <summary>Primary style: the face graphic tints idle → hover → pressed (ink → ink-hover → near-black).</summary>
        public InkButton WithFaceTint(Graphic faceGraphic, Color idle, Color hover, Color pressed)
        {
            _faceGraphic = faceGraphic;
            _faceIdle = idle; _faceHover = hover; _facePressed = pressed;
            _hasFaceTint = true;
            faceGraphic.color = idle;
            return this;
        }

        /// <summary>
        /// Ghost style: the wash underline idles part-drawn and translucent;
        /// hover finishes the swipe (fillAmount → 1) and deepens it. The
        /// image is switched to horizontal Filled here.
        /// </summary>
        public InkButton WithWash(Image wash, Color idle, Color hover, float idleFill = 0.94f)
        {
            _wash = wash;
            _washIdle = idle; _washHover = hover; _washIdleFill = idleFill;
            _hasWash = true;
            wash.type = Image.Type.Filled;
            wash.fillMethod = Image.FillMethod.Horizontal;
            wash.fillOrigin = (int)Image.OriginHorizontal.Left;
            wash.fillAmount = idleFill;
            wash.color = idle;
            return this;
        }

        /// <summary>Hover annotation ("— to the workshop"): fades in and settles 6 px leftward-in.</summary>
        public InkButton WithAnnotation(CanvasGroup annot, RectTransform annotRt)
        {
            _annot = annot;
            _annotRt = annotRt;
            _annotRestPos = annotRt.anchoredPosition;
            annot.alpha = 0f;
            annotRt.anchoredPosition = _annotRestPos + new Vector2(-6f, 0f);
            return this;
        }

        public InkButton WithCues(AudioCue hover, AudioCue click)
        {
            _hoverCue = hover; _clickCue = click;
            return this;
        }

        /// <summary>Replace the click cue with a composite voice (e.g. <see cref="UiCues.Confirm"/>).</summary>
        public InkButton WithClickVoice(Action voice)
        {
            _clickVoice = voice;
            return this;
        }

        public InkButton OnClick(Action handler)
        {
            _onClick = handler;
            return this;
        }

        // -----------------------------------------------------------------
        // Pointer handlers
        // -----------------------------------------------------------------

        public void OnPointerEnter(PointerEventData _)
        {
            _hover = true;
            AudioRouter.PlayUI(_hoverCue);
            if (_hasFaceTint && !_pressed)
                UiTween.Tint(_faceGraphic, _faceHover, UiMotion.Tick);
            if (_face != null && !_pressed && _hoverScale != 1f)
                UiTween.Scale(_face, _hoverScale, UiMotion.Stroke);
            if (_face != null && !float.IsNaN(_hoverRotZ))
                UiTween.RotZ(_face, _hoverRotZ, UiMotion.Stroke);
            if (_hasWash)
            {
                UiTween.Fill(_wash, 1f, UiMotion.Stroke);
                UiTween.Tint(_wash, _washHover, UiMotion.Stroke);
            }
            if (_annot != null)
            {
                UiTween.Alpha(_annot, 0.9f, UiMotion.Stroke);
                UiTween.Move(_annotRt, _annotRestPos, UiMotion.Stroke);
            }
            HoverChanged?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData _)
        {
            _hover = false;
            if (_pressed) ReleaseVisual(); // dragged off — un-stamp
            _pressed = false;
            if (_hasFaceTint)
                UiTween.Tint(_faceGraphic, _faceIdle, UiMotion.Tick);
            if (_face != null && _hoverScale != 1f)
                UiTween.Scale(_face, 1f, UiMotion.Stroke);
            if (_face != null && !float.IsNaN(_hoverRotZ))
                UiTween.RotZ(_face, _faceRestRotZ, UiMotion.Stroke);
            if (_hasWash)
            {
                UiTween.Fill(_wash, _washIdleFill, UiMotion.Stroke);
                UiTween.Tint(_wash, _washIdle, UiMotion.Stroke);
            }
            if (_annot != null)
            {
                UiTween.Alpha(_annot, 0f, UiMotion.Stroke);
                UiTween.Move(_annotRt, _annotRestPos + new Vector2(-6f, 0f), UiMotion.Stroke);
            }
            HoverChanged?.Invoke(false);
        }

        public void OnPointerDown(PointerEventData _)
        {
            _pressed = true;
            if (_face != null)
            {
                UiTween.Scale(_face, UiMotion.PressScale, UiMotion.Stroke);
                UiTween.Move(_face, _faceRestPos + new Vector2(0f, -UiMotion.PressDipPx), UiMotion.Stroke);
            }
            if (_hasFaceTint)
                UiTween.Tint(_faceGraphic, _facePressed, UiMotion.Tick);
        }

        public void OnPointerUp(PointerEventData _)
        {
            if (!_pressed) return;
            _pressed = false;
            ReleaseVisual();
            if (_hasFaceTint)
                UiTween.Tint(_faceGraphic, _hover ? _faceHover : _faceIdle, UiMotion.Tick);
        }

        public void OnPointerClick(PointerEventData _)
        {
            if (_clickVoice != null) _clickVoice();
            else AudioRouter.PlayUI(_clickCue);
            _onClick?.Invoke();
        }

        private void ReleaseVisual()
        {
            if (_face == null) return;
            // Coming up from a press lands on the hover pose while the
            // pointer is still over us, the rest pose otherwise.
            UiTween.Scale(_face, _hover ? _hoverScale : 1f, UiMotion.Stroke);
            UiTween.Move(_face, _faceRestPos, UiMotion.Stroke);
        }
    }
}
