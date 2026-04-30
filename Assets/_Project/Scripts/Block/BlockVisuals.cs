using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Helpers for the visual rig pattern shared by movement / weapon block
    /// behaviours: hide the host primitive's renderer, find-or-create
    /// child transforms, etc.
    /// </summary>
    /// <remarks>
    /// Block behaviours often sit on a unit-cube primitive used as a
    /// collider + spawn host. Their real visuals live as children
    /// (turret yoke, wheel hub, wing mesh, thruster nozzle). This class
    /// encapsulates the rig-construction boilerplate.
    /// </remarks>
    public static class BlockVisuals
    {
        /// <summary>
        /// Hide the host GameObject's renderer/mesh so child rig visuals
        /// show through. The collider is intentionally preserved — it
        /// still serves as a hit volume for damage raycasts.
        /// </summary>
        public static void HideHostMesh(GameObject host)
        {
            if (host == null) return;
            MeshRenderer mr = host.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            MeshFilter mf = host.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = null;
        }

        /// <summary>
        /// Return the child of <paramref name="parent"/> named
        /// <paramref name="name"/>, creating an empty one if absent. The
        /// new transform is parented with default local TRS.
        /// </summary>
        public static Transform GetOrCreateChild(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go.transform;
        }

        /// <summary>
        /// Return the child of <paramref name="parent"/> named
        /// <paramref name="name"/> if present, else create a Unity primitive
        /// of <paramref name="primitive"/>, strip its collider, parent it,
        /// and return its transform. Caller positions / scales as needed.
        /// </summary>
        public static Transform GetOrCreatePrimitiveChild(
            Transform parent, string name, PrimitiveType primitive,
            bool stripCollider = true)
        {
            if (parent == null) return null;
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            if (stripCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }
    }
}
