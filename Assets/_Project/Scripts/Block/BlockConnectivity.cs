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
    /// The authoritative source is the SO flag set
    /// (<see cref="BlockDefinition.IsLeafBlockRaw"/> and siblings),
    /// authored per definition by <c>BlockDefinitionWizard</c> — the old
    /// hardcoded fallback lists are gone (ADR-0008). A new block
    /// classifies by wizard arguments, not by registering here.
    /// </para>
    /// </remarks>
    public static class BlockConnectivity
    {
        /// <summary>True if the definition's faces are all non-connective —
        /// i.e. nothing can be placed using this block as a host. Reads the
        /// authored SO flag only (ADR-0008 — the hardcoded fallback list is
        /// gone; BlockDefinitionWizard authors the flag on every asset).</summary>
        public static bool IsLeaf(BlockDefinition def)
        {
            return def != null && def.IsLeafBlockRaw;
        }

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
            // Companion exception (ADR-0008): a leaf that declares a
            // companion keeps its +mount-up face connective — that face is
            // where the companion sits (rotor → mechanism cube).
            if (hostDef.HasCompanion) return placementUp == up;
            // Rope's tip face = the chain's free end direction = +mount-up
            // (per session 52's chain redesign — chain extends OUTWARD
            // from the chassis face, so the tip is at +up, not -up).
            // Single-block semantics, stays code until a second chain
            // block needs the hook (ADR-0008).
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

            // Companion-block rule (ADR-0008, non-leaf host, lateral-face
            // restriction): when the host is some block's auto-placed
            // companion — the owner sits one cell along the host's
            // mount-up and declares this id as its companion — lateral
            // faces accept only the owner's declared attach list (the
            // rotor authors its blade/rope ring: aero foils adopt into
            // the kinematic hub; ropes adopt for the centrifugal chain).
            if (grid != null)
            {
                Vector3Int cellBelow = host.GridPosition - host.Up;
                if (grid.TryGetBlock(cellBelow, out BlockBehaviour below) && below != null
                    && below.Definition != null
                    && below.Definition.HasCompanion
                    && below.Definition.CompanionBlockId == hostDef.Id
                    && below.Up == host.Up)
                {
                    int dot = placementUp.x * host.Up.x + placementUp.y * host.Up.y + placementUp.z * host.Up.z;
                    bool lateral = (dot == 0);
                    if (lateral)
                    {
                        if (placementDef == null) return AcceptDecision.HostFaceRejectsBlockType;
                        var allowed = below.Definition.CompanionLateralAttachIds;
                        bool accepted = false;
                        for (int i = 0; i < allowed.Count; i++)
                        {
                            if (allowed[i] == placementDef.Id) { accepted = true; break; }
                        }
                        if (!accepted) return AcceptDecision.HostFaceRejectsBlockType;
                    }
                }
            }

            // Past the mechanism-cube guard, non-leaves accept any face.
            if (!IsLeaf(hostDef)) return AcceptDecision.None;

            // Leaf-host exceptions:
            Vector3Int up = host.Up == Vector3Int.zero ? Vector3Int.up : host.Up;
            // Companion exception (ADR-0008): +mount-up face stays open —
            // that's where the declared companion sits.
            if (hostDef.HasCompanion)
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
                return BlockIds.IsTipId(placementDef.Id)
                    ? AcceptDecision.None
                    : AcceptDecision.HostFaceRejectsBlockType;
            }
            return AcceptDecision.HostIsLeaf;
        }

        // -----------------------------------------------------------------
        // Mount-face constraints
        // -----------------------------------------------------------------

        /// <summary>
        /// True if this block can only mount on side faces of a host
        /// (chassis ±X / ±Z, never ±Y). Caller is responsible for
        /// rejecting placements with up=±Y when this returns true.
        /// Authored SO flag only (ADR-0008).
        /// </summary>
        public static bool RequiresSideMount(BlockDefinition def)
        {
            return def != null && def.SideMountOnlyRaw;
        }

        /// <summary>True if <paramref name="up"/> is a side-face direction (±X or ±Z, not ±Y).</summary>
        public static bool IsSideMountFace(Vector3Int up)
        {
            return up.y == 0 && (up.x != 0 || up.z != 0);
        }

        /// <summary>
        /// True if this block can only mount on the top face of a host
        /// (chassis +Y). Caller rejects placements with up != +Y when true.
        /// Authored SO flag only (ADR-0008; the mortar is the model case —
        /// its tube fires upward into a lob).
        /// </summary>
        public static bool RequiresTopMount(BlockDefinition def)
        {
            return def != null && def.TopMountOnlyRaw;
        }

        /// <summary>
        /// Combined check: would placing this block with this mount-up
        /// satisfy the block's mount-face constraint? Returns true if the
        /// block has no constraint OR the up matches its required face.
        /// </summary>
        public static bool IsValidMountFace(BlockDefinition def, Vector3Int up)
        {
            if (RequiresSideMount(def)) return IsSideMountFace(up);
            if (RequiresTopMount(def))  return up == Vector3Int.up;
            return true;
        }
    }
}
