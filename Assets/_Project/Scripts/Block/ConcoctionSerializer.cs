using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure (no I/O) JSON round-trip for <see cref="Concoction"/>. Decoupled
    /// from disk so tests drive it without the file system;
    /// <see cref="ConcoctionLibrary"/> is the file-system layer on top.
    /// Mirrors <see cref="BlueprintSerializer"/>: one explicit DTO with a
    /// <c>schemaVersion</c> knob for forward migration.
    /// </summary>
    /// <remarks>
    /// v2 schema (current — session 141 adds speed + spread levers):
    /// <code>
    /// { "schemaVersion": 2, "id": "...", "displayName": "Dark Madder Concoction",
    ///   "damagePct": 0.5, "sizePct": 0.5, "knockbackPct": 0.5,
    ///   "speedPct": 0.5, "spreadPct": 0.5 }
    /// </code>
    /// v1 files load with the two new levers defaulted to neutral (JsonUtility
    /// zero-fills missing fields, so the loader must branch on the version —
    /// a v1 file's absent speedPct reads 0, which would silently halve speed).
    /// </remarks>
    public static class ConcoctionSerializer
    {
        public const int CurrentSchemaVersion = 2;

        [Serializable]
        private struct Dto
        {
            public int schemaVersion;
            public string id;
            public string displayName;
            public float damagePct;
            public float sizePct;
            public float knockbackPct;
            public float speedPct;
            public float spreadPct;
        }

        public static string ToJson(Concoction concoction, bool prettyPrint = true)
        {
            if (concoction == null) throw new ArgumentNullException(nameof(concoction));
            var dto = new Dto
            {
                schemaVersion = CurrentSchemaVersion,
                id = concoction.Id,
                displayName = string.IsNullOrEmpty(concoction.DisplayName) ? "Concoction" : concoction.DisplayName,
                damagePct = concoction.DamagePct,
                sizePct = concoction.SizePct,
                knockbackPct = concoction.KnockbackPct,
                speedPct = concoction.SpeedPct,
                spreadPct = concoction.SpreadPct,
            };
            return JsonUtility.ToJson(dto, prettyPrint);
        }

        public static bool TryFromJson(string json, out Concoction concoction, out string error)
        {
            concoction = null;
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
                error = $"Concoction schema v{dto.schemaVersion} is newer than this build (v{CurrentSchemaVersion}). Update the game?";
                return false;
            }
            if (string.IsNullOrEmpty(dto.id))
            {
                error = "Concoction has no id.";
                return false;
            }

            // v1 files predate the speed/spread levers; JsonUtility zero-fills
            // the absent fields, so restore the neutral default explicitly.
            float speed  = dto.schemaVersion >= 2 ? dto.speedPct  : Concoction.DefaultPct;
            float spread = dto.schemaVersion >= 2 ? dto.spreadPct : Concoction.DefaultPct;

            var c = new Concoction(dto.id,
                string.IsNullOrEmpty(dto.displayName) ? "Concoction" : dto.displayName,
                dto.damagePct, dto.sizePct, dto.knockbackPct, speed, spread);
            c.Validate(); // clamp on the way in — never trust on-disk values
            concoction = c;
            return true;
        }
    }
}
