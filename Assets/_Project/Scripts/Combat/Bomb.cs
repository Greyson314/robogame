using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Gravity-driven bomb dropped from a <see cref="BombBayBlock"/>.
    /// On collision (or after <see cref="MaxLifetimeSeconds"/>) it spawns
    /// the configured Cartoon-FX explosion VFX and applies splash damage
    /// to every <see cref="Robot"/> with at least one block inside the
    /// blast radius.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a real Rigidbody (vs. swept ray like <see cref="Projectile"/>)?</b>
    /// Bombs are slow, low-fire-rate, and players need to <em>read</em> their
    /// arc to lead targets — the visible parabola from gravity-driven
    /// motion is part of the feel. PhysX handles the integration for free
    /// and the cost (one extra rigidbody for the ~2 s of flight) is
    /// negligible at the cadence bombs spawn at.
    /// </para>
    /// <para>
    /// <b>Damage model.</b> On impact, all colliders inside <c>radius</c>
    /// are considered; for each unique <see cref="Robot"/> found we walk
    /// its <see cref="Block.BlockGrid"/> and queue one
    /// <see cref="Block.BlockGrid.ApplySplashDamage"/> call per cell, with
    /// damage falloff <c>1 - (d/r)^2</c> from the impact world point.
    /// Non-Robot <see cref="IDamageable"/> hits get a single direct-hit
    /// damage call with the same falloff factor.
    /// </para>
    /// <para>
    /// <b>Owner filtering.</b> The owning robot is excluded so the bomber
    /// doesn't murder itself when it's at low altitude — bombs already
    /// fall away from the chassis but a tight radius + slow forward
    /// velocity can still clip back.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Bomb : MonoBehaviour
    {
        public const float MaxLifetimeSeconds = 8f;
        public const float OwnerImmunitySeconds = 0.25f;

        // Reuse buffer across all bombs. Fine because OnCollisionEnter
        // runs synchronously on the main thread and we process one bomb
        // at a time.
        private static readonly Collider[] s_overlapBuffer = new Collider[64];

        private float _damage;
        private float _radius;
        private LayerMask _hitMask;
        private Robot _owner;
        private Rigidbody _rb;
        private float _expireAt;
        private float _spawnTime;
        private bool _exploded;

        public void Configure(float damage, float radius, LayerMask hitMask, Robot owner, Vector3 initialVelocity)
        {
            _damage = damage;
            _radius = Mathf.Max(0.5f, radius);
            _hitMask = hitMask;
            _owner = owner;
            _expireAt = Time.time + MaxLifetimeSeconds;
            _spawnTime = Time.time;
            _exploded = false;

            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.linearVelocity = initialVelocity;
            _rb.angularVelocity = Random.insideUnitSphere * 1.5f;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_exploded) return;
            if (Time.time >= _expireAt) Explode(transform.position, null);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_exploded) return;

            // Brief immunity window so a bomb falling out of an open bay
            // doesn't immediately re-collide with the chassis it left.
            if (Time.time - _spawnTime < OwnerImmunitySeconds && _owner != null)
            {
                Robot hitRobot = collision.collider.GetComponentInParent<Robot>();
                if (hitRobot == _owner)
                {
                    // Don't explode on the launching chassis during the
                    // bay-clearance window. Drop log left as Verbose-level
                    // for now; remove once shipping.
                    return;
                }
            }

            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            Explode(point, collision.collider);
        }

        private void Explode(Vector3 worldPoint, Collider primaryHit)
        {
            _exploded = true;

            SpawnVfx(worldPoint);
            ApplyAreaDamage(worldPoint);

            // Bombs aren't pooled (low fire rate, cheap allocation).
            Destroy(gameObject);
        }

        private static void SpawnVfx(Vector3 worldPoint)
        {
            CombatVfxLibrary lib = CombatVfxLibrary.Load();
            if (lib == null || lib.BombExplosion == null) return;
            // Quaternion.identity — most CFXR explosions are spherical.
            // The CFXR auto-destroy component on the prefab cleans up.
            Object.Instantiate(lib.BombExplosion, worldPoint, Quaternion.identity);
        }

        private void ApplyAreaDamage(Vector3 worldPoint)
        {
            int count = Physics.OverlapSphereNonAlloc(worldPoint, _radius, s_overlapBuffer, _hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            // Fan out by Robot so each robot's grid receives one damage
            // pass per cell rather than one per collider — colliders
            // overlap (block primitive + chassis-level mount).
            HashSet<Robot> seenRobots = new HashSet<Robot>();
            HashSet<IDamageable> seenLooseTargets = new HashSet<IDamageable>();

            for (int i = 0; i < count; i++)
            {
                Collider c = s_overlapBuffer[i];
                if (c == null) continue;

                Robot robot = c.GetComponentInParent<Robot>();
                if (robot != null)
                {
                    if (robot == _owner) continue;
                    if (!seenRobots.Add(robot)) continue;
                    DamageRobot(robot, worldPoint);
                    continue;
                }

                // Non-robot damageable (training dummy, destructible).
                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null) continue;
                if (!seenLooseTargets.Add(d)) continue;
                float falloff = 1f; // full damage; no per-cell distance graph
                d.TakeDamage(_damage * falloff);
            }
        }

        private void DamageRobot(Robot robot, Vector3 worldPoint)
        {
            BlockGrid grid = robot.Grid;
            if (grid == null) return;

            // Gather all blocks of this grid within radius. ApplySplashDamage
            // takes a single cell + per-ring damage table; for an explosion
            // we'd rather call TakeDamage directly per block with a quadratic
            // falloff so visualization matches the actual sphere.
            float r2 = _radius * _radius;
            foreach (BlockBehaviour block in robot.GetComponentsInChildren<BlockBehaviour>(includeInactive: false))
            {
                if (block == null || !block.IsAlive) continue;
                Vector3 center = block.transform.position;
                float d2 = (center - worldPoint).sqrMagnitude;
                if (d2 > r2) continue;
                // Quadratic falloff: 1 at centre, 0 at edge. Square the
                // normalized radius so damage drops off slower near the
                // impact point and crashes near the rim.
                float t = 1f - (d2 / r2);
                float dmg = _damage * t;
                block.TakeDamage(dmg);
            }
        }
    }
}
