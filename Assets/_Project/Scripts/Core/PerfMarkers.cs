using Unity.Profiling;

namespace Robogame.Core
{
    /// <summary>
    /// Pre-allocated <see cref="ProfilerMarker"/> instances for the project's
    /// known hot paths. Use the existing markers when adding work in a tagged
    /// area rather than allocating fresh ones — markers cost ~50 ns when the
    /// profiler is detached, but allocating one per call site bloats the
    /// trace and makes captures harder to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Names use the <c>Robogame.&lt;Module&gt;.&lt;Method&gt;</c> convention
    /// so they sort together in the Profiler hierarchy and are trivially
    /// greppable in a saved trace. Keep this file the single source of truth;
    /// don't re-declare markers locally in the file they tag.
    /// </para>
    /// <para>
    /// Adding a new marker is a one-line change here plus a single
    /// <c>using (PerfMarkers.X.Auto()) { ... }</c> at the call site. Do not
    /// blanket every method with a marker — favour module-level entry points
    /// (one per significant subsystem tick) over fine-grained sub-section
    /// markers, which fragment the trace.
    /// </para>
    /// </remarks>
    public static class PerfMarkers
    {
        // -----------------------------------------------------------------
        // Movement / physics-driven blocks
        // -----------------------------------------------------------------

        /// <summary>The full Verlet rope integrator, all chains, one FixedUpdate.</summary>
        public static readonly ProfilerMarker VerletRopeFixedUpdate
            = new ProfilerMarker("Robogame.VerletRope.FixedUpdate");

        /// <summary>Single rotor's per-step spin + kinematic-hub drive.</summary>
        public static readonly ProfilerMarker RotorFixedUpdate
            = new ProfilerMarker("Robogame.Rotor.FixedUpdate");

        /// <summary>Single aero surface's lift / drag / sideslip integration.</summary>
        public static readonly ProfilerMarker AeroSurfaceFixedUpdate
            = new ProfilerMarker("Robogame.Aero.FixedUpdate");

        /// <summary>Wheel ground-contact raycast + suspension force.</summary>
        public static readonly ProfilerMarker WheelFixedUpdate
            = new ProfilerMarker("Robogame.Wheel.FixedUpdate");

        // -----------------------------------------------------------------
        // Robot / chassis aggregates
        // -----------------------------------------------------------------

        /// <summary>Recompute mass, COM, inertia tensor, and CPU count from the block grid.</summary>
        public static readonly ProfilerMarker RobotRecalcAggregates
            = new ProfilerMarker("Robogame.Robot.RecalcAggregates");

        /// <summary>Connectivity flood-fill from CPU after a damage event.</summary>
        public static readonly ProfilerMarker RobotConnectivity
            = new ProfilerMarker("Robogame.Robot.Connectivity");

        /// <summary>Full chassis materialisation (one-time at spawn — slow path tagged for visibility).</summary>
        public static readonly ProfilerMarker ChassisFactoryBuild
            = new ProfilerMarker("Robogame.ChassisFactory.Build");

        // -----------------------------------------------------------------
        // Gameplay / world systems
        // -----------------------------------------------------------------

        /// <summary>Per-vertex water surface deformation + foam mask.</summary>
        public static readonly ProfilerMarker WaterMeshUpdate
            = new ProfilerMarker("Robogame.Water.MeshUpdate");

        /// <summary>Per-block buoyancy sample + force application for one chassis.</summary>
        public static readonly ProfilerMarker BuoyancyFixedUpdate
            = new ProfilerMarker("Robogame.Buoyancy.FixedUpdate");

        // -----------------------------------------------------------------
        // Camera / aim / input
        // -----------------------------------------------------------------

        /// <summary>RobotDrive's camera-cursor aim raycast + self-skip.</summary>
        public static readonly ProfilerMarker AimComputeAimPoint
            = new ProfilerMarker("Robogame.Aim.ComputeAimPoint");

        /// <summary>FollowCamera's smoothing + obstacle-avoidance SphereCast.</summary>
        public static readonly ProfilerMarker FollowCameraLateUpdate
            = new ProfilerMarker("Robogame.FollowCamera.LateUpdate");

        // -----------------------------------------------------------------
        // Combat
        // -----------------------------------------------------------------

        /// <summary>Single projectile's swept-ray ballistic step.</summary>
        public static readonly ProfilerMarker ProjectileFixedUpdate
            = new ProfilerMarker("Robogame.Projectile.FixedUpdate");

        // -----------------------------------------------------------------
        // Match / AI loop
        // -----------------------------------------------------------------

        /// <summary>Per-frame match state-machine tick (timer + win-condition checks).</summary>
        public static readonly ProfilerMarker MatchControllerUpdate
            = new ProfilerMarker("Robogame.Match.Update");

        /// <summary>Per-bot AI brain tick (target select, steering, fire decisions).</summary>
        public static readonly ProfilerMarker BotInputUpdate
            = new ProfilerMarker("Robogame.Bot.InputUpdate");

        // -----------------------------------------------------------------
        // Repair / one-shot rebuilds
        // -----------------------------------------------------------------

        /// <summary>One step of a RepairPad gradual rebuild — heal-one-block or place-one-block. Tagged so a slow heal cadence is visible against frame budget.</summary>
        public static readonly ProfilerMarker RepairPadStep
            = new ProfilerMarker("Robogame.RepairPad.Step");

        // -----------------------------------------------------------------
        // VFX
        // -----------------------------------------------------------------

        /// <summary>VfxSpawner.Update — sweeps live instances and returns expired ones to the pool.</summary>
        public static readonly ProfilerMarker VfxSpawnerUpdate
            = new ProfilerMarker("Robogame.Vfx.SpawnerUpdate");
    }
}
