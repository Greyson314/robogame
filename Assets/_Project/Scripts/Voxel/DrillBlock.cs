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

        [Tooltip("How far past the cell center the brush emits, in metres, along the drill's mount-up. " +
                 "A cell-mounted drill on a wheeled chassis floats above the terrain — without this offset " +
                 "the brush only nicks the surface and idempotency kills repeat carving. 0.6m projects the " +
                 "brush ahead/below into uncarved material so each tick advances the tunnel.")]
        [SerializeField, Min(0f)] private float _tipForwardOffset = 0.6f;

        [Tooltip("Minimum seconds between emitted brush ops. 0.033 ≈ 30 Hz, matching the design's drill tick rate.")]
        [SerializeField, Min(0.005f)] private float _emitInterval = 0.033f;

        [Header("Cone aim")]
        [Tooltip("Maximum angle in degrees the drill bit can swivel away from its mount-up direction. " +
                 "30° is enough to dig downward from a forward-mounted drill without losing the cell's " +
                 "fundamental orientation. The brush emits along the AIMED direction, so the player can " +
                 "look at the ground and drill toward it.")]
        [SerializeField, Range(0f, 90f)] private float _maxAimAngle = 30f;

        [Tooltip("World-space length of the cone visual extending past the drill cell. " +
                 "Cosmetic; brush emission distance is _tipForwardOffset.")]
        [SerializeField, Min(0.05f)] private float _coneVisualLength = 1.0f;

        [Tooltip("World-space base radius of the cone visual. Cosmetic only.")]
        [SerializeField, Min(0.05f)] private float _coneVisualRadius = 0.22f;

        [Tooltip("Camera used as the aim source. Falls back to Camera.main if unassigned.")]
        [SerializeField] private Camera _aimCamera;

        private Vector3 _lastTipPosWorld;
        private bool _haveLastTipPos;
        private float _lastEmitTime = float.NegativeInfinity;
        // Player input gate — drilling is held-fire continuous (mirrors the
        // BombBay / Cannon / ProjectileGun input pattern). The contact and
        // FixedUpdate auto-poll paths both check this; the public Drill(zone)
        // method does NOT, so tests + scripted callers can fire deterministically.
        private IInputSource _input;
        // Looped AudioCue.DrillActive while FireHeld — the "motor spinning"
        // bed that the per-emit AudioCue.DrillContact "bite" cue plays over.
        // Owned by the block, started on FireHeld true → false → true
        // edges and stopped on the opposite edge (or OnDisable).
        private AudioLoopHandle _activeLoop = AudioLoopHandle.Invalid;
        private bool _loopActive;

        // Cone visual + aim — see _maxAimAngle, _coneVisualLength,
        // _coneVisualRadius. The bit is a procedural cone child whose
        // localRotation is updated each LateUpdate from the aim camera's
        // forward direction (clamped to the cone). TipWorldPosition uses
        // the bit's local +Y in world space so the brush emits along
        // the aimed direction, not the cell's static mount-up.
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
        /// World-space point the brush emits from. The direction follows
        /// the aimed cone bit's local +Y, which is the cell's mount-up
        /// when no aim camera is present and the camera-clamped aim
        /// direction otherwise (within <see cref="_maxAimAngle"/>°).
        /// Distance is <see cref="_tipForwardOffset"/> metres.
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
        /// Emit a single <see cref="BrushKind.CapsuleSubtract"/> spanning
        /// the drill tip's motion from the previous tick to the current
        /// position. Returns the number of SDF cells changed across all
        /// chunks the brush touched.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="OnCollisionStay"/> in game; also exposed
        /// so PlayMode tests can drive synthetic drill cycles without a
        /// full physics simulation.
        /// </remarks>
        public int Drill(DigZone zone)
        {
            if (zone == null) return 0;

            Vector3 currentTip = TipWorldPosition;
            Vector3 prevTip = _haveLastTipPos ? _lastTipPosWorld : currentTip;

            BrushOp op = new BrushOp
            {
                kind = BrushKind.CapsuleSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(prevTip),
                p1 = Vector3Fixed.FromVector3(currentTip),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(_radius * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };

            int changed = zone.ApplyBrush(op);

            _lastTipPosWorld = currentTip;
            _haveLastTipPos = true;
            _lastEmitTime = Time.time;

            if (changed > 0)
            {
                AudioRouter.PlayOneShot(AudioCue.DrillContact, currentTip);
                VfxSpawner.Spawn(VfxKind.DebrisDust, currentTip, Quaternion.identity, scale: 0.5f);
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
            if (!IsFireHeld) return;
            if (Time.time - _lastEmitTime < _emitInterval) return;
            IDigZone zone = DigField.ZoneAt(transform.position);
            if (zone is DigZone concrete) Drill(concrete);
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

        private void LateUpdate()
        {
            // Track tip position even when not in contact so the first
            // contact's swept capsule covers the actual motion (not a
            // teleport from the previous contact point).
            _lastTipPosWorld = TipWorldPosition;
            _haveLastTipPos = true;
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
