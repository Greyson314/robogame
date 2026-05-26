using Robogame.Core;

namespace Robogame.Block
{
    /// <summary>
    /// Dev-build-only override layer for the chassis-level tuning configs
    /// (<see cref="PlaneTuningConfig"/> / <see cref="GroundTuningConfig"/> /
    /// <see cref="ChassisDampingConfig"/> / <see cref="ThrusterTuningConfig"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Session 85 migrated every gameplay-observable
    /// chassis tuning knob off the per-machine <see cref="Tweakables"/>
    /// onto the server-authoritative <see cref="ChassisBlueprint"/> to
    /// satisfy invariant #1 (no Tweakable affects gameplay outcomes; would
    /// desync the moment MP lands). That migration removed the familiar
    /// pitch / roll / thrust sliders from the in-game settings menu.
    /// </para>
    /// <para>
    /// <b>What it does.</b> Re-registers the same Tweakables as dev-only
    /// overrides — when the master <see cref="Tweakables.DevOverrideChassisTuning"/>
    /// flag is true, the consumer reads the Tweakable value instead of
    /// the blueprint value at <c>OnEnable</c> + on every
    /// <see cref="Tweakables.Changed"/> event. The override path is
    /// compile-stripped from shipping builds (<c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c>),
    /// so the shipped binary has zero MP-desync risk — the override
    /// path simply does not exist.
    /// </para>
    /// <para>
    /// <b>How consumers use it.</b> After resolving their tuning from the
    /// blueprint, they call the matching <c>Apply*</c> method by
    /// <c>ref</c>; the helper mutates the config in place when the
    /// override is active, leaves it alone otherwise. They should also
    /// subscribe to <see cref="Tweakables.Changed"/> and re-resolve so
    /// the slider drag updates live.
    /// </para>
    /// </remarks>
    public static class DevTuningOverride
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool Active => Tweakables.GetBool(Tweakables.DevOverrideChassisTuning);
#endif

        public static void ApplyPlane(ref PlaneTuningConfig cfg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            cfg.PitchPower   = Tweakables.Get(Tweakables.DevPlanePitchPower);
            cfg.RollPower    = Tweakables.Get(Tweakables.DevPlaneRollPower);
            cfg.YawFromBank  = Tweakables.Get(Tweakables.DevPlaneYawFromBank);
            cfg.PitchDamping = Tweakables.Get(Tweakables.DevPlanePitchDamping);
            cfg.RollDamping  = Tweakables.Get(Tweakables.DevPlaneRollDamping);
            cfg.YawDamping   = Tweakables.Get(Tweakables.DevPlaneYawDamping);
#endif
        }

        public static void ApplyGround(ref GroundTuningConfig cfg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            cfg.Acceleration = Tweakables.Get(Tweakables.DevGroundAcceleration);
            cfg.MaxSpeed     = Tweakables.Get(Tweakables.DevGroundMaxSpeed);
            cfg.TurnRate     = Tweakables.Get(Tweakables.DevGroundTurnRate);
#endif
        }

        public static void ApplyChassisDamping(ref ChassisDampingConfig cfg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            cfg.LinearDamping  = Tweakables.Get(Tweakables.DevChassisLinearDamping);
            cfg.AngularDamping = Tweakables.Get(Tweakables.DevChassisAngularDamping);
#endif
        }

        public static void ApplyThruster(ref ThrusterTuningConfig cfg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            cfg.IdleThrottle     = Tweakables.Get(Tweakables.DevThrusterIdleThrottle);
            cfg.ThrottleResponse = Tweakables.Get(Tweakables.DevThrusterThrottleResponse);
#endif
        }

        /// <summary>
        /// Hover blade tuning. Three baseline knobs (N=2 spring constant,
        /// damping coefficient, target altitude); the per-instance N²
        /// scaling happens inside <c>HoverBladeBlock</c>. The struct
        /// argument is passed by ref so the consumer can keep its own
        /// defaults when the master toggle is off — but
        /// <c>HoverBladeBlock</c> uses static fields, so it just calls
        /// the helpers via the out-parameter convenience overload below.
        /// </summary>
        public static void ApplyHoverBlade(ref HoverBladeTuningConfig cfg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            cfg.SpringK        = Tweakables.Get(Tweakables.DevHoverSpringK);
            cfg.DampingC       = Tweakables.Get(Tweakables.DevHoverDampingC);
            cfg.TargetAltitude = Tweakables.Get(Tweakables.DevHoverTargetAltitude);
#endif
        }
    }

    /// <summary>
    /// Tuning struct for <see cref="HoverBladeBlock"/>. Values are the
    /// N=2 baseline; per-instance N² scaling is applied at the call site.
    /// Built initialised to the shipped defaults so the override layer
    /// can mutate it in place when the master toggle is on, leave it
    /// untouched (i.e. shipped defaults) otherwise.
    /// </summary>
    public struct HoverBladeTuningConfig
    {
        public float SpringK;
        public float DampingC;
        public float TargetAltitude;

        public static HoverBladeTuningConfig Default => new HoverBladeTuningConfig
        {
            SpringK = 800f,
            DampingC = 60f,
            TargetAltitude = 2.5f,
        };
    }
}
