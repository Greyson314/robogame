using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Minimal screen-centre crosshair drawn via OnGUI. No prefabs, no
    /// Canvas — just paints four short bars and a centre dot in immediate
    /// mode. Lives on the same GameObject as <see cref="FollowCamera"/>
    /// (typically Main Camera) so the reticle and the camera-ray aim
    /// stay in lockstep.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AimReticle : MonoBehaviour
    {
        [Tooltip("Length of each crosshair arm, in pixels.")]
        [SerializeField, Min(1f)] private float _armLength = 8f;

        [Tooltip("Thickness of each arm, in pixels.")]
        [SerializeField, Min(1f)] private float _thickness = 2f;

        [Tooltip("Gap between the centre and the inner edge of each arm.")]
        [SerializeField, Min(0f)] private float _gap = 4f;

        [Tooltip("Diameter of the centre dot. 0 to hide.")]
        [SerializeField, Min(0f)] private float _dotSize = 2f;

        [SerializeField] private Color _color = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color _outlineColor = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField, Min(0f)] private float _outline = 1f;

        private void OnGUI()
        {
            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            float len = _armLength;
            float th  = _thickness;
            float gap = _gap;

            // Horizontal & vertical arms (left, right, up, down).
            DrawBar(cx - gap - len, cy - th * 0.5f, len, th);
            DrawBar(cx + gap,       cy - th * 0.5f, len, th);
            DrawBar(cx - th * 0.5f, cy - gap - len, th, len);
            DrawBar(cx - th * 0.5f, cy + gap,       th, len);

            if (_dotSize > 0f)
            {
                DrawBar(cx - _dotSize * 0.5f, cy - _dotSize * 0.5f, _dotSize, _dotSize);
            }
        }

        private void DrawBar(float x, float y, float w, float h)
        {
            if (_outline > 0f)
            {
                Rect outline = new Rect(x - _outline, y - _outline, w + _outline * 2f, h + _outline * 2f);
                DrawRect(outline, _outlineColor);
            }
            DrawRect(new Rect(x, y, w, h), _color);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
