using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Full-screen edge vignette that pulses red when the local chassis
    /// drops under <see cref="_threshold"/>. Pure visceral feedback —
    /// the precise HP fraction is already on the <see cref="Gameplay.ObjectiveHud"/>
    /// HP rail; this layer just makes "you are about to die" undeniable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IMGUI to match the rest of the player HUD stack (AimReticle,
    /// VehicleStatsHud, HitMarkerOverlay). Renders four edge bands
    /// (top / bottom / left / right) whose alpha scales with how far
    /// below the threshold the chassis has sunk — a true radial
    /// vignette needs a shader/quad blit and isn't worth the extra
    /// pipeline cost for what amounts to a danger frame.
    /// </para>
    /// <para>
    /// Audio: declares <see cref="AudioCue.LowHealthAlert"/> and fires
    /// it on a fixed interval while in the danger band. Per invariant #8
    /// the cue is declared even before a clip exists — the missing-cue
    /// logger surfaces it. Re-armed at every transition out of the
    /// threshold so respawning a healthy chassis resets cleanly.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LowHealthVignetteHud : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Chassis HP fraction below which the vignette starts pulsing. " +
                 "Matches ObjectiveHud's alert threshold by default so the rail " +
                 "going red and the screen going red happen on the same frame.")]
        [SerializeField, Range(0f, 1f)] private float _threshold = 0.30f;

        [Header("Vignette")]
        [Tooltip("Vignette tint at maximum intensity (HP fraction = 0).")]
        [SerializeField] private Color _color = new Color(0.95f, 0.10f, 0.10f, 1f);

        [Tooltip("Thickness of each edge band in screen pixels.")]
        [SerializeField, Min(8f)] private float _bandThickness = 96f;

        [Tooltip("Pulse rate (Hz) of the vignette intensity oscillation.")]
        [SerializeField, Min(0.1f)] private float _pulseHz = 1.4f;

        [Header("Audio")]
        [Tooltip("Seconds between LowHealthAlert audio pings while in the danger band.")]
        [SerializeField, Min(0.2f)] private float _audioInterval = 2.0f;

        private FollowCamera _follow;
        private Transform _boundChassis;
        private Robot _robot;
        private float _nextAudioAt = -1f;
        private bool _wasInDanger;

        // Persisted-color GUIStyle / texture state. Pixel texture pulled
        // from HudStyles so the asset cost is shared with every other HUD.

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            // Rebind on chassis swap (respawn). Mirrors VehicleStatsHud /
            // ObjectiveHud's target-resolution pattern.
            Transform t = _follow != null ? _follow.Target : null;
            if (t != _boundChassis)
            {
                _boundChassis = t;
                _robot = _boundChassis != null ? _boundChassis.GetComponent<Robot>() : null;
                _wasInDanger = false;
                _nextAudioAt = -1f;
            }

            if (!TryGetHpFraction(out float frac)) return;
            bool inDanger = frac < _threshold;

            if (inDanger)
            {
                if (!_wasInDanger || _nextAudioAt < 0f)
                {
                    AudioRouter.PlayUI(AudioCue.LowHealthAlert);
                    _nextAudioAt = Time.unscaledTime + _audioInterval;
                }
                else if (Time.unscaledTime >= _nextAudioAt)
                {
                    AudioRouter.PlayUI(AudioCue.LowHealthAlert);
                    _nextAudioAt = Time.unscaledTime + _audioInterval;
                }
            }
            _wasInDanger = inDanger;
        }

        private bool TryGetHpFraction(out float frac)
        {
            frac = 0f;
            if (_robot == null || _robot.InitialBlockCount <= 0) return false;
            frac = Mathf.Clamp01((float)_robot.BlockCount / _robot.InitialBlockCount);
            return true;
        }

        private void OnGUI()
        {
            if (!TryGetHpFraction(out float frac)) return;
            if (frac >= _threshold) return;

            // Severity goes 0 → 1 as HP fraction drops from threshold → 0.
            float severity = Mathf.Clamp01((_threshold - frac) / Mathf.Max(0.0001f, _threshold));
            // Pulse — small breath on top of the base severity (15 % swing).
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * _pulseHz * Mathf.PI * 2f);
            float alpha = severity * pulse;

            Color tint = _color;
            tint.a *= alpha;
            Color prev = GUI.color;
            GUI.color = tint;

            float w = Screen.width;
            float h = Screen.height;
            float b = _bandThickness;

            // Four edge bands. Inner edges fall off to zero by varying
            // alpha down through three sub-bands per side; the band itself
            // is drawn with descending alpha to fake a soft falloff
            // without leaving IMGUI.
            DrawSoftEdge(new Rect(0f, 0f, w, b), Vector2.up, tint);            // top
            DrawSoftEdge(new Rect(0f, h - b, w, b), Vector2.down, tint);        // bottom
            DrawSoftEdge(new Rect(0f, 0f, b, h), Vector2.right, tint);          // left
            DrawSoftEdge(new Rect(w - b, 0f, b, h), Vector2.left, tint);        // right

            GUI.color = prev;
        }

        // Three-slice "soft" edge: front (full alpha), middle (50%), tail
        // (15%). Cheap stand-in for a shader-based radial vignette while
        // staying inside the immediate-mode HUD pattern.
        private static void DrawSoftEdge(Rect band, Vector2 inward, Color tint)
        {
            const int slices = 3;
            float[] alphas = { 1f, 0.5f, 0.15f };
            Color baseTint = tint;
            for (int i = 0; i < slices; i++)
            {
                Color c = baseTint;
                c.a *= alphas[i];
                GUI.color = c;
                Rect r = SliceRect(band, inward, i, slices);
                GUI.DrawTexture(r, HudStyles.Pixel);
            }
        }

        private static Rect SliceRect(Rect band, Vector2 inward, int slice, int slices)
        {
            // For a top band, inward = +Y. We split the band into N
            // horizontal sub-bands; slice 0 sits at the outer edge (top
            // of the screen), slice N-1 sits at the inner edge. Same
            // pattern works for the other three sides by aligning to
            // inward direction.
            float t = (float)slice / slices;
            float t1 = (float)(slice + 1) / slices;
            if (inward == Vector2.up)
                return new Rect(band.x, band.y + band.height * t, band.width, band.height * (t1 - t));
            if (inward == Vector2.down)
                return new Rect(band.x, band.y + band.height * (1f - t1), band.width, band.height * (t1 - t));
            if (inward == Vector2.right)
                return new Rect(band.x + band.width * t, band.y, band.width * (t1 - t), band.height);
            // Vector2.left
            return new Rect(band.x + band.width * (1f - t1), band.y, band.width * (t1 - t), band.height);
        }
    }
}
