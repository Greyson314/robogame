using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Per-block connectivity rules: which blocks can host other blocks
    /// on their faces, and which are "leaves" with no connective faces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Robogame's build-mode rule (mirrors Robocraft): you cannot place
    /// a block on top of a wing, weapon, thruster, or other specialty
    /// block. Those blocks have one mount face (the one they themselves
    /// use to attach to a host) and zero connective faces — nothing
    /// builds on them.
    /// </para>
    /// <para>
    /// The authoritative source is <see cref="BlockDefinition.IsLeafBlockRaw"/>
    /// on the SO. The hardcoded id list below is a defensive fallback so
    /// shipped assets without the flag still behave correctly — flagged
    /// here rather than scattered through the placement code so future
    /// scalable parts (Phase 2 wheels, Phase 4 panels, …) only need to
    /// register here.
    /// </para>
    /// </remarks>
    public static class BlockConnectivity
    {
        // Block ids that are leaves regardless of their SO flag. Lets us
        // ship the rule without having to re-author every preset asset.
        private static readonly HashSet<string> s_hardcodedLeafIds = new()
        {
            BlockIds.Aero,
            BlockIds.AeroFin,
            BlockIds.Thruster,
            BlockIds.Rudder,
            BlockIds.Rotor,
            BlockIds.Weapon,
            BlockIds.Cannon,
            BlockIds.BombBay,
            BlockIds.Hook,
            BlockIds.Mace,
            BlockIds.Wheel,
            BlockIds.WheelSteer,
            BlockIds.Rope,
        };

        /// <summary>True if the definition's faces are all non-connective —
        /// i.e. nothing can be placed using this block as a host.</summary>
        public static bool IsLeaf(BlockDefinition def)
        {
            if (def == null) return false;
            if (def.IsLeafBlockRaw) return true;
            return s_hardcodedLeafIds.Contains(def.Id);
        }

        /// <summary>
        /// Lookup by stable id (when the BlockBehaviour reference isn't
        /// at hand — e.g. validating a blueprint plan pre-instantiation).
        /// </summary>
        public static bool IsLeafId(string blockId) => s_hardcodedLeafIds.Contains(blockId);

        // -----------------------------------------------------------------
        // Mount-face constraints
        // -----------------------------------------------------------------

        // Block ids that must mount on a side face (chassis ±X or ±Z).
        // Used for wheels: the stem is horizontal, so a top / bottom mount
        // would point the stem straight up or down. Hardcoded fallback so
        // shipped wheel assets work without re-authoring the SO.
        private static readonly HashSet<string> s_hardcodedSideMountOnlyIds = new()
        {
            BlockIds.Wheel,
            BlockIds.WheelSteer,
        };

        /// <summary>
        /// True if this block can only mount on side faces of a host
        /// (chassis ±X / ±Z, never ±Y). Caller is responsible for
        /// rejecting placements with up=±Y when this returns true.
        /// </summary>
        public static bool RequiresSideMount(BlockDefinition def)
        {
            if (def == null) return false;
            if (def.SideMountOnlyRaw) return true;
            return s_hardcodedSideMountOnlyIds.Contains(def.Id);
        }

        /// <summary>True if <paramref name="up"/> is a side-face direction (±X or ±Z, not ±Y).</summary>
        public static bool IsSideMountFace(Vector3Int up)
        {
            return up.y == 0 && (up.x != 0 || up.z != 0);
        }

        /// <summary>
        /// Combined check: would placing this block with this mount-up
        /// satisfy the block's mount-face constraint? Returns true if
        /// the block has no constraint OR the up is a valid side face.
        /// </summary>
        public static bool IsValidMountFace(BlockDefinition def, Vector3Int up)
        {
            if (!RequiresSideMount(def)) return true;
            return IsSideMountFace(up);
        }
    }
}
