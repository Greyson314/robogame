namespace Robogame.Movement
{
    /// <summary>
    /// Pure, allocation-free spring math shared by every block that models a
    /// spring. The first two consumers are <see cref="HoverBladeBlock"/> (a
    /// continuous Hooke spring-damper holding a target altitude) and
    /// <see cref="SpringBlock"/> (a one-shot jump impulse). Future suspension
    /// / bumper / pogo blocks should call into here rather than re-deriving
    /// the same formulas inline — the user's "generalise spring mechanics"
    /// directive (session 104) is what this file is for.
    /// </summary>
    /// <remarks>
    /// No Unity dependency on purpose: every method is testable in EditMode
    /// without a GameObject, and Burst-ifying later (if a per-tick rate ever
    /// makes it worth it) stays trivial.
    /// </remarks>
    public static class SpringSolver
    {
        /// <summary>
        /// Hooke spring force minus velocity damping, clamped to ≥ 0.
        /// <c>stiffness × (target − displacement) − damping × velocity</c>.
        /// </summary>
        /// <remarks>
        /// This is exactly the formula <see cref="HoverBladeBlock"/> used
        /// inline before session 104: the spring pushes harder the further
        /// <paramref name="displacement"/> sits below <paramref name="target"/>,
        /// and the damping term bleeds off energy proportional to
        /// <paramref name="velocity"/> (positive = moving away from the
        /// surface). The ≥ 0 clamp is the load-bearing bit: a spring can
        /// push but never pull, so a body already past its target produces
        /// zero force rather than sucking back down.
        /// </remarks>
        /// <param name="stiffness">Spring constant (already scaled by any per-instance factor, e.g. hover's N²).</param>
        /// <param name="damping">Damping coefficient (already scaled to match).</param>
        /// <param name="displacement">Current distance from the braced surface (hover: gap to ground).</param>
        /// <param name="target">Rest distance the spring drives toward (hover: target altitude).</param>
        /// <param name="velocity">Velocity along the spring axis, positive = extending / moving away from the surface.</param>
        /// <returns>Force magnitude ≥ 0 to apply along the spring's push axis.</returns>
        public static float HookeDamped(float stiffness, float damping, float displacement, float target, float velocity)
        {
            float force = stiffness * (target - displacement) - damping * velocity;
            return force > 0f ? force : 0f;
        }

        /// <summary>
        /// Resolve an effective impulse strength, preferring the per-block
        /// server-authoritative <paramref name="configValue"/> when it is
        /// positive, else falling back to <paramref name="defaultImpulse"/>.
        /// Mirrors the <c>ThrusterBlock.MaxThrust</c> / <c>ConfigValue &gt; 0
        /// ? ConfigValue : default</c> pattern so spring strength rides the
        /// blueprint (invariant #1) rather than a gameplay Tweakable.
        /// </summary>
        public static float ResolveImpulse(float configValue, float defaultImpulse)
            => configValue > 0f ? configValue : defaultImpulse;
    }
}
