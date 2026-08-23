namespace Robogame.Block
{
    /// <summary>
    /// Which key → intent mapping a chassis uses. The scheme fills the
    /// six <c>DriveIntent</c> demands (surge / sway / heave / pitch / roll /
    /// yaw) from the player's three raw axes; blocks then serve whichever
    /// demands they can physically affect. <see cref="Auto"/> derives the
    /// scheme from the blueprint's composition at spawn
    /// (<see cref="ControlSchemes.Resolve"/>); the other values are an
    /// explicit per-blueprint override (server-authoritative, frozen at
    /// match start like every other blueprint field).
    /// </summary>
    // TRACE[ADR-0009]: the scheme is the ONLY place keys are interpreted —
    // no drive subsystem reads raw keys by chassis category any more.
    public enum ControlScheme : byte
    {
        Auto       = 0,
        Ground     = 1,
        Plane      = 2,
        Helicopter = 3,
    }

    /// <summary>
    /// Blueprint-composition → concrete <see cref="ControlScheme"/>.
    /// Pure data; same answer on every peer because it reads only the
    /// frozen blueprint (ids + mount axes), never runtime state.
    /// </summary>
    public static class ControlSchemes
    {
        /// <summary>
        /// Resolve <paramref name="blueprint"/>'s scheme to a concrete
        /// (non-<see cref="ControlScheme.Auto"/>) value.
        /// <list type="number">
        ///   <item>An explicit override on the blueprint wins.</item>
        ///   <item>A lift rotor (vertical-axis rotor with
        ///   <see cref="ChassisBlueprint.RotorsGenerateLift"/>) and no forward
        ///   thrust → <see cref="ControlScheme.Helicopter"/>.</item>
        ///   <item><see cref="ChassisKind.Plane"/> → <see cref="ControlScheme.Plane"/>.</item>
        ///   <item>Aero surfaces with forward thrust, or aero surfaces on a
        ///   chassis with no wheels / hover pads → Plane (a tank with a
        ///   spoiler stays Ground; a glider or a winged prop-car is a plane).</item>
        ///   <item>Otherwise <see cref="ControlScheme.Ground"/>.</item>
        /// </list>
        /// <paramref name="hasWheels"/> / <paramref name="hasHover"/> /
        /// <paramref name="hasAero"/> come from the definitions'
        /// <c>DriveSubsystemNeed</c> (ADR-0008); thruster / rotor presence is
        /// read off the entries by id because their mount AXIS matters.
        /// </summary>
        public static ControlScheme Resolve(ChassisBlueprint blueprint, bool hasWheels, bool hasHover, bool hasAero)
        {
            if (blueprint == null) return ControlScheme.Ground;
            if (blueprint.ControlScheme != ControlScheme.Auto) return blueprint.ControlScheme;

            bool forwardThrust = false, liftRotor = false;
            ChassisBlueprint.Entry[] entries = blueprint.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                string id = entries[i].BlockId;
                if (id == BlockIds.Thruster) { forwardThrust = true; continue; }
                if (id != BlockIds.Rotor) continue;
                UnityEngine.Vector3Int up = entries[i].EffectiveUp;
                if (up.z != 0) forwardThrust = true;
                else if (up.y != 0 && blueprint.RotorsGenerateLift) liftRotor = true;
            }

            if (liftRotor && !forwardThrust) return ControlScheme.Helicopter;
            if (blueprint.Kind == ChassisKind.Plane) return ControlScheme.Plane;
            if (hasAero && (forwardThrust || !(hasWheels || hasHover))) return ControlScheme.Plane;
            return ControlScheme.Ground;
        }

        /// <summary>
        /// Id-based convenience for callers without a definition library
        /// (hand-built test chassis, <c>RobotDrive</c>'s lazy fallback).
        /// Classifies wheels / hover / aero by <see cref="BlockIds"/>.
        /// </summary>
        public static ControlScheme ResolveFromIds(ChassisBlueprint blueprint)
        {
            if (blueprint == null) return ControlScheme.Ground;
            bool wheels = false, hover = false, aero = false;
            ChassisBlueprint.Entry[] entries = blueprint.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                string id = entries[i].BlockId;
                if (id == BlockIds.Wheel || id == BlockIds.WheelSteer) wheels = true;
                else if (id == BlockIds.HoverBlade) hover = true;
                else if (AeroShape.IsAeroId(id)) aero = true;
            }
            return Resolve(blueprint, wheels, hover, aero);
        }
    }
}
