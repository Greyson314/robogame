using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Disk-backed registry of player-authored <see cref="Concoction"/>s.
    /// Lives under <see cref="Application.persistentDataPath"/>/<c>concoctions/</c>
    /// so saves survive game updates. Stateless façade over the file system —
    /// every call hits disk fresh (the directory stays tiny). Pairs with
    /// <see cref="ConcoctionSerializer"/> for the on-disk format; pure runtime
    /// (no <c>AssetDatabase</c>) so it works in player builds.
    /// </summary>
    /// <remarks>
    /// Structurally a sibling of <see cref="UserBlueprintLibrary"/>. The project's
    /// first non-blueprint player-content library — see
    /// <c>docs/decisions/0004-concoction-persistence.md</c>.
    /// </remarks>
    public static class ConcoctionLibrary
    {
        public const string SubFolder = "concoctions";
        public const string Extension = ".concoction.json";

        /// <summary>Fired whenever Save / Delete mutate the on-disk catalog.</summary>
        public static event Action Changed;

        /// <summary>Absolute path to the concoction directory. Created on first access.</summary>
        public static string DirectoryPath
        {
            get
            {
                string p = Path.Combine(Application.persistentDataPath, SubFolder);
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>One on-disk record paired with the file it came from (for delete / overwrite).</summary>
        public readonly struct Record
        {
            public readonly string FileName;
            public readonly Concoction Concoction;

            public Record(string fileName, Concoction concoction)
            {
                FileName = fileName;
                Concoction = concoction;
            }
        }

        // -----------------------------------------------------------------
        // Read
        // -----------------------------------------------------------------

        /// <summary>Load every <c>*.concoction.json</c>. Malformed files are skipped with a warning.</summary>
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
                Debug.LogWarning($"[Robogame] ConcoctionLibrary: cannot read '{dir}': {e.Message}");
                return result;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                Concoction c = LoadFile(path, out string err);
                if (c == null)
                {
                    Debug.LogWarning($"[Robogame] ConcoctionLibrary: skipped '{Path.GetFileName(path)}': {err}");
                    continue;
                }
                result.Add(new Record(Path.GetFileName(path), c));
            }
            return result;
        }

        private static Concoction LoadFile(string fullPath, out string error)
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
            return ConcoctionSerializer.TryFromJson(json, out Concoction c, out error) ? c : null;
        }

        // -----------------------------------------------------------------
        // Write
        // -----------------------------------------------------------------

        /// <summary>
        /// Persist a concoction as JSON. If <paramref name="fileName"/> is null,
        /// a slug is generated from <see cref="Concoction.DisplayName"/> with a
        /// uniqueness suffix. Returns the filename actually used.
        /// </summary>
        public static string Save(Concoction concoction, string fileName = null)
        {
            if (concoction == null) throw new ArgumentNullException(nameof(concoction));

            string finalName = string.IsNullOrEmpty(fileName)
                ? GenerateUniqueFileName(concoction.DisplayName)
                : SanitizeFileName(fileName);

            string fullPath = Path.Combine(DirectoryPath, finalName);
            File.WriteAllText(fullPath, ConcoctionSerializer.ToJson(concoction, prettyPrint: true), Encoding.UTF8);
            Changed?.Invoke();
            return finalName;
        }

        /// <summary>Delete a concoction by filename. Returns true if it existed.</summary>
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
                Debug.LogWarning($"[Robogame] ConcoctionLibrary: failed to delete '{fileName}': {e.Message}");
                return false;
            }
        }

        // -----------------------------------------------------------------
        // Filename helpers (mirrors UserBlueprintLibrary)
        // -----------------------------------------------------------------

        private static string GenerateUniqueFileName(string displayName)
        {
            string slug = Slugify(displayName);
            if (string.IsNullOrEmpty(slug)) slug = "concoction";

            string candidate = slug + Extension;
            if (!File.Exists(Path.Combine(DirectoryPath, candidate))) return candidate;

            for (int n = 2; n < 9999; n++)
            {
                candidate = $"{slug}-{n}{Extension}";
                if (!File.Exists(Path.Combine(DirectoryPath, candidate))) return candidate;
            }
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
            while (sb.Length > 0 && sb[sb.Length - 1] == '-') sb.Length--;
            return sb.ToString();
        }
    }
}
