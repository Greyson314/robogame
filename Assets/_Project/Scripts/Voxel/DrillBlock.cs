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

        [Header("Drill-glide")]
        [Tooltip("Bore speed (m/s). While the drill is actively cutting solid voxels, the " +
                 "chassis enters 'glide': gravity + wheel grip are suspended and the body is " +
                 "moved along the aim/tunnel direction at exactly this speed (kinematic move — " +
                 "terrain collision can't pin it, which is why a wheeled bot can finally bore " +
                 "straight up). Glide is gated strictly on real cutting: the instant the drill " +
                 "stops removing material (broke into air, released fire), normal dynamic " +
                 "physics + gravity resume within ~1.5 emit intervals — it is NOT an " +
                 "anti-gravity button you can hold mid-air. Keep this notably below drive " +
                 "speed so tunnelling reads as the slow, deliberate option.")]
        [SerializeField, Min(0.1f)] private float _digTargetSpeed = 2.0f;

        [Tooltip("How fast (deg/sec) the chassis swings to point along the bore while gliding. " +
                 "Without this the kinematic body keeps its entry orientation and reads as " +
                 "'suspended in space'; with it the bot noses into the dig direction. Rate-" +
                 "capped so it banks in smoothly rather than snapping. 0 disables the turn " +
                 "(translate only).")]
        [SerializeField, Min(0f)] private float _glideTurnSpeed = 270f;

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

        // Last-tick diagnostic snapshot, populated by Drill()/
        // UpdateDrillGlide and rendered by OnGUI when _debugReadout is
        // on. Pure
        // instrumentation — never read by gameplay code.
        private struct DigDiag
        {
            public float Time;          // Time.time of the snapshot
            public int LastChanged;     // cells carved on the last emit
            public Vector3 AimDir;      // world-space aim
            public float ElevationDeg;  // aim angle above horizontal
            public bool HaveBody;
            public float MassKg;
            public bool Gliding;        // drill-glide engaged this step
            public Vector3 Velocity;    // chassis linearVelocity
            public float ChassisY;      // chassis world Y (is it rising?)
            public bool PullArmed;      // Time.time <= _pullActiveUntil (real cutting)
        }
        private DigDiag _diag;

        private float _lastEmitTime = float.NegativeInfinity;
        // Drill-glide is active every FixedUpdate while a recent carve is
        // still "live". Drill() refreshes _pullActiveUntil + _pullAimDir
        // ONLY on a cutting emit (changed > 0); FixedUpdate glides the
        // chassis along _pullAimDir until the window lapses (~1.5 emit
        // intervals after the last material was removed). That gate is
        // what keeps glide honest — it cannot engage in open air, so it
        // is not an anti-gravity toggle.
        private float _pullActiveUntil = float.NegativeInfinity;
        private Vector3 _pullAimDir = Vector3.up;
        // Drill-glide state. While gliding, the chassis Rigidbody is held
        // kinematic so terrain collision can't pin a wheeled bot trying
        // to bore upward, gravity + wheel grip are suspended, and the
        // body is MovePosition'd along the aim at _digTargetSpeed.
        // _glidePrevKinematic remembers the body's pre-glide kinematic
        // flag so disengage restores it exactly. NOTE: assumes one drill
        // per chassis (the DrillBot case); multiple drills toggling the
        // shared body's kinematic flag is not coordinated — future work.
        private bool _gliding;
        private bool _glidePrevKinematic;
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

                // Arm drill-glide. The glide itself runs every
                // FixedUpdate (see UpdateDrillGlide) while this window
                // stays live — kept alive a touch past the next expected
                // emit so a missed/throttled tick doesn't drop it. Set
                // ONLY here, inside changed>0, so glide engages strictly
                // while biting solid voxels and never in open air (not an
                // anti-gravity toggle).
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

            // Drill-glide. Active only while a recent CUTTING emit keeps
            // the window fresh (Drill arms it on changed>0 only), so it
            // can never engage in open air. While active the chassis is
            // kinematic and slid along the bore — terrain collision can't
            // pin it, so a wheeled bot finally climbs straight up.
            bool boring = Time.time <= _pullActiveUntil;
            UpdateDrillGlide(boring);
        }

        /// <summary>
        /// Drill-glide state machine. While the drill is actively cutting
        /// solid voxels (<paramref name="boring"/>), the chassis enters a
        /// kinematic "glide": gravity + wheel grip are suspended and the
        /// body is <see cref="Rigidbody.MovePosition"/>'d along the aim at
        /// <see cref="_digTargetSpeed"/>. The instant cutting stops the
        /// body is restored to its prior (dynamic) state with the bore
        /// momentum carried out, so gravity resumes — it is not an
        /// anti-gravity hover.
        /// </summary>
        /// <remarks>
        /// Kinematic motion is the load-bearing trick: a dynamic body
        /// pushed up against the still-solid (async-baked) terrain
        /// MeshCollider just gets its upward component cancelled by the
        /// contact solver — which is exactly why every force-based
        /// attempt failed to climb. A kinematic body is not stopped by
        /// static colliders, so it glides through the carved SDF along
        /// the tunnel the drill is opening ahead of it.
        /// </remarks>
        private void UpdateDrillGlide(bool boring)
        {
            Rigidbody body = ChassisBody;
            if (body == null) return;

            if (boring)
            {
                if (!_gliding)
                {
                    _glidePrevKinematic = body.isKinematic;
                    body.isKinematic = true;        // suspends gravity + wheel grip
                    _gliding = true;
                }
                // Slide along the bore. Kinematic MovePosition isn't
                // blocked by the terrain MeshCollider, so vertical bores
                // actually move up instead of being pinned to the floor.
                body.MovePosition(
                    body.position + _pullAimDir * (_digTargetSpeed * Time.fixedDeltaTime));

                // Nose into the bore. Rotate the chassis so the drill
                // cell's mount-up (transform.up — convention-independent,
                // works regardless of how the bot is built) swings onto
                // the bore direction. Rate-capped so it banks in smoothly
                // instead of snapping, and so a fast camera flick doesn't
                // whip the body around. Without this the kinematic body
                // keeps its entry pose and reads as "suspended in space".
                if (_glideTurnSpeed > 0f &&
                    _pullAimDir.sqrMagnitude > 1e-6f &&
                    transform.up.sqrMagnitude > 1e-6f)
                {
                    Quaternion align = Quaternion.FromToRotation(transform.up, _pullAimDir);
                    Quaternion target = align * body.rotation;
                    body.MoveRotation(Quaternion.RotateTowards(
                        body.rotation, target, _glideTurnSpeed * Time.fixedDeltaTime));
                }
            }
            else if (_gliding)
            {
                EndGlide(body);
            }

            if (_debugReadout)
            {
                _diag.HaveBody = true;
                _diag.MassKg = body.mass;
                _diag.Gliding = _gliding;
                _diag.Velocity = body.linearVelocity;
                _diag.ChassisY = body.position.y;
                _diag.PullArmed = boring;
            }
        }

        // Restore dynamic physics and carry the bore momentum out so the
        // chassis doesn't dead-stop on exit (it pops out moving, then
        // gravity takes over). Robust to being called from OnDisable.
        private void EndGlide(Rigidbody body)
        {
            body.isKinematic = _glidePrevKinematic;
            _gliding = false;
            if (!body.isKinematic)
                body.linearVelocity = _pullAimDir * _digTargetSpeed;
        }

        private void OnDisable()
        {
            // A drill destroyed / disabled mid-bore must not leave the
            // chassis stuck kinematic forever.
            if (_gliding)
            {
                Rigidbody body = ChassisBody;
                if (body != null) EndGlide(body);
                else _gliding = false;
            }
            StopActiveLoop();
        }

        private void OnGUI()
        {
            if (!_debugReadout) return;

            const float w = 320f, h = 240f, pad = 10f;
            var rect = new Rect(Screen.width - w - pad, pad, w, h);
            GUI.Box(rect, "DrillBlock diag");

            float staleFor = Time.time - _diag.Time;
            string body = _diag.HaveBody
                ? $"mass={_diag.MassKg:0.0}kg"
                : "<no chassis Rigidbody resolved>";

            string text =
                $"FireHeld:   {IsFireHeld}\n" +
                $"last emit:  {staleFor:0.00}s ago\n" +
                $"changed:    {_diag.LastChanged} cells  (0 = carving air!)\n" +
                $"aim:        {_diag.AimDir:F2}\n" +
                $"elevation:  {_diag.ElevationDeg:0.0}° above horizontal\n" +
                $"boring:     {_diag.PullArmed}  (real cutting → glide)\n" +
                $"GLIDING:    {_diag.Gliding}  (kinematic, grav off)\n" +
                $"body:       {body}\n" +
                $"digSpeed:   {_digTargetSpeed:0.0} m/s\n" +
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
