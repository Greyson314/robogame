using System.Collections.Generic;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Stateless executors for the module abilities that perform a world
    /// mutation (spring launch, EMP lockout, blink teleport, disc shield).
    /// Each is invoked server-side only (the caller — <c>ModuleSystem</c> —
    /// gates on <see cref="NetworkContext"/>). VFX + audio are fired by the
    /// caller so these stay pure gameplay mutations. Smoke + invisibility have
    /// no world mutation (a cloud VFX + a healthbar-hidden flag, and a renderer
    /// fade respectively) so they live on <c>ModuleSystem</c> / <c>StealthVisual</c>.
    /// </summary>
    /// <remarks>
    /// Allocation discipline (invariant #6): the EMP overlap uses a shared
    /// pre-sized buffer + reused <see cref="HashSet{T}"/>. Abilities fire at
    /// most once per cooldown, so the transient helper components
    /// (<see cref="EmpDisable"/>, <see cref="ShieldBubble"/>) they attach are
    /// not a per-frame allocation.
    /// </remarks>
    public static class ModuleEffects
    {
        private static readonly Collider[] s_overlap = new Collider[64];
        private static readonly HashSet<Robot> s_seen = new(16);

        /// <summary>
        /// Launch the chassis off the spring's mount face. Direction is
        /// <c>-block.up</c> — the chassis-inward normal of the mount, so an
        /// underside spring jumps the bot up and a side spring dashes it
        /// sideways. Derived from the block pose, so it works on flat and
        /// spherical arenas. <paramref name="impulse"/> is the resolved power
        /// (N·s). The grounded gate lives on <c>ModuleBlock.ContextAvailable</c>.
        /// </summary>
        public static void SpringLaunch(Transform block, Rigidbody rb, float impulse)
        {
            if (block == null || rb == null) return;
            Vector3 launchDir = -block.up;
            rb.AddForceAtPosition(launchDir * impulse, block.position, ForceMode.Impulse);
        }

        /// <summary>
        /// Disable every enemy <see cref="ProjectileGun"/> within
        /// <paramref name="radius"/> of <paramref name="origin"/> for
        /// <paramref name="duration"/> seconds. The owner's own weapons are
        /// untouched.
        /// </summary>
        public static void EmpBurst(Vector3 origin, float radius, float duration, Robot owner)
        {
            int n = Physics.OverlapSphereNonAlloc(origin, radius, s_overlap, ~0, QueryTriggerInteraction.Ignore);
            s_seen.Clear();
            for (int i = 0; i < n; i++)
            {
                Collider c = s_overlap[i];
                if (c == null) continue;
                Robot robot = c.GetComponentInParent<Robot>();
                if (robot == null || robot == owner) continue;
                if (!s_seen.Add(robot)) continue; // one pass per robot

                foreach (ProjectileGun gun in robot.GetComponentsInChildren<ProjectileGun>(includeInactive: true))
                {
                    if (gun == null || !gun.enabled) continue;
                    EmpDisable.Apply(gun, duration);
                }
            }
        }

        /// <summary>
        /// Teleport the chassis along <paramref name="dir"/> by up to
        /// <paramref name="range"/> metres, stopping short of any solid
        /// surface so the chassis never blinks inside terrain. Returns the
        /// arrival position (for the destination VFX).
        /// </summary>
        public static Vector3 Blink(Rigidbody rb, Vector3 dir, float range)
        {
            Vector3 from = rb.position;
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float dist = range;
            // Clamp to the first solid surface ahead, leaving a small skin so
            // we don't end up flush inside a wall. Ignore the owner's own
            // colliders by sphere-casting from slightly ahead of the hull.
            if (Physics.SphereCast(from + d * 1.5f, 0.75f, d, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                Robot self = rb.GetComponent<Robot>();
                bool hitSelf = self != null && hit.collider.GetComponentInParent<Robot>() == self;
                if (!hitSelf) dist = Mathf.Max(0f, hit.distance + 1.5f - 1.0f);
            }
            Vector3 to = from + d * dist;
            rb.position = to;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return to;
        }

        /// <summary>
        /// Spawn a transient projectile-blocking bubble around the chassis
        /// for <paramref name="duration"/> seconds. Blocks incoming enemy
        /// fire; the owner shoots straight through (its collider is folded
        /// into the owner's projectile-exclusion filter).
        /// </summary>
        public static void DiscShield(Robot owner, float radius, float duration)
        {
            if (owner == null) return;
            ShieldBubble.Spawn(owner, radius, duration);
        }
    }

    /// <summary>
    /// Re-enables a single <see cref="ProjectileGun"/> after an EMP lockout.
    /// Attached transiently to the gun; ticks its own timer and removes
    /// itself. Only restores the gun if it was the one that disabled it
    /// (so a garage-disabled gun isn't force-enabled mid-lockout).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EmpDisable : MonoBehaviour
    {
        private ProjectileGun _gun;
        private float _expireAt;

        internal static void Apply(ProjectileGun gun, float duration)
        {
            EmpDisable d = gun.GetComponent<EmpDisable>();
            if (d == null) d = gun.gameObject.AddComponent<EmpDisable>();
            d._gun = gun;
            d._expireAt = Time.time + duration;
            gun.enabled = false;
        }

        private void Update()
        {
            if (Time.time < _expireAt) return;
            if (_gun != null) _gun.enabled = true;
            Destroy(this);
        }
    }

    /// <summary>
    /// A transient kinematic sphere that follows the chassis and physically
    /// blocks enemy projectiles for its lifetime. Built procedurally; carries
    /// its own kinematic <see cref="Rigidbody"/> so its collider stays out of
    /// the chassis's compound collider (single-Rigidbody invariant #4).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShieldBubble : MonoBehaviour
    {
        private Robot _owner;
        private float _expireAt;
        private static Material s_material;

        internal static void Spawn(Robot owner, float radius, float duration)
        {
            var go = new GameObject("[ShieldBubble]");
            go.transform.SetParent(owner.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            // Match a real block collider's layer so the bubble sits in the
            // projectile HitMask (blocks already collide with projectiles).
            Collider sample = owner.GetComponentInChildren<Collider>();
            go.layer = sample != null ? sample.gameObject.layer : owner.gameObject.layer;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = false;

            BuildVisual(go.transform, radius);

            var bubble = go.AddComponent<ShieldBubble>();
            bubble._owner = owner;
            bubble._expireAt = Time.time + duration;

            // Fold the bubble collider into the owner's projectile-exclusion
            // filter so the player can fire out through their own shield.
            ProjectileWorld.InvalidateOwnerColliders(owner);
        }

        private void Update()
        {
            if (_owner == null || Time.time >= _expireAt)
            {
                Robot owner = _owner;
                Destroy(gameObject);
                // Drop the now-stale collider from the owner's filter cache.
                if (owner != null) ProjectileWorld.InvalidateOwnerColliders(owner);
            }
        }

        private static void BuildVisual(Transform parent, float radius)
        {
            GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            // Strip the auto-added collider — the bubble's blocking collider
            // lives on the root with the kinematic body.
            Collider c = vis.GetComponent<Collider>();
            if (c != null) Destroy(c);
            vis.transform.SetParent(parent, worldPositionStays: false);
            vis.transform.localScale = Vector3.one * radius * 2f;

            MeshRenderer mr = vis.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = Material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private static Material Material
        {
            get
            {
                if (s_material != null) return s_material;
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Standard");
                s_material = new Material(shader) { name = "ShieldBubbleMat" };
                Color c = RuntimePalette.Cyan;
                c.a = 0.22f;
                // URP transparent setup.
                if (s_material.HasProperty("_Surface")) s_material.SetFloat("_Surface", 1f);
                if (s_material.HasProperty("_Blend")) s_material.SetFloat("_Blend", 0f);
                if (s_material.HasProperty("_BaseColor")) s_material.SetColor("_BaseColor", c);
                if (s_material.HasProperty("_Color")) s_material.SetColor("_Color", c);
                s_material.renderQueue = 3000;
                return s_material;
            }
        }
    }
}
