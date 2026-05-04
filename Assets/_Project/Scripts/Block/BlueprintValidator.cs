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
        private static readonly Vector3Int[] s_faceOffsets = new[]
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
        };

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

            // 3. CPU connectivity via face-adjacency BFS. Mirrors the
            //    runtime BlockGrid.FindDisconnectedFrom flood-fill.
            if (cpuCount >= 1)
            {
                HashSet<Vector3Int> positions = new HashSet<Vector3Int>(plan.Entries.Length);
                foreach (ChassisBlueprint.Entry e in plan.Entries) positions.Add(e.Position);
                HashSet<Vector3Int> visited = new HashSet<Vector3Int> { cpuPos };
                Queue<Vector3Int> q = new Queue<Vector3Int>();
                q.Enqueue(cpuPos);
                while (q.Count > 0)
                {
                    Vector3Int cur = q.Dequeue();
                    for (int i = 0; i < s_faceOffsets.Length; i++)
                    {
                        Vector3Int next = cur + s_faceOffsets[i];
                        if (positions.Contains(next) && visited.Add(next)) q.Enqueue(next);
                    }
                }
                foreach (ChassisBlueprint.Entry e in plan.Entries)
                {
                    if (!visited.Contains(e.Position))
                        r.AddError($"Cell {e.Position} ({e.BlockId}) is not connected to the CPU via face-adjacency.");
                }
            }

            return r;
        }
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
