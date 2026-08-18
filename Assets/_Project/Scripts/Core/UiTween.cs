using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>Opaque handle to a live tween. Stale handles no-op safely.</summary>
    public struct UiTweenHandle
    {
        internal int Index;
        internal int Version;
        public bool IsValid => Version != 0;
    }

    /// <summary>
    /// The project's one UI tween driver — a fixed-capacity, allocation-free
    /// interpolator for the handful of properties the ink UI animates:
    /// CanvasGroup.alpha, Image.fillAmount, RectTransform scale / anchored
    /// position / Z rotation, Graphic.color.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not coroutines / an asset-store tween lib:</b> per-panel
    /// coroutines allocate per start and die with their host (a hover-off
    /// during a scene fade leaks half-finished state); the single driver
    /// ticks a struct pool in one Update with zero steady-state allocation
    /// (INV-6), on unscaled time so paused menus still feel alive.
    /// </para>
    /// <para>
    /// <b>Retarget, never restart.</b> Starting a tween on a (target,
    /// channel) that is already animating reuses that slot and re-aims from
    /// the target's <i>current</i> value — hover-off mid-animation turns
    /// around smoothly instead of snapping, the CSS-transition semantics the
    /// motion handoff specifies.
    /// </para>
    /// <para>
    /// <b>From-state contract:</b> a tween's start value is whatever the
    /// target holds at schedule time. For a delayed entrance, set the hidden
    /// state first (alpha 0), then schedule — the slot writes nothing until
    /// its delay elapses.
    /// </para>
    /// <para>
    /// Bootstraps itself like <see cref="AudioRouter"/>: lazy scene-root
    /// singleton, statics reset via SubsystemRegistration, adopts a
    /// surviving instance after a mid-play domain reload.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: retargetable tween driver.
    [DisallowMultipleComponent]
    public sealed class UiTween : MonoBehaviour
    {
        private const int Capacity = 160;

        private enum Channel : byte { Alpha, Fill, Scale, Move, Tint, RotZ }

        private struct Slot
        {
            public bool Active;
            public int Version;
            public Channel Channel;
            public Object Target;          // CanvasGroup / Image / RectTransform / Graphic
            public float T;                // elapsed seconds; negative while delayed
            public float Duration;
            public UiMotion.Ease Ease;
            public float From, To;         // scalar channels
            public Vector2 VFrom, VTo;     // Move
            public Color CFrom, CTo;       // Tint
        }

        private static UiTween s_instance;
        private static GameObject s_root;

        private Slot[] _slots;
        private int _versionCounter;

        // -----------------------------------------------------------------
        // Bootstrap (mirrors AudioRouter's pattern)
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_root = null;
        }

        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_instance = FindFirstObjectByType<UiTween>();
            if (s_instance != null)
            {
                s_root = s_instance.gameObject;
                return;
            }
            s_root = new GameObject("[UiTween]");
            DontDestroyOnLoad(s_root);
            s_instance = s_root.AddComponent<UiTween>();
        }

        private void Awake()
        {
            _slots ??= new Slot[Capacity];
        }

        private void OnEnable()
        {
            // Mid-play domain reload re-runs OnEnable but not Awake on the
            // surviving instance; the non-serialized pool is null here then.
            _slots ??= new Slot[Capacity];
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>Fade a CanvasGroup's alpha.</summary>
        public static UiTweenHandle Alpha(CanvasGroup target, float to, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
            => Start(target, Channel.Alpha, to, duration, ease, delay);

        /// <summary>Animate an Image's fillAmount — draw-ins and wet-ink washes.</summary>
        public static UiTweenHandle Fill(Image target, float to, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
            => Start(target, Channel.Fill, to, duration, ease, delay);

        /// <summary>Animate a RectTransform's uniform local scale (press stamps, seal lands).</summary>
        public static UiTweenHandle Scale(RectTransform target, float to, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
            => Start(target, Channel.Scale, to, duration, ease, delay);

        /// <summary>Animate a RectTransform's anchoredPosition.</summary>
        public static UiTweenHandle Move(RectTransform target, Vector2 to, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
        {
            EnsureBootstrap();
            if (s_instance == null || target == null) return default;
            ref Slot s = ref s_instance.Claim(target, Channel.Move);
            s.VFrom = target.anchoredPosition;
            s.VTo = to;
            return s_instance.Commit(ref s, duration, ease, delay);
        }

        /// <summary>Animate a Graphic's colour (washes deepening, ink lightening).</summary>
        public static UiTweenHandle Tint(Graphic target, Color to, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
        {
            EnsureBootstrap();
            if (s_instance == null || target == null) return default;
            ref Slot s = ref s_instance.Claim(target, Channel.Tint);
            s.CFrom = target.color;
            s.CTo = to;
            return s_instance.Commit(ref s, duration, ease, delay);
        }

        /// <summary>Animate a RectTransform's local Z rotation in degrees.</summary>
        public static UiTweenHandle RotZ(RectTransform target, float toDegrees, float duration,
            UiMotion.Ease ease = UiMotion.Ease.Settle, float delay = 0f)
            => Start(target, Channel.RotZ, toDegrees, duration, ease, delay);

        /// <summary>Stop a tween where it is (no jump). Stale handles no-op.</summary>
        public static void Cancel(UiTweenHandle h)
        {
            if (s_instance == null || !Valid(h)) return;
            s_instance._slots[h.Index].Active = false;
            s_instance._slots[h.Index].Target = null;
        }

        /// <summary>Jump a tween to its end value and release it. Stale handles no-op.</summary>
        public static void Complete(UiTweenHandle h)
        {
            if (s_instance == null || !Valid(h)) return;
            ref Slot s = ref s_instance._slots[h.Index];
            s_instance.WriteValue(ref s, 1f);
            s.Active = false;
            s.Target = null;
        }

        /// <summary>
        /// Jump every live tween to its end value — the entrance-skip:
        /// any input during a staged entrance completes it instantly.
        /// </summary>
        public static void CompleteAll()
        {
            if (s_instance == null || s_instance._slots == null) return;
            for (int i = 0; i < s_instance._slots.Length; i++)
            {
                ref Slot s = ref s_instance._slots[i];
                if (!s.Active) continue;
                s_instance.WriteValue(ref s, 1f);
                s.Active = false;
                s.Target = null;
            }
        }

        /// <summary>Live tween count — test / diagnostics hook.</summary>
        public static int ActiveCount
        {
            get
            {
                if (s_instance == null || s_instance._slots == null) return 0;
                int n = 0;
                for (int i = 0; i < s_instance._slots.Length; i++)
                    if (s_instance._slots[i].Active) n++;
                return n;
            }
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private static bool Valid(UiTweenHandle h)
        {
            if (s_instance == null || s_instance._slots == null) return false;
            if (h.Index < 0 || h.Index >= s_instance._slots.Length) return false;
            ref Slot s = ref s_instance._slots[h.Index];
            return s.Active && s.Version == h.Version;
        }

        private static UiTweenHandle Start(Object target, Channel channel, float to,
            float duration, UiMotion.Ease ease, float delay)
        {
            EnsureBootstrap();
            if (s_instance == null || target == null) return default;
            ref Slot s = ref s_instance.Claim(target, channel);
            s.From = s_instance.ReadScalar(target, channel);
            // Rotation takes the short way round: a face resting at -0.7°
            // reads back as 359.3°, and a naive lerp to 0 would spin it a
            // full turn.
            s.To = channel == Channel.RotZ ? s.From + Mathf.DeltaAngle(s.From, to) : to;
            return s_instance.Commit(ref s, duration, ease, delay);
        }

        /// <summary>
        /// Find the slot animating (target, channel) — retarget semantics —
        /// or a free one, or evict the slot nearest completion when full.
        /// </summary>
        private ref Slot Claim(Object target, Channel channel)
        {
            int free = -1;
            int soonest = 0;
            float soonestRemaining = float.MaxValue;
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (s.Active)
                {
                    if (ReferenceEquals(s.Target, target) && s.Channel == channel)
                        return ref _slots[i]; // retarget in place
                    float remaining = s.Duration - s.T;
                    if (remaining < soonestRemaining) { soonestRemaining = remaining; soonest = i; }
                }
                else if (free < 0) free = i;
            }
            if (free >= 0)
            {
                _slots[free].Target = target;
                _slots[free].Channel = channel;
                return ref _slots[free];
            }
            // Pool exhausted (160 simultaneous UI tweens = a bug upstream,
            // but never drop the new request): finish the one closest to
            // done — visually the least disruptive victim.
            ref Slot victim = ref _slots[soonest];
            WriteValue(ref victim, 1f);
            victim.Target = target;
            victim.Channel = channel;
            return ref victim;
        }

        private UiTweenHandle Commit(ref Slot s, float duration, UiMotion.Ease ease, float delay)
        {
            s.Active = true;
            s.Version = ++_versionCounter;
            s.T = -Mathf.Max(0f, delay);
            s.Duration = Mathf.Max(0.0001f, duration);
            s.Ease = ease;
            int index = IndexOf(ref s);
            return new UiTweenHandle { Index = index, Version = s.Version };
        }

        private int IndexOf(ref Slot s)
        {
            // Slots live in one array; pointer arithmetic isn't available on
            // managed refs, so scan by version (unique per Commit).
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Version == s.Version && _slots[i].Active) return i;
            return -1;
        }

        private float ReadScalar(Object target, Channel channel)
        {
            switch (channel)
            {
                case Channel.Alpha: return ((CanvasGroup)target).alpha;
                case Channel.Fill:  return ((Image)target).fillAmount;
                case Channel.Scale: return ((RectTransform)target).localScale.x;
                case Channel.RotZ:  return ((RectTransform)target).localEulerAngles.z;
                default: return 0f;
            }
        }

        private void WriteValue(ref Slot s, float tNorm)
        {
            if (s.Target == null) return; // destroyed mid-flight (Unity fake-null)
            float e = UiMotion.Evaluate(s.Ease, tNorm);
            switch (s.Channel)
            {
                case Channel.Alpha:
                    ((CanvasGroup)s.Target).alpha = Mathf.LerpUnclamped(s.From, s.To, e);
                    break;
                case Channel.Fill:
                    ((Image)s.Target).fillAmount = Mathf.LerpUnclamped(s.From, s.To, e);
                    break;
                case Channel.Scale:
                {
                    float v = Mathf.LerpUnclamped(s.From, s.To, e);
                    ((RectTransform)s.Target).localScale = new Vector3(v, v, 1f);
                    break;
                }
                case Channel.Move:
                    ((RectTransform)s.Target).anchoredPosition = Vector2.LerpUnclamped(s.VFrom, s.VTo, e);
                    break;
                case Channel.Tint:
                    ((Graphic)s.Target).color = Color.LerpUnclamped(s.CFrom, s.CTo, e);
                    break;
                case Channel.RotZ:
                {
                    Vector3 eu = ((RectTransform)s.Target).localEulerAngles;
                    eu.z = Mathf.LerpUnclamped(s.From, s.To, e);
                    ((RectTransform)s.Target).localEulerAngles = eu;
                    break;
                }
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (!s.Active) continue;
                if (s.Target == null) // destroyed target — release
                {
                    s.Active = false;
                    continue;
                }
                s.T += dt;
                if (s.T <= 0f) continue; // still delayed; hold the from-state
                float tNorm = s.T / s.Duration;
                if (tNorm >= 1f)
                {
                    WriteValue(ref s, 1f);
                    s.Active = false;
                    s.Target = null;
                }
                else
                {
                    WriteValue(ref s, tNorm);
                }
            }
        }
    }
}
