using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.UI
{
    /// <summary>
    /// Runtime IMGUI overlay with 1-click test actions: rebuild robots, damage,
    /// destroy CPU. Intentionally minimal — toggle with F1, no scene/asset setup.
    /// </summary>
    /// <remarks>
    /// Lives in the UI module so it doesn't pollute gameplay code. Wired up by
    /// the scene scaffolder onto a dedicated DevHud GameObject.
    /// </remarks>
    public sealed class DevHud : MonoBehaviour
    {
        [Tooltip("Key that toggles the dev HUD on/off.")]
        [SerializeField] private Key _toggleKey = Key.F1;

        [Tooltip("Per-ring damage applied by the 'Damage Random Block' button. Index 0 = direct hit.")]
        [SerializeField] private float[] _splashRings = { 200f, 100f, 40f };

        // Always start hidden — dev panel is opt-in via the toggle key.
        // Field is intentionally NOT serialized: an earlier version had a
        // [SerializeField] private bool _visibleAtStart = true; that got
        // baked into scaffolded scenes and persistently re-opened the HUD
        // on every Play. Keeping this purely runtime side-steps that drift.
        private bool _visible = false;
        private Vector2 _scroll;
        // Per-section foldout state. Keyed by the section header label
        // ("Plane", "Water · Waves", etc.) — first frame defaults are
        // applied lazily in DrawSection so adding a new Tweakables group
        // never requires touching DevHud.
        private readonly System.Collections.Generic.Dictionary<string, bool> _sectionExpanded
            = new System.Collections.Generic.Dictionary<string, bool>();

        // Sections we want open the very first time the HUD appears.
        // Everything else collapses by default to keep the panel tidy.
        private static readonly System.Collections.Generic.HashSet<string> s_openByDefault
            = new System.Collections.Generic.HashSet<string> { "Water · Waves" };

        private void Awake()
        {
            // Build the blocker up-front. EventSystem dispatches UGUI
            // clicks during Update, BEFORE OnGUI runs — so a blocker
            // created lazily inside OnGUI doesn't exist yet on the
            // frame the click is processed and the click leaks through
            // to the underlying UGUI button. Authoring it once in Awake
            // and just toggling active state from OnGUI fixes that.
            BuildRaycastBlocker();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // Guard against stale serialized values from the old KeyCode field
            // (KeyCode.F1 == 282, which is out of range for the Key enum).
            if (!System.Enum.IsDefined(typeof(Key), _toggleKey) || _toggleKey == Key.None) return;
            try
            {
                if (kb[_toggleKey].wasPressedThisFrame) _visible = !_visible;
            }
            catch (System.ArgumentOutOfRangeException)
            {
                _toggleKey = Key.F1;
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                EnsureRaycastBlocker(active: false, screenRect: default);
                return;
            }

            const float pad = 8f;
            const float w = 280f;
            // The scroll view inside the area handles overflow; the area
            // height just needs to leave room for the screen edges.
            float h = Screen.height - pad * 2f;
            Rect panelRect = new Rect(pad, pad, w, h);
            GUILayout.BeginArea(panelRect, GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>Robogame Dev</b>", RichLabel());
            GUILayout.Label($"FPS: {Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f))}");

            if (GUILayout.Button("Rebuild Player Robot")) RebuildByName("Robot");
            if (GUILayout.Button("Rebuild Combat Dummy")) RebuildByName("CombatDummy");
            if (GUILayout.Button("Damage Random Block")) DamageRandomBlock();
            if (GUILayout.Button("Destroy CPU")) DestroyCpu();

            GUILayout.Space(6f);
            DrawTweakSections();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Toggle: {_toggleKey}");
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Drop a UGUI raycast-blocker over the same screen rect.
            // IMGUI does NOT go through the EventSystem, so without
            // this an IMGUI button click would pass through to any
            // UGUI button (e.g. "Launch", "Build Mode") drawn under
            // it and trigger a second action. The blocker is a fully
            // transparent UGUI Image with raycastTarget = true on a
            // top-most overlay canvas; EventSystem hits it first and
            // the underlying UGUI button stays unclicked. PlayerInput-
            // Handler.FireHeld and the cameras already check
            // EventSystem.IsPointerOverGameObject(), so suppressing
            // fire / cursor-capture is automatic too.
            EnsureRaycastBlocker(active: true, screenRect: panelRect);
        }

        // -----------------------------------------------------------------
        // Tweakable sliders — auto-grouped from Tweakables.All. Adding a
        // new Register(...) call in Tweakables.cs is enough to make a new
        // slider appear here; the only special-case is splitting the
        // "Water" group into Buoyancy vs Waves so each foldout is digestible.
        // -----------------------------------------------------------------

        private void DrawTweakSections()
        {
            // Group specs into ordered buckets. Dictionary preserves
            // insertion order which keeps related sliders together by
            // mirroring the registration order in Tweakables.cs.
            var sections = new System.Collections.Generic.Dictionary<
                string, System.Collections.Generic.List<Tweakables.Spec>>();
            foreach (var spec in Tweakables.All)
            {
                string section = SectionFor(spec);
                if (!sections.TryGetValue(section, out var list))
                {
                    list = new System.Collections.Generic.List<Tweakables.Spec>();
                    sections[section] = list;
                }
                list.Add(spec);
            }

            foreach (var kv in sections)
            {
                DrawSection(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Maps a tweakable to its HUD section header. Splits the Water
        /// group so wave-shape and buoyancy/drag knobs each get their
        /// own foldout — they're mentally distinct subsystems even if
        /// they share a Tweakables group string.
        /// </summary>
        private static string SectionFor(Tweakables.Spec spec)
        {
            if (spec.Group == "Water")
            {
                return spec.Key.Contains(".Wave") ? "Water · Waves" : "Water · Buoyancy";
            }
            return spec.Group;
        }

        private void DrawSection(string title, System.Collections.Generic.List<Tweakables.Spec> specs)
        {
            if (!_sectionExpanded.TryGetValue(title, out bool open))
            {
                open = s_openByDefault.Contains(title);
                _sectionExpanded[title] = open;
            }

            string arrow = open ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} {title}", SectionHeader()))
            {
                _sectionExpanded[title] = !open;
                open = !open;
            }
            if (!open) return;

            foreach (var spec in specs) DrawTweakSlider(spec);

            // Per-section reset just walks the same spec list — keeps the
            // helper generic and avoids per-section reset code.
            if (GUILayout.Button($"Reset {title}"))
            {
                foreach (var spec in specs) Tweakables.Reset(spec.Key);
            }
            GUILayout.Space(4f);
        }

        private static void DrawTweakSlider(Tweakables.Spec spec)
        {
            float current = Tweakables.Get(spec.Key);
            GUILayout.Label($"{spec.Label}: {current:0.00}");
            float next = GUILayout.HorizontalSlider(current, spec.Min, spec.Max);
            // Tweakables.Set persists to disk + raises Changed; only push
            // when the value actually moved so we don't write every frame
            // the slider is hovered.
            if (!Mathf.Approximately(next, current))
            {
                Tweakables.Set(spec.Key, next);
            }
        }

        private static GUIStyle RichLabel()
        {
            var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
            return s;
        }

        private static GUIStyle SectionHeader()
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
            };
            return s;
        }

        private static void RebuildByName(string n)
        {
            Robot.RebuildByName(n);
        }

        // -----------------------------------------------------------------
        // Test actions (operate on whatever robot we can find — dummy preferred)
        // -----------------------------------------------------------------

        private void DamageRandomBlock()
        {
            Robot target = PickTargetRobot();
            if (target == null || target.Grid == null || target.Grid.Count == 0) return;

            var live = new System.Collections.Generic.List<Vector3Int>();
            foreach (var kvp in target.Grid.Blocks)
            {
                if (kvp.Value != null && kvp.Value.IsAlive) live.Add(kvp.Key);
            }
            if (live.Count == 0) return;

            Vector3Int pos = live[Random.Range(0, live.Count)];
            target.Grid.ApplySplashDamage(pos, _splashRings);
        }

        private void DestroyCpu()
        {
            Robot target = PickTargetRobot();
            if (target != null && target.CpuBlock != null)
            {
                target.CpuBlock.TakeDamage(float.MaxValue);
            }
        }

        private static Robot PickTargetRobot()
        {
            // Prefer the dummy if it's around — that's almost always what you want
            // to poke during dev. Fall back to any robot.
            GameObject dummy = GameObject.Find("CombatDummy");
            if (dummy != null)
            {
                Robot r = dummy.GetComponent<Robot>();
                if (r != null) return r;
            }
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<Robot>();
#else
            return Object.FindObjectOfType<Robot>();
#endif
        }

        // -----------------------------------------------------------------
        // UGUI raycast blocker (click-through suppression)
        // -----------------------------------------------------------------

        private GameObject _blockerGO;
        private RectTransform _blockerRT;

        /// <summary>
        /// Author the transparent UGUI <see cref="Image"/> overlay once,
        /// up-front. EventSystem dispatches UGUI clicks during Update,
        /// BEFORE OnGUI runs -- so a blocker created lazily inside OnGUI
        /// doesn't exist yet on the frame the click is processed and the
        /// click leaks through to the underlying button. Building it in
        /// Awake and just toggling active state from OnGUI fixes that.
        /// raycastTarget = true so the EventSystem treats anything beneath
        /// it as occluded; alpha = 0 so it's invisible. Lives on its own
        /// ScreenSpace-Overlay canvas at a very high sortingOrder so it
        /// wins precedence over every other gameplay HUD canvas
        /// (BuildHotbar=95, SceneTransitionHud=100, SettingsHud=500).
        /// </summary>
        private void BuildRaycastBlocker()
        {
            if (_blockerGO != null) return;

            _blockerGO = new GameObject("DevHudRaycastBlocker");
            _blockerGO.transform.SetParent(transform, worldPositionStays: false);

            Canvas canvas = _blockerGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            _blockerGO.AddComponent<GraphicRaycaster>();

            Image img = _blockerGO.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // fully transparent
            img.raycastTarget = true;

            _blockerRT = img.rectTransform;
            _blockerRT.anchorMin = Vector2.zero;
            _blockerRT.anchorMax = Vector2.zero;
            _blockerRT.pivot = new Vector2(0f, 1f); // top-left, like IMGUI

            _blockerGO.SetActive(false);
        }

        private void EnsureRaycastBlocker(bool active, Rect screenRect)
        {
            if (_blockerGO == null) BuildRaycastBlocker();

            if (!active)
            {
                if (_blockerGO.activeSelf) _blockerGO.SetActive(false);
                return;
            }

            if (!_blockerGO.activeSelf) _blockerGO.SetActive(true);

            // GUI rect is top-left origin (y grows down); UGUI is bottom-left
            // origin (y grows up). Pivot is top-left, anchored at (0,0)
            // so the rect's bottom-left in UGUI space is
            // (rect.x, Screen.height - rect.y).
            _blockerRT.anchoredPosition = new Vector2(screenRect.x, Screen.height - screenRect.y);
            _blockerRT.sizeDelta = new Vector2(screenRect.width, screenRect.height);
        }
    }
}
