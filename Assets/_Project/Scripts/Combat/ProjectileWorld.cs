using System;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Scene-root singleton that owns every flying projectile in the
    /// game. Custom-stepped (no Rigidbody, no PhysX collider on the
    /// projectile itself) per the textbook PvP-shooter pattern —
    /// integrate ballistic state in <see cref="FixedUpdate"/>, sweep
    /// a <see cref="Physics.Raycast"/> or <see cref="Physics.SphereCast"/>
    /// per step, dispatch hits to existing damage routing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this shape?</b> See <c>docs/changes/32-projectile-unification.md</c>
    /// for the full research summary. Headlines:
    /// </para>
    /// <list type="bullet">
    /// <item>No projectile collider → chassis <c>MomentumImpactHandler</c>
    ///       can't bill itself for "ramming damage" off our shots.</item>
    /// <item>Swept cast → fast bullets can't tunnel through walls
    ///       thinner than v·dt.</item>
    /// <item>Owner-collider self-filter is a <see cref="HashSet{T}"/>
    ///       lookup, immediate (no PhysX timing quirks).</item>
    /// <item>Pure deterministic state → server-rewind / client-prediction
    ///       drop-in when netcode lands.</item>
    /// </list>
    /// <para>
    /// <b>Hot-path budget.</b> Steady state: zero allocations. Active
    /// list is a flat array, swap-remove on despawn. Hit buffer is
    /// static. Owner-collider sets are cached per-Robot and only
    /// rebuilt when invalidated explicitly. Visual GameObjects are
    /// pooled across kinds.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProjectileWorld : MonoBehaviour
    {
        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Fired when a projectile lands a damaging hit on a non-owner
        /// target. Args: (firingChassis, worldHitPoint). HUD overlays
        /// (e.g. <c>HitMarkerOverlay</c>) listen here to render hit
        /// markers when the local player is the owner.
        /// </summary>
        public static event Action<Robot, Vector3> HitLanded;

        /// <summary>
        /// Spawn a new projectile from <paramref name="spec"/>. Caller
        /// owns building the spec (origin, velocity, splash profile,
        /// hit mask, owner). Allocation-free in steady state.
        /// </summary>
        public static void Spawn(in ProjectileSpec spec)
        {
            EnsureBootstrap();
            if (s_instance == null) return;
            s_instance.SpawnInternal(in spec);
        }

        /// <summary>
        /// Drop the cached collider snapshot for <paramref name="owner"/>.
        /// Call this when the chassis loses or gains blocks (block
        /// detach, chassis rebuild) so subsequent shots respect the
        /// new collider set. Cheap; the next fire rebuilds the cache.
        /// </summary>
        public static void InvalidateOwnerColliders(Robot owner)
        {
            if (s_instance == null || owner == null) return;
            s_instance._ownerColliderCache.Remove(owner);
        }

        // -----------------------------------------------------------------
        // Bootstrap + state
        // -----------------------------------------------------------------

        private static ProjectileWorld s_instance;
        private static GameObject s_root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_root = null;
            HitLanded = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_root = new GameObject("[ProjectileWorld]");
            DontDestroyOnLoad(s_root);
            s_instance = s_root.AddComponent<ProjectileWorld>();
        }

        private struct Live
        {
            public ProjectileSpec Spec;
            public Vector3 Pos;
            public Vector3 Vel;
            public float AgeRemaining;
            public ProjectileVisual Visual;
        }

        // Active projectiles. Resized on overflow; in practice the cap
        // is set generously and resize never fires.
        private Live[] _alive = new Live[256];
        private int _count;

        // Reusable hit buffer for swept casts. Static — only one cast
        // is in flight at any moment (FixedUpdate is single-threaded).
        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>
        /// Multiplier mapping a bomb's combat-splash radius to its
        /// terrain-crater radius. Combat splash sizes are balanced for
        /// chassis damage (default ~18m); using the same value as a
        /// crater radius would erase a small dig zone outright. 0.3×
        /// is the empirical floor that still produces a visible crater
        /// while leaving room for repeated bombing to actually tunnel.
        /// </summary>
        public const float TerrainCraterScale = 0.3f;

        // Visual pools (separate per kind because the underlying GO
        // shape differs: trail-only vs mesh vs both).
        private readonly Stack<ProjectileVisual> _trailPool = new(32);
        private readonly Stack<ProjectileVisual> _meshPool = new(32);

        // Owner collider cache — built per-Robot on first fire,
        // refreshed via InvalidateOwnerColliders when chassis state
        // changes (block detach, chassis rebuild).
        private readonly Dictionary<Robot, Collider[]> _ownerColliderCache = new(16);

        // Reusable per-cast filter set. Size grows monotonically with
        // the largest-ever-encountered chassis; never shrinks (avoids
        // resize cost on every shot).
        private readonly HashSet<Collider> _hitFilter = new(64);

        // Materials shared across visuals — built lazily, cached.
        private static Material s_trailMaterial;
        private static Material s_ballMaterial;

        // Per-bomb scratch for area splash. Reused across explosions
        // so steady state allocates nothing.
        private readonly HashSet<Robot> _splashRobots = new(16);
        private readonly HashSet<IDamageable> _splashLooseTargets = new(16);
        private static readonly Collider[] s_splashOverlap = new Collider[64];

        // -----------------------------------------------------------------
        // Spawn
        // -----------------------------------------------------------------

        private void SpawnInternal(in ProjectileSpec spec)
        {
            if (_count >= _alive.Length) Array.Resize(ref _alive, _alive.Length * 2);

            ref Live p = ref _alive[_count++];
            p.Spec = spec;
            p.Pos = spec.Origin;
            p.Vel = spec.InitialVelocity;
            p.AgeRemaining = Mathf.Max(0.05f, spec.MaxLifetime);

            // Visual checkout order matters: position the GameObject
            // BEFORE Configure runs (which calls TrailRenderer.Clear()
            // + emitting=true). Otherwise the trail's first emit sample
            // lands at the visual's previous-release position, the
            // second at spawn position, and the trail draws a long
            // visible line between the two — the "ghost ray" bug.
            p.Visual = AcquireVisualInactive(in spec);
            if (p.Visual != null)
            {
                p.Visual.SyncTo(p.Pos, p.Vel);          // position first
                p.Visual.gameObject.SetActive(true);     // then activate
                ConfigureVisual(p.Visual, in spec);      // then clear trail at the correct position
            }
        }

        private void Despawn(int idx)
        {
            ref Live p = ref _alive[idx];
            if (p.Visual != null)
            {
                ReleaseVisual(p.Visual, p.Spec.Kind);
                p.Visual = null;
            }
            // Swap-remove keeps the array tightly packed for the
            // FixedUpdate iteration.
            int last = _count - 1;
            if (idx != last) _alive[idx] = _alive[last];
            _alive[last] = default;
            _count = last;
        }

        // -----------------------------------------------------------------
        // Integrator
        // -----------------------------------------------------------------

        private void FixedUpdate()
        {
            using var _scope = PerfMarkers.ProjectileFixedUpdate.Auto();

            float dt = Time.fixedDeltaTime;
            for (int i = _count - 1; i >= 0; i--)
            {
                ref Live p = ref _alive[i];
                p.AgeRemaining -= dt;
                if (p.AgeRemaining <= 0f) { Despawn(i); continue; }

                Vector3 step = p.Vel * dt;
                float dist = step.magnitude;
                if (dist > 1e-5f)
                {
                    Vector3 dir = step / dist;
                    if (TrySweep(in p.Spec, p.Pos, dir, dist, out RaycastHit hit))
                    {
                        Resolve(in p.Spec, hit);
                        Despawn(i);
                        continue;
                    }
                }

                p.Pos += step;
                p.Vel += p.Spec.GravityWorld * dt;
                if (p.Visual != null) p.Visual.SyncTo(p.Pos, p.Vel);
            }
        }

        private bool TrySweep(in ProjectileSpec spec, Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            int n = spec.CastRadius > 0f
                ? Physics.SphereCastNonAlloc(origin, spec.CastRadius, dir, s_hits, dist, spec.HitMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, dir, s_hits, dist, spec.HitMask, QueryTriggerInteraction.Ignore);

            BuildOwnerFilter(spec.Owner);

            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = s_hits[i].collider;
                if (c == null) continue;
                if (_hitFilter.Contains(c)) continue;
                if (s_hits[i].distance < bestDist)
                {
                    bestDist = s_hits[i].distance;
                    best = s_hits[i];
                    found = true;
                }
            }
            return found;
        }

        private void BuildOwnerFilter(Robot owner)
        {
            _hitFilter.Clear();
            if (owner == null) return;
            if (!_ownerColliderCache.TryGetValue(owner, out Collider[] cols))
            {
                cols = owner.GetComponentsInChildren<Collider>(includeInactive: true);
                _ownerColliderCache[owner] = cols;
            }
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) _hitFilter.Add(cols[i]);
            }
        }

        // -----------------------------------------------------------------
        // Hit resolution
        // -----------------------------------------------------------------

        private void Resolve(in ProjectileSpec spec, RaycastHit hit)
        {
            Vector3 hitPoint = hit.point;
            Vector3 hitNormal = hit.normal.sqrMagnitude > 1e-4f ? hit.normal : Vector3.up;

            // Damage routing — see ProjectileSpec.cs for the priority
            // ladder.
            if (spec.SplashRadius > 0f)
            {
                ApplyAreaSplash(in spec, hitPoint);
            }
            else if (spec.SplashRings != null && spec.SplashRings.Length > 0)
            {
                ApplyRingSplashOnHit(in spec, hit);
            }
            else if (spec.Damage > 0f)
            {
                ApplyDirect(in spec, hit);
            }

            DispatchImpactFx(spec.Kind, hitPoint, hitNormal, in spec);
        }

        private void ApplyDirect(in ProjectileSpec spec, RaycastHit hit)
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) return;
            // Suppress own-chassis (defensive — owner filter already
            // excludes own colliders, but a parented IDamageable on an
            // unfiltered collider would otherwise sneak through).
            Robot targetRobot = (target as Component) != null
                ? ((Component)target).GetComponentInParent<Robot>()
                : null;
            if (targetRobot != null && targetRobot == spec.Owner) return;
            // Friendly fire is silently dropped — bullet stops on the
            // teammate's collider but applies no damage. V1 limitation:
            // shots don't pass through, but they also don't grief the
            // ally. See SCRAP_LOOP_PLAN.md § 2.
            if (IsFriendlyFire(spec.Owner, targetRobot)) return;
            target.TakeDamage(spec.Damage);
            HitLanded?.Invoke(spec.Owner, hit.point);
        }

        private void ApplyRingSplashOnHit(in ProjectileSpec spec, RaycastHit hit)
        {
            // Ring splash: prefer the BlockBehaviour's grid cell as the
            // splash centre — its position is exact (no rounding error
            // off the contact point).
            BlockBehaviour block = hit.collider.GetComponentInParent<BlockBehaviour>();
            if (block != null)
            {
                Robot targetRobot = block.GetComponentInParent<Robot>();
                if (targetRobot != null && targetRobot != spec.Owner && targetRobot.Grid != null)
                {
                    if (IsFriendlyFire(spec.Owner, targetRobot)) return;
                    targetRobot.Grid.ApplySplashDamage(block.GridPosition, spec.SplashRings);
                    HitLanded?.Invoke(spec.Owner, hit.point);
                    return;
                }
            }

            // Fallback: non-Robot damageable (training dummy without a
            // grid). Single direct-hit damage from ring 0.
            IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg == null) return;
            Robot owner = (dmg as Component) != null
                ? ((Component)dmg).GetComponentInParent<Robot>()
                : null;
            if (owner != null && owner == spec.Owner) return;
            if (IsFriendlyFire(spec.Owner, owner)) return;
            dmg.TakeDamage(spec.SplashRings[0]);
            HitLanded?.Invoke(spec.Owner, hit.point);
        }

        private void ApplyAreaSplash(in ProjectileSpec spec, Vector3 worldPoint)
        {
            int count = Physics.OverlapSphereNonAlloc(worldPoint, spec.SplashRadius, s_splashOverlap, spec.HitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            _splashRobots.Clear();
            _splashLooseTargets.Clear();
            float r2 = spec.SplashRadius * spec.SplashRadius;

            for (int i = 0; i < count; i++)
            {
                Collider c = s_splashOverlap[i];
                if (c == null) continue;

                Robot robot = c.GetComponentInParent<Robot>();
                if (robot != null)
                {
                    if (robot == spec.Owner) continue;
                    if (IsFriendlyFire(spec.Owner, robot)) continue;
                    if (!_splashRobots.Add(robot)) continue;
                    DamageRobotInRadius(robot, worldPoint, r2, spec.Damage);
                    continue;
                }

                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null) continue;
                if (!_splashLooseTargets.Add(d)) continue;
                d.TakeDamage(spec.Damage);
            }

            // Splash hit-marker: fire once with the most-damaged robot's
            // anchor — for simplicity, the explosion centre.
            HitLanded?.Invoke(spec.Owner, worldPoint);
        }

        // Friendly-fire test. Returns true when both chassis are alive,
        // both have a non-neutral team, and those teams match. Neutral
        // (TeamId.None) targets — training dummies, props — are always
        // damageable so dev sandbox flows keep working.
        private static bool IsFriendlyFire(Robot owner, Robot target)
        {
            if (owner == null || target == null) return false;
            if (owner.Team == TeamId.None || target.Team == TeamId.None) return false;
            return owner.Team == target.Team;
        }

        private static void DamageRobotInRadius(Robot robot, Vector3 worldPoint, float r2, float headlineDamage)
        {
            BlockGrid grid = robot.Grid;
            if (grid == null) return;
            // Per-block quadratic falloff: 1 at centre, 0 at radius.
            // Same shape as the prior Bomb.DamageRobot path.
            foreach (BlockBehaviour block in robot.GetComponentsInChildren<BlockBehaviour>(includeInactive: false))
            {
                if (block == null || !block.IsAlive) continue;
                Vector3 centre = block.transform.position;
                float d2 = (centre - worldPoint).sqrMagnitude;
                if (d2 > r2) continue;
                float t = 1f - (d2 / r2);
                block.TakeDamage(headlineDamage * t);
            }
        }

        // -----------------------------------------------------------------
        // VFX / audio dispatch
        // -----------------------------------------------------------------

        private void DispatchImpactFx(ProjectileKind kind, Vector3 pos, Vector3 normal, in ProjectileSpec spec)
        {
            // Audio cue is always the spec's override — every caller
            // sets one explicitly. Don't try to "default" via enum
            // comparisons (default(AudioCue) == AudioCue.WeaponFire
            // would swallow that cue if a future caller did pick it
            // for impact).
            AudioCue impactCue = spec.ImpactAudioOverride;

            switch (kind)
            {
                case ProjectileKind.SmgPellet:
                    VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, scale: 0.85f);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    break;

                case ProjectileKind.Cannonball:
                    VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, scale: 1.4f);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    break;

                case ProjectileKind.Bomb:
                    // Combined CFXR explosion + procedural shockwave +
                    // bomb-blast audio.
                    CombatVfxLibrary lib = CombatVfxLibrary.Load();
                    if (lib != null && lib.BombExplosion != null)
                    {
                        UnityEngine.Object.Instantiate(lib.BombExplosion, pos, Quaternion.identity);
                    }
                    float shockScale = Mathf.Clamp(spec.SplashRadius * 0.5f, 0.6f, 3.0f);
                    VfxSpawner.Spawn(VfxKind.BombShockwave, pos, Quaternion.identity, shockScale);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    // Phase 3c: if the bomb detonated inside a dig zone,
                    // emit a SphereSubtract crater. No-op outside any zone.
                    // The terrain crater radius is the combat splash
                    // scaled by `TerrainCraterScale` — a default-18m
                    // bomb splash is balanced for chassis damage and
                    // would obliterate a small dig zone (16m deep)
                    // outright. 0.3× gives a proportional crater
                    // (~5–6m for the default bomb) and leaves room to
                    // see tunneling as the player keeps bombing.
                    Voxel.TerrainCratering.OnBombDetonation(pos, spec.SplashRadius * TerrainCraterScale);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // Visual pool
        // -----------------------------------------------------------------

        // Pop a pooled visual (or create one) but DO NOT activate or
        // configure yet. The caller must SyncTo (position) before
        // SetActive + Configure so the trail's Clear() lands at the
        // correct world position.
        private ProjectileVisual AcquireVisualInactive(in ProjectileSpec spec)
        {
            Stack<ProjectileVisual> pool = spec.ShowMesh ? _meshPool : _trailPool;
            ProjectileVisual v;
            if (pool.Count > 0)
            {
                v = pool.Pop();
            }
            else
            {
                var go = new GameObject(spec.ShowMesh ? "ProjectileVisual_Mesh" : "ProjectileVisual_Trail");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.SetActive(false);
                v = go.AddComponent<ProjectileVisual>();
            }
            return v;
        }

        private static void ConfigureVisual(ProjectileVisual v, in ProjectileSpec spec)
        {
            v.Configure(
                showTrail: spec.ShowTrail,
                showMesh: spec.ShowMesh,
                tint: spec.VisualTint,
                meshDiameter: spec.VisualMeshDiameter,
                trailMaterial: TrailMaterial,
                meshMaterial: BallMaterial);
        }

        private void ReleaseVisual(ProjectileVisual v, ProjectileKind kind)
        {
            if (v == null) return;
            v.Stop();
            v.gameObject.SetActive(false);
            // Pool by visual shape, not gameplay kind — bomb and
            // cannonball share the mesh pool; SMG owns the trail pool.
            (kind == ProjectileKind.SmgPellet ? _trailPool : _meshPool).Push(v);
        }

        private static Material TrailMaterial
        {
            get
            {
                if (s_trailMaterial != null) return s_trailMaterial;
                s_trailMaterial = new Material(Shader.Find("Sprites/Default")) { name = "ProjectileTrail" };
                return s_trailMaterial;
            }
        }

        private static Material BallMaterial
        {
            get
            {
                if (s_ballMaterial != null) return s_ballMaterial;
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                s_ballMaterial = new Material(sh) { name = "ProjectileBall" };
                Color iron = new Color(0.10f, 0.10f, 0.12f);
                if (s_ballMaterial.HasProperty("_BaseColor")) s_ballMaterial.SetColor("_BaseColor", iron);
                if (s_ballMaterial.HasProperty("_Color"))     s_ballMaterial.SetColor("_Color", iron);
                if (s_ballMaterial.HasProperty("_Smoothness")) s_ballMaterial.SetFloat("_Smoothness", 0.6f);
                return s_ballMaterial;
            }
        }
    }
}
