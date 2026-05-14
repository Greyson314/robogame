using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Team-aligned drop-off + score-tick zone. Phase 3 of the
    /// scrap-loop plan: a robot of the matching <see cref="MatchSide"/>
    /// that enters the AOE drains its <see cref="Robot.ScrapHeld"/> into
    /// the depot's <see cref="BankedScrap"/> pool <i>instantly</i>; the
    /// depot then ticks that pool down into the team's
    /// <see cref="MatchController.DepositScrap"/> total at a fixed rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two-stage transfer.</b> Robot → Depot is instant (touch the
    /// pad, drop the load). Depot → Team score is gradual (1 scrap /
    /// sec by default). The gap exists so an enemy raider has time to
    /// pile damage onto a defender mid-bank — the depot becomes a
    /// contested location, not a one-touch dunk.
    /// </para>
    /// <para>
    /// <b>AOE damage (Phase 4).</b> Enemies inside the volume take
    /// damage-over-time (configurable HP/sec). An enemy that dies inside
    /// drops <see cref="ScrapDropper"/>'s loot multiplied by
    /// <see cref="GrinderKillBonus"/> — typically 2× — billed at the
    /// moment of death via <see cref="ArenaController"/>'s death event.
    /// Friendly bots are immune to the depot's grinder.
    /// </para>
    /// <para>
    /// <b>Faction filter.</b> Matches <see cref="Robot.Team"/> against
    /// <see cref="Team"/>. The side-lookup callback wired by
    /// <see cref="ArenaController"/> is still used so neutral entities
    /// (dummies) behave predictably.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ScrapDepot : MonoBehaviour
    {
        [SerializeField] private MatchSide _team = MatchSide.Player;
        [Tooltip("AOE radius. Bumped from 5.5 m → 9 m in session 59 so the depot reads as a real volume you fight over rather than a contact pad. The grinder DPS distributes through this whole sphere; deposit trigger also fires anywhere inside.")]
        [SerializeField, Min(0.5f)] private float _triggerRadius = 9.0f;

        [Header("Score-tick (Phase 3)")]
        [Tooltip("Seconds between each scrap-banked → team-score increment. 1.0 = score climbs at 1 scrap/sec while a deposit sits in the pool.")]
        [SerializeField, Min(0.05f)] private float _scoreTickInterval = 1.0f;

        [Tooltip("Scrap moved from BankedScrap to team total per tick. Increase to drain the pool faster.")]
        [SerializeField, Min(1)] private int _scoreTickAmount = 1;

        [Header("Grinder (Phase 4)")]
        [Tooltip("If true, enemy robots inside the volume take damage-over-time. Set false to disable the grinder hazard while keeping the deposit feature.")]
        [SerializeField] private bool _grinderEnabled = true;

        [Tooltip("HP per second applied to each enemy block in the volume. Distributed via splash damage on the closest block to the depot centre. 50 HP/s ≈ 3–5 s for a small chassis to die. Per SCRAP_LOOP_PLAN § 4.")]
        [SerializeField, Min(0f)] private float _grinderDamagePerSecond = 50f;

        [Tooltip("Seconds between grinder damage applications. Coarser ticks ⇒ fewer splash calls; 0.25 s = 4 Hz which reads as a continuous burn without being expensive.")]
        [SerializeField, Min(0.05f)] private float _grinderDamageInterval = 0.25f;

        [Tooltip("Multiplier applied to the dropped-scrap value of an enemy killed inside this depot's volume. 2× = the 'grinder kill' bonus per SCRAP_LOOP_PLAN § 4.")]
        [SerializeField, Min(1f)] private float _grinderKillBonus = 2f;

        public float GrinderKillBonus => _grinderKillBonus;

        // Procedural visual rig: a recessed "hole" cavity with a raised
        // ring rim flush with the ground, plus a tall column-of-light
        // beam above so the depot is findable from across the arena.
        // Rim is the bright pulsing element; the inner cavity sits ~3 m
        // below ground and reads as a literal pit you drive over.
        // Session 59 rewrite — see commit log for the v2-puck → hole
        // transition.
        private Transform _rimVisual;
        private Renderer _rimRenderer;
        private Transform _wellVisual;
        private Renderer _wellRenderer;
        private Transform _beamVisual;
        private Renderer _beamRenderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int s_emissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        private MatchController _match;
        private System.Func<Robot, MatchSide> _sideLookup;

        // Score-tick state. The depot's "banked" pool persists across
        // player respawns — that's the whole point of the staged transfer.
        private float _scoreTickTimer;
        private int _bankedScrap;

        /// <summary>Scrap waiting to score from this depot. Public so HUDs / tests can poll it.</summary>
        public int BankedScrap => _bankedScrap;

        // Grinder state — set of enemy chassis currently inside the
        // volume. Maintained by OnTriggerEnter/Exit; the per-tick body
        // iterates the set and applies damage. Static-free; cleared on
        // disable.
        private readonly HashSet<Robot> _enemiesInside = new();
        // Set of robots currently inside the volume regardless of side —
        // used so a kill landed inside earns the grinder bonus (the
        // killer themselves doesn't have to be in the volume).
        private static readonly HashSet<Robot> s_robotsInsideAnyDepot = new();

        private float _grinderTickTimer;

        // Pulse animation state.
        private float _spawnTime;
        private Color _baseColor;
        private Color _emitColor;

        /// <summary>The team this depot accepts deposits from.</summary>
        public MatchSide Team => _team;

        /// <summary>Trigger radius in metres. Mostly used by the dev cheats for teleport diagnostics.</summary>
        public float TriggerRadius => _triggerRadius;

        /// <summary>
        /// True when <paramref name="victim"/> was inside any depot's
        /// volume at the moment of this query. ArenaController calls
        /// this on <see cref="Robot.Destroyed"/> to decide whether the
        /// scrap drop earns the grinder-kill bonus.
        /// </summary>
        public static bool IsRobotInsideAnyDepot(Robot victim)
        {
            if (victim == null) return false;
            return s_robotsInsideAnyDepot.Contains(victim);
        }

        /// <summary>
        /// Try to find the depot that <paramref name="victim"/> is
        /// currently inside. Returns the first match (depots don't
        /// overlap in practice). Null when not inside any.
        /// </summary>
        public static ScrapDepot FindDepotContaining(Robot victim)
        {
            if (victim == null) return null;
            for (int i = 0; i < s_allDepots.Count; i++)
            {
                ScrapDepot d = s_allDepots[i];
                if (d == null) continue;
                if (d._enemiesInside.Contains(victim)) return d;
                // The friendlies-in-volume set isn't tracked separately;
                // we just check both. For now any chassis inside the
                // volume counts.
            }
            return null;
        }

        // Registry of every spawned depot — keeps the static lookups
        // above O(depot-count) which is 2 today. SubsystemRegistration
        // reset clears it across domain reloads.
        private static readonly List<ScrapDepot> s_allDepots = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_allDepots.Clear();
            s_robotsInsideAnyDepot.Clear();
        }

        /// <summary>
        /// Wire the depot to a live match controller + a side-lookup
        /// callback. The callback comes from <see cref="ArenaController"/>'s
        /// registration map — given a Robot, return which side it
        /// belongs to.
        /// </summary>
        public void Bind(MatchController match, MatchSide team, System.Func<Robot, MatchSide> sideLookup)
        {
            _match = match;
            _team = team;
            _sideLookup = sideLookup;
            ApplyTeamColor();
        }

        private void Awake()
        {
            _spawnTime = Time.time;
            SphereCollider trig = GetComponent<SphereCollider>();
            if (trig == null) trig = gameObject.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = _triggerRadius;

            BuildVisual();
            ApplyTeamColor();
        }

        private void OnEnable()
        {
            if (!s_allDepots.Contains(this)) s_allDepots.Add(this);
        }

        private void OnDisable()
        {
            s_allDepots.Remove(this);
            // Defensive: don't leak entries when the depot tears down.
            foreach (Robot r in _enemiesInside) s_robotsInsideAnyDepot.Remove(r);
            _enemiesInside.Clear();
        }

        private void Update()
        {
            // Pulse on rim emission — strong sine so the rim throbs as
            // "active / collecting". The well stays steady (dim glow);
            // the beam picks up a separate, faster pulse so it reads as
            // a beacon rather than a static column.
            if (_rimRenderer != null || _beamRenderer != null || _wellRenderer != null)
            {
                float t = Time.time - _spawnTime;
                float rimPulse  = 0.55f + 0.45f * Mathf.Sin(t * 2.2f);
                float beamPulse = 0.45f + 0.55f * Mathf.Sin(t * 3.4f + 1.1f);
                const float wellGlow = 0.30f;
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                PushPulse(_rimRenderer, rimPulse);
                PushPulse(_beamRenderer, beamPulse);
                PushPulse(_wellRenderer, wellGlow);
            }

            TickScoreDrain();
            TickGrinder();
        }

        // -----------------------------------------------------------------
        // Trigger enter / stay / exit
        // -----------------------------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            Robot robot = other.GetComponentInParent<Robot>();
            if (robot == null || robot.IsDestroyed) return;
            // Track every chassis inside any depot for kill-bonus
            // attribution (Phase 4). The robot doesn't have to be on the
            // grinder's team for the kill bonus — what matters is whether
            // the victim died inside a depot's volume.
            s_robotsInsideAnyDepot.Add(robot);

            // Faction-tagged: opposing team chassis are eligible for the
            // grinder hazard. Matching team chassis get the deposit
            // treatment via OnTriggerStay.
            MatchSide side = _sideLookup != null ? _sideLookup(robot) : MatchSide.None;
            if (_grinderEnabled && side != MatchSide.None && side != _team)
            {
                _enemiesInside.Add(robot);
            }
            // Instant transfer for matching-team haulers.
            if (side == _team) TryInstantTransfer(robot);
        }

        private void OnTriggerStay(Collider other)
        {
            // Same gate as Enter, except we just refresh the membership
            // (defensive — Unity can drop the Enter event on certain
            // collider parenting changes). Cheap; HashSet.Add is O(1).
            Robot robot = other.GetComponentInParent<Robot>();
            if (robot == null || robot.IsDestroyed) return;
            s_robotsInsideAnyDepot.Add(robot);
            MatchSide side = _sideLookup != null ? _sideLookup(robot) : MatchSide.None;
            if (_grinderEnabled && side != MatchSide.None && side != _team) _enemiesInside.Add(robot);
            if (side == _team) TryInstantTransfer(robot);
        }

        private void OnTriggerExit(Collider other)
        {
            Robot robot = other.GetComponentInParent<Robot>();
            if (robot == null) return;
            // Only clear the inside-set tracker if no OTHER depot still
            // contains this chassis. With two depots this is just a
            // second-depot check; the helper does it inline.
            _enemiesInside.Remove(robot);
            bool stillInside = false;
            for (int i = 0; i < s_allDepots.Count; i++)
            {
                ScrapDepot d = s_allDepots[i];
                if (d == null || d == this) continue;
                if (d._enemiesInside.Contains(robot)) { stillInside = true; break; }
            }
            if (!stillInside) s_robotsInsideAnyDepot.Remove(robot);
        }

        // -----------------------------------------------------------------
        // Instant transfer (Phase 3)
        // -----------------------------------------------------------------

        private void TryInstantTransfer(Robot robot)
        {
            if (_match == null) return;
            if (robot.ScrapHeld <= 0) return;
            int banked = robot.DepositScrap();
            if (banked <= 0) return;
            _bankedScrap += banked;
            // Cosmetic feedback — fires once per chunk transferred (the
            // staged-into-score ticks don't repeat the VFX; they'd be
            // tinnitus at 1/sec).
            VfxSpawner.Spawn(VfxKind.ScrapBurst, robot.transform.position, Quaternion.identity, 1.4f);
            AudioRouter.PlayOneShot(AudioCue.ScrapCollect, transform.position);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Robogame] ScrapDepot ({_team}) instant-banked {banked} → pending pool {_bankedScrap}.", this);
#endif
        }

        // -----------------------------------------------------------------
        // Score tick (Phase 3)
        // -----------------------------------------------------------------

        private void TickScoreDrain()
        {
            if (_match == null || _bankedScrap <= 0) return;
            _scoreTickTimer += Time.deltaTime;
            if (_scoreTickTimer < _scoreTickInterval) return;
            _scoreTickTimer -= _scoreTickInterval;
            int amount = Mathf.Min(_scoreTickAmount, _bankedScrap);
            if (amount <= 0) return;
            _bankedScrap -= amount;
            _match.DepositScrap(_team, amount);
            // Quiet tick cue — ScrapTick fires per increment; consumers
            // who don't want the pulse can blank the cue's library entry.
            AudioRouter.PlayOneShot(AudioCue.ScrapTick, transform.position);
        }

        // -----------------------------------------------------------------
        // Grinder damage (Phase 4)
        // -----------------------------------------------------------------

        // Single splash profile reused per tick to avoid GC. Three rings
        // matches what MomentumImpactHandler already uses for ramming
        // splash; depot grinder is conceptually a per-block burn.
        private static readonly float[] s_grinderRings = new float[3];

        private void TickGrinder()
        {
            if (!_grinderEnabled || _grinderDamagePerSecond <= 0f) return;
            if (_enemiesInside.Count == 0) return;
            _grinderTickTimer += Time.deltaTime;
            if (_grinderTickTimer < _grinderDamageInterval) return;
            float dt = _grinderTickTimer;
            _grinderTickTimer = 0f;

            float damagePerTick = _grinderDamagePerSecond * dt;
            // Three-ring splash profile concentrates damage at the contact
            // block then falls off. Distributing across rings reads as
            // chassis-level damage without paying per-block in-volume
            // queries; see SCRAP_LOOP_PLAN § 4.
            s_grinderRings[0] = damagePerTick;
            s_grinderRings[1] = damagePerTick * 0.5f;
            s_grinderRings[2] = damagePerTick * 0.25f;

            // Walk the inside set. Tolerant of dead/null entries — we
            // can't modify the set while iterating, so we collect dead
            // refs and prune after.
            _grinderPruneScratch.Clear();
            foreach (Robot enemy in _enemiesInside)
            {
                if (enemy == null || enemy.IsDestroyed) { _grinderPruneScratch.Add(enemy); continue; }
                ApplyGrinderDamage(enemy);
            }
            for (int i = 0; i < _grinderPruneScratch.Count; i++)
            {
                Robot r = _grinderPruneScratch[i];
                _enemiesInside.Remove(r);
                s_robotsInsideAnyDepot.Remove(r);
            }
        }

        private static readonly List<Robot> _grinderPruneScratch = new(8);

        private void ApplyGrinderDamage(Robot enemy)
        {
            BlockGrid grid = enemy.Grid;
            if (grid == null) return;
            // BlockGrid.ApplySplashDamage requires the centre cell to be a
            // live block — it short-circuits otherwise. The depot's world
            // position translated to the enemy's grid coords almost
            // never lands on a live cell (the depot floor sits below /
            // outside the chassis volume), so picking the closest live
            // block to the depot is the reliable seed.
            Vector3Int centre;
            if (!TryFindClosestBlock(grid, transform.position, out centre)) return;
            grid.ApplySplashDamage(centre, s_grinderRings);

            // Light VFX flicker every couple of ticks so the player sees
            // the grinder eating the enemy. Throttled — the audio cue is
            // played once on enter (below) so the AudioRouter pool isn't
            // hammered.
            if (Random.value < 0.35f)
            {
                VfxSpawner.Spawn(VfxKind.HitSpark, enemy.transform.position, Vector3.up, 0.7f);
            }
        }

        // Walk the grid for the live block closest to <paramref name="worldPoint"/>.
        // Small chassis (≤40 blocks) iterate cheaply; the per-tick cost
        // is bounded by the in-volume enemy count × block count, which
        // is well inside budget at 4 Hz.
        private static bool TryFindClosestBlock(BlockGrid grid, Vector3 worldPoint, out Vector3Int gridPos)
        {
            gridPos = default;
            float bestSqr = float.PositiveInfinity;
            bool found = false;
            foreach (var kvp in grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || !b.IsAlive) continue;
                Vector3 pos = b.transform.position;
                float sqr = (pos - worldPoint).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; gridPos = kvp.Key; found = true; }
            }
            return found;
        }

        // -----------------------------------------------------------------
        // Visual + team palette
        // -----------------------------------------------------------------

        private void BuildVisual()
        {
            if (_rimVisual != null) return;

            // Rim: short flat torus-ish ring sitting flush with the
            // ground. We approximate a torus with a wide-flat cylinder
            // because a true torus mesh would need a custom mesh build —
            // overkill for procedural set dressing. The ring's inner
            // cavity is faked by drawing the well cylinder slightly
            // smaller + recessed; from the player's overhead viewpoint
            // it reads as a literal pit you drive into.
            float diameter = _triggerRadius * 2f;
            float rimHeight = 0.45f;
            _rimVisual = MakeDepotChild(
                "ScrapDepotRim",
                PrimitiveType.Cylinder,
                localPos: new Vector3(0f, rimHeight * 0.5f, 0f),
                localScale: new Vector3(diameter, rimHeight, diameter));
            _rimRenderer = _rimVisual.GetComponent<Renderer>();

            // Well: recessed inner cavity. Sits below ground (negative
            // local Y) and is narrower than the rim so the rim reads as
            // an annular collar around the hole. Tinted darker so it
            // reads as depth rather than a solid plate.
            float wellInsetRadius = _triggerRadius * 0.78f;
            float wellInsetDiameter = wellInsetRadius * 2f;
            float wellDepth = 2.4f;
            // Position the well so its top cap sits ~0.1 m below ground.
            // Depot transform y is 0.2 in world (from ArenaController's
            // _playerDepotPosition Y); local y = -1.5 puts the well's
            // top cap at world y ≈ -0.1, bottom cap at world y ≈ -2.5.
            // Anything deeper would clip through the displaced HillsGround
            // mesh at the depot's offset position.
            _wellVisual = MakeDepotChild(
                "ScrapDepotWell",
                PrimitiveType.Cylinder,
                localPos: new Vector3(0f, -wellDepth * 0.5f - 0.3f, 0f),
                localScale: new Vector3(wellInsetDiameter, wellDepth, wellInsetDiameter));
            _wellRenderer = _wellVisual.GetComponent<Renderer>();

            // Beam: tall column of light above the depot so it's findable
            // from across the larger session-59 arena. Slightly thicker
            // than the legacy beam (0.7 m vs 0.4 m) to read against the
            // mountain silhouette ring.
            _beamVisual = MakeDepotChild(
                "ScrapDepotBeam",
                PrimitiveType.Cylinder,
                localPos: new Vector3(0f, 14f, 0f),
                localScale: new Vector3(0.7f, 14f, 0.7f));
            _beamRenderer = _beamVisual.GetComponent<Renderer>();
        }

        /// <summary>
        /// Spawn one decorative child primitive parented under the depot
        /// transform, collider stripped (depots use the SphereCollider on
        /// the depot transform itself for triggers; the visual children
        /// must not register their own contact volumes).
        /// </summary>
        private Transform MakeDepotChild(string name, PrimitiveType prim, Vector3 localPos, Vector3 localScale)
        {
            GameObject go = GameObject.CreatePrimitive(prim);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go.transform;
        }

        private void PushPulse(Renderer r, float strength)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(s_emissionId, _emitColor * strength);
            _mpb.SetColor(s_baseColorId, _baseColor);
            _mpb.SetColor(s_legacyColorId, _baseColor);
            r.SetPropertyBlock(_mpb);
        }

        private void ApplyTeamColor()
        {
            // Per-team palette: bright accent for the player's depot, alert
            // red for the enemy. Multiplied 1.6× into the emit channel so
            // the rim + beam glow against the darker scene ambient.
            (Color baseCol, Color emit) = _team switch
            {
                MatchSide.Player => (new Color(0.95f, 0.55f, 0.10f, 1f), new Color(0.95f, 0.55f, 0.10f, 1f) * 1.8f),
                MatchSide.Enemy  => (new Color(0.85f, 0.20f, 0.20f, 1f), new Color(0.85f, 0.20f, 0.20f, 1f) * 1.8f),
                _                => (new Color(0.55f, 0.55f, 0.60f, 1f), new Color(0.55f, 0.55f, 0.60f, 1f) * 1.0f),
            };
            _baseColor = baseCol;
            _emitColor = emit;
            // Tint each piece — rim + beam carry the bright accent; the
            // well's base reads darker so it suggests depth.
            EnableEmissionFor(_rimRenderer);
            EnableEmissionFor(_beamRenderer);
            EnableEmissionFor(_wellRenderer);
        }

        private static void EnableEmissionFor(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return;
            r.sharedMaterial.EnableKeyword("_EMISSION");
        }

        /// <summary>
        /// Spawn a ScrapDepot at <paramref name="worldPos"/>, bound to
        /// <paramref name="match"/> and <paramref name="team"/>.
        /// </summary>
        public static ScrapDepot CreateProcedural(Vector3 worldPos, Transform parent,
            MatchController match, MatchSide team, System.Func<Robot, MatchSide> sideLookup)
        {
            string n = $"ScrapDepot_{team}";
            GameObject go = new GameObject(n);
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPos;
            ScrapDepot depot = go.AddComponent<ScrapDepot>();
            depot.Bind(match, team, sideLookup);
            return depot;
        }
    }
}
