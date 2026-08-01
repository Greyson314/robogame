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
        /// Fired immediately after a projectile is spawned (any caller, any
        /// peer). The Network layer subscribes server-side to fan out a
        /// cosmetic <c>ProjectileSpawnEvent</c> ClientRpc — NETCODE_PLAN §9.
        /// Singleplayer has no subscriber, so the dispatch is a null-check
        /// (zero baseline cost — invariant #5).
        /// </summary>
        public static event Action<ProjectileSpec> Spawned;

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
        /// Detonate an explosion at <paramref name="center"/> with no
        /// projectile in flight — area-splash damage + explosive knockback +
        /// the bomb explosion VFX/audio/crater treatment, in one call. Used
        /// by deployed explosives (mines) and any future "explode here" effect.
        /// Owner + its teammates are spared (same friendly-fire rules as a
        /// thrown bomb). Allocation-free in steady state.
        /// </summary>
        public static void Detonate(Vector3 center, float radius, float damage, float knockback,
            Robot owner, LayerMask mask, Robogame.Core.AudioCue impactAudio)
        {
            EnsureBootstrap();
            if (s_instance == null || radius <= 0f) return;
            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = ProjectileKind.Bomb,             // reuse the bomb explosion treatment
                Origin = center,
                GravityWorld = Physics.gravity,         // crater bias direction in DispatchImpactFx
                Damage = damage,
                SplashRadius = radius,
                Knockback = knockback,
                HitMask = mask,
                Owner = owner,
                ImpactAudioOverride = impactAudio,
            };
            s_instance.ApplyAreaSplash(in spec, center);
            s_instance.DispatchImpactFx(ProjectileKind.Bomb, center, Vector3.up, in spec);
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
            Spawned = null;
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
            // Position at the previous fixed step — Update() lerps the
            // visual between PrevPos and Pos so rendering stays smooth at
            // frame rates that aren't multiples of the 50 Hz sim tick.
            public Vector3 PrevPos;
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
        /// terrain-crater radius. Bombs read as wide-but-shallow scorches:
        /// 0.5× makes the crater half the blast width (a default ~18m splash
        /// → ~9m crater; a maxed-size concoction → ~18m) while the depth cap
        /// below keeps even huge blasts from tunnelling.
        /// </summary>
        public const float TerrainCraterScale = 0.5f;

        /// <summary>
        /// Baseline shallowness for SMALL craters: the SphereSubtract centre is
        /// pushed UP (along -gravity) so only a thin cap of the sphere dips
        /// below the surface. 0.8 leaves ~20% of the radius as depth for small
        /// blasts; <see cref="MaxCraterDepth"/> then hard-caps the absolute
        /// depth so a big bomb spreads WIDE rather than digging DEEP.
        /// </summary>
        public const float TerrainCraterUpwardBias = 0.8f;

        /// <summary>
        /// Absolute cap (metres) on how far below the impact point a crater
        /// carves, independent of blast radius. Keeps even maxed-size bombs to
        /// a wide, very shallow dish you drive across — not a pit to climb out
        /// of. Per the user's "wide but very shallow, even for huge bombs."
        /// </summary>
        public const float MaxCraterDepth = 1.25f;

        // Visual pools (separate per kind because the underlying GO
        // shape differs: trail-only vs mesh vs both).
        private readonly Stack<ProjectileVisual> _trailPool = new(32);
        private readonly Stack<ProjectileVisual> _meshPool = new(32);

        // Owner collider cache — built per-Robot on first fire. Stamped
        // with the grid's StructureVersion so any structural change
        // (block detach, RepairPad re-placement, rebuild) rebuilds the
        // snapshot on the next fire — InvalidateOwnerColliders had zero
        // callers, so a stale snapshot made your own shots die on your
        // hull after regen. Dead-Robot keys are pruned periodically so
        // this DontDestroyOnLoad singleton doesn't leak across matches.
        private struct OwnerColliders
        {
            public Collider[] Colliders;
            public int StructureVersion;
        }
        private readonly Dictionary<Robot, OwnerColliders> _ownerColliderCache = new(16);
        private readonly List<Robot> _deadOwnerScratch = new(16);
        private float _nextOwnerCachePrune;

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
            p.PrevPos = spec.Origin;
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

            Spawned?.Invoke(spec);
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
                        Resolve(in p.Spec, hit, dir);
                        Despawn(i);
                        continue;
                    }
                }

                p.PrevPos = p.Pos;
                p.Pos += step;
                p.Vel += p.Spec.GravityWorld * dt;
            }
        }

        // Render interpolation. The sim advances in FixedUpdate (50 Hz);
        // snapping visuals to raw step positions made slow near-camera
        // projectiles (falling bombs) visibly stutter at render rates
        // that aren't multiples of the tick. Same scheme as Rigidbody
        // interpolation: render up to one tick in the past, lerped
        // between the last two sim states. Alloc-free loop.
        private void Update()
        {
            float fdt = Time.fixedDeltaTime;
            float alpha = fdt > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / fdt) : 1f;
            for (int i = 0; i < _count; i++)
            {
                ref Live p = ref _alive[i];
                if (p.Visual == null) continue;
                p.Visual.SyncTo(Vector3.LerpUnclamped(p.PrevPos, p.Pos, alpha), p.Vel);
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
            int version = owner.Grid != null ? owner.Grid.StructureVersion : 0;
            if (!_ownerColliderCache.TryGetValue(owner, out OwnerColliders cached)
                || cached.StructureVersion != version)
            {
                cached = new OwnerColliders
                {
                    Colliders = owner.GetComponentsInChildren<Collider>(includeInactive: true),
                    StructureVersion = version,
                };
                _ownerColliderCache[owner] = cached;
            }
            Collider[] cols = cached.Colliders;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) _hitFilter.Add(cols[i]);
            }
        }

        // Drop cache entries whose Robot has been destroyed. Runs on a
        // slow cadence — the dictionary is small, but this component is
        // a DontDestroyOnLoad singleton, so without the sweep dead keys
        // accumulate across respawns and scene loads forever.
        private void PruneDeadOwnerCache()
        {
            if (Time.unscaledTime < _nextOwnerCachePrune) return;
            _nextOwnerCachePrune = Time.unscaledTime + 10f;
            _deadOwnerScratch.Clear();
            foreach (var kvp in _ownerColliderCache)
            {
                if (kvp.Key == null) _deadOwnerScratch.Add(kvp.Key);
            }
            for (int i = 0; i < _deadOwnerScratch.Count; i++)
            {
                _ownerColliderCache.Remove(_deadOwnerScratch[i]);
            }
        }

        // -----------------------------------------------------------------
        // Hit resolution
        // -----------------------------------------------------------------

        private void Resolve(in ProjectileSpec spec, RaycastHit hit, Vector3 travelDir)
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
                ApplyRingSplashOnHit(in spec, hit, travelDir);
            }
            else if (spec.Damage > 0f)
            {
                ApplyDirect(in spec, hit, travelDir);
            }

            DispatchImpactFx(spec.Kind, hitPoint, hitNormal, in spec);
        }

        private void ApplyDirect(in ProjectileSpec spec, RaycastHit hit, Vector3 travelDir)
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
            // ally. See docs/changes/58-scrap-loop-v1.md § 2.
            if (Teams.IsFriendlyFire(spec.Owner, targetRobot)) return;
            // Wedge deflection (session 155): glancing hits on a wedge block
            // deal reduced damage. 1 for every non-wedge target.
            BlockBehaviour hitBlock = hit.collider.GetComponentInParent<BlockBehaviour>();
            float deflect = ArmorDeflection.ComputeMultiplier(hitBlock, hit.normal, travelDir);
            PlayDeflectFeedback(deflect, hit.point);
            target.TakeDamage(spec.Damage * deflect);
            DamageAttribution.Report(spec.Owner, targetRobot, spec.Damage * deflect);
            MusicalHits.Report(spec.Owner, targetRobot, spec.Kind, spec.Damage * deflect);
            if (spec.Knockback > 0f)
                ApplyKineticKnockback(targetRobot, travelDir, spec.Knockback, spec.KnockbackSmoothed);
            HitLanded?.Invoke(spec.Owner, hit.point);
        }

        /// <summary>
        /// Wedge-deflection feedback (invariant #8, wave-1 FX/audio pass):
        /// a bright ricochet ping when a wedge sheds meaningful damage.
        /// Threshold skips near-full-damage hits so only a real graze
        /// pings; the generic impact VFX from DispatchImpactFx still
        /// plays either way. Ram-path deflections stay silent — the
        /// RamSpark + ChassisRam pair already covers those.
        /// </summary>
        private static void PlayDeflectFeedback(float deflect, Vector3 point)
        {
            if (deflect < 0.9f) AudioRouter.PlayOneShot(AudioCue.ArmorDeflect, point);
        }

        private void ApplyRingSplashOnHit(in ProjectileSpec spec, RaycastHit hit, Vector3 travelDir)
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
                    if (Teams.IsFriendlyFire(spec.Owner, targetRobot)) return;
                    // Wedge deflection (session 155): scales ring 0 only —
                    // splash falloff through the rest of the chassis is
                    // unchanged.
                    float deflect = ArmorDeflection.ComputeMultiplier(block, hit.normal, travelDir);
                    PlayDeflectFeedback(deflect, hit.point);
                    targetRobot.Grid.ApplySplashDamage(block.GridPosition, spec.SplashRings, deflect);
                    DamageAttribution.Report(spec.Owner, targetRobot, spec.SplashRings[0] * deflect);
                    MusicalHits.Report(spec.Owner, targetRobot, spec.Kind, spec.SplashRings[0] * deflect);
                    if (spec.Knockback > 0f)
                        ApplyKineticKnockback(targetRobot, travelDir, spec.Knockback, spec.KnockbackSmoothed);
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
            if (Teams.IsFriendlyFire(spec.Owner, owner)) return;
            dmg.TakeDamage(spec.SplashRings[0]);
            DamageAttribution.Report(spec.Owner, owner, spec.SplashRings[0]);
            HitLanded?.Invoke(spec.Owner, hit.point);
        }

        private void ApplyAreaSplash(in ProjectileSpec spec, Vector3 worldPoint)
        {
            int count = Physics.OverlapSphereNonAlloc(worldPoint, spec.SplashRadius, s_splashOverlap, spec.HitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            _splashRobots.Clear();
            _splashLooseTargets.Clear();
            float r2 = spec.SplashRadius * spec.SplashRadius;
            bool hitAny = false;

            for (int i = 0; i < count; i++)
            {
                Collider c = s_splashOverlap[i];
                if (c == null) continue;

                Robot robot = c.GetComponentInParent<Robot>();
                if (robot != null)
                {
                    if (!_splashRobots.Add(robot)) continue;
                    // TRACE[LOG-115]: friendly-fire spares damage but NOT knockback (bomb-jump)
                    // Self + teammates take NO damage, but they DO get knocked:
                    // a bomb dropped at your own feet should launch you
                    // (bomb-jumping) and shove a nearby ally, just without the
                    // friendly damage. Enemies take both.
                    bool friendly = robot == spec.Owner || Teams.IsFriendlyFire(spec.Owner, robot);
                    if (!friendly)
                    {
                        DamageRobotInRadius(robot, worldPoint, r2, spec.Damage);
                        DamageAttribution.Report(spec.Owner, robot, spec.Damage);
                        MusicalHits.Report(spec.Owner, robot, spec.Kind, spec.Damage);
                        hitAny = true;
                    }
                    if (spec.Knockback > 0f)
                        ApplyExplosiveKnockback(robot, worldPoint, spec.SplashRadius, spec.Knockback);
                    continue;
                }

                IDamageable d = c.GetComponentInParent<IDamageable>();
                if (d == null) continue;
                if (!_splashLooseTargets.Add(d)) continue;
                d.TakeDamage(spec.Damage);
                hitAny = true;
            }

            // Splash hit-marker: fire once at the explosion centre, but ONLY if a
            // real target actually took damage — a blast that overlapped only
            // terrain / the owner / teammates shouldn't flash a hit marker.
            if (hitAny) HitLanded?.Invoke(spec.Owner, worldPoint);
        }

        // Reused splash snapshot. Lethal TakeDamage removes the block from
        // the grid synchronously (Destroyed → RemoveBlock), so the loop
        // must not hold a live dictionary enumerator; the old
        // GetComponentsInChildren snapshot allocated a fresh array per
        // explosion on the FixedUpdate stack (INV-6, 109-audit finding 8)
        // and also missed grid blocks reparented away from the chassis
        // (rotor foils under a scene-root hub).
        private static readonly List<BlockBehaviour> s_splashScratch = new(64);

        private static void DamageRobotInRadius(Robot robot, Vector3 worldPoint, float r2, float headlineDamage)
        {
            BlockGrid grid = robot.Grid;
            if (grid == null) return;
            s_splashScratch.Clear();
            foreach (var kvp in grid.BlocksNonAlloc)
            {
                if (kvp.Value != null) s_splashScratch.Add(kvp.Value);
            }
            // Per-block quadratic falloff: 1 at centre, 0 at radius.
            // Same shape as the prior Bomb.DamageRobot path.
            for (int i = 0; i < s_splashScratch.Count; i++)
            {
                BlockBehaviour block = s_splashScratch[i];
                if (block == null || !block.IsAlive) continue;
                Vector3 centre = block.transform.position;
                float d2 = (centre - worldPoint).sqrMagnitude;
                if (d2 > r2) continue;
                float t = 1f - (d2 / r2);
                block.TakeDamage(headlineDamage * t);
            }
            s_splashScratch.Clear();
        }

        // -----------------------------------------------------------------
        // Knockback
        // -----------------------------------------------------------------

        // Upward bias added to an explosion's radial push direction
        // before normalising — gives the "knockUP" pop on a ground blast
        // without launching bots straight up. Reserved for explosions;
        // kinetic hits stay horizontal.
        private const float ExplosiveUpwardBias = 0.4f;

        // Explosive knockback is mass-aware: the weapon's Knockback value is
        // read as a target delta-v (m/s) per this factor, then turned into an
        // impulse of mass × Δv so a heavy chassis is shoved as much as a light
        // one. Without this the raw-impulse path imparted Δv = impulse/mass,
        // which is imperceptible on a multi-block bot. The earlier 0.03 fix made
        // it mass-aware but left it far too weak to feel (~1 m/s); 0.12 gives a
        // blast real shove. At the blast centre: a base bomb (40) ≈ 4.8 m/s, a
        // maxed-knockback concoction (80) ≈ 9.6 m/s, a maxed mortar (110) ≈ 13.2
        // → clamped to the MaxExplosiveDeltaV (12 m/s) ceiling in KnockbackReceiver.
        private const float ExplosiveKnockbackDeltaVPerUnit = 0.12f;

        private static KnockbackReceiver GetOrAddReceiver(Robot robot)
        {
            if (robot == null) return null;
            KnockbackReceiver r = robot.GetComponent<KnockbackReceiver>();
            if (r == null) r = robot.gameObject.AddComponent<KnockbackReceiver>();
            return r;
        }

        // Kinetic: stagger the target along the shot's *horizontal* travel
        // direction. Vertical is dropped so bullets shove, never pop —
        // knockUP is reserved for explosions.
        private static void ApplyKineticKnockback(Robot robot, Vector3 travelDir, float magnitude, bool smoothed)
        {
            Vector3 horiz = new Vector3(travelDir.x, 0f, travelDir.z);
            float h = horiz.magnitude;
            if (h < 1e-4f) return; // near-vertical shot — no sensible push direction
            Vector3 impulse = (horiz / h) * magnitude;
            KnockbackReceiver recv = GetOrAddReceiver(robot);
            if (recv == null) return;
            if (smoothed) recv.AddSmoothed(impulse);
            else recv.ApplyImmediate(impulse);
        }

        // Explosive: push the chassis away from the blast centre with a
        // slight upward bias (the pop), scaled by linear distance falloff.
        // Always immediate — explosions never route through the debt buffer.
        private static void ApplyExplosiveKnockback(Robot robot, Vector3 center, float radius, float magnitude)
        {
            if (robot == null || radius <= 1e-4f) return;
            Rigidbody rb = robot.Rigidbody;
            if (rb == null) return;
            Vector3 toBot = rb.worldCenterOfMass - center;
            float d = toBot.magnitude;
            if (d > radius) return;
            float falloff = 1f - (d / radius);             // 1 at centre → 0 at edge
            Vector3 dir = d > 1e-4f ? toBot / d : Vector3.up;
            dir = (dir + Vector3.up * ExplosiveUpwardBias).normalized;
            // Mass-aware: target Δv → impulse of mass × Δv (clamped to the
            // explosive ceiling downstream, not the lower kinetic one).
            float targetDeltaV = magnitude * ExplosiveKnockbackDeltaVPerUnit * falloff;
            GetOrAddReceiver(robot)?.ApplyImmediate(dir * (rb.mass * targetDeltaV), KnockbackReceiver.MaxExplosiveDeltaV);
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
                    // Size lever fattens the base spark (LOG-153) — the
                    // kinetic kinds have no shock ring for size to read on.
                    float smgScale = 0.85f * (1f + 0.5f * spec.FxSize);
                    if (spec.TintImpact) VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, smgScale, spec.VisualTint);
                    else VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, scale: smgScale);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    break;

                case ProjectileKind.Cannonball:
                    float ballScale = 1.4f * (1f + 0.5f * spec.FxSize);
                    if (spec.TintImpact) VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, ballScale, spec.VisualTint);
                    else VfxSpawner.Spawn(VfxKind.HitSpark, pos, normal, scale: ballScale);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    break;

                case ProjectileKind.Bomb:
                case ProjectileKind.MortarShell:
                    // Combined CFXR explosion + procedural shockwave +
                    // blast audio. Mortar shells reuse the bomb's explosion
                    // treatment (both are area-splash detonations); the
                    // distinct identity is the lobbed delivery, not the boom.
                    CombatVfxLibrary lib = CombatVfxLibrary.Load();
                    if (lib != null && lib.BombExplosion != null)
                    {
                        UnityEngine.Object.Instantiate(lib.BombExplosion, pos, Quaternion.identity);
                    }
                    float shockScale = Mathf.Clamp(spec.SplashRadius * 0.5f, 0.6f, 3.0f);
                    if (spec.TintImpact) VfxSpawner.Spawn(VfxKind.BombShockwave, pos, Quaternion.identity, shockScale, spec.VisualTint);
                    else VfxSpawner.Spawn(VfxKind.BombShockwave, pos, Quaternion.identity, shockScale);
                    AudioRouter.PlayOneShot(impactCue, pos);
                    // Phase 3c: if the bomb detonated inside a dig zone,
                    // emit a SphereSubtract crater. No-op outside any zone.
                    // The crater radius is the combat splash scaled by
                    // `TerrainCraterScale` (wide). The carve depth below the
                    // impact point is the small-blast shallowness
                    // (R × (1 − bias)) HARD-CAPPED at `MaxCraterDepth`, so a
                    // huge bomb spreads wide-but-very-shallow rather than
                    // digging a pit. The sphere centre is placed so its lowest
                    // point sits exactly `depth` below the impact, along
                    // -gravity (correct on spherical arenas; arc weapons store
                    // the local gravity vector on the bomb's spec at fire time).
                    float craterR = spec.SplashRadius * TerrainCraterScale;
                    Vector3 upDir = spec.GravityWorld.sqrMagnitude > 1e-4f
                        ? -spec.GravityWorld.normalized
                        : Vector3.up;
                    float craterDepth = Mathf.Min(craterR * (1f - TerrainCraterUpwardBias), MaxCraterDepth);
                    Vector3 craterCentre = pos + upDir * (craterR - craterDepth);
                    Voxel.TerrainCratering.OnBombDetonation(craterCentre, craterR);
                    break;
            }

            SpawnConcoctionExtras(in spec, pos, normal);
        }

        // Per-recipe impact layering (LOG-153): each concoction lever
        // spiked above neutral adds its own read at the point of damage —
        // damage = dense ember burst, knockback = shock ring, spread =
        // debris scatter, speed = a streak back along the approach path
        // (size fattens the base spark above). All pooled VfxSpawner
        // kinds tinted with the recipe's pigment. Thresholded so
        // near-neutral recipes stay on the base FX, and SMG pellets only
        // pay on every third impact so a 12 Hz stream doesn't turn to soup.
        private const float ConcoctionFxThreshold = 0.15f;
        private int _smgExtraCounter;

        private void SpawnConcoctionExtras(in ProjectileSpec spec, Vector3 pos, Vector3 normal)
        {
            const float T = ConcoctionFxThreshold;
            if (!spec.TintImpact) return;
            if (spec.FxDamage < T && spec.FxKnockback < T && spec.FxSpeed < T && spec.FxSpread < T) return;
            if (spec.Kind == ProjectileKind.SmgPellet && (++_smgExtraCounter % 3) != 0) return;

            Color tint = spec.VisualTint;
            if (spec.FxDamage >= T)
                VfxSpawner.Spawn(VfxKind.RamSpark, pos, normal, 0.5f + 0.8f * spec.FxDamage, tint);
            if (spec.FxKnockback >= T)
                VfxSpawner.Spawn(VfxKind.BombShockwave, pos, Quaternion.identity, 0.35f + 0.55f * spec.FxKnockback, tint);
            if (spec.FxSpread >= T)
                VfxSpawner.Spawn(VfxKind.DebrisDust, pos, normal, 0.45f + 0.55f * spec.FxSpread, tint);
            if (spec.FxSpeed >= T && spec.InitialVelocity.sqrMagnitude > 1e-4f)
                VfxSpawner.Spawn(VfxKind.HitSpark, pos, -spec.InitialVelocity.normalized, 0.5f + 0.6f * spec.FxSpeed, tint);
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
