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
        /// <summary>
        /// True when this block exposes per-instance variant config
        /// (foil span/pitch, rope segment count, module power slider,
        /// concoction chooser, ammo multiplier). Authored SO flag only
        /// (ADR-0008) — BlockDefinitionWizard sets it per definition; the
        /// old hardcoded id list (which had already drifted: ModuleMines
        /// and ModuleRepair were missing, so their power sliders were
        /// unreachable) is gone.
        /// </summary>
        public static bool HasVariantConfig(BlockDefinition def)
        {
            return def != null && def.HasVariantConfigRaw;
        }
    }
}
