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

        /// <summary>
        /// Per-face connectivity: would the host accept a new block
        /// mounting on the face whose normal points along
        /// <paramref name="placementUp"/>? Used by the blueprint
        /// validator (no live grid). The richer runtime check is
        /// <see cref="AcceptsPlacement"/>, which also vets the
        /// placement's block id and looks at the host's own host
        /// (e.g. mechanism cube identity).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Default rule: non-leaves accept any face; leaves accept none.
        /// Per-block exceptions live in this method so the placement
        /// rules engine has one entry point for the question.
        /// </para>
        /// <para>
        /// <b>Rotor exception.</b> The rotor is a "leaf" for its lateral
        /// faces (you can't mount a wing on a rotor's side) but its
        /// spin-axis face IS the natural host for a structural
        /// mechanism cube.
        /// </para>
        /// <para>
        /// <b>Rope exception.</b> The rope's lateral and top faces
        /// are leaf, but its tip face (opposite the mount-up) IS the
        /// natural host for a hook / mace tip block.
        /// </para>
        /// </remarks>
        public static bool IsConnectiveFace(BlockDefinition hostDef, Vector3Int hostUp, Vector3Int placementUp)
        {
            if (!IsLeaf(hostDef)) return true;
            if (hostDef == null) return false;
            Vector3Int up = hostUp == Vector3Int.zero ? Vector3Int.up : hostUp;
            if (hostDef.Id == BlockIds.Rotor) return placementUp == up;
            // Rope's tip face = the chain's free end direction = +mount-up
            // (per session 52's chain redesign — chain extends OUTWARD
            // from the chassis face, so the tip is at +up, not -up).
            if (hostDef.Id == BlockIds.Rope)  return placementUp == up;
            return false;
        }

        /// <summary>
        /// Tri-state result for <see cref="AcceptsPlacement"/>. Maps
        /// to <see cref="Robogame.Block.PlacementRules.PlacementError"/>
        /// at the call site.
        /// </summary>
        public enum AcceptDecision
        {
            None,
            HostIsLeaf,
            HostFaceRejectsBlockType,
        }

        /// <summary>
        /// Runtime placement gate that <see cref="IsConnectiveFace"/>
        /// can't fully express because it depends on grid context
        /// ("is this cube hosted on a rotor below?") and on the
        /// placement block id ("rope's tip face only accepts hook /
        /// mace"). Used by <c>PlacementRules.CheckHostIsConnective</c>
        /// in the build editor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Mechanism cube rule.</b> A cube whose own host is a rotor
        /// on its spin-axis face only accepts aero blocks on the four
        /// lateral faces. Anything else placed there wouldn't be
        /// adopted by the rotor's adoption pass (only AeroSurfaceBlocks
        /// are), so the player would end up with a static block sitting
        /// next to a spinning rotor — visually broken.
        /// </para>
        /// <para>
        /// <b>Rope tip rule.</b> The rope's tip face accepts only hook
        /// or mace tip blocks; the rope's adoption pass looks for
        /// TipBlock components specifically.
        /// </para>
        /// </remarks>
        public static AcceptDecision AcceptsPlacement(
            BlockGrid grid,
            BlockBehaviour host,
            Vector3Int placementUp,
            BlockDefinition placementDef)
        {
            if (host == null || host.Definition == null) return AcceptDecision.HostIsLeaf;
            BlockDefinition hostDef = host.Definition;

            // Mechanism-cube rule (non-leaf host, lateral-face restriction).
            if (hostDef.Id == BlockIds.Cube && grid != null)
            {
                Vector3Int cellBelow = host.GridPosition - host.Up;
                if (grid.TryGetBlock(cellBelow, out BlockBehaviour below) && below != null
                    && below.Definition != null && below.Definition.Id == BlockIds.Rotor
                    && below.Up == host.Up)
                {
                    int dot = placementUp.x * host.Up.x + placementUp.y * host.Up.y + placementUp.z * host.Up.z;
                    bool lateral = (dot == 0);
                    if (lateral)
                    {
                        if (placementDef == null) return AcceptDecision.HostFaceRejectsBlockType;
                        // Aero blades adopt cleanly into the rotor's
                        // kinematic hub. Ropes also adopt — rule of cool
                        // per the user — and provide a centrifugal-chain
                        // effect when the rotor spins.
                        if (placementDef.Id != BlockIds.Aero
                            && placementDef.Id != BlockIds.AeroFin
                            && placementDef.Id != BlockIds.Rope)
                            return AcceptDecision.HostFaceRejectsBlockType;
                    }
                }
            }

            // Past the mechanism-cube guard, non-leaves accept any face.
            if (!IsLeaf(hostDef)) return AcceptDecision.None;

            // Leaf-host exceptions:
            Vector3Int up = host.Up == Vector3Int.zero ? Vector3Int.up : host.Up;
            if (hostDef.Id == BlockIds.Rotor)
            {
                return placementUp == up ? AcceptDecision.None : AcceptDecision.HostIsLeaf;
            }
            if (hostDef.Id == BlockIds.Rope)
            {
                // Rope's tip face = +mount-up (chain's free end side)
                // per session 52's redesign. Was -up under the old
                // chain-extends-toward-chassis convention.
                if (placementUp != up) return AcceptDecision.HostIsLeaf;
                if (placementDef == null) return AcceptDecision.HostFaceRejectsBlockType;
                bool isTip = placementDef.Id == BlockIds.Hook || placementDef.Id == BlockIds.Mace;
                return isTip ? AcceptDecision.None : AcceptDecision.HostFaceRejectsBlockType;
            }
            return AcceptDecision.HostIsLeaf;
        }

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
