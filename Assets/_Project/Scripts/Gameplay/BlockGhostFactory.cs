using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Builds a translucent "ghost" GameObject that mirrors the visual
    /// shape of a placed block, for the build-mode placement preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shapes here intentionally duplicate the small primitive rigs
    /// authored by the gameplay block components (<see cref="Robogame.Movement.AeroSurfaceBlock"/>,
    /// <see cref="Robogame.Movement.ThrusterBlock"/>, etc.) instead of
    /// instantiating those components, because:
    /// <list type="bullet">
    ///   <item><description>The real components want a Rigidbody parent and self-register with binders.</description></item>
    ///   <item><description>Stripping that off after construction is fragile (visual rigs run in <c>Awake</c>).</description></item>
    ///   <item><description>Ghost shapes are deliberately *approximations* — they should read instantly, not mimic exactly.</description></item>
    /// </list>
    /// If a new block id appears, we fall back to a plain cube so the
    /// editor still works while the ghost shape is being authored.
    /// </para>
    /// </remarks>
    public static class BlockGhostFactory
    {
        /// <summary>
        /// Construct a ghost preview matching <paramref name="def"/>. The
        /// returned root has no colliders, no shadows, and a single shared
        /// material (the caller swaps between valid/invalid via
        /// <see cref="ApplyMaterial"/>).
        /// <paramref name="up"/> is the mount face normal in chassis-local
        /// space; the caller applies the cell rotation (<see cref="BlockGrid.OrientationFromUp"/>)
        /// to the returned ghost. Foil shapes use <paramref name="up"/> to
        /// pick horizontal-vs-vertical mesh-axis treatment so the ghost
        /// matches what <see cref="Robogame.Movement.RobotAeroBinder"/>
        /// will configure on the placed block.
        /// </summary>
        public static GameObject Build(BlockDefinition def, Material initialMat,
            Vector3 dims = default, Vector3Int targetCell = default, Vector3Int up = default,
            float pitchDeg = 0f)
        {
            var root = new GameObject("BlockGhost");
            // Parent will set position/rotation; root scale stays 1 so the
            // primitive child scales below behave like authored values.

            switch (def != null ? def.Id : null)
            {
                case BlockIds.Wheel:
                case BlockIds.WheelSteer:
                    BuildWheel(root.transform);
                    break;
                case BlockIds.Thruster:
                    BuildThruster(root.transform);
                    break;
                case BlockIds.Aero:
                case BlockIds.AeroFin:
                    BuildWing(root.transform, dims: dims, cellPos: targetCell, pitchDeg: pitchDeg);
                    break;
                case BlockIds.Rudder:
                    BuildRudder(root.transform);
                    break;
                case BlockIds.Weapon:
                    BuildWeapon(root.transform);
                    break;
                case BlockIds.Rope:
                    BuildRope(root.transform, dims);
                    break;
                case BlockIds.Rotor:
                    BuildRotor(root.transform);
                    break;
                case BlockIds.Hook:
                    BuildHook(root.transform);
                    break;
                case BlockIds.Mace:
                    BuildMace(root.transform);
                    break;
                default:
                    BuildCube(root.transform);
                    break;
            }

            ApplyToAll(root, initialMat);
            return root;
        }

        /// <summary>Swap every renderer on the ghost root to <paramref name="mat"/>.</summary>
        public static void ApplyMaterial(GameObject ghost, Material mat)
        {
            if (ghost == null) return;
            ApplyToAll(ghost, mat);
        }

        // -----------------------------------------------------------------
        // Per-shape builders. All children: collider stripped, no shadows.
        // -----------------------------------------------------------------

        private static void BuildCube(Transform parent)
        {
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        private static void BuildWheel(Transform parent)
        {
            // Mirror WheelBlock.EnsureRig's stem + hub + tyre layout so the
            // ghost matches what gets spawned. Block-local +Y = mount-up =
            // stem axis (the cell's "outward" direction toward the wheel
            // hub); host face is at block-local -Y.
            //
            // Ghost shows the placement state (no suspension drop), so the
            // stem is the static half-cell from host face to cell centre.
            // The placed block dynamically extends the stem to follow the
            // wheel as suspension compresses; the ghost intentionally
            // approximates the rest position.
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, -0.25f, 0f),
                Quaternion.identity, new Vector3(0.18f, 0.25f, 0.18f));

            // Tyre: full-radius disc, thin along the axle (block-local Y).
            const float radius = 0.5f; // matches WheelBlock._radius default
            float d = radius * 2f;
            Spawn(parent, PrimitiveType.Cylinder, Vector3.zero,
                Quaternion.identity, new Vector3(d, 0.09f, d));

            // Hub cap: small cylinder pushed slightly outboard so the ghost
            // reads as "tyre + hub" rather than a plain dark disc.
            float hubD = radius * 0.55f;
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f),
                Quaternion.identity, new Vector3(hubD, 0.07f, hubD));
        }

        private static void BuildThruster(Transform parent)
        {
            // Mirrors ThrusterBlock.EnsureRig: nozzle cube + small flame cylinder behind.
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.6f, 0.6f, 0.9f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.7f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.5f, 0.4f, 0.5f));
        }

        private static void BuildWing(Transform parent, Vector3 dims, Vector3Int cellPos, float pitchDeg)
        {
            // Single source of truth — the same helpers the placed
            // AeroSurfaceBlock uses for its mesh. Build-mode placement
            // is never rotor-adopted at hover time, so rotorMode=false.
            // Pitch rotates the visual around foil-local +Z (chord axis),
            // matching AeroSurfaceBlock.ApplyOrientationToVisual so the
            // ghost mirrors what the placed block will look like.
            AeroSurfaceBlock.ResolveDims(dims, out float span, out float thickness, out float chord);
            Vector3 size = AeroSurfaceBlock.ComputeFoilMeshScale(span, thickness, chord, rotorMode: false);
            Vector3 shift = AeroSurfaceBlock.ComputeWingShift(cellPos, span, rotorMode: false);
            Quaternion pitchRot = Quaternion.AngleAxis(pitchDeg, Vector3.forward);
            Spawn(parent, PrimitiveType.Cube, shift, pitchRot, size);
        }

        private static void BuildWeapon(Transform parent)
        {
            // Body + forward barrel — matches HitscanWeaponBlock's typical visual.
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.7f, 0.7f, 0.7f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.55f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.18f, 0.4f, 0.18f));
        }

        private static void BuildRudder(Transform parent)
        {
            // Mirrors RudderBlock.EnsureRig: thin vertical blade,
            // long axis Z (chord points fore/aft), filling most of the
            // cell so it reads as "deep blade in the water".
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.08f, 0.9f, 0.7f));
        }

        private static void BuildRope(Transform parent, Vector3 dims)
        {
            // Hologram = the entire rope's chain visualised at the
            // segment count the variant panel currently has dialled
            // in. No "rope base" cube — the rope itself is just the
            // chain, extending from the chassis-side face of the
            // placement cell (rope-local -Y) along the mount-up
            // direction (rope-local +Y) by the full length.
            int segments = (dims.x > 0f) ? Mathf.Clamp(Mathf.RoundToInt(dims.x), 2, 32) : 8;
            // Read live segment-length / radius Tweakables so the
            // hologram length tracks what the placed rope will
            // actually be. RopeBlock.LiveSegmentLength/Radius use the
            // same Tweakables with the same Mathf.Max guards.
            float segLen = Mathf.Max(0.05f, Tweakables.Get(Tweakables.RopeSegmentLength));
            float segRad = Mathf.Max(0.01f, Tweakables.Get(Tweakables.RopeSegmentRadius));
            float fullLen = segLen * segments;

            Vector3 startLocal = new Vector3(0f, -0.5f, 0f);
            Vector3 endLocal   = new Vector3(0f, -0.5f + fullLen, 0f);
            Vector3 mid = (startLocal + endLocal) * 0.5f;
            Vector3 axis = endLocal - startLocal;
            float length = axis.magnitude;
            if (length < 1e-4f) return;

            Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis / length);
            // Cylinder primitive native height = 2; scale Y to half the
            // visible length so the cylinder spans `length` along its
            // local +Y direction.
            Spawn(parent, PrimitiveType.Cylinder, mid, rot,
                new Vector3(segRad * 2f, length * 0.5f, segRad * 2f));
        }

        private static void BuildRotor(Transform parent)
        {
            // Mirror RotorBlock.BuildBlockVisual exactly so the ghost
            // previews the rotor's full 2-cell visual footprint: mast
            // spans the rotor cell up into the mechanism cell, disc +
            // crossed bars sit at MechanismHeight (y = +1.0 in cell
            // units, which is the centre of the cell ABOVE). Placing
            // the rotor under an existing block is legal but the
            // existing block's cell will end up clipping with this
            // disc; the ghost makes that geometry visible BEFORE the
            // click so the player isn't surprised.
            // Mast (rotor cell + into the cell above).
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f),
                Quaternion.identity, new Vector3(0.25f, 0.75f, 0.25f));
            // Disc at mechanism cell centre (y = +1.0).
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 1.0f, 0f),
                Quaternion.identity, new Vector3(0.70f, 0.06f, 0.70f));
            // Crossed bars on top of the disc — two cubes intersecting
            // at right angles so the spin direction reads at a glance.
            Spawn(parent, PrimitiveType.Cube, new Vector3(0f, 1.0f, 0f),
                Quaternion.identity, new Vector3(0.95f, 0.08f, 0.10f));
            Spawn(parent, PrimitiveType.Cube, new Vector3(0f, 1.0f, 0f),
                Quaternion.identity, new Vector3(0.10f, 0.08f, 0.95f));
        }

        private static void BuildHook(Transform parent)
        {
            // J-shape mirroring HookBlock.BuildTipVisual but scaled into
            // a single cell. The actual hook geometry extends ~1.7 cells
            // along the rope-local +Z axis; for a build-mode preview we
            // shrink it so the player sees a recognisable hook shape
            // inside the placement cell. Frame: +Y points forward
            // (chassis-forward when sitting at a grid cell), -Y stays
            // inside the cell.
            // Vertical shaft.
            Spawn(parent, PrimitiveType.Cube, new Vector3(0f, 0f, 0.18f),
                Quaternion.identity, new Vector3(0.18f, 0.18f, 0.7f));
            // Horizontal barb arm (curl back forward).
            Spawn(parent, PrimitiveType.Cube, new Vector3(0f, 0.30f, 0.45f),
                Quaternion.identity, new Vector3(0.18f, 0.7f, 0.18f));
            // Barb tip (pointing back up).
            Spawn(parent, PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.20f),
                Quaternion.identity, new Vector3(0.18f, 0.18f, 0.4f));
        }

        private static void BuildMace(Transform parent)
        {
            // Spiked sphere mirroring MaceBlock.BuildTipVisual at unit-
            // cell scale: central ball + 6 short axial spikes. Reads as
            // "spiky ball" against the plain-cube fallback that other
            // weapons use.
            Spawn(parent, PrimitiveType.Sphere, Vector3.zero, Quaternion.identity,
                new Vector3(0.7f, 0.7f, 0.7f));
            Vector3[] axes =
            {
                new Vector3( 1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3( 0, 1, 0), new Vector3( 0,-1, 0),
                new Vector3( 0, 0, 1), new Vector3( 0, 0,-1),
            };
            for (int i = 0; i < axes.Length; i++)
            {
                Vector3 upHint = (Mathf.Abs(axes[i].y) > 0.5f) ? Vector3.right : Vector3.up;
                Spawn(parent, PrimitiveType.Cube, axes[i] * 0.40f,
                    Quaternion.LookRotation(axes[i], upHint),
                    new Vector3(0.14f, 0.14f, 0.30f));
            }
        }

        // -----------------------------------------------------------------

        private static GameObject Spawn(Transform parent, PrimitiveType type,
            Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = "GhostPart";
            // Ghost must not block raycasts (we'd self-target) or cast shadows.
            Object.Destroy(go.GetComponent<Collider>());
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            return go;
        }

        private static void ApplyToAll(GameObject root, Material mat)
        {
            if (root == null || mat == null) return;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = mat;
        }
    }
}
