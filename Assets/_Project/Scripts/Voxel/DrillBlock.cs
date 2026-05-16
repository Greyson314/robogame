using Robogame.Core;
using Robogame.Input;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// A drill block. When its collider contacts a <see cref="DigChunk"/>,
    /// it emits a <see cref="BrushKind.CapsuleSubtract"/> swept from the
    /// drill tip's previous-tick world position to the current one, with
    /// a constant radius. Carves voxel terrain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 3b — the first gameplay-visible terraforming feature. The
    /// drill is conceptually a "tip-block sibling to HookBlock / MaceBlock"
    /// per TERRAFORMING_PLAN §12, but for Phase 3b we ship a minimal
    /// standalone MonoBehaviour without the rope-adoption plumbing —
    /// the rope path can be layered on later by mirroring TipBlock's
    /// attach/forwarder pattern.
    /// </para>
    /// <para>
    /// Emission rate is gated by <see cref="_emitInterval"/>: even if
    /// Unity fires <c>OnCollisionStay</c> every physics tick (50 Hz at
    /// the default fixed timestep), the drill only emits one brush per
    /// interval. Default 0.033 s ≈ 30 Hz, matching TERRAFORMING_PLAN
    /// §4's drill-tick rate.
    /// </para>
    /// <para>
    /// Audio: <see cref="AudioCue.DrillContact"/> on each emit. VFX:
    /// <see cref="VfxKind.DebrisDust"/> at the drill tip. The audio cue
    /// is new in the catalogue; the library entry is left to the audio
    /// pass to author (per the AUDIO_PLAN.md missing-cue logging path).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DrillBlock : MonoBehaviour
    {
        [Tooltip("Drill-bit radius in metres. Defines the carved tunnel's cross-section. " +
                 "Must be wide enough that the chassis fits through what gets carved — " +
                 "for a 3×3 chassis (≈2.5m incl. wheels) 3.0m gives a 6m diameter tunnel " +
                 "with comfortable margin. NOTE: brush is currently a sphere so depth = " +
                 "width; a true anisotropic-brush kind is a future ask (see handoff).")]
        [SerializeField, Min(0.05f)] private float _radius = 3.0f;

        [Tooltip("How far past the cell center the audio/VFX anchor sits, in metres, along the aim direction. " +
                 "Cosmetic only — gives the per-strike DrillContact cue and DebrisDust VFX a sensible " +
                 "position near the drill bit. The brush itself extends much further (see _brushReach).")]
        [SerializeField, Min(0f)] private float _tipForwardOffset = 0.6f;

        [Tooltip("Axial length of the brush capsule along the aim direction, in metres. The capsule's " +
                 "p0 is the drill cell, p1 is the drill cell + aim * _brushReach. Higher values reach " +
                 "further ahead and bite more uncarved material per tick (good for fast tunnelling); " +
                 "lower values keep the carve tight around the bot so it doesn't over-excavate. Even " +
                 "below _radius there's still an axial bias from the segment between the caps. Default " +
                 "2.5m with _radius = 3m carves a snug tunnel that hugs the chassis.")]
        [SerializeField, Min(0.1f)] private float _brushReach = 2.5f;

        [Tooltip("Minimum seconds between emitted brush ops. 0.033 ≈ 30 Hz, matching the design's drill tick rate.")]
        [SerializeField, Min(0.005f)] private float _emitInterval = 0.033f;

        [Header("Dig pull")]
        [Tooltip("Force (N) pulling the chassis along the aim direction whenever the drill is " +
                 "actively carving material (changed > 0). Applied at the drill cell, so a " +
                 "front-mounted (off-COM) drill also yaws/pitches the chassis to follow the dig " +
                 "direction — that's the 'worm through terrain' feel: look up, the bit bites, the " +
                 "body gets dragged up after it. Only applies while biting solid voxels, never " +
                 "while drilling air, so it can't double as a free thruster. 0 disables the pull. " +
                 "This is the CEILING on the servo (see _digTargetSpeed) — the pull tapers below " +
                 "this as the bot approaches the target dig speed, so it never accelerates without " +
                 "bound. Weight opposing the aim is cancelled separately (full gravity comp), so " +
                 "this only has to provide the slow crawl + beat wheel friction, not lift the " +
                 "chassis's whole weight.")]
        [SerializeField, Min(0f)] private float _digPullForce = 1500f;

        [Tooltip("Target speed (m/s) the dig-pull servos the chassis toward along the aim/" +
                 "tunnel axis. Two-sided: while drilling, the drill regulates your tunnel-axis " +
                 "speed to this value — pushing if slower, braking if faster (both capped at " +
                 "_digPullForce). So digging is always a slow deliberate crawl regardless of how " +
                 "fast you were moving when you started, and releasing fire frees full drive " +
                 "speed again. Keep this notably below drive speed so tunnelling reads as the " +
                 "slow option. Only the aim-axis speed is regulated; motion perpendicular to the " +
                 "tunnel (e.g. lateral drift) is untouched.")]
        [SerializeField, Min(0.1f)] private float _digTargetSpeed = 2.0f;

        [Header("Cone aim")]
        [Tooltip("Maximum angle in degrees the drill bit can swivel away from its mount-up direction. " +
                 "50° lets a forward-mounted drill dig nearly straight down or up — at 30° drilling " +
                 "either direction was effectively impossible because the cone bottomed out before the " +
                 "camera reached the floor. The brush emits along the AIMED direction, so the player " +
                 "can look at the ground and drill toward it.")]
        [SerializeField, Range(0f, 90f)] private float _maxAimAngle = 50f;

        [Tooltip("World-space length of the cone visual extending past the drill cell. " +
                 "Cosmetic; brush emission length along aim is _brushReach.")]
        [SerializeField, Min(0.05f)] private float _coneVisualLength = 1.0f;

        [Tooltip("World-space base radius of the cone visual. Cosmetic only.")]
        [SerializeField, Min(0.05f)] private float _coneVisualRadius = 0.22f;

        [Tooltip("Camera used as the aim source. Falls back to Camera.main if unassigned.")]
        [SerializeField] private Camera _aimCamera;

        [Header("Diagnostics")]
        [Tooltip("Draw a live on-screen readout of the drill physics state (aim, carve, " +
                 "force, velocity) so dig-feel problems can be diagnosed from real gameplay " +
                 "instead of guessed at. TEMPORARILY defaulted ON for the up-dig diagnosis — " +
                 "revert to false (off in shipping) once the readout has been captured.")]
        [SerializeField] private bool _debugReadout = true;

        // Last-tick diagnostic snapshot, populated by Drill()/ApplyDigPull
        // and rendered by OnGUI when _debugReadout is on. Pure
        // instrumentation — never read by gameplay code.
        private struct DigDiag
        {
            public float Time;          // Time.time of the snapshot
            public int LastChanged;     // cells carved on the last emit
            public Vector3 AimDir;      // world-space aim
            public float ElevationDeg;  // aim angle above horizontal
            public bool HaveBody;
            public bool Kinematic;
            public bool UseGravity;
            public float Mass;
            public float VAlong;        // chassis speed along aim
            public float GravCompN;     // gravity-comp force this step
            public float ServoN;        // servo force this step
            public Vector3 Velocity;    // chassis linearVelocity
            public float ChassisY;      // chassis world Y (is it rising?)
            public bool PullArmed;      // Time.time <= _pullActiveUntil
        }
        private DigDiag _diag;

        private float _lastEmitTime = float.NegativeInfinity;
        // Dig-pull is applied every FixedUpdate while a recent carve is
        // still "live", NOT only on the ~30 Hz emit tick. Decoupling the
        // pull from the emit rate is load-bearing: gravity acts every
        // physics step (50 Hz), so applying the pull at 30 Hz attenuated
        // the average force ~40 % and the servo (whose math assumes
        // per-step application) could no longer out-muscle gravity to
        // climb. Drill() refreshes _pullActiveUntil + _pullAimDir on each
        // carving emit; FixedUpdate applies the servo until it lapses.
        private float _pullActiveUntil = float.NegativeInfinity;
        private Vector3 _pullAimDir = Vector3.up;
        // Player input gate — drilling is held-fire continuous (mirrors the
        // BombBay / Cannon / ProjectileGun input pattern). The contact and
        // FixedUpdate auto-poll paths both check this; the public Drill(zone)
        // method does NOT, so tests + scripted callers can fire deterministically.
        private IInputSource _input;
        // Chassis Rigidbody the dig-pull pushes. Lazily resolved (mirrors
        // _input) because the drill block may be AddComponent'd before
        // it's parented under the chassis root that owns the Rigidbody.
        // Single Rigidbody per chassis (PHYSICS_PLAN § 1.4) — this is
        // always the chassis body, never a per-block one.
        private Rigidbody _rb;
        // Looped AudioCue.DrillActive while FireHeld — the "motor spinning"
        // bed that the per-emit AudioCue.DrillContact "bite" cue plays over.
        // Owned by the block, started on FireHeld true → false → true
        // edges and stopped on the opposite edge (or OnDisable).
        private AudioLoopHandle _activeLoop = AudioLoopHandle.Invalid;
        private bool _loopActive;

        // Cone visual + aim — see _maxAimAngle, _coneVisualLength,
        // _coneVisualRadius. The bit is a procedural cone child whose
        // localRotation is updated each LateUpdate from the aim camera's
        // forward direction (clamped to the cone). Drill() uses the
        // bit's local +Y in world space to project the brush capsule
        // along the aimed direction (not the cell's static mount-up).
        private Transform _bitTransform;

        private void Awake()
        {
            _input = GetComponentInParent<IInputSource>();
            BuildConeBit();
        }

        private void BuildConeBit()
        {
            var bitGo = new GameObject("DrillBit");
            bitGo.transform.SetParent(transform, worldPositionStays: false);
            // Sit the cone's base at the cube's top face (parent-local
            // y=0.5 — Unity primitive cubes are ±0.5 around origin). The
            // cell's localScale is the dig-zone cell size (0.5m by
            // default), so compensate so the cone reads at its serialized
            // world dimensions regardless of cell scale.
            float invParentScale = transform.lossyScale.y > 0.001f
                ? 1f / transform.lossyScale.y
                : 1f;
            bitGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            bitGo.transform.localScale = Vector3.one * invParentScale;

            var mf = bitGo.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildConeMesh(_coneVisualRadius, _coneVisualLength, segments: 12);

            var mr = bitGo.AddComponent<MeshRenderer>();
            var ownMr = GetComponent<MeshRenderer>();
            if (ownMr != null && ownMr.sharedMaterial != null)
                mr.sharedMaterial = ownMr.sharedMaterial;

            _bitTransform = bitGo.transform;
        }

        private static Mesh BuildConeMesh(float baseRadius, float length, int segments)
        {
            var mesh = new Mesh { name = "DrillBitCone" };
            var verts = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                verts[i] = new Vector3(Mathf.Cos(a) * baseRadius, 0f, Mathf.Sin(a) * baseRadius);
            }
            // Apex along +Y so the cone's "point" tracks the cell's
            // mount-up by default; aim rotation reorients from there.
            verts[segments] = new Vector3(0f, length, 0f);
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[i * 3 + 0] = segments;
                tris[i * 3 + 1] = i;
                tris[i * 3 + 2] = next;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDisable()
        {
            // Defensive: a block destroyed mid-drill must not leave the
            // active-spin loop running on a dead GameObject.
            StopActiveLoop();
        }

        private void Update()
        {
            // Pure audio bookkeeping — start / stop the spin loop on the
            // FireHeld edges. FixedUpdate's drilling path is independent.
            bool nowHeld = IsFireHeld;
            if (nowHeld && !_loopActive)
            {
                _activeLoop = AudioRouter.PlayLoop(AudioCue.DrillActive, transform);
                _loopActive = true;
            }
            else if (!nowHeld && _loopActive)
            {
                StopActiveLoop();
            }
        }

        private void StopActiveLoop()
        {
            if (_activeLoop.IsValid) _activeLoop.Stop();
            _activeLoop = AudioLoopHandle.Invalid;
            _loopActive = false;
        }

        /// <summary>
        /// World-space anchor for per-strike audio + a passable point of
        /// reference for tests that exercise "tip projects past the cell
        /// center along aim." The direction follows the aimed cone bit's
        /// local +Y (camera-clamped within <see cref="_maxAimAngle"/>°,
        /// or the cell's mount-up when no aim camera is present), and the
        /// offset is <see cref="_tipForwardOffset"/> metres — small, just
        /// enough to seat the audio source near the drill bit rather than
        /// at the cell center. The brush itself extends much further; see
        /// <see cref="Drill"/> and <see cref="_brushReach"/>.
        /// </summary>
        public Vector3 TipWorldPosition
        {
            get
            {
                Vector3 aimUp = _bitTransform != null ? _bitTransform.up : transform.up;
                return transform.position + aimUp * _tipForwardOffset;
            }
        }

        public float Radius => _radius;

        /// <summary>
        /// Emit a single <see cref="BrushKind.CapsuleSubtract"/> running
        /// from the drill cell along the aim direction by
        /// <see cref="_brushReach"/> metres, with radius
        /// <see cref="_radius"/>. Returns the number of SDF cells changed
        /// across all chunks the brush touched.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The capsule is constructed from the current aim direction each
        /// emit — not a swept-motion capsule between previous and current
        /// tip positions. The aim-direction capsule is what makes looking
        /// up / down produce an angled tunnel rather than a sphere
        /// shifted by the small <see cref="_tipForwardOffset"/>. At
        /// 30 Hz emit rate and any plausible chassis speed, consecutive
        /// emits overlap by more than 90 % of the brush volume, so
        /// dropping the swept-motion safeguard costs nothing in practice.
        /// </para>
        /// <para>
        /// Called by <see cref="OnCollisionStay"/> in game; also exposed
        /// so PlayMode tests can drive synthetic drill cycles without a
        /// full physics simulation.
        /// </para>
        /// </remarks>
        public int Drill(DigZone zone)
        {
            if (zone == null) return 0;

            Vector3 aimDir = _bitTransform != null ? _bitTransform.up : transform.up;
            Vector3 p0 = transform.position;
            Vector3 p1 = transform.position + aimDir * _brushReach;

            BrushOp op = new BrushOp
            {
                kind = BrushKind.CapsuleSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(p0),
                p1 = Vector3Fixed.FromVector3(p1),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(_radius * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };

            int changed = zone.ApplyBrush(op);
            _lastEmitTime = Time.time;

            if (_debugReadout)
            {
                _diag.Time = Time.time;
                _diag.LastChanged = changed;
                _diag.AimDir = aimDir;
                _diag.ElevationDeg = Mathf.Asin(Mathf.Clamp(aimDir.normalized.y, -1f, 1f))
                                     * Mathf.Rad2Deg;
            }

            if (changed > 0)
            {
                AudioRouter.PlayOneShot(AudioCue.DrillContact, TipWorldPosition);
                // Dust anchored at the capsule midpoint so the effect
                // sits where actual carving happens, not at the drill bit
                // 0.6 m from the cell.
                Vector3 vfxAnchor = (p0 + p1) * 0.5f;
                VfxSpawner.Spawn(VfxKind.DebrisDust, vfxAnchor, Quaternion.identity, scale: 0.5f);

                // Arm the dig-pull. The force itself is applied every
                // FixedUpdate (see ApplyDigPull) while this stays live —
                // not here, so the servo runs at the physics rate rather
                // than the throttled ~30 Hz emit rate. Kept alive a touch
                // past the next expected emit so a missed/throttled tick
                // doesn't briefly drop the pull. Gated on changed>0 so it
                // only arms while biting solid voxels, never while
                // drilling air (no free-flight exploit).
                _pullAimDir = aimDir;
                _pullActiveUntil = Time.time + _emitInterval * 1.5f;
            }
            return changed;
        }

        private void OnCollisionStay(Collision collision)
        {
            // Standalone-drill path (rare in production: drill not under a
            // chassis Rigidbody). When the drill IS under a chassis,
            // Unity routes physics callbacks to the chassis-root
            // Rigidbody GameObject, not this child — see
            // <see cref="DrillCollisionForwarder"/>.
            HandleContact(collision.collider);
        }

        private void FixedUpdate()
        {
            // Surface-contact-only drilling fails for a body-mounted
            // chassis drill: wheels keep the chassis above the terrain
            // surface, so the drill's BoxCollider doesn't physically
            // intersect the chunk's surface MeshCollider. Poll
            // DigField each fixed step: if the tip sits inside ANY
            // registered DigZone's volume, emit a brush. Brushes on
            // already-exterior cells are no-ops (max-fold idempotent),
            // so this only costs work where it actually carves.
            if (IsFireHeld && Time.time - _lastEmitTime >= _emitInterval)
            {
                IDigZone zone = DigField.ZoneAt(transform.position);
                if (zone is DigZone concrete) Drill(concrete);
            }

            // Dig-pull, applied every physics step while a recent carve
            // is still live (armed by Drill on changed>0). Runs at the
            // physics rate — independent of the throttled emit — so the
            // servo can deliver sustained force against gravity.
            if (Time.time <= _pullActiveUntil)
            {
                Rigidbody body = ChassisBody;
                if (body != null && !body.isKinematic && _digPullForce > 0f)
                    ApplyDigPull(body, _pullAimDir);
            }
        }

        /// <summary>
        /// One physics-step of the dig-pull: cancel the slice of gravity
        /// opposing the aim (so an upward dig doesn't sag), then a one-
        /// sided velocity servo toward <see cref="_digTargetSpeed"/> along
        /// the aim axis, capped at <see cref="_digPullForce"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Gravity compensation.</b> The servo alone can't reliably
        /// climb: in steady state it only requests enough force to fix
        /// the small recent speed deficit, which is far less than the
        /// chassis weight, so a vertical dig nets ~zero. Cancelling the
        /// weight component opposing the aim (only when it opposes — a
        /// downward dig keeps gravity's help) lets the servo provide just
        /// the slow crawl regardless of orientation. Applied at the COM
        /// (plain <see cref="Rigidbody.AddForce"/>) so it adds no torque;
        /// the propulsion stays at the drill cell for the angular feel.
        /// </para>
        /// <para>
        /// <b>Servo.</b> Force that would close the speed deficit in one
        /// physics step, clamped to the ceiling, never negative — so a
        /// chassis already moving faster than target along the aim (e.g.
        /// wheel-driving) gets zero extra dig push. Digging stays a slow,
        /// deliberate crawl; driving is never braked.
        /// </para>
        /// </remarks>
        private void ApplyDigPull(Rigidbody body, Vector3 aimDir)
        {
            // Gravity comp — only the part of weight opposing the aim,
            // and only if the body is actually under gravity.
            float gravCompN = 0f;
            if (body.useGravity)
            {
                Vector3 weight = Physics.gravity * body.mass;     // points down
                float weightAlongAim = Vector3.Dot(weight, aimDir);
                if (weightAlongAim < 0f)                          // aim has an upward component
                {
                    gravCompN = -weightAlongAim;
                    body.AddForce(aimDir * gravCompN, ForceMode.Force);
                }
            }

            // Two-sided speed servo along the aim/tunnel axis. While the
            // drill is biting, IT is the authority on how fast you travel
            // along the tunnel: too slow → push toward target; too fast
            // → brake toward target. The brake half is load-bearing —
            // non-dig velocity (driving / sliding) projects onto the aim
            // axis and, with a one-sided servo, made it conclude "already
            // fast enough" and apply zero pull (the readout that exposed
            // this: v·aim = 13 m/s from a 16 m/s sideways slide → 0 N).
            // Regulating both directions also delivers the original ask:
            // holding fire paces you to the slow dig crawl regardless of
            // entry speed; releasing fire (pull lapses) frees full drive
            // speed again.
            float vAlong = Vector3.Dot(body.linearVelocity, aimDir);
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            float wantedN = (_digTargetSpeed - vAlong) * body.mass / dt;
            float servoN = Mathf.Clamp(wantedN, -_digPullForce, _digPullForce);
            if (servoN >= 0f)
            {
                // Propulsion: at the drill cell (off-COM) so the nose
                // yaws/pitches to follow the dig — the "angular pull".
                body.AddForceAtPosition(aimDir * servoN, transform.position, ForceMode.Force);
            }
            else
            {
                // Braking: at the COM so yanking a fast slide back to dig
                // pace decelerates cleanly instead of inducing a spin.
                body.AddForce(aimDir * servoN, ForceMode.Force);
            }

            if (_debugReadout)
            {
                _diag.HaveBody = true;
                _diag.Kinematic = body.isKinematic;
                _diag.UseGravity = body.useGravity;
                _diag.Mass = body.mass;
                _diag.VAlong = vAlong;
                _diag.GravCompN = gravCompN;
                _diag.ServoN = servoN;
                _diag.Velocity = body.linearVelocity;
                _diag.ChassisY = body.position.y;
                _diag.PullArmed = true;
            }
        }

        private void OnGUI()
        {
            if (!_debugReadout) return;

            const float w = 320f, h = 240f, pad = 10f;
            var rect = new Rect(Screen.width - w - pad, pad, w, h);
            GUI.Box(rect, "DrillBlock diag");

            float staleFor = Time.time - _diag.Time;
            string body = _diag.HaveBody
                ? $"mass={_diag.Mass:0.0}kg kin={_diag.Kinematic} grav={_diag.UseGravity}"
                : "<no chassis Rigidbody resolved>";

            string text =
                $"FireHeld:   {IsFireHeld}\n" +
                $"last emit:  {staleFor:0.00}s ago\n" +
                $"changed:    {_diag.LastChanged} cells  (0 = carving air!)\n" +
                $"aim:        {_diag.AimDir:F2}\n" +
                $"elevation:  {_diag.ElevationDeg:0.0}° above horizontal\n" +
                $"pull armed: {_diag.PullArmed}\n" +
                $"body:       {body}\n" +
                $"v·aim:      {_diag.VAlong:0.00} m/s  (target {_digTargetSpeed:0.0})\n" +
                $"gravComp:   {_diag.GravCompN:0} N\n" +
                $"servo:      {_diag.ServoN:0} N  (ceiling {_digPullForce:0})\n" +
                $"velocity:   {_diag.Velocity:F2}\n" +
                $"chassis Y:  {_diag.ChassisY:0.00} m";

            GUI.Label(new Rect(rect.x + 8f, rect.y + 22f, w - 16f, h - 30f), text);
        }

        /// <summary>
        /// Process a single physics contact with <paramref name="otherCollider"/>.
        /// If the other collider belongs to a <see cref="DigChunk"/>, emits
        /// one <see cref="BrushKind.CapsuleSubtract"/> through the chunk's
        /// parent <see cref="DigZone"/> (throttled to <see cref="_emitInterval"/>).
        /// Returns <c>true</c> if a brush was actually emitted.
        /// </summary>
        /// <remarks>
        /// Public so <see cref="DrillCollisionForwarder"/> can forward
        /// chassis-root <c>OnCollisionStay</c> events to drill blocks
        /// living on child GameObjects (Unity's physics-message routing
        /// only fires on the Rigidbody's GameObject, not children).
        /// </remarks>
        public bool HandleContact(Collider otherCollider)
        {
            if (otherCollider == null) return false;
            if (!IsFireHeld) return false;
            if (Time.time - _lastEmitTime < _emitInterval) return false;

            DigChunk chunk = otherCollider.GetComponentInParent<DigChunk>();
            if (chunk == null) return false;
            DigZone zone = chunk.GetComponentInParent<DigZone>();
            if (zone == null) return false;

            Drill(zone);
            return true;
        }

        // FireHeld gate, with a late lookup fallback for cases where the
        // DrillBlock is instantiated and later re-parented under a chassis
        // root that carries the IInputSource — the cached _input from
        // Awake would be stale. The lookup is cheap and only runs while
        // _input is still null.
        private bool IsFireHeld
        {
            get
            {
                if (_input == null) _input = GetComponentInParent<IInputSource>();
                return _input != null && _input.FireHeld;
            }
        }

        // Chassis Rigidbody, lazily resolved + cached. Same late-lookup
        // rationale as IsFireHeld: the drill may be AddComponent'd before
        // it's re-parented under the chassis root. Null for a standalone
        // drill (e.g., a unit test driving Drill(zone) directly) — callers
        // null-check before applying the dig-pull.
        private Rigidbody ChassisBody
        {
            get
            {
                if (_rb == null) _rb = GetComponentInParent<Rigidbody>();
                return _rb;
            }
        }

        private void LateUpdate()
        {
            // Each emit builds a fresh aim-direction capsule from the
            // current cell position, so the prev-tip tracking the swept
            // motion approach needed is no longer load-bearing — only
            // aim-bit reorientation runs here.
            AimCone();
        }

        private void AimCone()
        {
            if (_bitTransform == null) return;
            if (_aimCamera == null) _aimCamera = Camera.main;
            if (_aimCamera == null)
            {
                // Headless / no main camera (e.g., PlayMode tests). Keep
                // the bit at its mount-up so contact + auto-poll behave
                // exactly like the pre-cone build.
                _bitTransform.localRotation = Quaternion.identity;
                return;
            }

            Vector3 worldAim = _aimCamera.transform.forward;
            Vector3 localAim = transform.InverseTransformDirection(worldAim);
            float sqr = localAim.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                _bitTransform.localRotation = Quaternion.identity;
                return;
            }
            localAim /= Mathf.Sqrt(sqr);

            Vector3 localMountUp = Vector3.up;
            float angle = Vector3.Angle(localMountUp, localAim);
            Vector3 clampedDir = (angle <= _maxAimAngle)
                ? localAim
                : Vector3.Slerp(localMountUp, localAim, _maxAimAngle / angle).normalized;

            _bitTransform.localRotation = Quaternion.FromToRotation(Vector3.up, clampedDir);
        }
    }
}
