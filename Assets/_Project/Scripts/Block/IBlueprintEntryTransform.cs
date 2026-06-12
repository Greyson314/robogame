using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// A pure transform that rewrites the fields of a single
    /// <see cref="ChassisBlueprint.Entry"/>. Implementing this interface
    /// (rather than open-coding "copy these fields, mutate those") gives
    /// a compile-time guarantee that every Entry field is addressed —
    /// adding a new field to <c>Entry</c> breaks every implementation
    /// until it's handled deliberately, "identity" or otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See §3a of <c>docs/research/historical/building-architecture-review.md</c> for the
    /// motivating bug class: subsystems that handle a *subset* of Entry
    /// fields and silently drop new ones at the next schema bump. Mirror
    /// dropped <c>Pitch</c>; the build-mode ghost dropped it; future
    /// schema additions would have repeated the pattern.
    /// </para>
    /// <para>
    /// Apply via <see cref="BlueprintEntryTransform.Apply"/> for the
    /// "transform every field" composition. Subsystems that genuinely
    /// only care about a subset (e.g. UI affordances) shouldn't implement
    /// this interface at all — that's a sign they aren't really
    /// transforming an Entry.
    /// </para>
    /// </remarks>
    public interface IBlueprintEntryTransform
    {
        /// <summary>Rewrite the entry's stable block id.</summary>
        string TransformBlockId(in ChassisBlueprint.Entry source);

        /// <summary>Rewrite the entry's grid cell.</summary>
        Vector3Int TransformPosition(in ChassisBlueprint.Entry source);

        /// <summary>Rewrite the entry's mount-up vector.</summary>
        Vector3Int TransformUp(in ChassisBlueprint.Entry source);

        /// <summary>Rewrite the entry's per-instance dimensions vector.</summary>
        Vector3 TransformDims(in ChassisBlueprint.Entry source);

        /// <summary>
        /// Rewrite the entry's pitch / incidence in degrees. Receives the
        /// full source so transforms whose pitch rule depends on other
        /// fields (e.g. mirror, where pitch sign depends on whether the
        /// mount-up gets reflected) have the data they need.
        /// </summary>
        float TransformPitch(in ChassisBlueprint.Entry source);

        /// <summary>
        /// Rewrite the entry's teeter tilt in degrees (chord-axis, foils
        /// only — session 123). Shares pitch's reflection parity: both are
        /// angles whose sign convention is keyed to the mount frame.
        /// </summary>
        float TransformTeeter(in ChassisBlueprint.Entry source);

        /// <summary>
        /// Rewrite the entry's server-authoritative scalar config (thrust /
        /// RPM / yaw-authority). Magnitude-only — it carries no orientation,
        /// so a reflecting transform copies it straight across.
        /// </summary>
        float TransformBlockConfig(in ChassisBlueprint.Entry source);

        /// <summary>
        /// Rewrite the entry's authored concoction id. A string tag, not a
        /// geometric quantity — a reflecting transform copies it straight
        /// across.
        /// </summary>
        string TransformConcoctionId(in ChassisBlueprint.Entry source);

        /// <summary>
        /// Rewrite the entry's yaw about the mount-up axis (0/90/180/270).
        /// Unlike pitch/teeter, yaw is a rotation about up rather than an
        /// angle keyed to a reflected mount axis, so it gets its own
        /// reflection rule (see <see cref="BlockMirror.MirrorYaw"/>) — a
        /// reflected yaw is not the source yaw.
        /// </summary>
        int TransformYaw(in ChassisBlueprint.Entry source);
    }

    /// <summary>
    /// Static helper that composes the per-field methods of an
    /// <see cref="IBlueprintEntryTransform"/> into a full
    /// <see cref="ChassisBlueprint.Entry"/> rewrite.
    /// </summary>
    public static class BlueprintEntryTransform
    {
        /// <summary>
        /// Apply <paramref name="t"/>'s per-field transforms to every
        /// field of <paramref name="source"/>, returning a new Entry.
        /// </summary>
        public static ChassisBlueprint.Entry Apply(IBlueprintEntryTransform t, in ChassisBlueprint.Entry source)
        {
            var entry = new ChassisBlueprint.Entry(
                t.TransformBlockId(in source),
                t.TransformPosition(in source),
                t.TransformUp(in source),
                t.TransformDims(in source),
                t.TransformPitch(in source));
            entry.Teeter = t.TransformTeeter(in source);
            // The 5-arg ctor leaves BlockConfig/ConcoctionId/Yaw at their
            // defaults; route them through the interface so a transform can
            // never silently drop them (the gap closed in session 124 —
            // they had drifted across schema v4/v7/v8). Yaw carries a real
            // reflection rule; the other two are orientation-free copies.
            entry.BlockConfig = t.TransformBlockConfig(in source);
            entry.ConcoctionId = t.TransformConcoctionId(in source);
            entry.Yaw = t.TransformYaw(in source);
            return entry;
        }
    }
}
