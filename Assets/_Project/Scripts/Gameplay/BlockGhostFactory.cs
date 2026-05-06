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
