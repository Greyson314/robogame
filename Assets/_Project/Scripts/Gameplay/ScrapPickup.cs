using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// World-floating collectible spawned when a chassis is destroyed.
    /// On overlap with a robot trigger, awards <see cref="Value"/> scrap
    /// to the collecting <see cref="Robot"/> and self-destructs. Despawns
    /// after <see cref="_lifetime"/> seconds even if uncollected so an
    /// arena doesn't accumulate orphan pickups across a long match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Foundation, not finished feature.</b> Session 35 lays the
    /// groundwork: a per-Robot <see cref="Robot.ScrapHeld"/> counter, a
    /// <see cref="Robot.ScrapAwarded"/> event, drop-on-death wiring, and
    /// pickup VFX/audio. Future work hooks into ScrapAwarded for
    /// match-score integration, persistent currency, build-mode
    /// purchasing, and similar systems.
    /// </para>
    /// <para>
    /// <b>Visual.</b> Tries <c>Resources.Load&lt;GameObject&gt;("Prefabs/ScrapPickup")</c>
    /// first. If no authored prefab is present at that path the spawner
    /// falls back to a procedural cube with a palette tint, so the
    /// gameplay loop works without any hand-authored asset.
    /// </para>
    /// <para>
    /// <b>Magnetic pickup.</b> Within
    /// <see cref="_magneticRadius"/> the pickup eases toward the nearest
    /// chassis Rigidbody — fixes the precision-driving footgun where
    /// scrap dropped a few cm off a chassis path is uncollectable.
    /// Disabled when zero.
    /// </para>
    /// <para>
    /// <b>Filter.</b> Only chassis with an active
    /// <see cref="Robot"/> component (and not <c>IsDestroyed</c>)
    /// collect. Debris piles, projectile world bodies, and the repair
    /// pad's beacon are all ignored at the trigger callback because
    /// <see cref="Collider.GetComponentInParent"/> returns null.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScrapPickup : MonoBehaviour
    {
        // -----------------------------------------------------------------
        // Asset path (Resources-relative)
        // -----------------------------------------------------------------

        /// <summary>
        /// Resources-relative path the spawner attempts to load. If the
        /// asset is missing the spawner falls back to a procedural
        /// cube. Public so tests can poke at the cache reset path if
        /// needed.
        /// </summary>
        public const string ResourcePath = "Prefabs/ScrapPickup";

        // -----------------------------------------------------------------
        // Tuning (per-instance, runtime-configurable via Configure)
        // -----------------------------------------------------------------

        [Tooltip("Scrap awarded to the chassis that picks this up.")]
        [SerializeField, Min(1)] private int _value = 1;

        [Tooltip("Seconds before this pickup despawns uncollected.")]
        [SerializeField, Min(1f)] private float _lifetime = 30f;

        [Tooltip("Distance at which the pickup begins drifting toward the nearest chassis. " +
                 "Zero disables the magnetic pull entirely.")]
        [SerializeField, Min(0f)] private float _magneticRadius = 4f;

        [Tooltip("Speed the pickup slides toward a chassis when inside the magnetic radius (m/s).")]
        [SerializeField, Min(0f)] private float _magneticPullSpeed = 9f;

        [Tooltip("Spin rate around the world up axis (deg/s) for visual readability.")]
        [SerializeField] private float _spinDegPerSec = 90f;

        [Tooltip("Vertical bob amplitude (m). Gives the pickup a 'this is collectible' read.")]
        [SerializeField, Min(0f)] private float _bobAmplitude = 0.15f;

        [Tooltip("Bob cycle frequency (Hz).")]
        [SerializeField, Min(0f)] private float _bobFrequency = 1.2f;

        [Tooltip("Brief delay after spawn before pickup is enabled. Keeps the killer from " +
                 "vacuuming up scrap they just spawned via residual velocity.")]
        [SerializeField, Min(0f)] private float _armDelay = 0.35f;

        [Header("Gravity-rest (session 100)")]
        [Tooltip("Vertical acceleration (m/s²) pulling airborne scrap toward the ground. " +
                 "Lower than real gravity (9.81) so a kill in the sky drops scrap gently — " +
                 "ground bots get a few seconds to drive over before it lands, but it does " +
                 "land. Set 0 to disable falling entirely (returns to v0 sky-floater behaviour).")]
        [SerializeField, Min(0f)] private float _gravity = 3.5f;

        [Tooltip("Resting altitude above the ground beneath the pickup (metres). The bob " +
                 "amplitude applies on top — slight hover, never sitting on the dirt.")]
        [SerializeField, Min(0f)] private float _restHeightAboveGround = 0.4f;

        [Tooltip("Max distance the ground-probe ray searches downward each frame. Anything " +
                 "beyond this counts as 'open air' and the pickup keeps falling. 80 m covers " +
                 "the highest realistic plane-kill altitude with margin.")]
        [SerializeField, Min(1f)] private float _groundProbeDistance = 80f;

        public int Value => _value;
        public float Lifetime => _lifetime;

        [Tooltip("Seconds between magnetic-trail VFX bursts while being pulled toward a chassis. " +
                 "Lower = denser sparkle, higher = quieter. Set to 0 to disable the trail.")]
        [SerializeField, Min(0f)] private float _trailInterval = 0.15f;

        // Runtime state.
        private Vector3 _anchorPosition; // bob centre — set at spawn, then driven down by gravity-rest
        private float _spawnTime;
        private float _phaseOffset;      // per-instance bob desync so a pile doesn't pulse in unison
        private bool _collected;
        // Time of the next magnet-trail VFX. Refreshed every emit; resets
        // when the pickup leaves the pull range so re-entry pings fresh.
        private float _nextTrailAt;
        private bool _trailLastFrame;
        // Accumulated downward velocity for the reduced-gravity fall.
        // Reset to 0 once the pickup settles at rest height; integrated each
        // Update while airborne.
        private float _fallVel;

        // Search buffer for the magnetic-pull overlap query. Static so
        // multiple pickups share one allocation; per-step usage is fine
        // because OverlapSphereNonAlloc is synchronous.
        private static readonly Collider[] s_overlapBuf = new Collider[16];

        // -----------------------------------------------------------------
        // Public configuration
        // -----------------------------------------------------------------

        /// <summary>Set the scrap value carried by this pickup. Call after Spawn.</summary>
        public void Configure(int value, float lifetime = -1f)
        {
            _value = Mathf.Max(1, value);
            if (lifetime > 0f) _lifetime = lifetime;
        }

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _anchorPosition = transform.position;
            _spawnTime = Time.time;
            _phaseOffset = Random.value * Mathf.PI * 2f;
            EnsureTriggerCollider();
        }

        // The trigger volume is sized for forgiving collection. Spawned
        // procedurally here so a hand-authored prefab without one still
        // works — plus we can override the radius from a single place.
        private void EnsureTriggerCollider()
        {
            SphereCollider trig = null;
            SphereCollider[] existing = GetComponents<SphereCollider>();
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].isTrigger) { trig = existing[i]; break; }
            }
            if (trig == null)
            {
                trig = gameObject.AddComponent<SphereCollider>();
                trig.isTrigger = true;
                trig.radius = 1.6f;
                trig.center = Vector3.zero;
            }
        }

        private void Update()
        {
            if (_collected) return;

            float t = Time.time - _spawnTime;

            // Despawn after lifetime. Kept simple — no fade for v0.
            if (t >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Gravity-rest. Each frame, probe downward to find the resting
            // altitude (ground.y + _restHeightAboveGround). If we're above
            // that, accelerate downward at reduced gravity; once we reach
            // it, snap and zero the fall velocity so the bob curve has a
            // stable centre. Result: a plane-kill at high altitude drops
            // scrap that settles to the ground over a few seconds, which
            // is collectable by wheeled / hover bots.
            ApplyGravityRest(Time.deltaTime);

            // Magnetic pull. After arm delay, find the nearest chassis
            // within radius and drift toward it on the ground plane —
            // gravity-rest already governs Y, so we strip the vertical
            // component to keep scrap from being yanked into the sky by a
            // hovering chassis. We update _anchorPosition (not transform
            // directly) so the bob curve continues to apply on top of the
            // pulled-toward base.
            bool pulling = false;
            if (_magneticRadius > 0f && t >= _armDelay)
            {
                Robot nearest = FindNearestChassis(_anchorPosition, _magneticRadius);
                if (nearest != null)
                {
                    Vector3 toChassis = nearest.transform.position - _anchorPosition;
                    toChassis.y = 0f; // ground-plane pull only
                    float dist = toChassis.magnitude;
                    if (dist > 0.05f)
                    {
                        float step = Mathf.Min(_magneticPullSpeed * Time.deltaTime, dist);
                        _anchorPosition += toChassis.normalized * step;
                        pulling = true;
                    }
                }
            }

            // Sparkle trail while being pulled — visual "this scrap is
            // coming to you" signal after a kill. Uses the pooled
            // VfxSpawner path so a 6-bot frag burst doesn't allocate per
            // spark. Reset the cadence on the leading edge so re-entering
            // pull range pings fresh.
            if (pulling && _trailInterval > 0f)
            {
                if (!_trailLastFrame || Time.time >= _nextTrailAt)
                {
                    VfxSpawner.Spawn(VfxKind.MagnetTrail, transform.position, Quaternion.identity);
                    _nextTrailAt = Time.time + _trailInterval;
                }
            }
            _trailLastFrame = pulling;

            // Bob + spin.
            Vector3 pos = _anchorPosition;
            pos.y += Mathf.Sin((t * _bobFrequency * Mathf.PI * 2f) + _phaseOffset) * _bobAmplitude;
            transform.position = pos;
            transform.Rotate(Vector3.up, _spinDegPerSec * Time.deltaTime, Space.World);
        }

        // Reduced-gravity rest. Cast straight down from a bit above the
        // current anchor (the small offset keeps the cast clear of any
        // collider sitting at the anchor position itself) and aim for
        // ground.y + _restHeightAboveGround. QueryTriggerInteraction.Ignore
        // skips other pickups, depot beacons, and the repair pad's trigger
        // — none of those should act as "ground". A chassis cube IS a
        // valid ground surface for this purpose; scrap landing on a
        // moving chassis just rides it briefly until the trigger collects.
        private void ApplyGravityRest(float dt)
        {
            if (_gravity <= 0f) return; // disabled — return to v0 floater behaviour
            Vector3 probeOrigin = _anchorPosition + Vector3.up * 0.5f;
            if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit,
                    _groundProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                float restY = hit.point.y + _restHeightAboveGround;
                if (_anchorPosition.y > restY + 1e-3f)
                {
                    _fallVel += _gravity * dt;
                    _anchorPosition.y -= _fallVel * dt;
                    if (_anchorPosition.y < restY) { _anchorPosition.y = restY; _fallVel = 0f; }
                }
                else
                {
                    _anchorPosition.y = restY;
                    _fallVel = 0f;
                }
            }
            else
            {
                // No ground within probe range — keep falling.
                _fallVel += _gravity * dt;
                _anchorPosition.y -= _fallVel * dt;
            }
        }

        private static Robot FindNearestChassis(Vector3 worldPos, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(
                worldPos, radius, s_overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            Robot best = null;
            float bestSqr = radius * radius;
            for (int i = 0; i < n; i++)
            {
                Collider c = s_overlapBuf[i];
                if (c == null) continue;
                Robot r = c.GetComponentInParent<Robot>();
                if (r == null || r.IsDestroyed) continue;
                float sqr = (r.transform.position - worldPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = r; }
            }
            return best;
        }

        // -----------------------------------------------------------------
        // Collection
        // -----------------------------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            if (_collected) return;
            if (Time.time - _spawnTime < _armDelay) return;

            Robot collector = other.GetComponentInParent<Robot>();
            if (collector == null || collector.IsDestroyed) return;

            TryCollect(collector);
        }

        // OnTriggerStay covers the case where a robot enters with a full
        // load (refused at the gate) then deposits at a depot WITHOUT
        // leaving the pickup's trigger volume. Once a slot opens up, the
        // pickup gets collected on the next physics tick.
        private void OnTriggerStay(Collider other)
        {
            if (_collected) return;
            if (Time.time - _spawnTime < _armDelay) return;

            Robot collector = other.GetComponentInParent<Robot>();
            if (collector == null || collector.IsDestroyed) return;
            if (collector.IsScrapFull) return;

            TryCollect(collector);
        }

        // Returns true if the pickup was fully banked. Partial-pickup
        // case: pickup banks what fits, reduces its value to the
        // remainder, stays in the world for another robot (or this one,
        // after a depot run). This way a robot with 7/8 capacity walking
        // through a value-3 pickup bands 1 scrap and leaves a value-2
        // pickup behind — never wastes scrap.
        private void TryCollect(Robot collector)
        {
            if (_collected) return;
            int awarded = collector.TryAwardScrap(_value);
            if (awarded <= 0) return; // chassis full — pickup stays
            if (awarded < _value)
            {
                _value -= awarded;
                return;
            }
            _collected = true;

            VfxSpawner.Spawn(VfxKind.ScrapBurst, transform.position, Quaternion.identity, 1f);
            AudioRouter.PlayOneShot(AudioCue.ScrapCollect, transform.position);

            Destroy(gameObject);
        }

        // -----------------------------------------------------------------
        // Static spawner
        // -----------------------------------------------------------------

        // Cached prefab reference so repeated drops don't hit Resources
        // for every scrap. Reset by domain reload via the static-reset
        // pattern the project uses elsewhere.
        private static GameObject s_prefab;
        private static bool s_prefabLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_prefab = null;
            s_prefabLoaded = false;
        }

        /// <summary>
        /// Spawn a scrap pickup at <paramref name="worldPos"/> carrying
        /// <paramref name="value"/> scrap. Uses the authored prefab in
        /// Resources when available, otherwise falls back to a procedural
        /// palette-tinted cube.
        /// </summary>
        public static ScrapPickup Spawn(Vector3 worldPos, int value)
        {
            GameObject prefab = ResolvePrefab();
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, worldPos, Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up));
                go.name = "ScrapPickup";
            }
            else
            {
                go = BuildProcedural(worldPos);
            }

            ScrapPickup pickup = go.GetComponent<ScrapPickup>();
            if (pickup == null) pickup = go.AddComponent<ScrapPickup>();
            pickup.Configure(value);

            VfxSpawner.Spawn(VfxKind.ScrapBurst, worldPos, Quaternion.identity, 0.7f);
            AudioRouter.PlayOneShot(AudioCue.ScrapDrop, worldPos);
            return pickup;
        }

        private static GameObject ResolvePrefab()
        {
            if (s_prefabLoaded) return s_prefab;
            s_prefab = Resources.Load<GameObject>(ResourcePath);
            s_prefabLoaded = true;
            return s_prefab;
        }

        // Procedural fallback: a small palette-tinted cube with emission
        // so the pickup reads against any arena lighting. Mirrors the
        // pattern used by the repair pad (which gets along fine without
        // a hand-authored asset on first run).
        private static GameObject BuildProcedural(Vector3 worldPos)
        {
            var root = new GameObject("ScrapPickup");
            root.transform.position = worldPos;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Collider visualCol = visual.GetComponent<Collider>();
            if (visualCol != null) Object.Destroy(visualCol);

            Renderer r = visual.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material mat = new Material(shader) { name = "ScrapMat" };
                Color baseCol = RuntimePalette.Hazard;       // warm orange — reads as "loot"
                Color emit    = RuntimePalette.Hazard * 1.6f;
                if (mat.HasProperty("_BaseColor"))     mat.SetColor("_BaseColor", baseCol);
                if (mat.HasProperty("_Color"))         mat.SetColor("_Color", baseCol);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emit);
                mat.EnableKeyword("_EMISSION");
                r.sharedMaterial = mat;
            }

            return root;
        }
    }
}
