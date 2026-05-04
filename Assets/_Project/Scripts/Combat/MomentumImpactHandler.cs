using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Applies kinetic-energy-based ramming damage to the chassis when it
    /// collides with another body. Damage scales with the relative speed
    /// and the <i>reduced</i> mass of the two bodies, so a small craft
    /// hitting a heavy one and vice-versa produce the same energy budget
    /// (Newton's third law in action), and a tiny mass on either side
    /// caps the damage gracefully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives on the chassis root Rigidbody. <c>OnCollisionEnter</c>
    /// bubbles up from child colliders, which is exactly the contract the
    /// compound-collider chassis (per <c>BEST_PRACTICES.md §3.1</c>)
    /// needs.
    /// </para>
    /// <para>
    /// Damage routing prefers <see cref="BlockGrid.ApplySplashDamage"/>
    /// using the contact-point's nearest block as the splash centre, so
    /// connectivity / debris bookkeeping stays correct. Bodies that
    /// aren't Robots (static geometry, props, the dummy when it loses
    /// its Robot for some reason) fall back to a single
    /// <see cref="IDamageable.TakeDamage"/> at the closest hit collider.
    /// </para>
    /// <para>
    /// Tweakables under the "Impact" group (damage scale, speed
    /// threshold, ring profile) drive the curve so the values can be
    /// dialled in live from Settings ▸ Tweaks.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MomentumImpactHandler : MonoBehaviour
    {
        // Reuse a single ring buffer per call to avoid per-impact GC.
        // Three rings is plenty for ramming — direct hit + 6 face
        // neighbours + 18 second-ring is the most we ever splash on a
        // body-on-body strike.
        private static readonly float[] s_ringScratch = new float[3];

        // Pair-cooldown: PhysX can fire several OnCollisionEnter messages
        // for one logical impact (compound colliders, bouncing apart and
        // contacting again on the same frame). Suppressing back-to-back
        // hits against the same opponent for a short window keeps
        // ramming damage from compounding into instakill.
        private readonly Dictionary<UnityEngine.Object, float> _cooldownByOther = new();
        private const float PairCooldownSeconds = 0.20f;

        private Rigidbody _rb;
        private Robot _robot;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _robot = GetComponent<Robot>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_rb == null) return;

            // Cooldown gate per-opponent. Use the other Rigidbody's
            // instance id when present, else the collider's, so static
            // geometry still benefits from the dedupe.
            UnityEngine.Object otherKey = (UnityEngine.Object)collision.rigidbody ?? collision.collider;
            float now = Time.time;
            if (otherKey != null
                && _cooldownByOther.TryGetValue(otherKey, out float lastTime)
                && now - lastTime < PairCooldownSeconds)
            {
                return;
            }
            if (otherKey != null) _cooldownByOther[otherKey] = now;

            // Self-collisions (debris from the same chassis) are ignored —
            // we don't want a freshly-detached block to one-shot its
            // former mothership on the way out.
            Robot otherRobot = collision.collider != null
                ? collision.collider.GetComponentInParent<Robot>()
                : null;
            if (otherRobot != null && otherRobot == _robot) return;

            float minSpeed = Mathf.Max(0.01f, Tweakables.Get(Tweakables.ImpactMinSpeed));
            float damagePerKj = Mathf.Max(0f, Tweakables.Get(Tweakables.ImpactDamagePerKj));
            if (damagePerKj <= 0f) return;

            // Relative velocity along the contact normal. Tangential
            // component is mostly grinding / drag and shouldn't ram-kill
            // a chassis just because it scraped a wall sideways.
            ContactPoint contact = collision.GetContact(0);
            Vector3 vRel = collision.relativeVelocity; // (a.velocity - b.velocity)
            float vNormal = Mathf.Abs(Vector3.Dot(vRel, contact.normal));
            if (vNormal < minSpeed) return;

            // Reduced mass: μ = m1*m2 / (m1+m2). When the other body is
            // static (no Rigidbody) treat it as infinitely massive, which
            // collapses μ to m1 — i.e. a wall slam dumps all of self's
            // KE into self, exactly what we want.
            float m1 = Mathf.Max(0.001f, _rb.mass);
            Rigidbody otherRb = collision.rigidbody;
            float reducedMass;
            if (otherRb == null || otherRb.isKinematic)
            {
                reducedMass = m1;
            }
            else
            {
                float m2 = Mathf.Max(0.001f, otherRb.mass);
                reducedMass = (m1 * m2) / (m1 + m2);
            }

            // KE of the collision in joules. Convert to kJ so the
            // tweakable damage-per-kJ scale sits in single-digit range
            // for typical chassis (a 50 kg plane at 30 m/s vs a 50 kg
            // dummy gives ~5.6 kJ → ~28 dmg at the default 5/kJ).
            float energyJoules = 0.5f * reducedMass * vNormal * vNormal;
            float energyKj = energyJoules * 0.001f;
            float baseDamage = energyKj * damagePerKj;
            if (baseDamage <= 0f) return;

            // Build a 3-ring damage profile from tweakables. Both bodies
            // take the same profile — Newton's third law: the impulse
            // they trade is identical, the only thing that differs is
            // each body's local block layout absorbing it.
            s_ringScratch[0] = baseDamage * Tweakables.Get(Tweakables.ImpactRing0Scale);
            s_ringScratch[1] = baseDamage * Tweakables.Get(Tweakables.ImpactRing1Scale);
            s_ringScratch[2] = baseDamage * Tweakables.Get(Tweakables.ImpactRing2Scale);

            // Self side — splash from the block nearest our contact point.
            // Each handler is responsible for *its own* chassis's damage:
            // OnCollisionEnter fires on both bodies, so if we also damaged
            // the other side here we'd double-apply when both chassis carry
            // a handler (plane vs plane, plane vs dummy). The other side's
            // handler will run independently and bill its own bookkeeping.
            ApplyDamageAtContact(_robot, contact.thisCollider, contact.point, s_ringScratch);

            // Bodies without their own handler still need to take damage
            // (destructible props, IDamageable widgets that aren't wired
            // through ChassisFactory). Detect by absence of handler on
            // the colliding rigidbody hierarchy and route a single direct
            // hit. A Robot opponent never falls into this branch because
            // ChassisFactory always installs a handler on Robot roots.
            Component otherComp = collision.collider;
            if (otherComp == null) return;
            bool otherHasHandler = otherComp.GetComponentInParent<MomentumImpactHandler>() != null;
            if (otherHasHandler) return;

            IDamageable plain = otherComp.GetComponentInParent<IDamageable>();
            if (plain != null) plain.TakeDamage(s_ringScratch[0]);
        }

        // Routes damage into a Robot's grid via splash, falling back to
        // the nearest BlockBehaviour's TakeDamage if the contact collider
        // isn't recognisably gridded (compound chassis colliders that
        // sit at the root rather than per-block).
        private static void ApplyDamageAtContact(
            Robot robot, Collider contactCollider, Vector3 worldPoint, float[] ringDamage)
        {
            if (robot == null || ringDamage == null || ringDamage.Length == 0) return;

            // Prefer the BlockBehaviour the contact collider belongs to —
            // its grid position is exact, no rounding error.
            BlockBehaviour direct = contactCollider != null
                ? contactCollider.GetComponentInParent<BlockBehaviour>()
                : null;

            if (direct == null && robot.Grid != null)
            {
                // Fallback: convert the contact point into grid space.
                // Round-to-nearest is fine; the worst case is splashing
                // into the neighbouring cell, which is still a sane
                // outcome for an oblique hit.
                Vector3Int gp = robot.Grid.WorldToGrid(worldPoint);
                robot.Grid.ApplySplashDamage(gp, ringDamage);
                return;
            }

            if (direct != null && robot.Grid != null)
            {
                robot.Grid.ApplySplashDamage(direct.GridPosition, ringDamage);
                return;
            }

            // Last-ditch: no grid at all (shouldn't happen for a Robot,
            // but be defensive). Just damage the block we hit.
            if (direct != null) direct.TakeDamage(ringDamage[0]);
        }
    }
}
