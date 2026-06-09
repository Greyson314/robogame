using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Shared helper that instantiates a stylized turret model under a weapon
    /// block and resolves its pitch pivot (<c>Turret</c>) + muzzle
    /// (<c>ShootPoint</c>), so the existing yaw-block / pitch-yoke / muzzle aim
    /// path drives the real model instead of a procedural primitive barrel.
    /// </summary>
    /// <remarks>
    /// Session 120 — replaces the red-cube / primitive weapon visuals with the
    /// Fatty low-poly turret pack. The Fatty prefabs share a convention: a
    /// <c>Turret</c> child is the yaw/pitch head and a <c>ShootPoint</c> child
    /// marks the muzzle. We drive <c>Turret</c> as the pitch yoke (the block
    /// itself still yaws), which reads as a turret tracking the reticle.
    /// </remarks>
    internal static class WeaponModelRig
    {
        /// <summary>
        /// Build (or re-resolve, idempotently) the turret model under
        /// <paramref name="host"/>. Returns false when no model is supplied so
        /// the caller can fall back to its procedural rig.
        /// </summary>
        public static bool TryBuild(MonoBehaviour host, GameObject model, float scale,
                                    Vector3 offset, out Transform yoke, out Transform muzzle)
        {
            yoke = null;
            muzzle = null;
            if (host == null || model == null) return false;

            Transform t = host.transform;
            BlockVisuals.HideHostMesh(host.gameObject);

            // Awake can re-run (asset reimport / scene reload) — reuse the
            // existing instance rather than stacking duplicates.
            Transform existing = t.Find("TurretModel");
            GameObject inst = existing != null
                ? existing.gameObject
                : Object.Instantiate(model, t);
            inst.name = "TurretModel";
            inst.transform.localPosition = offset;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            yoke = FindByName(inst.transform, "Turret") ?? inst.transform;
            muzzle = FindByName(inst.transform, "ShootPoint") ?? yoke;
            return true;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
