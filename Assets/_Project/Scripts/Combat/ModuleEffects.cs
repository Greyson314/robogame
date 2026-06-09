using System.Collections.Generic;
using Robogame.Block;
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

        /// <summary>Radius (m) within which a Repair pulse mends the chassis's own blocks.</summary>
        private const float RepairRadius = 8f;

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
        /// Mend the owner's own still-alive blocks within <see cref="RepairRadius"/>
        /// of <paramref name="center"/> by <paramref name="healPerBlock"/> HP each
        /// (clamped to each block's max). Field self-repair: the repair PAD
        /// rebuilds DESTROYED blocks at base, this tops up DAMAGED ones mid-fight.
        /// Owner-only — no enemy or ally effect. Returns total HP restored (so the
        /// caller can gate VFX on a non-empty pulse). Healing never removes a
        /// block, so iterating the live grid here is safe (unlike the splash-damage
        /// path). Server-side mutation like the rest of this class.
        /// </summary>
        public static float RepairPulse(Robot owner, Vector3 center, float healPerBlock)
        {
            if (owner == null || owner.Grid == null || healPerBlock <= 0f) return 0f;
            float r2 = RepairRadius * RepairRadius;
            float total = 0f;
            foreach (KeyValuePair<Vector3Int, BlockBehaviour> kv in owner.Grid.Blocks)
            {
                BlockBehaviour b = kv.Value;
                if (b == null || !b.IsAlive) continue;
                if ((b.transform.position - center).sqrMagnitude > r2) continue;
                total += b.Heal(healPerBlock);
            }
            return total;
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
                // TRACE[AUDIT-15]: skip teammates, not just the owner (EMP used to disable allies)
                if (robot == null || robot == owner || Teams.IsFriendlyFire(owner, robot)) continue;
                if (!s_seen.Add(robot)) continue; // one pass per robot

                foreach (ProjectileGun gun in robot.GetComponentsInChildren<ProjectileGun>(includeInactive: true))
                {
                    if (gun == null || !gun.enabled) continue;
                    EmpDisable.Apply(gun, duration);
                }
            }
        }

        /// <summary>
        /// Forward burst of speed (afterburner): an instant velocity kick of
        /// <paramref name="deltaV"/> m/s along <paramref name="dir"/>. Replaces
        /// the old Blink teleport (session 120 — "blink is cringe"). Uses
        /// VelocityChange so the kick is mass-independent — a heavy and a light
        /// bot get the same lurch forward. Adds to current velocity, so chaining
        /// it while already moving stacks speed (intended "boost" feel).
        /// </summary>
        public static void SpeedBurst(Rigidbody rb, Vector3 dir, float deltaV)
        {
            if (rb == null) return;
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            rb.AddForce(d * deltaV, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Deploy a detached projectile-blocking dome on the ground at the
        /// chassis's current position. Stays put (does not follow), blocks
        /// every projectile that passes through it (cover for both sides), but
        /// vehicles drive straight through. Lasts <paramref name="duration"/>s.
        /// </summary>
        public static void DiscShield(Robot owner, float radius, float duration)
        {
            if (owner == null) return;
            ShieldBubble.Spawn(owner.transform.position, radius, duration);
        }

        /// <summary>
        /// Drop a proximity mine on the ground beneath the chassis. It arms
        /// after a short delay, then detonates one physics-tick after an enemy
        /// drives into its trigger radius. <paramref name="damage"/> is the
        /// resolved module power (centre HP); <paramref name="lifetime"/> is
        /// how long it sits before self-expiring. Active mines per owner are
        /// capped (oldest trims out). No friendly fire; the deployer is immune.
        /// </summary>
        public static void DeployMine(Robot owner, float damage, float lifetime)
        {
            if (owner == null) return;

            Rigidbody rb = owner.Rigidbody;
            Vector3 from = rb != null ? rb.worldCenterOfMass : owner.transform.position;

            // World-down along gravity (planet-aware), so the mine lands on the
            // surface even on spherical arenas.
            Vector3 g = GravityField.SampleAt(from);
            Vector3 down = g.sqrMagnitude > 1e-4f ? g.normalized : Vector3.down;

            // Start the ground ray just below the hull so we don't latch onto
            // the chassis's own underside collider.
            Vector3 start = from + down * 1.5f;
            Vector3 pos, up;
            if (Physics.Raycast(start, down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Robot>() != owner)
            {
                pos = hit.point + hit.normal * 0.06f; // rest just above the surface
                up = hit.normal;
            }
            else
            {
                pos = start;        // airborne deploy — drop it where the hull was
                up = -down;
            }

            DeployedMine.Spawn(owner, pos, up, damage, lifetime);
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
    /// A detached, world-anchored dome that blocks projectiles but lets
    /// vehicles drive through. Deployed on the ground where the chassis stood,
    /// it stays put for its lifetime. The blocking is a raycast fact, not a
    /// physical one: its non-trigger collider sits on the projectile-
    /// transparent <see cref="ShieldLayer"/> name — projectile sweeps
    /// (<c>Physics.SphereCast</c> against <c>~0</c>) hit it, but its layer's
    /// physics-collision matrix is cleared so no Rigidbody ever contacts it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShieldBubble : MonoBehaviour
    {
        /// <summary>Named layer (TagManager index 8) for drive-through-but-shoot-blocking colliders.</summary>
        public const string ShieldLayer = "ShieldField";
        private float _expireAt;

        internal static void Spawn(Vector3 worldPos, float radius, float duration)
        {
            EnsureLayerIsCollisionFree();

            var go = new GameObject("[ShieldDome]");
            go.transform.position = worldPos; // detached — stays where deployed
            go.layer = ResolveShieldLayer();

            // No Rigidbody: a static non-trigger collider is raycast-hittable
            // (projectiles stop on it) and, with its layer's collision matrix
            // cleared, produces zero physical contacts (vehicles pass through).
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = false;

            BuildVisual(go.transform, radius);

            var bubble = go.AddComponent<ShieldBubble>();
            bubble._expireAt = Time.time + duration;
        }

        private void Update()
        {
            if (Time.time >= _expireAt) Destroy(gameObject);
        }

        // -----------------------------------------------------------------
        // Layer plumbing: resolve the ShieldField layer and (once) clear its
        // physics-collision matrix so nothing physically collides with the
        // dome — raycasts ignore the matrix, so projectiles still stop on it.
        // -----------------------------------------------------------------
        private const int FallbackShieldLayer = 8;
        private static bool s_matrixCleared;

        private static int ResolveShieldLayer()
        {
            int l = LayerMask.NameToLayer(ShieldLayer);
            return l >= 0 ? l : FallbackShieldLayer;
        }

        private static void EnsureLayerIsCollisionFree()
        {
            if (s_matrixCleared) return;
            int shield = ResolveShieldLayer();
            for (int other = 0; other < 32; other++)
                Physics.IgnoreLayerCollision(shield, other, true);
            s_matrixCleared = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Statics survive domain reload; the physics matrix resets on play
            // start, so re-clear it lazily on the next deploy.
            s_material = null;
            s_matrixCleared = false;
        }

        private static Material s_material;

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
                Color c = RuntimePalette.Cyan;
                c.a = 0.18f; // faint translucent dome, not an opaque ball
                s_material = RuntimeMaterials.UnlitTransparent(c);
                s_material.name = "ShieldDomeMat";
                return s_material;
            }
        }
    }

    /// <summary>
    /// A detached proximity mine resting on the ground. State machine:
    /// <c>Arming</c> (red tell, can't trigger) → <c>Armed</c> (steady amber,
    /// watching) → on an enemy entering the trigger radius, a one-physics-tick
    /// fuse → <c>Detonate</c> (area-splash damage + explosive knockback via
    /// <see cref="ProjectileWorld.Detonate"/>). The deployer and its teammates
    /// are spared (no friendly fire). Self-expires after its lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Visible but subtle</b> (design brief): a small dark disc with a tiny
    /// state-coloured glow dot — readable if you're looking at the ground, not
    /// from across the arena at speed.
    /// </para>
    /// <para>
    /// No collider: proximity is a per-tick <see cref="Physics.OverlapSphereNonAlloc"/>
    /// from the mine against the shared buffer, so the mine never physically
    /// blocks a bot and adds zero contact-solver cost. Mine TYPES later: pass a
    /// profile to <see cref="Spawn"/> instead of the constants below.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DeployedMine : MonoBehaviour
    {
        // One mine type today. Future types pass these as a profile.
        private const float ArmDelay = 1.2f;          // settle + tell before live
        private const float TriggerRadius = 2.2f;     // an enemy must basically drive over it
        private const float SplashRadius = 7f;        // detonation damage radius
        private const float KnockbackImpulse = 45f;   // explosive knockback at the blast
        private const int   MaxActivePerOwner = 3;    // older mines trim out at the cap

        private static readonly Color s_armingColor = new Color(0.85f, 0.12f, 0.10f); // red while arming
        private static readonly Color s_armedColor  = new Color(0.95f, 0.62f, 0.10f); // amber when live

        private static readonly Collider[] s_overlap = new Collider[32];
        private static readonly List<DeployedMine> s_active = new(16);
        private static Material s_glowMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active.Clear();   // statics survive domain reload; deployed GameObjects don't
            s_glowMaterial = null;
        }

        private Robot _owner;
        private float _damage;
        private float _armAt;
        private float _expireAt;
        private bool _armed;
        private bool _detonatePending;
        private Renderer _glow;

        internal static DeployedMine Spawn(Robot owner, Vector3 pos, Vector3 up, float damage, float lifetime)
        {
            var go = new GameObject("[Mine]");
            go.transform.position = pos;
            if (up.sqrMagnitude > 1e-4f) go.transform.up = up; // lie flat on the surface

            var mine = go.AddComponent<DeployedMine>();
            mine._owner = owner;
            mine._damage = damage;
            mine._armAt = Time.time + ArmDelay;
            mine._expireAt = Time.time + Mathf.Max(ArmDelay + 1f, lifetime);
            mine.BuildVisual();
            mine.SetGlow(s_armingColor);

            s_active.Add(mine);
            TrimToCap(owner);
            return mine;
        }

        // Keep at most MaxActivePerOwner mines per deployer; destroy the oldest
        // (front of the list) beyond the cap.
        private static void TrimToCap(Robot owner)
        {
            int count = 0;
            for (int i = 0; i < s_active.Count; i++)
                if (s_active[i] != null && s_active[i]._owner == owner) count++;
            while (count > MaxActivePerOwner)
            {
                for (int i = 0; i < s_active.Count; i++)
                {
                    DeployedMine m = s_active[i];
                    if (m != null && m._owner == owner)
                    {
                        s_active.RemoveAt(i);
                        Destroy(m.gameObject);
                        count--;
                        break;
                    }
                }
            }
        }

        private void OnDestroy() => s_active.Remove(this);

        private void FixedUpdate()
        {
            // One-tick fuse: an enemy was detected last tick → boom now. This
            // tiny gap is the "oh no" beat the design asks for, and cleanly
            // separates the trigger event from the explosion in the server log.
            if (_detonatePending) { Detonate(); return; }

            if (Time.time >= _expireAt) { Destroy(gameObject); return; }

            if (!_armed)
            {
                if (Time.time >= _armAt) { _armed = true; SetGlow(s_armedColor); }
                return;
            }

            if (EnemyInRange()) _detonatePending = true;
        }

        private bool EnemyInRange()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, TriggerRadius, s_overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = s_overlap[i];
                if (c == null) continue;
                Robot r = c.GetComponentInParent<Robot>();
                if (r == null || r == _owner) continue;
                if (IsFriendly(_owner, r)) continue;   // teammates don't set it off
                return true;
            }
            return false;
        }

        // Mirrors ProjectileWorld.IsFriendlyFire: neutral teams are always
        // hostile so dev-sandbox dummies still trip mines.
        private static bool IsFriendly(Robot a, Robot b)
        {
            if (a == null || b == null) return false;
            if (a.Team == TeamId.None || b.Team == TeamId.None) return false;
            return a.Team == b.Team;
        }

        private void Detonate()
        {
            // Owner + teammates spared by ProjectileWorld's friendly-fire rules.
            ProjectileWorld.Detonate(transform.position, SplashRadius, _damage, KnockbackImpulse,
                _owner, ~0, AudioCue.BombExplosion);
            Destroy(gameObject);
        }

        // -----------------------------------------------------------------
        // Visual: small dark disc + a state-coloured glow dot
        // -----------------------------------------------------------------

        private void BuildVisual()
        {
            // Flat disc body — a squashed cylinder. Strip its auto collider so
            // the mine never physically interacts; detection is the overlap query.
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Collider dc = disc.GetComponent<Collider>();
            if (dc != null) Destroy(dc);
            disc.transform.SetParent(transform, worldPositionStays: false);
            // Session 120 playtest: mines were almost invisible — bigger,
            // lighter disc so it reads against dark ground.
            disc.transform.localScale = new Vector3(0.85f, 0.06f, 0.85f);
            Tint(disc.GetComponent<Renderer>(), new Color(0.16f, 0.17f, 0.20f));

            // Glow dot on top — the blinking arming→armed tell, now larger.
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider sc = dot.GetComponent<Collider>();
            if (sc != null) Destroy(sc);
            dot.transform.SetParent(transform, worldPositionStays: false);
            dot.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            dot.transform.localScale = Vector3.one * 0.26f;
            _glow = dot.GetComponent<Renderer>();
            if (_glow != null)
            {
                if (s_glowMaterial == null) s_glowMaterial = RuntimeMaterials.UnlitTransparent(Color.white);
                _glow.sharedMaterial = s_glowMaterial;       // colour comes from the per-mine MPB
                _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _glow.receiveShadows = false;
            }

            // Tall amber beacon so the mine reads from a distance and from
            // above, not just when you're standing on it (playtest, session 120).
            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Collider bc = beacon.GetComponent<Collider>();
            if (bc != null) Destroy(bc);
            beacon.transform.SetParent(transform, worldPositionStays: false);
            beacon.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            beacon.transform.localScale = new Vector3(0.06f, 0.45f, 0.06f);
            Renderer br = beacon.GetComponent<Renderer>();
            if (br != null)
            {
                if (s_glowMaterial == null) s_glowMaterial = RuntimeMaterials.UnlitTransparent(Color.white);
                br.sharedMaterial = s_glowMaterial;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                br.receiveShadows = false;
                Tint(br, new Color(0.95f, 0.62f, 0.10f));
            }
        }

        private void SetGlow(Color color)
        {
            if (_glow == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            _glow.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), color);
            mpb.SetColor(Shader.PropertyToID("_Color"), color);
            _glow.SetPropertyBlock(mpb);
        }

        private static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), color);
            mpb.SetColor(Shader.PropertyToID("_Color"), color);
            r.SetPropertyBlock(mpb);
        }
    }
}
