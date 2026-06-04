using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Network.Prediction
{
    /// <summary>
    /// Owns the owner-client's isolated physics prediction scene
    /// (ADR-0002). A single <see cref="Scene"/> opened with
    /// <see cref="LocalPhysicsMode.Physics3D"/> is excluded from the global
    /// <c>Physics.Simulate</c> / <c>FixedUpdate</c> step and is advanced
    /// only by an explicit <see cref="PhysicsScene.Simulate(float)"/>. CSP
    /// reconciliation re-steps the owner chassis here — via a colliderless
    /// <em>mirror</em> Rigidbody (see <see cref="CreateMirrorBody"/>) — so
    /// replay never touches any other body in the live arena.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lifecycle.</b> The scene is created lazily on the first
    /// <see cref="CreateMirrorBody"/> and destroyed when the last mirror is
    /// released (<see cref="ReleaseMirrorBody"/>). Mirror count is the
    /// ref-count. Owner-client only: server / host / non-owner remotes
    /// never call in, so they pay zero cost (invariant #5).
    /// </para>
    /// <para>
    /// <b>Invariant #4 carve-out.</b> The mirror is the one sanctioned
    /// second Rigidbody for a chassis — prediction-only, never networked,
    /// destroyed on despawn. See ADR-0002.
    /// </para>
    /// <para>
    /// <b>Domain-reload safety.</b> <see cref="Scene"/> is a value type that
    /// survives a domain reload as a stale handle; the owned mirror
    /// GameObjects do not. <see cref="ResetStatics"/> clears all state on
    /// subsystem registration so a fresh play session never trusts a dead
    /// handle (known failure mode).
    /// </para>
    /// </remarks>
    public static class PredictionScene
    {
        private const string SceneName = "[Prediction]";

        private static Scene s_scene;
        private static bool s_created;
        private static int s_mirrorCount;

        /// <summary>True while the prediction scene exists and is valid.</summary>
        public static bool IsCreated => s_created && s_scene.IsValid();

        /// <summary>
        /// The prediction scene's physics handle. Only valid while
        /// <see cref="IsCreated"/>; call after at least one
        /// <see cref="CreateMirrorBody"/>. Step it with
        /// <c>PhysicsScene.Simulate(dt)</c> during replay.
        /// </summary>
        public static PhysicsScene PhysicsScene =>
            IsCreated ? s_scene.GetPhysicsScene() : default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_scene = default;
            s_created = false;
            s_mirrorCount = 0;
        }

        /// <summary>
        /// Create a colliderless, renderless mirror Rigidbody in the
        /// prediction scene whose mass distribution (mass, COM, inertia
        /// tensor, damping, gravity, constraints) is copied from
        /// <paramref name="source"/>. Creates the scene if this is the first
        /// mirror. The returned body has no colliders, so it integrates
        /// under forces and gravity but never collides — replay relies on
        /// the next authoritative snapshot to correct any contact it missed
        /// (ADR-0002).
        /// </summary>
        public static Rigidbody CreateMirrorBody(Rigidbody source)
        {
            if (source == null) return null;
            EnsureScene();

            // TRACE[INV-4]: the prediction mirror is the one sanctioned 2nd Rigidbody for a chassis
            var go = new GameObject("[PredictionMirror]");
            var mirror = go.AddComponent<Rigidbody>();
            CopyMassProperties(source, mirror);
            // The mirror is hand-stepped; interpolation/auto-sync would just
            // burn cycles. Discrete + no interpolation keeps it deterministic.
            mirror.interpolation = RigidbodyInterpolation.None;
            mirror.collisionDetectionMode = CollisionDetectionMode.Discrete;

            SceneManager.MoveGameObjectToScene(go, s_scene);
            s_mirrorCount++;
            return mirror;
        }

        /// <summary>
        /// Destroy a mirror body created by <see cref="CreateMirrorBody"/>.
        /// Tears the prediction scene down once the last mirror is gone.
        /// Safe to pass null.
        /// </summary>
        public static void ReleaseMirrorBody(Rigidbody mirror)
        {
            if (mirror != null && mirror.gameObject != null)
            {
                Object.Destroy(mirror.gameObject);
                s_mirrorCount--;
            }
            if (s_mirrorCount <= 0) DestroyScene();
        }

        /// <summary>
        /// Re-copy mass distribution from <paramref name="source"/> onto an
        /// existing <paramref name="mirror"/>. Called after the live chassis
        /// sheds blocks (mass / COM / inertia change). One-shot value writes,
        /// no allocation.
        /// </summary>
        public static void SyncMassProperties(Rigidbody source, Rigidbody mirror)
        {
            if (source == null || mirror == null) return;
            CopyMassProperties(source, mirror);
        }

        private static void CopyMassProperties(Rigidbody source, Rigidbody mirror)
        {
            mirror.mass = source.mass;
            mirror.linearDamping = source.linearDamping;
            mirror.angularDamping = source.angularDamping;
            mirror.useGravity = source.useGravity;
            mirror.constraints = source.constraints;
            // Solver / clamp params that shape the integration, so the mirror
            // steps the chassis identically — not just its mass distribution.
            mirror.maxAngularVelocity = source.maxAngularVelocity;
            mirror.solverIterations = source.solverIterations;
            mirror.solverVelocityIterations = source.solverVelocityIterations;
            // Assigning COM + inertia explicitly pins them so PhysX doesn't
            // try to recompute from (nonexistent) colliders on the mirror.
            mirror.centerOfMass = source.centerOfMass;
            mirror.inertiaTensor = source.inertiaTensor;
            mirror.inertiaTensorRotation = source.inertiaTensorRotation;
        }

        private static void EnsureScene()
        {
            if (IsCreated) return;
            s_scene = SceneManager.CreateScene(
                SceneName,
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            s_created = true;
            s_mirrorCount = 0;
        }

        private static void DestroyScene()
        {
            s_mirrorCount = 0;
            if (!IsCreated)
            {
                s_created = false;
                s_scene = default;
                return;
            }
            // Unloading a scene is async; we don't await it. The handle is
            // invalidated immediately for IsCreated purposes.
            Scene toUnload = s_scene;
            s_scene = default;
            s_created = false;
            if (toUnload.IsValid() && toUnload.isLoaded)
            {
                SceneManager.UnloadSceneAsync(toUnload);
            }
        }
    }
}
