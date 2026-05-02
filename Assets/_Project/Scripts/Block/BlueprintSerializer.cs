using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure (no I/O) JSON round-trip for <see cref="ChassisBlueprint"/>.
    /// Decoupled from disk so it's easy to share over network, paste from
    /// clipboard, embed in Base64 links, etc. <see cref="UserBlueprintLibrary"/>
    /// is the file-system layer on top of this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Schema is intentionally explicit (one DTO per record) rather than
    /// relying on Unity-internal JsonUtility serialization of the SO. That
    /// keeps the on-disk shape stable across refactors of
    /// <see cref="ChassisBlueprint"/> and gives us a single
    /// <c>schemaVersion</c> knob for forward migration.
    /// </para>
    /// <para>
    /// v1 schema:
    /// <code>
    /// {
    ///   "schemaVersion": 1,
    ///   "displayName": "My Robot",
    ///   "kind": "Ground",          // ChassisKind enum name
    ///   "createdUtc": "2025-04-30T18:42:11Z",
    ///   "entries": [{ "id": "block.cpu.standard", "x": 0, "y": 0, "z": 0 }, ...]
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static class BlueprintSerializer
    {
        public const int CurrentSchemaVersion = 1;

        // -----------------------------------------------------------------
        // DTOs (private — JsonUtility needs concrete [Serializable] types)
        // -----------------------------------------------------------------

        [Serializable]
        private struct Dto
        {
            public int schemaVersion;
            public string displayName;
            public string kind;
            public string createdUtc;
            public EntryDto[] entries;
        }

        [Serializable]
        private struct EntryDto
        {
            public string id;
            public int x;
            public int y;
            public int z;
        }

        // -----------------------------------------------------------------
        // Serialize
        // -----------------------------------------------------------------

        /// <summary>
        /// Serialize a blueprint to JSON. Pretty-printed by default so the
        /// files are diff-friendly and human-readable on disk.
        /// </summary>
        public static string ToJson(ChassisBlueprint blueprint, bool prettyPrint = true)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

            ChassisBlueprint.Entry[] src = blueprint.Entries ?? Array.Empty<ChassisBlueprint.Entry>();
            var entries = new EntryDto[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                entries[i] = new EntryDto
                {
                    id = src[i].BlockId,
                    x = src[i].Position.x,
                    y = src[i].Position.y,
                    z = src[i].Position.z,
                };
            }

            var dto = new Dto
            {
                schemaVersion = CurrentSchemaVersion,
                displayName = string.IsNullOrEmpty(blueprint.DisplayName) ? "Untitled" : blueprint.DisplayName,
                kind = blueprint.Kind.ToString(),
                createdUtc = DateTime.UtcNow.ToString("o"),
                entries = entries,
            };
            return JsonUtility.ToJson(dto, prettyPrint);
        }

        // -----------------------------------------------------------------
        // Deserialize
        // -----------------------------------------------------------------

        /// <summary>
        /// Try to parse a JSON blueprint into a fresh runtime
        /// <see cref="ChassisBlueprint"/> ScriptableObject. Returns
        /// <c>true</c> on success, populates <paramref name="error"/> with
        /// a human-readable message on failure.
        /// </summary>
        public static bool TryFromJson(string json, out ChassisBlueprint blueprint, out string error)
        {
            blueprint = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty JSON.";
                return false;
            }

            Dto dto;
            try
            {
                dto = JsonUtility.FromJson<Dto>(json);
            }
            catch (Exception e)
            {
                error = "Malformed JSON: " + e.Message;
                return false;
            }

            if (dto.schemaVersion <= 0)
            {
                error = "Missing or invalid schemaVersion.";
                return false;
            }
            if (dto.schemaVersion > CurrentSchemaVersion)
            {
                error = $"Blueprint schema v{dto.schemaVersion} is newer than this build (v{CurrentSchemaVersion}). Update the game?";
                return false;
            }
            // Future-proofing: if we bump the version, place migration here
            // before the per-version copy out below.

            ChassisKind kind = ChassisKind.Ground;
            if (!string.IsNullOrEmpty(dto.kind) && !Enum.TryParse(dto.kind, ignoreCase: true, out kind))
            {
                error = $"Unknown chassis kind '{dto.kind}'.";
                return false;
            }

            EntryDto[] dtoEntries = dto.entries ?? Array.Empty<EntryDto>();
            var copy = new List<ChassisBlueprint.Entry>(dtoEntries.Length);
            for (int i = 0; i < dtoEntries.Length; i++)
            {
                EntryDto e = dtoEntries[i];
                if (string.IsNullOrEmpty(e.id))
                {
                    error = $"Entry [{i}] has no block id.";
                    return false;
                }
                copy.Add(new ChassisBlueprint.Entry(e.id, new Vector3Int(e.x, e.y, e.z)));
            }

            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.name = string.IsNullOrEmpty(dto.displayName) ? "Untitled (Loaded)" : dto.displayName + " (Loaded)";
            bp.DisplayName = string.IsNullOrEmpty(dto.displayName) ? "Untitled" : dto.displayName;
            bp.Kind = kind;
            bp.SetEntries(copy.ToArray());

            blueprint = bp;
            return true;
        }
    }
}
