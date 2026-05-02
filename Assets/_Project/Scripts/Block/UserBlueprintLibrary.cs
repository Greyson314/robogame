using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Disk-backed registry of player-authored chassis blueprints. Lives
    /// under <see cref="Application.persistentDataPath"/>/<c>blueprints/</c>
    /// so saves survive game updates and asset-folder reshuffles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stateless façade over the file system: every public call hits disk
    /// fresh. Cheap because the directory will be tiny (dozens of small
    /// JSON files for a long time). If we ever care, throw a small in-memory
    /// cache invalidated by <see cref="Changed"/>.
    /// </para>
    /// <para>
    /// Pairs with <see cref="BlueprintSerializer"/> for the on-disk format.
    /// Pure runtime — does not touch <c>AssetDatabase</c> so it works in
    /// player builds.
    /// </para>
    /// </remarks>
    public static class UserBlueprintLibrary
    {
        public const string SubFolder = "blueprints";
        public const string Extension = ".robot.json";

        /// <summary>Fired whenever Save / Delete mutate the on-disk catalog.</summary>
        public static event Action Changed;

        /// <summary>Absolute path to the user-blueprint directory. Created on first access.</summary>
        public static string DirectoryPath
        {
            get
            {
                string p = Path.Combine(Application.persistentDataPath, SubFolder);
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>One on-disk record paired with the file it came from (used for delete / overwrite).</summary>
        public readonly struct Record
        {
            public readonly string FileName;
            public readonly ChassisBlueprint Blueprint;

            public Record(string fileName, ChassisBlueprint blueprint)
            {
                FileName = fileName;
                Blueprint = blueprint;
            }
        }

        // -----------------------------------------------------------------
        // Read
        // -----------------------------------------------------------------

        /// <summary>
        /// Load every <c>*.robot.json</c> from <see cref="DirectoryPath"/>.
        /// Malformed files are skipped with a warning logged. Returned
        /// blueprints are runtime <see cref="ScriptableObject"/> instances —
        /// callers may mutate them freely.
        /// </summary>
        public static List<Record> LoadAll()
        {
            var result = new List<Record>();
            string dir = DirectoryPath;
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + Extension, SearchOption.TopDirectoryOnly);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Robogame] UserBlueprintLibrary: cannot read '{dir}': {e.Message}");
                return result;
            }

            // Stable, name-sorted order so the dropdown doesn't dance.
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                ChassisBlueprint bp = LoadFile(path, out string err);
                if (bp == null)
                {
                    Debug.LogWarning($"[Robogame] UserBlueprintLibrary: skipped '{Path.GetFileName(path)}': {err}");
                    continue;
                }
                result.Add(new Record(Path.GetFileName(path), bp));
            }
            return result;
        }

        /// <summary>Load a single blueprint by filename (e.g. <c>"my-robot.robot.json"</c>).</summary>
        public static ChassisBlueprint Load(string fileName, out string error)
        {
            string path = Path.Combine(DirectoryPath, fileName);
            return LoadFile(path, out error);
        }

        private static ChassisBlueprint LoadFile(string fullPath, out string error)
        {
            error = null;
            string json;
            try
            {
                json = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                error = "I/O: " + e.Message;
                return null;
            }
            if (!BlueprintSerializer.TryFromJson(json, out ChassisBlueprint bp, out string parseErr))
            {
                error = parseErr;
                return null;
            }
            return bp;
        }

        // -----------------------------------------------------------------
        // Write
        // -----------------------------------------------------------------

        /// <summary>
        /// Persist a blueprint as JSON. If <paramref name="fileName"/> is
        /// null, a slug is generated from <see cref="ChassisBlueprint.DisplayName"/>
        /// and a uniqueness suffix is appended if needed. Returns the
        /// filename actually used.
        /// </summary>
        public static string Save(ChassisBlueprint blueprint, string fileName = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

            string finalName = string.IsNullOrEmpty(fileName)
                ? GenerateUniqueFileName(blueprint.DisplayName)
                : SanitizeFileName(fileName);

            string fullPath = Path.Combine(DirectoryPath, finalName);
            string json = BlueprintSerializer.ToJson(blueprint, prettyPrint: true);
            File.WriteAllText(fullPath, json, Encoding.UTF8);
            Changed?.Invoke();
            return finalName;
        }

        /// <summary>Delete a blueprint by filename. Returns true if it existed.</summary>
        public static bool Delete(string fileName)
        {
            string fullPath = Path.Combine(DirectoryPath, SanitizeFileName(fileName));
            if (!File.Exists(fullPath)) return false;
            try
            {
                File.Delete(fullPath);
                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Robogame] UserBlueprintLibrary: failed to delete '{fileName}': {e.Message}");
                return false;
            }
        }

        // -----------------------------------------------------------------
        // Filename helpers
        // -----------------------------------------------------------------

        private static string GenerateUniqueFileName(string displayName)
        {
            string slug = Slugify(displayName);
            if (string.IsNullOrEmpty(slug)) slug = "robot";

            string candidate = slug + Extension;
            string fullPath = Path.Combine(DirectoryPath, candidate);
            if (!File.Exists(fullPath)) return candidate;

            // Collision: slug-2.robot.json, slug-3.robot.json, ...
            for (int n = 2; n < 9999; n++)
            {
                candidate = $"{slug}-{n}{Extension}";
                fullPath = Path.Combine(DirectoryPath, candidate);
                if (!File.Exists(fullPath)) return candidate;
            }
            // Fallback: timestamp.
            return $"{slug}-{DateTime.UtcNow:yyyyMMddHHmmss}{Extension}";
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);
            foreach (char c in fileName)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            bool lastDash = false;
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            // Trim trailing dash.
            while (sb.Length > 0 && sb[sb.Length - 1] == '-') sb.Length--;
            return sb.ToString();
        }
    }
}
