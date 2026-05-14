using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure-data validation of a <see cref="BlueprintPlan"/>. Catches
    /// shape problems (no CPU, duplicate cells, orphans, unknown block
    /// ids) before a chassis is built — saves you running the game to
    /// notice that the rotor's mechanism cell is missing.
    /// </summary>
    /// <remarks>
    /// Runs on the data only; doesn't touch any Unity scene state. Safe
    /// to call from edit-mode tests.
    /// </remarks>
    public static class BlueprintValidator
    {
        public static BlueprintValidationResult Validate(BlueprintPlan plan, BlockDefinitionLibrary library = null)
        {
            BlueprintValidationResult r = new BlueprintValidationResult();
            if (plan.Entries.Length == 0)
            {
                r.AddError("Blueprint has no entries.");
                return r;
            }

            // 1. CPU presence + duplicate cells.
            int cpuCount = 0;
            Vector3Int cpuPos = default;
            HashSet<Vector3Int> seen = new HashSet<Vector3Int>();
            foreach (ChassisBlueprint.Entry e in plan.Entries)
            {
                if (e.BlockId == BlockIds.Cpu) { cpuCount++; cpuPos = e.Position; }
                if (!seen.Add(e.Position))
                    r.AddError($"Duplicate cell at {e.Position} (id '{e.BlockId}').");
            }
            if (cpuCount == 0) r.AddError("Blueprint has no CPU.");
            else if (cpuCount > 1) r.AddWarning($"Blueprint has {cpuCount} CPUs (only the last placed is authoritative for connectivity).");

            // 2. Unknown block ids (only if a library was supplied).
            if (library != null)
            {
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    if (!library.Contains(e.BlockId))
                        r.AddError($"Unknown block id '{e.BlockId}' at {e.Position}.");
                }
            }

            // 3. CPU connectivity via face-adjacency BFS, with the
            //    rope-bridge virtual edge (rope.cell ↔ rope.tipCell) so
            //    a Hook / Mace at the chain's free end resolves as
            //    reachable through the rope it's attached to. Same
            //    primitive the runtime placement / removal paths use.
            if (cpuCount >= 1)
            {
                HashSet<Vector3Int> positions = new HashSet<Vector3Int>(plan.Entries.Length);
                Dictionary<Vector3Int, ChassisBlueprint.Entry> entriesByCell =
                    new Dictionary<Vector3Int, ChassisBlueprint.Entry>(plan.Entries.Length);
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    positions.Add(e.Position);
                    entriesByCell[e.Position] = e;
                }
                BlockGraph.Buffers buffers = new BlockGraph.Buffers();
                BlockGraph.BfsFrom(positions, entriesByCell, cpuPos, buffers);
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    if (!buffers.Visited.Contains(e.Position))
                        r.AddError($"Cell {e.Position} ({e.BlockId}) is not connected to the CPU via face-adjacency.");
                }
            }

            // 4. Per-entry placement rules: host-exists, host-is-not-leaf,
            //    side-mount face. These are the same rules
            //    BlockEditor.IsValidPlacement enforces at click time, so a
            //    blueprint loaded from disk that violates any of them
            //    can't quietly slip past the editor's "what's a legal
            //    placement" gate. Library is required to look up
            //    definitions (we need IsLeaf / RequiresSideMount).
            if (library != null)
            {
                Dictionary<Vector3Int, ChassisBlueprint.Entry> byCell = new Dictionary<Vector3Int, ChassisBlueprint.Entry>(plan.Entries.Length);
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    // SetEntries' canonical sort guarantees stable iteration;
                    // duplicate-cell entries are already an error from rule 1
                    // so the last-write-wins behaviour here is harmless.
                    byCell[e.Position] = e;
                }
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    if (e.BlockId == BlockIds.Cpu) continue; // CPU has no host
                    BlockDefinition def = library.Get(e.BlockId);
                    if (def == null) continue;             // covered by rule 2

                    // Mount-face: side-mount-only blocks (wheels) reject ±Y up.
                    if (!BlockConnectivity.IsValidMountFace(def, e.EffectiveUp))
                    {
                        r.AddError(
                            $"Cell {e.Position} ({e.BlockId}) has invalid mount face " +
                            $"up={e.EffectiveUp} for a side-mount-only block.");
                    }

                    // Host-exists + host-face-is-connective. Cells whose
                    // host is outside the blueprint are caught by rule 3
                    // (connectivity), but we also flag them here for a
                    // more specific message. Per-face check via
                    // IsConnectiveFace so the rotor's spin-axis face
                    // (legitimate mechanism-cube mount point) doesn't
                    // false-positive as a leaf rejection.
                    Vector3Int hostCell = e.Position - e.EffectiveUp;
                    if (byCell.TryGetValue(hostCell, out ChassisBlueprint.Entry hostEntry))
                    {
                        BlockDefinition hostDef = library.Get(hostEntry.BlockId);
                        if (hostDef != null && !BlockConnectivity.IsConnectiveFace(hostDef, hostEntry.EffectiveUp, e.EffectiveUp))
                        {
                            r.AddError(
                                $"Cell {e.Position} ({e.BlockId}) is hosted on cell " +
                                $"{hostCell} ({hostEntry.BlockId}) which doesn't accept " +
                                $"a mount on that face — leaf block, or non-spin-axis face on a rotor.");
                        }
                    }
                }
            }

            // 5. Swept-volume overlap. Default-dim blocks are unit cubes that
            //    fit their cell, so this only matters for scalable parts
            //    (foils today, more in later phases) whose extent depends on
            //    Dims. Cell size of 1.0 because overlap is scale-invariant —
            //    BlockOccupancy answers in cell-local units and the result
            //    holds for whatever cellSize the chassis ships with.
            ChassisBlueprint.Entry[] entries = plan.Entries;
            int n = entries.Length;
            Bounds[] bounds = new Bounds[n];
            for (int i = 0; i < n; i++)
            {
                bounds[i] = BlockOccupancy.ComputeSweptBoundsLocal(
                    entries[i].BlockId, entries[i].Position, entries[i].EffectiveUp,
                    entries[i].Dims, cellSize: 1f);
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (BlockOccupancy.StrictOverlap(bounds[i], bounds[j]))
                    {
                        r.AddError(
                            $"Cell {entries[i].Position} ({entries[i].BlockId}) overlaps " +
                            $"cell {entries[j].Position} ({entries[j].BlockId}).");
                    }
                }
            }

            // 6. Pitch range. Soft warning past ±18° (stall margin per
            //    AeroSurfaceBlock._stallAoA = 0.35 rad ≈ 20°), hard error
            //    past ±20° so blueprints can't author pitches that the
            //    physics will silently clamp anyway.
            for (int i = 0; i < n; i++)
            {
                float p = entries[i].Pitch;
                float absP = Mathf.Abs(p);
                if (absP > PitchHardLimitDeg)
                {
                    r.AddError(
                        $"Cell {entries[i].Position} ({entries[i].BlockId}) has pitch " +
                        $"{p:F1}° beyond the ±{PitchHardLimitDeg:F0}° hard limit.");
                }
                else if (absP > PitchSoftLimitDeg)
                {
                    r.AddWarning(
                        $"Cell {entries[i].Position} ({entries[i].BlockId}) has pitch " +
                        $"{p:F1}° in the stall margin (±{PitchSoftLimitDeg:F0}° soft limit).");
                }
            }

            return r;
        }

        public const float PitchSoftLimitDeg = 18f;
        public const float PitchHardLimitDeg = 20f;
    }

    /// <summary>
    /// Result of <see cref="BlueprintValidator.Validate"/>. <see cref="IsValid"/>
    /// is true iff <see cref="Errors"/> is empty; warnings don't gate validity.
    /// </summary>
    public sealed class BlueprintValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public bool IsValid => Errors.Count == 0;

        public void AddError(string message) => Errors.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);

        public override string ToString()
        {
            if (Errors.Count == 0 && Warnings.Count == 0) return "OK";
            StringBuilder sb = new StringBuilder();
            if (Errors.Count > 0)
            {
                sb.AppendLine($"Errors ({Errors.Count}):");
                foreach (string err in Errors) sb.AppendLine($"  - {err}");
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine($"Warnings ({Warnings.Count}):");
                foreach (string warn in Warnings) sb.AppendLine($"  - {warn}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
