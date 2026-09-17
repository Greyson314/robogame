using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Robogame.Tests.EditMode.Provenance
{
    /// <summary>
    /// Charter I6 (docs/loop/CHARTER.md): generated assets record their
    /// generator. artgen/README.md is that record for every FBX under
    /// Assets/_Project/Art/Models: which script produced it, from which
    /// study, in which Blender. A generated mesh with no row is an asset of
    /// unknown provenance in a product that aspires to ship (LAUNCH-READINESS
    /// L1), and a row whose script is gone is a record that can no longer be
    /// reproduced. Both fail here instead of surfacing at release.
    /// </summary>
    public sealed class ArtgenManifestTests
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? "";
        private static string ManifestPath => Path.Combine(ProjectRoot, "artgen", "README.md");
        private const string ModelsFolder = "Assets/_Project/Art/Models";

        /// <summary>
        /// Parses the manifest's table rows of the form
        /// <c>| &lt;fbx path relative to Art/Models&gt; | &lt;generator script&gt; | ... |</c>.
        /// Only rows whose first cell ends in ".fbx" count; prose and the header are skipped.
        /// </summary>
        private static Dictionary<string, string> ReadManifest()
        {
            var rows = new Dictionary<string, string>();
            if (!File.Exists(ManifestPath)) return rows;
            foreach (string raw in File.ReadAllLines(ManifestPath))
            {
                string line = raw.Trim();
                if (!line.StartsWith("|")) continue;
                string[] cells = line.Trim('|').Split('|');
                if (cells.Length < 2) continue;
                string fbx = cells[0].Trim().Trim('`');
                if (!fbx.EndsWith(".fbx")) continue;
                rows[fbx.Replace('\\', '/')] = cells[1].Trim().Trim('`');
            }
            return rows;
        }

        [Test]
        public void EveryGeneratedFbxHasAManifestRow()
        {
            Assert.IsTrue(File.Exists(ManifestPath), "artgen/README.md is missing: the generated-asset manifest (charter I6) does not exist.");
            Dictionary<string, string> rows = ReadManifest();
            string modelsAbs = Path.Combine(ProjectRoot, ModelsFolder);
            var missing = new List<string>();
            foreach (string abs in Directory.GetFiles(modelsAbs, "*.fbx", SearchOption.AllDirectories))
            {
                string rel = abs.Substring(modelsAbs.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (!rows.ContainsKey(rel)) missing.Add(rel);
            }
            Assert.That(missing, Is.Empty, "FBX files under " + ModelsFolder + " with no row in artgen/README.md (which script generated them?):\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void EveryManifestRowNamesAnExistingScript()
        {
            Dictionary<string, string> rows = ReadManifest();
            Assert.That(rows, Is.Not.Empty, "artgen/README.md has no FBX rows to check.");
            var broken = new List<string>();
            foreach (KeyValuePair<string, string> kv in rows)
            {
                // A cell may name more than one script ("inv_export.py (study: inv_cube.py)"):
                // every *.py token must exist under artgen/.
                // Backticks are delimiters too: a cell reads `inv_export.py` (study: `inv_cube.py`),
                // and a token that keeps its backtick never ends in ".py" (red team, CHG-001 round 1:
                // 32 of 33 rows were silently unchecked).
                foreach (string token in kv.Value.Split(' ', '(', ')', ',', ';', '`'))
                {
                    if (!token.EndsWith(".py")) continue;
                    if (!File.Exists(Path.Combine(ProjectRoot, "artgen", token)))
                        broken.Add(kv.Key + " -> " + token);
                }
            }
            Assert.That(broken, Is.Empty, "Manifest rows whose generator script no longer exists under artgen/:\n  " + string.Join("\n  ", broken));
        }

        [Test]
        public void EveryManifestRowNamesAnExistingFbx()
        {
            Dictionary<string, string> rows = ReadManifest();
            Assert.That(rows, Is.Not.Empty, "artgen/README.md has no FBX rows to check.");
            string modelsAbs = Path.Combine(ProjectRoot, ModelsFolder);
            var orphans = new List<string>();
            foreach (string rel in rows.Keys)
            {
                if (!File.Exists(Path.Combine(modelsAbs, rel))) orphans.Add(rel);
            }
            Assert.That(orphans, Is.Empty, "Manifest rows whose FBX no longer exists under " + ModelsFolder + " (delete the row with the asset, or restore the asset):\n  " + string.Join("\n  ", orphans));
        }
    }
}
