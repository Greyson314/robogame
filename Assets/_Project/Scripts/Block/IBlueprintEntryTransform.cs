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
    /// See §3a of <c>docs/BUILDING_ARCHITECTURE_REVIEW.md</c> for the
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
            => new ChassisBlueprint.Entry(
                t.TransformBlockId(in source),
                t.TransformPosition(in source),
                t.TransformUp(in source),
                t.TransformDims(in source),
                t.TransformPitch(in source));
    }
}
