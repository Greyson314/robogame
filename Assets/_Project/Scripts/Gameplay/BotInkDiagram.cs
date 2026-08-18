using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Draws the player's current blueprint as an ink figure on the home
    /// sheet — an isometric union-outline of the bot's cells inside a
    /// slowly turning dashed construction ring. Wordless by design: the
    /// only text it ever shows is the hover-focus answers. "The sketch
    /// became the machine, and you can still see the pencil marks."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How the drawing is made:</b> every occupied cell contributes its
    /// three camera-facing faces (+Y, +X, +Z); every face contributes its
    /// four lattice edges into a counting set; edges seen exactly once are
    /// the silhouette + step lines, edges seen twice are interior seams and
    /// are dropped. That union-outline is precisely how a draftsman would
    /// ink the part — no cell grid soup.
    /// </para>
    /// <para>
    /// Built once on menu load (allocation at build time only). The only
    /// per-frame work is one transform rotation on the ring's nested
    /// canvas; <see cref="UiMotion.Reduced"/> freezes it. Hover focus
    /// (<see cref="SetFocus"/>) retints cached line lists through
    /// <see cref="UiTween"/> — event-driven, nothing polls.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: home diagram, player bot.
    [DisallowMultipleComponent]
    public sealed class BotInkDiagram : MonoBehaviour
    {
        public enum Focus { None, Pilot, Works, Rest }

        /// <summary>Ordered for the entrance stagger: figure lines, annotations, spin ring.</summary>
        public CanvasGroup[] EntranceGroups { get; private set; }

        private RectTransform _ring;
        // Focus lists remember each line's idle colour: depth fade gives
        // every edge its own alpha, so "untint" must restore per-line.
        private readonly List<(Graphic g, Color idle)> _pilotLines = new();
        private readonly List<(Graphic g, Color idle)> _worksLines = new();
        private CanvasGroup _fxPilot, _fxWorks, _fxRest;
        private Image _leadPilot, _leadWorks;
        private Color _lineFocus;
        private Focus _focus = Focus.None;

        private const float LineThickness = 3.1f;
        // Depth cue in place of hidden-line removal: near edges ink darker,
        // far edges fade — the union outline stops reading as wire soup.
        private const float LineAlphaNear = 0.62f;
        private const float LineAlphaFar = 0.28f;
        private const int MaxFocusTints = 56; // stay well inside the tween pool

        // Projection basis: +X right-down, +Z left-down, +Y up. Cells whose
        // x+z is larger sit lower on screen (nearer the viewer).
        private static Vector2 Project(Vector3Int c)
            => new(c.x - c.z, c.y * 1.15f - (c.x + c.z) * 0.5f);

        /// <summary>
        /// Build the diagram for <see cref="GameStateController.CurrentBlueprint"/>
        /// under <paramref name="parent"/>, occupying the right two-thirds
        /// of the reference canvas. Never throws on missing state — a menu
        /// without a bootstrap just gets an empty-shelf note.
        /// </summary>
        public static BotInkDiagram Build(Transform parent)
        {
            GameObject host = UguiKit.NewChild("BotDiagram", parent);
            var rt = (RectTransform)host.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(1060f, 1000f);
            rt.anchoredPosition = new Vector2(-30f, 6f);

            var diagram = host.AddComponent<BotInkDiagram>();
            diagram.BuildContent();
            return diagram;
        }

        private void BuildContent()
        {
            var gsc = GameStateController.Instance;
            ChassisBlueprint bp = gsc != null ? gsc.CurrentBlueprint : null;
            BlockDefinitionLibrary lib = gsc != null ? gsc.Library : null;

            _lineFocus = new Color(UguiPalette.Accent.r, UguiPalette.Accent.g, UguiPalette.Accent.b, 0.92f);

            // Entrance groups (menu staggers their alphas).
            CanvasGroup lines = NewGroup("Lines");
            CanvasGroup annots = NewGroup("Annotations");
            CanvasGroup ring = NewGroup("Ring");
            EntranceGroups = new[] { lines, annots, ring };

            if (bp == null || bp.Entries == null || bp.Entries.Length == 0)
            {
                // No bootstrap (direct scene play) or an empty blueprint.
                UguiKit.AddText(annots.transform, "the shelf is empty — visit the workshop",
                    InkKit.Annotation, 19, FontStyle.Italic, HudStyles.TextMuted, TextAnchor.MiddleCenter,
                    anchorMin: Vector2.zero, anchorMax: Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                    raycastTarget: false);
                return;
            }

            // ---------------- cells + category map ----------------
            var cells = new Dictionary<Vector3Int, BlockCategory>(bp.Entries.Length);
            foreach (ChassisBlueprint.Entry e in bp.Entries)
            {
                BlockDefinition def = lib != null ? lib.Get(e.BlockId) : null;
                cells[e.Position] = def != null ? def.Category : BlockCategory.Structure;
            }

            // ---------------- face → edge accumulation ----------------
            // Key: lattice corner pair (ordered); value: hit count + owner.
            var edges = new Dictionary<(Vector3Int, Vector3Int), (int count, BlockCategory cat)>(cells.Count * 12);
            void AddEdge(Vector3Int a, Vector3Int b, BlockCategory cat)
            {
                // Normalize order so shared edges collide.
                if (a.x > b.x || (a.x == b.x && (a.y > b.y || (a.y == b.y && a.z > b.z))))
                    (a, b) = (b, a);
                var key = (a, b);
                edges[key] = edges.TryGetValue(key, out var v) ? (v.count + 1, v.cat) : (1, cat);
            }
            void AddFace(Vector3Int c0, Vector3Int c1, Vector3Int c2, Vector3Int c3, BlockCategory cat)
            {
                AddEdge(c0, c1, cat); AddEdge(c1, c2, cat);
                AddEdge(c2, c3, cat); AddEdge(c3, c0, cat);
            }

            foreach (KeyValuePair<Vector3Int, BlockCategory> kv in cells)
            {
                Vector3Int p = kv.Key;
                BlockCategory cat = kv.Value;
                if (!cells.ContainsKey(p + Vector3Int.up))
                    AddFace(new(p.x, p.y + 1, p.z), new(p.x + 1, p.y + 1, p.z),
                            new(p.x + 1, p.y + 1, p.z + 1), new(p.x, p.y + 1, p.z + 1), cat);
                if (!cells.ContainsKey(p + Vector3Int.right))
                    AddFace(new(p.x + 1, p.y, p.z), new(p.x + 1, p.y + 1, p.z),
                            new(p.x + 1, p.y + 1, p.z + 1), new(p.x + 1, p.y, p.z + 1), cat);
                if (!cells.ContainsKey(p + Vector3Int.forward))
                    AddFace(new(p.x, p.y, p.z + 1), new(p.x + 1, p.y, p.z + 1),
                            new(p.x + 1, p.y + 1, p.z + 1), new(p.x, p.y + 1, p.z + 1), cat);
            }

            // ---------------- fit to the host rect ----------------
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            float depthMin = float.MaxValue, depthMax = float.MinValue;
            foreach (var kv in edges)
            {
                if (kv.Value.count != 1) continue;
                Vector2 a = Project(kv.Key.Item1);
                Vector2 b = Project(kv.Key.Item2);
                min = Vector2.Min(min, Vector2.Min(a, b));
                max = Vector2.Max(max, Vector2.Max(a, b));
                // Depth along the view axis: larger x+z sits nearer the viewer.
                float depth = (kv.Key.Item1.x + kv.Key.Item1.z + kv.Key.Item2.x + kv.Key.Item2.z) * 0.5f;
                depthMin = Mathf.Min(depthMin, depth);
                depthMax = Mathf.Max(depthMax, depth);
            }
            Vector2 span = max - min;
            if (span.x < 0.01f || span.y < 0.01f) return;
            const float fitW = 780f, fitH = 660f;
            float scale = Mathf.Min(fitW / span.x, fitH / span.y, 40f);
            Vector2 center = (min + max) * 0.5f;
            Vector2 P(Vector3Int c) => (Project(c) - center) * scale;

            // ---------------- draw the union outline ----------------
            Color tp = HudStyles.TextPrimary;
            float depthSpan = Mathf.Max(0.001f, depthMax - depthMin);
            foreach (var kv in edges)
            {
                if (kv.Value.count != 1) continue;
                float depth = (kv.Key.Item1.x + kv.Key.Item1.z + kv.Key.Item2.x + kv.Key.Item2.z) * 0.5f;
                float near01 = (depth - depthMin) / depthSpan;
                var idle = new Color(tp.r, tp.g, tp.b, Mathf.Lerp(LineAlphaFar, LineAlphaNear, near01));
                Image line = DrawLine(lines.transform, P(kv.Key.Item1), P(kv.Key.Item2), LineThickness, idle);
                switch (kv.Value.cat)
                {
                    case BlockCategory.Cpu:
                        if (_pilotLines.Count < MaxFocusTints) _pilotLines.Add((line, idle));
                        break;
                    case BlockCategory.Movement:
                        if (_worksLines.Count < MaxFocusTints) _worksLines.Add((line, idle));
                        break;
                }
            }

            // ---------------- CPU beacon motif ----------------
            Vector3Int? cpuCell = null;
            foreach (var kv in cells)
                if (kv.Value == BlockCategory.Cpu) { cpuCell = kv.Key; break; }
            Vector2 pilotAnchor = Vector2.zero;
            if (cpuCell.HasValue)
            {
                Vector3Int p = cpuCell.Value;
                // Top-face center → short mast + tip splat (the beacon motif).
                Vector2 topCenter = (P(new Vector3Int(p.x, p.y + 1, p.z)) + P(new Vector3Int(p.x + 1, p.y + 1, p.z + 1))) * 0.5f;
                var mastIdle = new Color(tp.r, tp.g, tp.b, LineAlphaNear);
                Image mast = DrawLine(lines.transform, topCenter, topCenter + new Vector2(0f, 26f), 2.2f, mastIdle);
                var tip = UguiKit.NewChild("BeaconTip", lines.transform);
                var tipRt = (RectTransform)tip.transform;
                tipRt.anchorMin = tipRt.anchorMax = new Vector2(0.5f, 0.5f);
                tipRt.sizeDelta = new Vector2(8f, 8f);
                tipRt.anchoredPosition = topCenter + new Vector2(0f, 30f);
                var tipImg = tip.AddComponent<Image>();
                tipImg.sprite = InkKit.Splat;
                tipImg.color = UguiPalette.Accent;
                tipImg.raycastTarget = false;
                if (_pilotLines.Count < MaxFocusTints)
                {
                    _pilotLines.Add((mast, mastIdle));
                    _pilotLines.Add((tipImg, tipImg.color));
                }
                pilotAnchor = topCenter + new Vector2(0f, 30f);
            }

            // ---------------- figure extents ----------------
            // No captions, stats, or dimension callouts — the sheet stays
            // wordless and the drawing speaks for itself (Grey, Aug 18).
            // These extents only anchor the focus leaders and size the ring.
            float dimL = (min.x - center.x) * scale;
            float dimR = (max.x - center.x) * scale;

            // ---------------- hover-focus annotations ----------------
            Vector2 worksAnchor = new((dimL + dimR) * 0.35f, (min.y - center.y) * scale + 8f);
            _fxPilot = BuildFx("FxPilot", pilotAnchor == Vector2.zero ? new Vector2(0f, 60f) : pilotAnchor,
                new Vector2(150f, 70f), "the pilot is ready", out _leadPilot, alignLeft: true);
            _fxWorks = BuildFx("FxWorks", worksAnchor, new Vector2(-170f, -60f), "tension the works", out _leadWorks, alignLeft: false);
            _fxRest = NewGroup("FxRest");
            _fxRest.alpha = 0f;
            UguiKit.AddText(_fxRest.transform, "shutter the workshop for the night", InkKit.Annotation, 17, FontStyle.Italic,
                HudStyles.TextMuted, TextAnchor.MiddleLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f),
                offsetMin: new Vector2(70f, 118f), offsetMax: new Vector2(480f, 150f),
                raycastTarget: false);

            // ---------------- spin ring ----------------
            BuildRing(ring.transform, Mathf.Max(dimR, -dimL) + 66f);
        }

        // -----------------------------------------------------------------
        // Focus (menu hover → the drawing answers)
        // -----------------------------------------------------------------

        public void SetFocus(Focus f)
        {
            if (_focus == f) return;
            _focus = f;

            TintList(_pilotLines, f == Focus.Pilot);
            TintList(_worksLines, f == Focus.Works);
            ShowFx(_fxPilot, _leadPilot, f == Focus.Pilot);
            ShowFx(_fxWorks, _leadWorks, f == Focus.Works);
            if (_fxRest != null)
                UiTween.Alpha(_fxRest, f == Focus.Rest ? 1f : 0f, UiMotion.Stroke);
        }

        private void TintList(List<(Graphic g, Color idle)> list, bool on)
        {
            for (int i = 0; i < list.Count; i++)
                UiTween.Tint(list[i].g, on ? _lineFocus : list[i].idle, UiMotion.Tick);
        }

        private void ShowFx(CanvasGroup fx, Image lead, bool on)
        {
            if (fx == null) return;
            UiTween.Alpha(fx, on ? 1f : 0f, UiMotion.Stroke);
            if (lead != null)
                UiTween.Fill(lead, on ? 1f : 0f, on ? 0.24f : UiMotion.Stroke, UiMotion.Ease.Draw);
        }

        // -----------------------------------------------------------------
        // Pieces
        // -----------------------------------------------------------------

        private CanvasGroup NewGroup(string name)
        {
            GameObject go = UguiKit.NewChild(name, transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go.AddComponent<CanvasGroup>();
        }

        /// <summary>Thin brush-sprite line between two local points; +2px hand-drawn overshoot per end.</summary>
        private static Image DrawLine(Transform parent, Vector2 a, Vector2 b, float thickness, Color color)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            GameObject go = UguiKit.NewChild("L", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len + 4f, thickness);
            rt.anchoredPosition = a - d.normalized * 2f;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.BarFill;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void AddArrow(Transform parent, Vector2 pos, float rotZ, Color color)
        {
            GameObject go = UguiKit.NewChild("Arrow", parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(13f, 13f);
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.ArrowTip;
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>A hidden leader line + note pair; the line draws in on focus (fillAmount).</summary>
        private CanvasGroup BuildFx(string name, Vector2 from, Vector2 offset, string note, out Image lead, bool alignLeft)
        {
            CanvasGroup fx = NewGroup(name);
            fx.alpha = 0f;
            Vector2 to = from + offset;

            Vector2 d = to - from;
            GameObject go = UguiKit.NewChild("Lead", fx.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(d.magnitude, 2f);
            rt.anchoredPosition = from;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            lead = go.AddComponent<Image>();
            lead.sprite = InkKit.BarFill;
            Color lc = HudStyles.TextMuted; lc.a = 0.7f;
            lead.color = lc;
            lead.raycastTarget = false;
            lead.type = Image.Type.Filled;
            lead.fillMethod = Image.FillMethod.Horizontal;
            lead.fillOrigin = (int)Image.OriginHorizontal.Left;
            lead.fillAmount = 0f;

            float noteX = alignLeft ? to.x + 10f : to.x - 330f;
            UguiKit.AddText(fx.transform, note, InkKit.Annotation, 17, FontStyle.Italic,
                HudStyles.TextMuted, alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(noteX, to.y - 16f), offsetMax: new Vector2(noteX + 320f, to.y + 16f),
                raycastTarget: false, horizontalOverflow: true);
            return fx;
        }

        private void BuildRing(Transform parent, float radius)
        {
            // Nested canvas: the ring rotates every frame, and isolating it
            // keeps the (static, several-hundred-element) menu canvas from
            // rebatching. Rotation is transform-only — no vertex rebuild.
            GameObject ringGo = UguiKit.NewChild("SpinRing", parent);
            ringGo.AddComponent<Canvas>();
            _ring = (RectTransform)ringGo.transform;
            _ring.anchorMin = _ring.anchorMax = new Vector2(0.5f, 0.5f);
            _ring.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            _ring.anchoredPosition = Vector2.zero;

            Color dashCol = UguiPalette.FrameLine;
            dashCol.a = 0.32f;
            const int dashes = 40;
            for (int i = 0; i < dashes; i++)
            {
                float ang = i / (float)dashes * Mathf.PI * 2f;
                Vector2 pos = new(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius);
                GameObject dash = UguiKit.NewChild("Dash", _ring);
                var rt = (RectTransform)dash.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(13f, 2.2f);
                rt.anchoredPosition = pos;
                rt.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg + 90f);
                var img = dash.AddComponent<Image>();
                img.color = dashCol;
                img.raycastTarget = false;
            }

            // Static tangent arrowhead at the ring's crown — "turns with vigor".
            AddArrow(parent, new Vector2(14f, radius), 0f, dashCol);
        }

        private void Update()
        {
            if (_ring == null || UiMotion.Reduced) return;
            // One transform write per frame; the drafting ring creeps
            // clockwise. This is the screen's entire idle-motion budget.
            Vector3 eu = _ring.localEulerAngles;
            eu.z -= 4f * Time.unscaledDeltaTime;
            _ring.localEulerAngles = eu;
        }
    }
}
