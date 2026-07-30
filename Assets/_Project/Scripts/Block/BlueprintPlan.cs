using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Immutable data tuple describing a chassis layout. Either the
    /// editor scaffolder writes this into a <see cref="ChassisBlueprint"/>
    /// asset on disk, or runtime code calls <see cref="ToBlueprint"/> to
    /// get an in-memory ScriptableObject instance for spawning.
    /// </summary>
    /// <remarks>
    /// Split out of <c>BlueprintBuilder</c> when the raw builder moved to
    /// the EditMode test assembly: the plan type is live production data
    /// (validator, ASCII dump, scaffolder), while raw entry authoring that
    /// bypasses the rules engine is a test-only concern.
    /// </remarks>
    public readonly struct BlueprintPlan
    {
        public readonly string DisplayName;
        public readonly ChassisKind Kind;
        public readonly ChassisBlueprint.Entry[] Entries;
        public readonly bool RotorsGenerateLift;

        public BlueprintPlan(string displayName, ChassisKind kind,
            ChassisBlueprint.Entry[] entries, bool rotorsGenerateLift)
        {
            DisplayName = displayName;
            Kind = kind;
            Entries = entries ?? Array.Empty<ChassisBlueprint.Entry>();
            RotorsGenerateLift = rotorsGenerateLift;
        }

        /// <summary>
        /// Materialise this plan into an in-memory <see cref="ChassisBlueprint"/>.
        /// The result is NOT persisted to disk — call this only when you
        /// want a runtime instance (e.g. test scaffolds, garage previews).
        /// </summary>
        public ChassisBlueprint ToBlueprint()
        {
            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = DisplayName;
            bp.Kind = Kind;
            bp.SetEntries(Entries);
            bp.RotorsGenerateLift = RotorsGenerateLift;
            return bp;
        }
    }
}
