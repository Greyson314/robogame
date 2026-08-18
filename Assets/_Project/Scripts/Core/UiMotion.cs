using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// The UI motion token table — durations, easing curves, and the press
    /// standard for the "ink behaves like ink" motion language. The single
    /// source of truth: panels never hard-code a duration or curve, the same
    /// rule <see cref="UguiPalette"/> enforces for colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four verbs cover every UI animation (see
    /// <c>docs/research/ui-design-handoff-motion.md</c>): <b>Draw</b>
    /// (entrances stroke in, <see cref="Draw"/>), <b>Wet</b> (value changes
    /// spread like wet ink, <see cref="Settle"/>), <b>Stamp</b>
    /// (confirmations land 1.5 → 1 and stop dead), <b>Blot</b> (exits fade
    /// down softly, always quieter than entrances). Nothing bounces — paper
    /// is calm; the slapstick lives in the arena.
    /// </para>
    /// <para>
    /// Easing is evaluated from 65-sample lookup tables baked once at
    /// init from the handoff's cubic béziers — no per-call allocation, no
    /// per-frame Newton iterations (INV-6 hygiene for the tween driver's
    /// hot loop).
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: token values verbatim.
    public static class UiMotion
    {
        // -----------------------------------------------------------------
        // Duration tokens (seconds)
        // -----------------------------------------------------------------

        /// <summary>Hover washes, colour fades — high-frequency feedback stays near-instant.</summary>
        public const float Tick = 0.08f;

        /// <summary>Press/release, toggle slide, hover wash draw, tab underline.</summary>
        public const float Stroke = 0.18f;

        /// <summary>Panels, modals, value washes, list rows — the Wet verb.</summary>
        public const float Settle = 0.26f;

        /// <summary>Entrance stroke draw-ins (underlines, rules, diagram) — the Draw verb.</summary>
        public const float Draw = 0.42f;

        /// <summary>Full-screen ink wipe between scenes.</summary>
        public const float Page = 0.64f;

        /// <summary>Delay between sibling entrances. Cap the sibling count, not the step.</summary>
        public const float Stagger = 0.07f;

        /// <summary>Every pressable face scales to this on pointer-down…</summary>
        public const float PressScale = 0.96f;

        /// <summary>…and dips this many reference pixels — a stamp pressing into paper.</summary>
        public const float PressDipPx = 1f;

        /// <summary>The Stamp verb: lands from this scale down to 1 and stops dead.</summary>
        public const float StampFromScale = 1.5f;

        // -----------------------------------------------------------------
        // Reduced motion
        // -----------------------------------------------------------------

        /// <summary>
        /// Accessibility gate (Settings → QoL). When on, entrance
        /// choreography collapses to a single quick fade and idle loops
        /// freeze; state feedback (hover, press, value washes) stays. UI
        /// presentation only — no gameplay outcome rides on this (INV-1).
        /// </summary>
        public static bool Reduced => Tweakables.GetBool(Tweakables.UiReducedMotion);

        // -----------------------------------------------------------------
        // Easing
        // -----------------------------------------------------------------

        public enum Ease
        {
            /// <summary>Constant rate — dash marches, spinners only.</summary>
            Linear,
            /// <summary>cubic-bezier(0.2, 0, 0, 1) — the default. Decisive start, soft landing, zero bounce.</summary>
            Settle,
            /// <summary>cubic-bezier(0.215, 0.61, 0.355, 1) — stroke draw-ins, a hand finishing a line.</summary>
            Draw,
            /// <summary>cubic-bezier(0.4, 0, 0.2, 1) — the wipe brush crossing the screen.</summary>
            Page,
        }

        private const int LutSize = 65;
        private static float[] s_lutSettle;
        private static float[] s_lutDraw;
        private static float[] s_lutPage;

        // Statics survive domain reload; cheap to just rebake.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lutSettle = null; s_lutDraw = null; s_lutPage = null;
        }

        /// <summary>Evaluate an easing curve at normalized time <paramref name="t"/> (clamped 0–1).</summary>
        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.Settle: return Sample(s_lutSettle ??= BakeBezier(0.20f, 0f, 0f, 1f), t);
                case Ease.Draw:   return Sample(s_lutDraw   ??= BakeBezier(0.215f, 0.61f, 0.355f, 1f), t);
                case Ease.Page:   return Sample(s_lutPage   ??= BakeBezier(0.40f, 0f, 0.20f, 1f), t);
                default:          return t;
            }
        }

        private static float Sample(float[] lut, float t)
        {
            float f = t * (LutSize - 1);
            int i = (int)f;
            if (i >= LutSize - 1) return 1f;
            return Mathf.Lerp(lut[i], lut[i + 1], f - i);
        }

        /// <summary>
        /// Bake y(x) samples of the CSS cubic bézier (x1,y1,x2,y2) —
        /// endpoints (0,0)/(1,1). Newton solve per sample; init-time only.
        /// </summary>
        private static float[] BakeBezier(float x1, float y1, float x2, float y2)
        {
            var lut = new float[LutSize];
            for (int i = 0; i < LutSize; i++)
            {
                float x = i / (float)(LutSize - 1);
                // Solve bezierX(u) = x for u.
                float u = x;
                for (int n = 0; n < 8; n++)
                {
                    float bx = BezierAxis(u, x1, x2) - x;
                    float dx = BezierAxisDeriv(u, x1, x2);
                    if (Mathf.Abs(dx) < 1e-6f) break;
                    u = Mathf.Clamp01(u - bx / dx);
                }
                lut[i] = BezierAxis(u, y1, y2);
            }
            lut[0] = 0f; lut[LutSize - 1] = 1f;
            return lut;
        }

        private static float BezierAxis(float u, float p1, float p2)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        private static float BezierAxisDeriv(float u, float p1, float p2)
        {
            float v = 1f - u;
            return 3f * v * v * p1 + 6f * v * u * (p2 - p1) + 3f * u * u * (1f - p2);
        }
    }
}
