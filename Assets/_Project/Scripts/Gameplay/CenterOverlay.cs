using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Garage build-mode overlay: three translucent world-space spheres marking
    /// the chassis's Center of Mass (white), Center of Lift (blue, from aero
    /// blocks) and Center of Thrust (orange, from thrusters / hover blades /
    /// rotors). The <i>spatial mismatch</i> between them is the information — a
    /// plane whose CoL sits well behind its CoM will pitch up, one whose CoT is
    /// off the roll axis will corkscrew. Now that wing mass + inertia are real
    /// (session 106), this turns "why does my plane fight me?" from a mystery
    /// into a glance. Toggle with G; visible only while building.
    /// </summary>
    /// <remarks>
    /// Pure read-only visualisation — no gameplay mutation, no Tweakable. CoM is
    /// the live <c>rb.worldCenterOfMass</c> (kept current by
    /// <see cref="Robot.RecalculateAggregates"/> on every edit); CoL/CoT are a
    /// single weighted pass over the block grid. The spheres are collider-less
    /// children of a standalone gizmo root, so they never touch the chassis
    /// compound collider (invariant #4) and cost nothing in the arena.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CenterOverlay : MonoBehaviour
    {
        // Single-sourced from ThrusterBlock: the old mirrored 310f had
        // drifted (thruster default is 900 N since session 120), so the
        // CoT marker under-weighted every untuned thruster ~3× (169).
        private const float ThrusterDefaultThrust = Robogame.Movement.ThrusterBlock.DefaultMaxThrust;
        private const Key ToggleKey = Key.G;

        [SerializeField] private GarageController _garage;
        [SerializeField] private BuildModeController _buildMode;

        private bool _enabledByUser = true;
        private bool _inBuildMode;

        private Transform _root;
        private Transform _comSphere, _colSphere, _cotSphere;
        private bool _hasCol, _hasCot;
        private GUIStyle _legendStyle;

        private static readonly Color s_comColor = new Color(0.95f, 0.97f, 1f, 0.55f);
        private static readonly Color s_colColor = new Color(0.25f, 0.55f, 1f, 0.55f);
        private static readonly Color s_cotColor = new Color(0.98f, 0.55f, 0.12f, 0.55f);

        public GarageController Garage { get => _garage; set => _garage = value; }

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode != null) { _buildMode.Entered -= HandleEntered; _buildMode.Exited -= HandleExited; }
                _buildMode = value;
                if (_buildMode != null) { _buildMode.Entered += HandleEntered; _buildMode.Exited += HandleExited; }
            }
        }

        private void Awake()
        {
            BuildSpheres();
            SetVisible(false);
            if (_buildMode != null) { _buildMode.Entered += HandleEntered; _buildMode.Exited += HandleExited; }
        }

        private void OnDestroy()
        {
            if (_buildMode != null) { _buildMode.Entered -= HandleEntered; _buildMode.Exited -= HandleExited; }
            if (_root != null) Destroy(_root.gameObject);
        }

        private void HandleEntered() { _inBuildMode = true; }
        private void HandleExited()  { _inBuildMode = false; SetVisible(false); }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame) _enabledByUser = !_enabledByUser;

            bool show = _inBuildMode && _enabledByUser;
            SetVisible(show);
            if (!show) return;

            Robot robot = ResolveRobot();
            if (robot == null || robot.Rigidbody == null || robot.Grid == null) { SetVisible(false); return; }

            // CoM — the live, authoritative centre of mass.
            Vector3 com = robot.Rigidbody.worldCenterOfMass;
            Place(_comSphere, com, 0.55f);

            // CoL / CoT — weighted block centroids.
            Vector3 colSum = Vector3.zero, cotSum = Vector3.zero;
            float colW = 0f, cotW = 0f;
            // Non-boxing enumeration (per-frame loop — INV-6).
            foreach (var kvp in robot.Grid.BlocksNonAlloc)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                string id = b.Definition.Id;
                Vector3 p = b.transform.position;

                if (id == BlockIds.Aero || id == BlockIds.AeroFin || id == BlockIds.Wing)
                {
                    // Wing was missing here (bat-wing planes showed no CoL
                    // sphere at all); AeroShape.ResolveDims handles the
                    // per-id defaults for all three foil families (169).
                    AeroShape.ResolveDims(id, b.Dims, out float span, out _, out float chord);
                    float area = span * chord;
                    // Weight at the foil's geometric centre — where lift
                    // actually acts since session 168 — not the mount cell,
                    // so a long wing pulls the CoL marker outboard for real.
                    Vector3 liftPoint = b.transform.TransformPoint(
                        Robogame.Movement.AeroSurfaceBlock.ComputeWingShift(b.GridPosition, span, rotorMode: false));
                    colSum += liftPoint * area; colW += area;
                }
                else if (id == BlockIds.Thruster)
                {
                    float t = b.ConfigValue > 0f ? b.ConfigValue : ThrusterDefaultThrust;
                    cotSum += p * t; cotW += t;
                }
                else if (id == BlockIds.HoverBlade)
                {
                    int n = BlockOccupancy.ResolveHoverBladeSize(b.Dims);
                    float t = n * n * 100f; // ~lift weight, N² scaling
                    cotSum += p * t; cotW += t;
                }
                else if (id == BlockIds.Rotor)
                {
                    const float t = 150f; // nominal rotor thrust weight
                    cotSum += p * t; cotW += t;
                }
            }

            _hasCol = colW > 0f;
            _hasCot = cotW > 0f;
            if (_hasCol) Place(_colSphere, colSum / colW, 0.45f + Mathf.Min(colW, 12f) * 0.02f);
            if (_hasCot) Place(_cotSphere, cotSum / cotW, 0.45f + Mathf.Min(cotW, 2000f) * 0.0002f);
            _colSphere.gameObject.SetActive(_hasCol);
            _cotSphere.gameObject.SetActive(_hasCot);
        }

        private Robot ResolveRobot()
        {
            GameObject go = _garage != null ? _garage.Chassis : null;
            return go != null ? go.GetComponent<Robot>() : null;
        }

        private static void Place(Transform t, Vector3 worldPos, float radius)
        {
            t.position = worldPos;
            t.localScale = Vector3.one * radius * 2f;
        }

        private void SetVisible(bool v)
        {
            if (_root != null && _root.gameObject.activeSelf != v) _root.gameObject.SetActive(v);
        }

        private void BuildSpheres()
        {
            _root = new GameObject("[CenterOverlay]").transform;
            _comSphere = MakeSphere("CoM", s_comColor);
            _colSphere = MakeSphere("CoL", s_colColor);
            _cotSphere = MakeSphere("CoT", s_cotColor);
        }

        private Transform MakeSphere(string name, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c); // visualisation only — never a physics object
            go.transform.SetParent(_root, worldPositionStays: false);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = RuntimeMaterials.UnlitTransparent(color);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            return go.transform;
        }

        private void OnGUI()
        {
            if (!_inBuildMode) return;
            _legendStyle ??= HudStyles.Bold(13, HudStyles.TextPrimary, TextAnchor.MiddleLeft);

            float x = 18f, y = Screen.height * 0.5f;
            string state = _enabledByUser ? "ON" : "OFF";
            DrawLegendRow(x, y,        s_comColor, "Center of Mass");
            DrawLegendRow(x, y + 20f,  s_colColor, "Center of Lift");
            DrawLegendRow(x, y + 40f,  s_cotColor, "Center of Thrust");
            GUI.Label(new Rect(x, y + 62f, 240f, 20f), $"[G] Centers overlay: {state}", _legendStyle);
        }

        private void DrawLegendRow(float x, float y, Color swatch, string label)
        {
            Color prev = GUI.color;
            GUI.color = new Color(swatch.r, swatch.g, swatch.b, 1f);
            GUI.DrawTexture(new Rect(x, y + 3f, 12f, 12f), HudStyles.Pixel);
            GUI.color = prev;
            GUI.Label(new Rect(x + 18f, y, 220f, 18f), label, _legendStyle);
        }
    }
}
