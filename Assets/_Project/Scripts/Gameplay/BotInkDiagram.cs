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
    /// <b>How the drawing is made:</b> every occupied cell contributes all
    /// six faces into an edge-counting set, tiered by visibility: edges of
    /// exactly one camera-facing exposed face ink the silhouette + steps at
    /// full strength; seams between two visible faces draw the block grid
    /// semi-transparent; buried and away-facing edges render as a faint
    /// X-ray. Every block is in the drawing, the silhouette still leads.
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
        // Tiered wireframe (Grey, Aug 18): visible-surface block seams and
        // buried/away-facing edges render see-through, so every block is in
        // the drawing without drowning the silhouette.
        private const float SeamAlpha = 0.15f;
        private const float HiddenAlpha = 0.07f;
        private const int MaxFocusTints = 56; // stay well inside the tween pool

        // Projection basis: +X right-down, +Z left-down, +Y up. Cells whose
        // x+z is larger sit lower on screen (nearer the viewer).
        private static Vector2 Project(Vector3Int c)
            => new(c.x - c.z, c.y * 1.15f - (c.x + c.z) * 0.5f);

        private static Vector2 ProjectF(Vector3 c)
            => new(c.x - c.z, c.y * 1.15f - (c.x + c.z) * 0.5f);

        // -----------------------------------------------------------------
        // Part glyphs — "solid where you shoot it, skeletal where it moves"
        // (the session-131 composition rule). Skeletal ids contribute no
        // cube to the union outline; their shape IS the drawing. Weapons
        // keep their mass and gain a barrel. Geometry is authored in the
        // entry's local frame (localY = EffectiveUp, yaw folded in) and
        // mirrors the 3D rigs in BlockGhostFactory / the block classes.
        // -----------------------------------------------------------------

        private struct GlyphSeg
        {
            public Vector3 A, B;     // blueprint-space endpoints
            public float Thickness;
            public bool Works;       // tinted by the "tension the works" focus
        }

        private static readonly HashSet<string> s_skeletalIds = new()
        {
            "block.movement.wheel", "block.movement.wheel.steer",
            "block.movement.thruster", "block.movement.aero",
            "block.movement.aero.fin", "block.movement.wing",
            "block.movement.rudder", "block.movement.hoverblade",
            "block.movement.gyro", "block.movement.pogo",
            "block.movement.spring",        // Module category, moving part
            "block.cosmetic.rotor", "block.cosmetic.rope",
            "block.weapon.tip.hook", "block.weapon.tip.mace", "block.weapon.tip.magnet",
        };

        /// <summary>Append the ink glyph for one entry; returns false when the id has no glyph.</summary>
        private static bool BuildGlyph(ChassisBlueprint.Entry e, List<GlyphSeg> outSegs)
        {
            Quaternion r = BlockGrid.OrientationFromUp(e.EffectiveUp, e.EffectiveYaw);
            Vector3 x = r * Vector3.right, y = r * Vector3.up, z = r * Vector3.forward;
            Vector3 c = (Vector3)e.Position + new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 d = e.Dims;

            void Seg(Vector3 a, Vector3 b, float t, bool works = true)
                => outSegs.Add(new GlyphSeg { A = a, B = b, Thickness = t, Works = works });
            void Circle(Vector3 center, Vector3 u, Vector3 v, float radius, int segs, float t, bool works = true)
            {
                Vector3 prev = center + u * radius;
                for (int i = 1; i <= segs; i++)
                {
                    float a = i / (float)segs * Mathf.PI * 2f;
                    Vector3 p = center + u * (Mathf.Cos(a) * radius) + v * (Mathf.Sin(a) * radius);
                    Seg(prev, p, t, works);
                    prev = p;
                }
            }
            void Rect(Vector3 center, Vector3 u, Vector3 v, float halfU, float halfV, float t, bool works = true)
            {
                Vector3 a = center - u * halfU - v * halfV, b = center + u * halfU - v * halfV;
                Vector3 g = center + u * halfU + v * halfV, h = center - u * halfU + v * halfV;
                Seg(a, b, t, works); Seg(b, g, t, works); Seg(g, h, t, works); Seg(h, a, t, works);
            }

            switch (e.BlockId)
            {
                case "block.movement.wheel":
                case "block.movement.wheel.steer":
                    // Tyre + hub, disc ⊥ the mount axis; stem back to the host face.
                    Circle(c, x, z, 0.5f, 14, 2.6f);
                    Circle(c, x, z, 0.15f, 8, 2f);
                    Seg(c, c - y * 0.5f, 2f);
                    return true;

                case "block.movement.thruster":
                    // Body profile + exhaust flare out the tail (thrust = +localZ).
                    Rect(c, z, y, 0.45f, 0.3f, 2.4f);
                    Seg(c - z * 0.45f + y * 0.18f, c - z * 0.8f + y * 0.32f, 2f);
                    Seg(c - z * 0.45f - y * 0.18f, c - z * 0.8f - y * 0.32f, 2f);
                    return true;

                case "block.movement.aero":
                case "block.movement.aero.fin":
                {
                    float span = d.x > 0f ? d.x : 1.0f;
                    float chord = d.z > 0f ? d.z : 0.9f;
                    Rect(c + y * (span * 0.5f - 0.5f), y, z, span * 0.5f, chord * 0.5f, 2.4f);
                    return true;
                }

                case "block.movement.wing":
                {
                    float span = d.x > 0f ? d.x : 1.828f;
                    float chord = d.z > 0f ? d.z : 1.004f;
                    Vector3 mid = c + y * (span * 0.5f - 0.5f);
                    Rect(mid, y, z, span * 0.5f, chord * 0.5f, 2.4f);
                    // One sweep stroke so it reads bat-wing, not plank.
                    Seg(mid - y * (span * 0.5f) + z * (chord * 0.5f),
                        mid + y * (span * 0.5f) + z * (chord * 0.15f), 2f);
                    return true;
                }

                case "block.movement.rudder":
                    Rect(c + y * 0.2f, y, z, 0.45f, 0.35f, 2.4f);
                    return true;

                case "block.movement.hoverblade":
                {
                    int n = Mathf.Clamp(d.x > 0f ? Mathf.RoundToInt(d.x) : 2, 2, 4);
                    Vector3 center = c + (x + z) * ((n - 1) * 0.5f);
                    Circle(center, x, z, n * 0.5f, 18, 2.6f);
                    Circle(center, x, z, 0.15f, 6, 2f);
                    return true;
                }

                case "block.movement.gyro":
                    Circle(c, x, z, 0.42f, 12, 2.4f);
                    return true;

                case "block.movement.pogo":
                    Seg(c, c + y * 0.5f, 2.2f);
                    Circle(c + y * 0.55f, x, z, 0.2f, 8, 2.2f);
                    return true;

                case "block.movement.spring":
                    // Coil zigzag + foot pad.
                    Seg(c - y * 0.15f - x * 0.2f, c - y * 0.02f + x * 0.2f, 2f);
                    Seg(c - y * 0.02f + x * 0.2f, c + y * 0.11f - x * 0.2f, 2f);
                    Seg(c + y * 0.11f - x * 0.2f, c + y * 0.24f + x * 0.2f, 2f);
                    Circle(c + y * 0.5f, x, z, 0.3f, 8, 2.2f);
                    return true;

                case "block.cosmetic.rotor":
                    // Mast up to the mechanism cell; disc + crossed bars there.
                    Seg(c - y * 0.5f, c + y * 1.0f, 3f);
                    Circle(c + y * 1.0f, x, z, 0.35f, 12, 2.4f);
                    Seg(c + y * 1.0f - x * 0.475f, c + y * 1.0f + x * 0.475f, 2.2f);
                    Seg(c + y * 1.0f - z * 0.475f, c + y * 1.0f + z * 0.475f, 2.2f);
                    return true;

                case "block.cosmetic.rope":
                {
                    int len = Mathf.Clamp(d.x > 0f ? Mathf.RoundToInt(d.x) : 4, 1, 16);
                    Vector3 start = c - y * 0.5f;
                    for (int k = 0; k < len; k++)
                        Seg(start + y * (k + 0.15f), start + y * (k + 0.6f), 2f);
                    return true;
                }

                // ---- weapons: mass stays, the business end is inked on ----
                case "block.weapon.hitscan":
                    Seg(c + y * 0.5f, c + y * 0.5f + z * 1.0f, 3.2f, works: false);
                    return true;

                case "block.weapon.cannon":
                {
                    Vector3 muzzle = c + y * 0.4f + z * 1.25f;
                    Seg(c + y * 0.4f, muzzle, 4.4f, works: false);
                    Seg(muzzle - x * 0.2f, muzzle + x * 0.2f, 3f, works: false);
                    return true;
                }

                case "block.weapon.mortar":
                {
                    Vector3 dir = (z * Mathf.Cos(35f * Mathf.Deg2Rad) + y * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized;
                    Seg(c + y * 0.45f, c + y * 0.45f + dir * 0.85f, 5f, works: false);
                    return true;
                }

                case "block.weapon.grapple_magnet":
                {
                    Vector3 muzzle = c + y * 0.5f + z * 1.0f;
                    Seg(c + y * 0.5f, muzzle, 4f, works: false);
                    Seg(muzzle, muzzle + z * 0.25f + x * 0.16f, 2.2f, works: false);
                    Seg(muzzle, muzzle + z * 0.25f - x * 0.16f, 2.2f, works: false);
                    return true;
                }

                case "block.tool.drill":
                {
                    Vector3 apex = c + y * 1.1f;
                    Circle(c + y * 0.5f, x, z, 0.25f, 8, 2.2f, works: false);
                    Seg(c + y * 0.5f + x * 0.25f, apex, 2.4f, works: false);
                    Seg(c + y * 0.5f - x * 0.25f, apex, 2.4f, works: false);
                    return true;
                }

                case "block.weapon.tip.hook":
                    Seg(c, c + z * 0.5f, 2.8f, works: false);
                    Seg(c + z * 0.5f, c + z * 0.62f - y * 0.32f, 2.8f, works: false);
                    Seg(c + z * 0.62f - y * 0.32f, c + z * 0.35f - y * 0.48f, 2.8f, works: false);
                    return true;

                case "block.weapon.tip.mace":
                    Circle(c, x, y, 0.35f, 10, 2.8f, works: false);
                    Seg(c + (x + y) * 0.27f, c + (x + y) * 0.42f, 2.4f, works: false);
                    Seg(c + (x - y) * 0.27f, c + (x - y) * 0.42f, 2.4f, works: false);
                    Seg(c - (x + y) * 0.27f, c - (x + y) * 0.42f, 2.4f, works: false);
                    Seg(c - (x - y) * 0.27f, c - (x - y) * 0.42f, 2.4f, works: false);
                    return true;

                case "block.weapon.tip.magnet":
                    Seg(c - x * 0.25f + z * 0.6f, c - x * 0.25f, 2.8f, works: false);
                    Seg(c - x * 0.25f, c + x * 0.25f, 2.8f, works: false);
                    Seg(c + x * 0.25f, c + x * 0.25f + z * 0.6f, 2.8f, works: false);
                    return true;

                default:
                    return false;
            }
        }

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

            // ---------------- cells + glyphs ----------------
            // Skeletal parts stay out of the occupancy map entirely, so the
            // structure behind a wheel still closes its outline.
            var cells = new Dictionary<Vector3Int, BlockCategory>(bp.Entries.Length);
            var glyphs = new List<GlyphSeg>(bp.Entries.Length * 4);
            foreach (ChassisBlueprint.Entry e in bp.Entries)
            {
                bool skeletal = s_skeletalIds.Contains(e.BlockId);
                if (!skeletal)
                {
                    BlockDefinition def = lib != null ? lib.Get(e.BlockId) : null;
                    cells[e.Position] = def != null ? def.Category : BlockCategory.Structure;
                }
                BuildGlyph(e, glyphs);
            }

            // ---------------- face → edge accumulation ----------------
            // Every cell contributes all six faces, so every block is in
            // the drawing (Grey, Aug 18 — the union outline alone didn't
            // read as the actual bot). Edges tier by how visible they are:
            //   tier 1  edge of exactly one camera-facing exposed face —
            //           the silhouette + steps (full ink, depth-faded)
            //   tier 2  seam between two visible faces — the block grid on
            //           an exposed surface (semi-transparent)
            //   tier 3  everything buried or facing away — the X-ray
            //           innards (faint whisper)
            var edges = new Dictionary<(Vector3Int, Vector3Int), (int vis, BlockCategory cat)>(cells.Count * 12);
            void AddEdge(Vector3Int a, Vector3Int b, BlockCategory cat, bool visibleFace)
            {
                // Normalize order so shared edges collide.
                if (a.x > b.x || (a.x == b.x && (a.y > b.y || (a.y == b.y && a.z > b.z))))
                    (a, b) = (b, a);
                var key = (a, b);
                edges[key] = edges.TryGetValue(key, out var v)
                    ? (v.vis + (visibleFace ? 1 : 0), v.cat)
                    : (visibleFace ? 1 : 0, cat);
            }
            void AddFace(Vector3Int c0, Vector3Int c1, Vector3Int c2, Vector3Int c3, BlockCategory cat, bool visible)
            {
                AddEdge(c0, c1, cat, visible); AddEdge(c1, c2, cat, visible);
                AddEdge(c2, c3, cat, visible); AddEdge(c3, c0, cat, visible);
            }

            foreach (KeyValuePair<Vector3Int, BlockCategory> kv in cells)
            {
                Vector3Int p = kv.Key;
                BlockCategory cat = kv.Value;
                // Camera-facing trio — visible when exposed.
                AddFace(new(p.x, p.y + 1, p.z), new(p.x + 1, p.y + 1, p.z),
                        new(p.x + 1, p.y + 1, p.z + 1), new(p.x, p.y + 1, p.z + 1), cat,
                        visible: !cells.ContainsKey(p + Vector3Int.up));
                AddFace(new(p.x + 1, p.y, p.z), new(p.x + 1, p.y + 1, p.z),
                        new(p.x + 1, p.y + 1, p.z + 1), new(p.x + 1, p.y, p.z + 1), cat,
                        visible: !cells.ContainsKey(p + Vector3Int.right));
                AddFace(new(p.x, p.y, p.z + 1), new(p.x + 1, p.y, p.z + 1),
                        new(p.x + 1, p.y + 1, p.z + 1), new(p.x, p.y + 1, p.z + 1), cat,
                        visible: !cells.ContainsKey(p + Vector3Int.forward));
                // Away-facing trio — never visible from this viewpoint, but
                // their edges give the X-ray its buried geometry.
                AddFace(new(p.x, p.y, p.z), new(p.x + 1, p.y, p.z),
                        new(p.x + 1, p.y, p.z + 1), new(p.x, p.y, p.z + 1), cat, visible: false);
                AddFace(new(p.x, p.y, p.z), new(p.x, p.y + 1, p.z),
                        new(p.x, p.y + 1, p.z + 1), new(p.x, p.y, p.z + 1), cat, visible: false);
                AddFace(new(p.x, p.y, p.z), new(p.x + 1, p.y, p.z),
                        new(p.x + 1, p.y + 1, p.z), new(p.x, p.y + 1, p.z), cat, visible: false);
            }

            // Faint tiers are additive charm on small bots and soup on huge
            // ones — drop them past a budget rather than drawing thousands
            // of whisper lines (menu-only cost, but still).
            bool drawHidden = edges.Count <= 2600;

            // ---------------- fit to the host rect ----------------
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            float depthMin = float.MaxValue, depthMax = float.MinValue;
            foreach (var kv in edges)
            {
                Vector2 a = Project(kv.Key.Item1);
                Vector2 b = Project(kv.Key.Item2);
                min = Vector2.Min(min, Vector2.Min(a, b));
                max = Vector2.Max(max, Vector2.Max(a, b));
                // Depth along the view axis: larger x+z sits nearer the viewer.
                float depth = (kv.Key.Item1.x + kv.Key.Item1.z + kv.Key.Item2.x + kv.Key.Item2.z) * 0.5f;
                depthMin = Mathf.Min(depthMin, depth);
                depthMax = Mathf.Max(depthMax, depth);
            }
            foreach (GlyphSeg g in glyphs)
            {
                min = Vector2.Min(min, Vector2.Min(ProjectF(g.A), ProjectF(g.B)));
                max = Vector2.Max(max, Vector2.Max(ProjectF(g.A), ProjectF(g.B)));
                float depth = (g.A.x + g.A.z + g.B.x + g.B.z) * 0.5f;
                depthMin = Mathf.Min(depthMin, depth);
                depthMax = Mathf.Max(depthMax, depth);
            }
            Vector2 span = max - min;
            if (span.x < 0.01f || span.y < 0.01f) return;
            const float fitW = 780f, fitH = 660f;
            float scale = Mathf.Min(fitW / span.x, fitH / span.y, 40f);
            Vector2 center = (min + max) * 0.5f;
            Vector2 P(Vector3Int c) => (Project(c) - center) * scale;
            Vector2 PF(Vector3 v) => (ProjectF(v) - center) * scale;

            // ---------------- draw the tiered wireframe ----------------
            Color tp = HudStyles.TextPrimary;
            float depthSpan = Mathf.Max(0.001f, depthMax - depthMin);
            foreach (var kv in edges)
            {
                int vis = kv.Value.vis;
                if (vis == 0 && !drawHidden) continue;
                float thickness;
                Color idle;
                if (vis == 1)
                {
                    // Silhouette + steps — full ink, depth-faded.
                    float depth = (kv.Key.Item1.x + kv.Key.Item1.z + kv.Key.Item2.x + kv.Key.Item2.z) * 0.5f;
                    float near01 = (depth - depthMin) / depthSpan;
                    idle = new Color(tp.r, tp.g, tp.b, Mathf.Lerp(LineAlphaFar, LineAlphaNear, near01));
                    thickness = LineThickness;
                }
                else if (vis >= 2)
                {
                    // Seam between two visible faces — the block grid on an
                    // exposed surface. Semi-transparent so blocks read
                    // without turning the surface into graph paper.
                    idle = new Color(tp.r, tp.g, tp.b, SeamAlpha);
                    thickness = 2f;
                }
                else
                {
                    // Buried / facing away — the X-ray whisper.
                    idle = new Color(tp.r, tp.g, tp.b, HiddenAlpha);
                    thickness = 2f;
                }
                Image line = DrawLine(lines.transform, P(kv.Key.Item1), P(kv.Key.Item2), thickness, idle);
                if (vis != 1) continue; // only strong lines join the focus tints
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

            // ---------------- part glyphs ----------------
            // Slightly darker than the structure lines at equal depth so
            // the working parts read as the machine's intent.
            foreach (GlyphSeg g in glyphs)
            {
                float depth = (g.A.x + g.A.z + g.B.x + g.B.z) * 0.5f;
                float near01 = (depth - depthMin) / depthSpan;
                var col = new Color(tp.r, tp.g, tp.b,
                    Mathf.Min(0.75f, Mathf.Lerp(LineAlphaFar, LineAlphaNear, near01) + 0.06f));
                Image line = DrawLine(lines.transform, PF(g.A), PF(g.B), g.Thickness, col);
                if (g.Works && _worksLines.Count < MaxFocusTints) _worksLines.Add((line, col));
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
