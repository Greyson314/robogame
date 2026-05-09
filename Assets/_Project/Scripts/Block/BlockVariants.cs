using System.Collections.Generic;

namespace Robogame.Block
{
    /// <summary>
    /// Schema-side query: does this block participate in the build-mode
    /// variant config UI (foil span/thickness/chord/pitch, rope segment
    /// count, rotor collective, …)? The build hotbar's "VAR" badge and
    /// the variant panel's visibility both flow from here.
    /// </summary>
    /// <remarks>
    /// Mirrors the <see cref="BlockConnectivity"/> pattern: the
    /// authoritative answer is the SO flag
    /// (<see cref="BlockDefinition.HasVariantConfigRaw"/>); the
    /// hardcoded id list below is a defensive fallback so shipped
    /// assets without the flag still behave correctly. New scalable
    /// blocks should set the flag on the SO and add an entry here for
    /// pre-asset-edit safety.
    /// </remarks>
    public static class BlockVariants
    {
        // Block ids that have variant config regardless of their SO flag.
        // Lets us ship the rule without having to re-author every preset
        // asset. Adding a new scalable block per
        // docs/SCALABLE_PARTS_PLAN.md only needs a new entry here.
        private static readonly HashSet<string> s_hardcodedVariableIds = new()
        {
            BlockIds.Aero,
            BlockIds.AeroFin,
            BlockIds.Rope,
            BlockIds.Rotor,
        };

        /// <summary>
        /// True when this block exposes per-instance variant config
        /// (foil span/pitch, rope segment count, …).
        /// </summary>
        public static bool HasVariantConfig(BlockDefinition def)
        {
            if (def == null) return false;
            if (def.HasVariantConfigRaw) return true;
            return s_hardcodedVariableIds.Contains(def.Id);
        }

        /// <summary>
        /// Lookup by stable id (when the BlockDefinition reference isn't
        /// at hand — e.g. parsing a raw blueprint payload).
        /// </summary>
        public static bool HasVariantConfigId(string blockId) => s_hardcodedVariableIds.Contains(blockId);
    }
}
