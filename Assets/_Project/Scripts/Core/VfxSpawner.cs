using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Pooled, allocation-free dispatcher for one-shot procedural VFX
    /// bursts (muzzle flashes, hit sparks, debris dust). One scene-root
    /// singleton, auto-bootstrapped via
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>; call
    /// <see cref="Spawn(VfxKind, Vector3, Quaternion)"/> from anywhere on
    /// the main thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why procedural, not asset prefabs?</b> Per the art direction
    /// (Forbidden List, palette discipline), every VFX must read as a
    /// palette-locked colour and respect the "stylized means cheap" rule.
    /// Bundled CFXR prefabs are great mood references but ship with
    /// off-palette tints and soft particles. Building bursts in code lets
    /// us pin the palette tokens at a single place
    /// (<see cref="RuntimePalette"/>) and tune shape / cadence per
    /// <see cref="VfxKind"/> without spelunking five .prefab files.
    /// </para>
    /// <para>
    /// <b>Performance contract.</b> Steady state: zero allocations per
    /// <see cref="Spawn(VfxKind, Vector3, Quaternion)"/>. Each kind keeps
    /// its own free / live list; the live list is swept once per
    /// <see cref="Update"/> by index (no enumerator). Statics survive
    /// domain reload, so the bootstrap path explicitly resets them.
    /// </para>
    /// <para>
    /// <b>Hard cap per kind.</b> If callers blow past
    /// <c>MaxConcurrentPerKind</c> we silently drop the oldest live
    /// instance and re-use it. That's the right call for a 16-player MP
    /// arena: a sustained-fire SMG must not chew the GC heap with a
    /// thousand muzzle flashes per second.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VfxSpawner : MonoBehaviour
    {
        public const int MaxConcurrentPerKind = 24;

        private static VfxSpawner s_instance;
        private static GameObject s_root;

        // Per-kind pools. Both lists are pre-sized to MaxConcurrentPerKind
        // so the steady-state spawn path is allocation-free; the only
        // GC happens on first-touch of a kind (template + pool warmup).
        // _poolsByKind is the lookup; _poolList is the iteration view —
        // foreach on Dictionary.Values still allocates an enumerator
        // object (PERFORMANCE.md § 2.1), so the per-frame sweep walks
        // the parallel List<KindPool> by index instead.
        private readonly Dictionary<VfxKind, KindPool> _poolsByKind = new(8);
        private readonly List<KindPool> _poolList = new(8);

        private sealed class KindPool
        {
            public ParticleSystem Template;
            public readonly List<Live> Live = new(MaxConcurrentPerKind);
            public readonly Stack<ParticleSystem> Free = new(MaxConcurrentPerKind);
        }

        private struct Live
        {
            public ParticleSystem Ps;
            public float ExpireAt;
        }

        // -----------------------------------------------------------------
        // Bootstrap
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Statics survive domain reload; the GameObject does not.
            s_instance = null;
            s_root = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_root = new GameObject("[VfxSpawner]");
            DontDestroyOnLoad(s_root);
            s_instance = s_root.AddComponent<VfxSpawner>();
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Fire a one-shot burst at <paramref name="position"/> oriented
        /// by <paramref name="rotation"/>. <paramref name="rotation"/>
        /// drives the emission cone direction for kinds that emit
        /// directionally (muzzle flash, hit spark); spherical kinds
        /// (debris dust, bomb shockwave) ignore it.
        /// </summary>
        /// <param name="kind">VFX preset to play.</param>
        /// <param name="position">World-space spawn position.</param>
        /// <param name="rotation">Burst orientation; <c>Quaternion.identity</c> if not directional.</param>
        public static void Spawn(VfxKind kind, Vector3 position, Quaternion rotation)
        {
            EnsureBootstrap();
            if (s_instance == null) return;
            s_instance.SpawnInternal(kind, position, rotation, scale: 1f);
        }

        /// <summary>Convenience: spawn at a forward direction with no roll.</summary>
        public static void Spawn(VfxKind kind, Vector3 position, Vector3 forward)
        {
            Quaternion rot = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            Spawn(kind, position, rot);
        }

        /// <summary>Spawn with a per-call scale multiplier — explosions grow with radius.</summary>
        public static void Spawn(VfxKind kind, Vector3 position, Quaternion rotation, float scale)
        {
            EnsureBootstrap();
            if (s_instance == null) return;
            s_instance.SpawnInternal(kind, position, rotation, Mathf.Max(0.05f, scale));
        }

        /// <summary>Forward + scale convenience — same orientation contract as the (kind, pos, forward) overload.</summary>
        public static void Spawn(VfxKind kind, Vector3 position, Vector3 forward, float scale)
        {
            Quaternion rot = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            Spawn(kind, position, rot, scale);
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private void SpawnInternal(VfxKind kind, Vector3 position, Quaternion rotation, float scale)
        {
            KindPool pool = GetOrCreatePool(kind);
            if (pool == null || pool.Template == null) return;

            ParticleSystem ps = AcquireFromPool(pool);
            Transform t = ps.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = new Vector3(scale, scale, scale);

            ps.gameObject.SetActive(true);
            // Always reset before play — pooled instances inherit state
            // from the previous burst (PlayingState hangs around if the
            // expire-sweep took the instance back early).
            ps.Clear(withChildren: true);
            ps.Play(withChildren: true);

            // Conservative duration estimate: main module duration +
            // longest particle lifetime + a safety margin. Re-compute
            // each spawn so a future tweak to the template is honoured.
            ParticleSystem.MainModule m = ps.main;
            float life = m.startLifetime.mode == ParticleSystemCurveMode.Constant
                ? m.startLifetime.constant
                : Mathf.Max(m.startLifetime.constantMin, m.startLifetime.constantMax);
            float expire = Time.time + m.duration + life + 0.1f;

            pool.Live.Add(new Live { Ps = ps, ExpireAt = expire });
        }

        private KindPool GetOrCreatePool(VfxKind kind)
        {
            if (_poolsByKind.TryGetValue(kind, out KindPool existing)) return existing;
            var pool = new KindPool { Template = BuildKindPrefab(kind) };
            _poolsByKind[kind] = pool;
            _poolList.Add(pool);
            return pool;
        }

        private ParticleSystem AcquireFromPool(KindPool pool)
        {
            if (pool.Free.Count > 0) return pool.Free.Pop();

            // Capped: re-use the oldest live instance instead of
            // allocating beyond MaxConcurrentPerKind.
            if (pool.Live.Count >= MaxConcurrentPerKind)
            {
                Live oldest = pool.Live[0];
                pool.Live.RemoveAt(0);
                ParticleSystem reused = oldest.Ps;
                if (reused != null)
                {
                    reused.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    return reused;
                }
            }

            return InstantiateFromTemplate(pool.Template);
        }

        private ParticleSystem InstantiateFromTemplate(ParticleSystem template)
        {
            ParticleSystem clone = Object.Instantiate(template, transform);
            clone.gameObject.SetActive(false);
            return clone;
        }

        private void Update()
        {
            using var _scope = PerfMarkers.VfxSpawnerUpdate.Auto();

            float now = Time.time;
            // Sweep each pool's live list once. Iterate by index — both
            // pools and live lists — so the per-frame path is
            // allocation-free.
            for (int p = 0; p < _poolList.Count; p++)
            {
                KindPool pool = _poolList[p];
                List<Live> live = pool.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    Live l = live[i];
                    if (l.Ps == null)
                    {
                        live.RemoveAt(i);
                        continue;
                    }
                    if (now >= l.ExpireAt)
                    {
                        l.Ps.gameObject.SetActive(false);
                        pool.Free.Push(l.Ps);
                        live.RemoveAt(i);
                    }
                }
            }
        }

        // -----------------------------------------------------------------
        // Procedural template construction
        // -----------------------------------------------------------------

        private static Material s_unlitMeshMat;
        private static Material s_unlitBillboardMat;

        private static Material UnlitMeshMaterial
        {
            get
            {
                if (s_unlitMeshMat != null) return s_unlitMeshMat;
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Particles/Standard Unlit")
                                ?? Shader.Find("Sprites/Default");
                s_unlitMeshMat = new Material(shader) { name = "VfxMeshUnlit" };
                if (s_unlitMeshMat.HasProperty("_Surface")) s_unlitMeshMat.SetFloat("_Surface", 1f); // transparent
                if (s_unlitMeshMat.HasProperty("_Blend")) s_unlitMeshMat.SetFloat("_Blend", 0f);   // alpha
                if (s_unlitMeshMat.HasProperty("_BaseColor")) s_unlitMeshMat.SetColor("_BaseColor", Color.white);
                if (s_unlitMeshMat.HasProperty("_Color")) s_unlitMeshMat.SetColor("_Color", Color.white);
                return s_unlitMeshMat;
            }
        }

        private static Material UnlitBillboardMaterial
        {
            get
            {
                if (s_unlitBillboardMat != null) return s_unlitBillboardMat;
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Particles/Standard Unlit")
                                ?? Shader.Find("Sprites/Default");
                s_unlitBillboardMat = new Material(shader) { name = "VfxBillboardUnlit" };
                if (s_unlitBillboardMat.HasProperty("_Surface")) s_unlitBillboardMat.SetFloat("_Surface", 1f);
                if (s_unlitBillboardMat.HasProperty("_Blend")) s_unlitBillboardMat.SetFloat("_Blend", 1f); // additive
                if (s_unlitBillboardMat.HasProperty("_BaseColor")) s_unlitBillboardMat.SetColor("_BaseColor", Color.white);
                return s_unlitBillboardMat;
            }
        }

        private static Mesh s_cubeMesh;
        private static Mesh CubeMesh
        {
            get
            {
                if (s_cubeMesh != null) return s_cubeMesh;
                // Built-in primitive — borrow the mesh from a temporary
                // GameObject so we don't ship a custom asset.
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
                return s_cubeMesh;
            }
        }

        private ParticleSystem BuildKindPrefab(VfxKind kind)
        {
            var go = new GameObject($"[VfxTemplate] {kind}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.SetActive(false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            // Stop the auto-play that ParticleSystem triggers on AddComponent.
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // All kinds share these defaults — they're then overridden per-kind.
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            main.scalingMode = ParticleSystemScalingMode.Local; // honour transform.localScale per spawn

            // Disable emission-over-time by default; bursts are the norm.
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            // Disable shape's auto-spread until per-kind config sets it.
            var shape = ps.shape;
            shape.enabled = true;

            ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            switch (kind)
            {
                case VfxKind.MuzzleFlash:    ConfigureMuzzleFlash(ps, rend);   break;
                case VfxKind.HitSpark:       ConfigureHitSpark(ps, rend);      break;
                case VfxKind.RamSpark:       ConfigureRamSpark(ps, rend);      break;
                case VfxKind.BombShockwave:  ConfigureBombShockwave(ps, rend); break;
                case VfxKind.DebrisDust:     ConfigureDebrisDust(ps, rend);    break;
                case VfxKind.FlipBurst:      ConfigureFlipBurst(ps, rend);     break;
                case VfxKind.RepairGlow:     ConfigureRepairGlow(ps, rend);    break;
                case VfxKind.BlockRespawn:   ConfigureBlockRespawn(ps, rend);  break;
                case VfxKind.ScrapBurst:     ConfigureScrapBurst(ps, rend);    break;
            }

            return ps;
        }

        // -----------------------------------------------------------------
        // Per-kind recipes
        // -----------------------------------------------------------------

        // Tactility note: the muzzle flash is intentionally tiny and
        // asymmetric — a hot core + a few sparks shot forward. Big
        // billboards read mushy at the chassis-cube scale.
        private static void ConfigureMuzzleFlash(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.04f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Hazard, RuntimePalette.Caution);
            main.gravityModifier = 0f;
            main.maxParticles = 24;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.05f;
            shape.length = 0.1f;
            shape.rotation = Vector3.zero; // forward = +Z (Cone default)

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Hazard);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Hit spark: 14ish sparks that reflect forward off the surface, with
        // a quick brightness flash. World-space simulation so the chassis it
        // hit can keep moving and the sparks stay where they happened.
        private static void ConfigureHitSpark(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Caution, RuntimePalette.Hazard);
            main.gravityModifier = 0.6f;
            main.maxParticles = 32;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.05f;
            // Hemisphere shape's "forward" is +Z; rotation honoured by
            // VfxSpawner via the spawn's Quaternion. Sparks fly away from
            // the surface the projectile hit.

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Alert);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Ram spark: heavier, slower, with a downward bias so debris
        // settles instead of arcing back up.
        private static void ConfigureRamSpark(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.08f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Hazard, RuntimePalette.Alert);
            main.gravityModifier = 1.2f;
            main.maxParticles = 48;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.SmokeDark);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Bomb shockwave: a fast outward ring + heavy chunky debris.
        // Used to augment the CFXR explosion (which carries the smoke /
        // fireball) with palette-locked fragments.
        private static void ConfigureBombShockwave(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Hazard, RuntimePalette.Caution);
            main.gravityModifier = 1.5f;
            main.maxParticles = 96;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 42) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.SmokeDark);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.x = new ParticleSystem.MinMaxCurve(-360f, 360f);
            rot.y = new ParticleSystem.MinMaxCurve(-360f, 360f);
            rot.z = new ParticleSystem.MinMaxCurve(-360f, 360f);

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Debris dust: slate-coloured slow puff plus a few drifting cube
        // chunks. Plays at the world position of every detached block —
        // doubles as the "this thing just died" tactility marker.
        private static void ConfigureDebrisDust(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.SlateLight, RuntimePalette.DustLight);
            main.gravityModifier = -0.05f; // very slight upward drift
            main.maxParticles = 32;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.DustLight, RuntimePalette.Slate);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            // Dust expands as it rises — flip the curve to grow then fade
            // (size set via separateAxes so we keep it cheap).
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeGrowAndFadeCurve());

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-90f, 90f);

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Flip burst: a quick outward kick at the chassis centre on a
        // self-righting flip. Cyan + cream so it reads as "system kicked
        // in" rather than "something exploded". Spherical shape so the
        // burst is independent of the chassis orientation at the moment
        // of the flip — the player just sees a pulse around their bot.
        private static void ConfigureFlipBurst(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.08f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Cyan, RuntimePalette.HotCore);
            main.gravityModifier = 0f;
            main.maxParticles = 40;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Cyan);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Repair glow: tall cyan/mint streamers rising from the pad
        // centre. Long-ish lifetime so the column reads as a sustained
        // healing field rather than a one-shot burst — the spawner is
        // re-fired by RepairPad on a timer to keep the field continuous
        // across the full rebuild duration.
        private static void ConfigureRepairGlow(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Cyan, RuntimePalette.Mint);
            main.gravityModifier = -0.2f; // very slight upward bias
            main.maxParticles = 96;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 1.5f;
            shape.length = 0.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone points up (world +Y)

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Cyan);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeGrowAndFadeCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Block respawn: tight bright pop at a single block position as
        // it returns from the void during a gradual repair. Short-lived
        // and small so the cadence reads cleanly even when 30 blocks pop
        // back over 10 s (one every ~330 ms).
        private static void ConfigureBlockRespawn(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.HotCore, RuntimePalette.Cyan);
            main.gravityModifier = 0f;
            main.maxParticles = 20;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Cyan);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // Scrap burst: warm hazard-orange pop with a few sparks. Used at
        // both drop and collect — drop call passes a smaller scale, so the
        // same recipe reads as "spawned" vs "vacuumed up" via scale alone.
        private static void ConfigureScrapBurst(ParticleSystem ps, ParticleSystemRenderer rend)
        {
            var main = ps.main;
            main.duration = 0.06f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(RuntimePalette.Hazard, RuntimePalette.Caution);
            main.gravityModifier = 0.4f;
            main.maxParticles = 24;

            var burst = ps.emission;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = MakeFadeOutGradient(RuntimePalette.HotCore, RuntimePalette.Hazard);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, MakeFadeOutCurve());

            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = CubeMesh;
            rend.sharedMaterial = UnlitMeshMaterial;
        }

        // -----------------------------------------------------------------
        // Curve / gradient helpers
        // -----------------------------------------------------------------

        private static Gradient MakeFadeOutGradient(Color start, Color end)
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(start, 0f),
                    new GradientColorKey(end,   1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        private static AnimationCurve MakeFadeOutCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.6f, 0.85f),
                new Keyframe(1f, 0f));
        }

        private static AnimationCurve MakeGrowAndFadeCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.4f, 1.2f),
                new Keyframe(1f, 0f));
        }
    }
}
