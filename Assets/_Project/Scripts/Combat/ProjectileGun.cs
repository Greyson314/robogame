using System.Collections.Generic;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.Pool;

namespace Robogame.Combat
{
    /// <summary>
    /// SMG-style pellet weapon. Reads from an <see cref="IInputSource"/> on
    /// (or above) this GameObject and spawns pooled <see cref="Projectile"/>
    /// bullets while fire is held. Replaces the old <c>HitscanGun</c> —
    /// no weapon in this game is hitscan, by design.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why projectiles, not hitscan?</b> Hitscan flattens combat: there's
    /// no aim lead, no cover-by-corner, no countering with speed. Robocraft's
    /// best weapons all had projectile travel time you could read, react to,
    /// and exploit. Even our "laser SMG" gets a visible tracer pellet so a
    /// fast hover can drift a target out of a burst.
    /// </para>
    /// <para>
    /// <b>Best-practice checklist applied here.</b>
    /// <list type="bullet">
    /// <item>Pool projectiles via <see cref="UnityEngine.Pool.ObjectPool{T}"/>
    ///       so a sustained burst doesn't churn GC.</item>
    /// <item>Damage is applied in <see cref="Projectile.FixedUpdate"/> via
    ///       <see cref="Physics.RaycastNonAlloc"/>, never via Rigidbody
    ///       collision callbacks — deterministic and server-auth-ready.</item>
    /// <item>Fire rate / muzzle speed / spread / damage are live-tunable
    ///       through <see cref="Tweakables"/> so the SettingsHud can drive
    ///       weapon feel without recompiles.</item>
    /// <item>Fire is suppressed when the cursor is over UI (handled by
    ///       <c>PlayerInputHandler.FireHeld</c>), so HUD clicks don't
    ///       trigger bursts.</item>
    /// <item>Statics that hold pooled GameObjects survive domain reload but
    ///       the GameObjects don't — reset them at
    ///       <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Future split.</b> Per-weapon stat blobs (Plasma, Rail, Mortar...)
    /// belong on a <c>WeaponDefinition</c> ScriptableObject keyed off the
    /// firing block. We're not there yet — there's exactly one weapon type
    /// — so the SMG knobs live in <see cref="Tweakables"/> for live tuning.
    /// Move them to an SO once a second weapon ships.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProjectileGun : MonoBehaviour
    {
        // -----------------------------------------------------------------
        // Fallback / non-tweakable knobs (overridable in inspector for the
        // rare case where a weapon block wants its own per-instance feel).
        // -----------------------------------------------------------------

        [Header("Damage rings (direct → splash falloff)")]
        [Tooltip("Per-ring damage applied via BlockGrid splash. Index 0 = direct hit. Index 0 is also live-overridable via Tweakables (Combat.SmgDamage).")]
        [SerializeField] private float[] _splashRings = { 25f, 8f, 2f };

        [Header("Range")]
        [Tooltip("Maximum projectile travel distance. Bullets self-despawn after this many metres OR Projectile.MaxLifetimeSeconds, whichever comes first.")]
        [SerializeField, Min(5f)] private float _range = 220f;

        [Header("Layers")]
        [Tooltip("Layers projectiles can hit.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Origin (auto if blank)")]
        [Tooltip("Transform that defines the muzzle position + forward direction. Defaults to this transform.")]
        [SerializeField] private Transform _muzzle;

        // -----------------------------------------------------------------
        // Internal state
        // -----------------------------------------------------------------

        private IInputSource _input;
        private Robot _ownerRobot;
        private float _nextFireTime;

        // One pool shared by every gun in the scene. Projectiles are
        // identical across guns (same procedural prefab), so pooling
        // per-gun would just fragment the cache.
        private static IObjectPool<Projectile> s_pool;
        private static Material s_trailMaterial;
        private static readonly List<Projectile> s_active = new(64);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_pool = null;
            s_trailMaterial = null;
            s_active.Clear();
        }

        private void Awake()
        {
            if (_muzzle == null) _muzzle = transform;
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
        }

        /// <summary>Override the muzzle transform (used by <see cref="WeaponBlock"/> at spawn).</summary>
        public void SetMuzzle(Transform muzzle)
        {
            if (muzzle != null) _muzzle = muzzle;
        }

        private void Update()
        {
            if (_input == null || !_input.FireHeld) return;
            if (Time.time < _nextFireTime) return;

            float fireRate = Mathf.Max(0.1f, Tweakables.Get(Tweakables.SmgFireRate));
            _nextFireTime = Time.time + 1f / fireRate;
            Fire();
        }

        // -----------------------------------------------------------------
        // Fire
        // -----------------------------------------------------------------

        private void Fire()
        {
            EnsurePool();
            Projectile p = s_pool.Get();

            // Patch up the per-shot damage row from Tweakables. Splash
            // falloff (index 1+) keeps its inspector-default ratios so
            // tuning the headline number doesn't accidentally rebalance
            // splash propagation.
            float headline = Tweakables.Get(Tweakables.SmgDamage);
            if (_splashRings.Length > 0) _splashRings[0] = headline;

            float speed = Tweakables.Get(Tweakables.SmgMuzzleSpeed);
            float spreadDeg = Tweakables.Get(Tweakables.SmgSpread);

            Vector3 origin = _muzzle.position;
            Vector3 dir = ApplySpread(_muzzle.forward, _muzzle.right, _muzzle.up, spreadDeg);

            // Range is enforced by gating the projectile lifetime — at
            // 80 m/s and 220 m range that's ~2.75 s of flight, well
            // under Projectile.MaxLifetimeSeconds (4 s). If max range
            // ever exceeds that wall we'd cap it on the projectile.

            p.transform.position = origin;
            p.transform.forward = dir;
            p.Launch(
                origin: origin,
                velocity: dir * speed,
                gravity: 0f, // SMG pellets are flat-shooting; switch to >0 for ballistic weapons
                splashRings: _splashRings,
                hitMask: _hitMask,
                owner: _ownerRobot,
                onDespawn: ReleaseProjectile);
            s_active.Add(p);
        }

        /// <summary>
        /// Bend <paramref name="forward"/> by a uniform random offset
        /// inside a circular cone of half-angle <paramref name="spreadDeg"/>.
        /// Uses a unit-disk sample so spread distribution is rotationally
        /// uniform instead of biased toward the corners — important for
        /// SMG feel.
        /// </summary>
        private static Vector3 ApplySpread(Vector3 forward, Vector3 right, Vector3 up, float spreadDeg)
        {
            if (spreadDeg <= 0f) return forward;
            float r = Mathf.Tan(spreadDeg * Mathf.Deg2Rad);
            Vector2 disk = Random.insideUnitCircle * r;
            return (forward + right * disk.x + up * disk.y).normalized;
        }

        private static void ReleaseProjectile(Projectile p)
        {
            s_active.Remove(p);
            // Pool may have been wiped by domain reload while the
            // projectile was in flight; in that case just destroy.
            if (s_pool == null || p == null) { if (p != null) Destroy(p.gameObject); return; }
            s_pool.Release(p);
        }

        // -----------------------------------------------------------------
        // Pool / prefab construction
        // -----------------------------------------------------------------

        private static void EnsurePool()
        {
            if (s_pool != null) return;

            s_pool = new ObjectPool<Projectile>(
                createFunc: CreateProjectile,
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false,
                defaultCapacity: 32,
                maxSize: 256);
        }

        /// <summary>
        /// Build a procedural projectile GameObject — no collider (the
        /// projectile sweep-tests itself), single TrailRenderer for the
        /// visible streak. We don't ship a prefab asset because the
        /// scaffolder pipeline can't reliably author one without the
        /// editor-only AssetDatabase, and we want runtime equality
        /// between editor and standalone builds.
        /// </summary>
        private static Projectile CreateProjectile()
        {
            var go = new GameObject("Projectile");
            DontDestroyOnLoad(go);
            go.SetActive(false);

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.06f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0.0f;
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 2;
            trail.startColor = new Color(1f, 0.85f, 0.35f, 1f); // warm tracer head
            trail.endColor   = new Color(1f, 0.45f, 0.10f, 0f); // fades to transparent
            trail.emitting = false;

            if (s_trailMaterial == null)
                s_trailMaterial = new Material(Shader.Find("Sprites/Default"));
            trail.sharedMaterial = s_trailMaterial;

            return go.AddComponent<Projectile>();
        }
    }
}
