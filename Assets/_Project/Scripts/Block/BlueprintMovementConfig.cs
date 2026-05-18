using System;

namespace Robogame.Block
{
    // Server-authoritative, chassis-level drive tuning carried on the
    // blueprint. These were per-machine Tweakables (Plane.* / Ground.* /
    // Chassis.* / Thruster idle+response) — gameplay-observable movement
    // forces that must not vary per client once netcode lands
    // (PHYSICS_PLAN §1.5 / §5, hard invariant #1).
    //
    // These are [Serializable] CLASSES, not structs, on purpose: a
    // default(struct) is all-zeros, which would silently zero every
    // existing save's handling. As classes with field initializers,
    // JsonUtility instantiates an absent field with these exact
    // pre-migration Tweakable defaults, so v1–v3 saves and old
    // ChassisBlueprint .asset files load behaviour-identical.

    /// <summary>Per-chassis plane control authority + damping. Defaults equal the pre-migration Plane.* Tweakables.</summary>
    [Serializable]
    public sealed class PlaneTuningConfig
    {
        public float PitchPower   = 7.5f;
        public float RollPower    = 9.0f;
        public float YawFromBank  = 2.0f;
        public float PitchDamping = 3.5f;
        public float RollDamping  = 2.8f;
        public float YawDamping   = 1.6f;
    }

    /// <summary>Per-chassis ground drive tuning. Defaults equal the pre-migration Ground.* Tweakables.</summary>
    [Serializable]
    public sealed class GroundTuningConfig
    {
        public float Acceleration = 26.25f;
        public float MaxSpeed     = 13.5f;
        public float TurnRate     = 7.5f;
    }

    /// <summary>Per-chassis Rigidbody damping. Defaults equal the pre-migration Chassis.* Tweakables.</summary>
    [Serializable]
    public sealed class ChassisDampingConfig
    {
        public float LinearDamping  = 0.2f;
        public float AngularDamping = 2.0f;
    }

    /// <summary>
    /// Per-chassis thruster feel (idle bias + throttle response). Option A
    /// of the migration: only per-thruster MaxThrust rides the per-block
    /// Entry config; idle/response are archetype-wide feel constants.
    /// Defaults equal the pre-migration Thruster.* Tweakables.
    /// </summary>
    [Serializable]
    public sealed class ThrusterTuningConfig
    {
        public float IdleThrottle     = 0.4f;
        public float ThrottleResponse = 2.6f;
    }
}
