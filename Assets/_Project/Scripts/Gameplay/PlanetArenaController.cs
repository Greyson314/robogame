using Robogame.Block;
using Robogame.Player;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Lives in the PlanetArena scene. Spawns the player's chassis on the
    /// surface of a <see cref="PlanetBody"/> and attaches a
    /// <see cref="PlanetGravityBody"/> so the chassis falls toward the
    /// planet's center instead of <c>-Y</c>. Mirrors
    /// <see cref="WaterArenaController"/> in shape; the only differences
    /// are the spawn pose (computed from the planet's pole + radius) and
    /// the per-chassis gravity hookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>v1 scope:</b> this is the first cut of the spherical-arenas
    /// design described in
    /// [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md).
    /// Gravity is real and spherical; locomotion is *not yet* \u2014
    /// <see cref="Robogame.Movement.WheelBlock"/> and
    /// <see cref="Robogame.Movement.GroundDriveSubsystem"/> still assume
    /// world <c>Vector3.up</c>. So the chassis sits stably on the polar
    /// cap, drives reasonably within ~30\u00b0 of latitude, and degrades
    /// past that until the locomotion substitution work in Phase A/B
    /// lands. Treat this scene as a feel test for the planet's *size*
    /// and *gravity*, not a full match arena.
    /// </para>
    /// <para>
    /// The combat dummy is intentionally omitted in v1 \u2014 a stationary
    /// dummy parked tens of metres from the spawn point would slide off
    /// the polar cap on the first frame because it has no
    /// <see cref="PlanetGravityBody"/>-aware ground to rest on. Re-add
    /// once Phase A is in.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlanetArenaController : MonoBehaviour
    {
        [Header("Planet")]
        [Tooltip("The planet body the player spawns on top of. Auto-resolved from the scene if left null.")]
        [SerializeField] private PlanetBody _planet;

        [Header("Player spawn")]
        [Tooltip("Altitude (m) above the planet's surface for ground chassis. " +
                 "1.5 m matches the flat-arena _groundSpawnPosition.y so build / drive feel is identical.")]
        [SerializeField, Min(0.1f)] private float _groundSpawnAltitude = 1.5f;

        [Tooltip("Altitude (m) above the planet's surface for plane chassis.")]
        [SerializeField, Min(0.1f)] private float _planeSpawnAltitude = 18f;

        [Tooltip("Initial forward speed (m/s) for plane-kind blueprints.")]
        [SerializeField] private float _planeSpawnForwardSpeed = 14f;

        [Tooltip("Name of the spawned chassis GameObject.")]
        [SerializeField] private string _chassisName = "Robot";

        public GameObject Chassis { get; private set; }
        public PlanetBody Planet => _planet;

        private void Start()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogError(
                    "[Robogame] PlanetArenaController: no GameStateController found. " +
                    "Open Bootstrap.unity and press Play from there.",
                    this);
                return;
            }

            if (state.CurrentBlueprint == null || state.Library == null)
            {
                Debug.LogError(
                    "[Robogame] PlanetArenaController: GameStateController is missing its " +
                    "blueprint or block-definition library. Run Robogame > Build Everything.",
                    this);
                return;
            }

            if (_planet == null) _planet = FindAnyObjectByType<PlanetBody>();
            if (_planet == null)
            {
                Debug.LogError(
                    "[Robogame] PlanetArenaController: no PlanetBody in the active scene. " +
                    "Re-run Robogame > Build Everything (it scaffolds the planet).",
                    this);
                return;
            }

            Chassis = SpawnPlayerChassis(state);
            BindFollowCamera(Chassis);
            BindPlanetGravity(Chassis);
        }

        private GameObject SpawnPlayerChassis(GameStateController state)
        {
            GameObject existing = GameObject.Find(_chassisName);
            if (existing != null) Destroy(existing);

            ChassisBlueprint bp = state.CurrentBlueprint;
            bool isPlane = bp != null && bp.Kind == ChassisKind.Plane;
            float altitude = isPlane ? _planeSpawnAltitude : _groundSpawnAltitude;

            // Spawn at the planet's "north pole" \u2014 the surface point at
            // (planetCenter + Vector3.up * radius). With the chassis
            // identity-rotated, its local up matches world up matches the
            // surface normal at the pole, so it lands flat on the cap.
            Vector3 pos = _planet.Center + Vector3.up * (_planet.Radius + altitude);

            var go = new GameObject(_chassisName);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            ChassisFactory.Build(go, bp, state.Library, state.InputActions);

            if (isPlane && _planeSpawnForwardSpeed > 0f)
            {
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = go.transform.forward * _planeSpawnForwardSpeed;
            }

            return go;
        }

        private static void BindPlanetGravity(GameObject chassis)
        {
            if (chassis == null) return;
            // ChassisFactory.Build always provides a Rigidbody, which is
            // PlanetGravityBody's RequireComponent dependency. Adding the
            // component flips Rigidbody.useGravity off (the component owns
            // it now) so we don't need to touch the Rigidbody directly.
            if (chassis.GetComponent<PlanetGravityBody>() == null)
                chassis.AddComponent<PlanetGravityBody>();
        }

        private void BindFollowCamera(GameObject chassis)
        {
            if (chassis == null) return;
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            FollowCamera follow = mainCam.GetComponent<FollowCamera>();
            if (follow == null) follow = mainCam.gameObject.AddComponent<FollowCamera>();
            follow.Target = chassis.transform;

            // Re-orient the orbit basis around the chassis's local up so
            // the chassis never appears tilted on screen as it crosses
            // latitude lines. Local up = -gravity. GravityField returns
            // Physics.gravity (flat) when no source covers the position,
            // which would re-introduce the world-up basis far from the
            // planet \u2014 so we sample directly from the planet to keep the
            // basis well-defined even when the chassis briefly leaves the
            // SOI padding shell.
            PlanetBody planet = _planet;
            follow.UpProvider = pos =>
            {
                if (planet == null) return Vector3.up;
                Vector3 toCenter = planet.Center - pos;
                float sq = toCenter.sqrMagnitude;
                if (sq < 0.0001f) return Vector3.up;
                return -toCenter / Mathf.Sqrt(sq);
            };

            if (mainCam.GetComponent<AimReticle>() == null)
                mainCam.gameObject.AddComponent<AimReticle>();
        }

        /// <summary>Transition back to the garage scene.</summary>
        public void Return()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.EnterGarage();
        }
    }
}
