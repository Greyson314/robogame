namespace Robogame.Movement
{
    /// <summary>
    /// One element of the composite drive on a robot. Wheels, thrusters,
    /// control surfaces, hover pads, walker legs all implement this. Each
    /// subsystem applies its own forces at its own world position via the
    /// shared chassis <c>Rigidbody</c>; force accumulation is handled by
    /// the physics engine, so subsystems don't need to coordinate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subsystems self-register with the chassis-level <see cref="RobotDrive"/>
    /// from <c>OnEnable</c>, and unregister from <c>OnDisable</c>. That makes
    /// the registry robust to template rebuilds, hot-swapped blocks, and
    /// destroyed-block cleanup.
    /// </para>
    /// <para>
    /// <see cref="Order"/> dictates dispatch order within a single
    /// <c>Tick</c>. Smaller runs earlier. Conventions:
    /// <list type="bullet">
    ///   <item>0 — actuators (thrust, drive torque, control surfaces)</item>
    ///   <item>100 — passive aero (drag, lift)</item>
    ///   <item>200 — assists (auto-stabilise, ABS)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IDriveSubsystem
    {
        /// <summary>Lower runs earlier. See remarks for conventions.</summary>
        int Order { get; }

        /// <summary>If false, the aggregator skips this subsystem this frame.</summary>
        bool IsOperational { get; }

        /// <summary>
        /// Apply this subsystem's forces / torques for the current physics step.
        /// </summary>
        void Tick(in DriveControl control);
    }
}
