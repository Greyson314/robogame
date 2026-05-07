using Robogame.Block;
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
        /// </summary>
        public static GameObject Build(BlockDefinition def, Material initialMat,
            Vector3 dims = default, Vector3Int targetCell = default)
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
                    BuildWing(root.transform, vertical: false, dims: dims, cellPos: targetCell);
                    break;
                case BlockIds.AeroFin:
                    BuildWing(root.transform, vertical: true,  dims: dims, cellPos: targetCell);
                    break;
                case BlockIds.Rudder:
                    BuildRudder(root.transform);
                    break;
                case BlockIds.Weapon:
                    BuildWeapon(root.transform);
                    break;
                case BlockIds.Rope:
                    BuildRope(root.transform);
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
            // Cylinder default long axis is Y; rotate 90° around Z so it
            // lies on its side like an actual wheel.
            Spawn(parent, PrimitiveType.Cylinder, Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f), new Vector3(0.6f, 0.45f, 0.6f));
        }

        private static void BuildThruster(Transform parent)
        {
            // Mirrors ThrusterBlock.EnsureRig: nozzle cube + small flame cylinder behind.
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.6f, 0.6f, 0.9f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.7f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.5f, 0.4f, 0.5f));
        }

        private static void BuildWing(Transform parent, bool vertical, Vector3 dims, Vector3Int cellPos)
        {
            // Mirror AeroSurfaceBlock.ApplyOrientationToVisual: same scale +
            // outward shift so the ghost is a faithful preview of what the
            // placed foil will look like. _rotorMode is always false here
            // (build-mode placement is never rotor-adopted at hover time).
            AeroSurfaceBlock.ResolveDims(dims, out float span, out float thickness, out float chord);
            Vector3 size = vertical
                ? new Vector3(thickness, span, chord)
                : new Vector3(span,      thickness, chord);
            Vector3 shift = AeroSurfaceBlock.ComputeWingShift(cellPos, span, vertical, rotorMode: false);
            Spawn(parent, PrimitiveType.Cube, shift, Quaternion.identity, size);
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

        private static void BuildRope(Transform parent)
        {
            // Compact host cube + a short downward stub indicating the
            // rope hangs from the cell's bottom face. The placed rope's
            // static visual is much longer (segLen × N), but a ghost
            // preview at unit-cell scale just needs to read as
            // "this is a rope cell". The stub is intentionally short so
            // the ghost stays inside the placement cell visually.
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.7f, 0.4f, 0.7f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, -0.35f, 0f),
                Quaternion.identity, new Vector3(0.18f, 0.15f, 0.18f));
        }

        private static void BuildRotor(Transform parent)
        {
            // Mirrors RotorBlock's cosmetic rig: short mast on the cell's
            // top face + a thin disc with one crossbar so the rotor reads
            // as "spinning thing" not "plain cube".
            Spawn(parent, PrimitiveType.Cube, Vector3.zero, Quaternion.identity,
                new Vector3(0.7f, 0.7f, 0.7f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f),
                Quaternion.identity, new Vector3(0.12f, 0.15f, 0.12f));
            Spawn(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.65f, 0f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.85f, 0.05f, 0.08f));
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
