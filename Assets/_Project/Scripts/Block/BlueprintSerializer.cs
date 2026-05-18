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
    /// v1 schema (legacy):
    /// <code>
    /// {
    ///   "schemaVersion": 1,
    ///   "displayName": "My Robot",
    ///   "kind": "Ground",
    ///   "createdUtc": "2025-04-30T18:42:11Z",
    ///   "entries": [{ "id": "block.cpu.standard", "x": 0, "y": 0, "z": 0 }, ...]
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// v2 schema. Adds:
    /// <list type="bullet">
    /// <item><c>rotorsGenerateLift</c> at the top level so helicopter saves
    /// reload as helicopters (was silently dropped on save in v1).</item>
    /// <item><c>ux/uy/uz</c> per-entry mount orientation (also silently
    /// dropped in v1).</item>
    /// <item><c>dx/dy/dz</c> per-entry "variable part" dimensions —
    /// foil span/thickness/chord, rope segment count. Vector3.zero means
    /// "use block defaults". See <see cref="ChassisBlueprint.Entry.Dims"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// v3 schema. Adds <c>pitch</c> per entry — foil incidence
    /// in degrees (additive AoA offset), or rotor collective pitch in
    /// degrees. Defaults to 0 for v1/v2 entries, which keeps free-wing
    /// behaviour unchanged and falls rotors back to their SO-default
    /// collective.
    /// </para>
    /// <para>
    /// v4 schema (current). Moves gameplay-observable drive tuning off the
    /// per-machine <c>Tweakables</c> onto the server-authoritative blueprint
    /// (PHYSICS_PLAN §1.5 / §5, hard invariant #1):
    /// <list type="bullet">
    /// <item>chassis-level <c>planeTuning</c> / <c>groundTuning</c> /
    /// <c>chassisDamping</c> / <c>thrusterTuning</c> objects (was Plane.* /
    /// Ground.* / Chassis.* / Thruster idle+response Tweakables).</item>
    /// <item>per-entry <c>blockConfig</c> scalar — ThrusterBlock max thrust,
    /// RudderBlock authority, RotorBlock RPM. 0 = use the block's authored
    /// default.</item>
    /// </list>
    /// </para>
    /// <para>
    /// v1–v3 saves load behaviour-identical: the config objects' field
    /// initializers equal the historical Tweakable defaults, and absent
    /// objects are coalesced to a fresh default instance on load; absent
    /// <c>blockConfig</c> is 0 (= use block default). Block-index ordering
    /// (the netcode contract, invariant #2) is unaffected — config rides
    /// existing records, it is not part of the sort key.
    /// </para>
    /// </remarks>
    public static class BlueprintSerializer
    {
        public const int CurrentSchemaVersion = 4;

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
            public bool rotorsGenerateLift;
            // v4: server-authoritative chassis-level drive tuning. Nested
            // [Serializable] classes; absent in v1–v3 JSON and coalesced
            // to defaults on load (TryFromJson).
            public PlaneTuningConfig planeTuning;
            public GroundTuningConfig groundTuning;
            public ChassisDampingConfig chassisDamping;
            public ThrusterTuningConfig thrusterTuning;
            public EntryDto[] entries;
        }

        [Serializable]
        private struct EntryDto
        {
            public string id;
            public int x;
            public int y;
            public int z;
            // v2 additions. JsonUtility writes default-valued ints as 0,
            // so loading a v1 entry into this DTO gives ux/uy/uz = 0 — we
            // detect that pattern and snap to (0,1,0) per Entry.EffectiveUp.
            public int ux;
            public int uy;
            public int uz;
            public float dx;
            public float dy;
            public float dz;
            // v3 addition. Per-entry pitch / incidence in degrees.
            // Foils: AoA offset. Rotors: collective. 0 = use block default.
            public float pitch;
            // v4 addition. Per-entry server-authoritative scalar config:
            // Thruster max thrust / Rudder authority / Rotor RPM.
            // 0 (absent in v1–v3) = use the block's authored default.
            public float blockConfig;
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
                Vector3Int up = src[i].EffectiveUp;
                Vector3 dims = src[i].Dims;
                entries[i] = new EntryDto
                {
                    id = src[i].BlockId,
                    x = src[i].Position.x,
                    y = src[i].Position.y,
                    z = src[i].Position.z,
                    ux = up.x,
                    uy = up.y,
                    uz = up.z,
                    dx = dims.x,
                    dy = dims.y,
                    dz = dims.z,
                    pitch = src[i].Pitch,
                    blockConfig = src[i].BlockConfig,
                };
            }

            var dto = new Dto
            {
                schemaVersion = CurrentSchemaVersion,
                displayName = string.IsNullOrEmpty(blueprint.DisplayName) ? "Untitled" : blueprint.DisplayName,
                kind = blueprint.Kind.ToString(),
                createdUtc = DateTime.UtcNow.ToString("o"),
                rotorsGenerateLift = blueprint.RotorsGenerateLift,
                planeTuning = blueprint.PlaneTuning,
                groundTuning = blueprint.GroundTuning,
                chassisDamping = blueprint.ChassisDamping,
                thrusterTuning = blueprint.ThrusterTuning,
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
            // v1 → v2 migration: missing fields fall through as zero values.
            // EntryDto's per-entry up/dims default to zero, which Entry's
            // EffectiveUp + per-block defaults already handle correctly.

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
                Vector3Int up = new Vector3Int(e.ux, e.uy, e.uz);
                // v1 entries hit Entry.EffectiveUp's zero → +Y fallback;
                // v2 entries with real (0,0,0) up are invalid by definition.
                Vector3 dims = new Vector3(e.dx, e.dy, e.dz);
                copy.Add(new ChassisBlueprint.Entry(e.id, new Vector3Int(e.x, e.y, e.z), up, dims, e.pitch, e.blockConfig));
            }

            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.name = string.IsNullOrEmpty(dto.displayName) ? "Untitled (Loaded)" : dto.displayName + " (Loaded)";
            bp.DisplayName = string.IsNullOrEmpty(dto.displayName) ? "Untitled" : dto.displayName;
            bp.Kind = kind;
            bp.RotorsGenerateLift = dto.rotorsGenerateLift;
            // v1–v3 saves have no tuning objects; JsonUtility leaves the
            // struct's class fields null. Coalesce to fresh defaults whose
            // field initializers equal the historical Tweakable defaults,
            // so an old save loads behaviour-identical.
            bp.PlaneTuning = dto.planeTuning ?? new PlaneTuningConfig();
            bp.GroundTuning = dto.groundTuning ?? new GroundTuningConfig();
            bp.ChassisDamping = dto.chassisDamping ?? new ChassisDampingConfig();
            bp.ThrusterTuning = dto.thrusterTuning ?? new ThrusterTuningConfig();
            bp.SetEntries(copy.ToArray());

            blueprint = bp;
            return true;
        }
    }
}
