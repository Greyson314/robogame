using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Guards <see cref="BlueprintAsciiDump"/>'s own consistency promise: a
    /// dump's legend names every glyph its grid prints, and an unmapped
    /// block id never silently falls back to '?' for a shipped preset
    /// (CHG-025 / F-035 — the Hover Tank's HoverBlade cells printed '?'
    /// because the glyph map and the hand-typed legend had drifted apart).
    /// Reuses <see cref="PresetBlueprintTests.PresetPaths"/> so this suite
    /// and the validation suite cannot drift onto different preset lists.
    /// </summary>
    public sealed class BlueprintAsciiDumpTests
    {
        [TestCaseSource(typeof(PresetBlueprintTests), nameof(PresetBlueprintTests.PresetPaths))]
        public void EveryGlyphInAPresetDump_IsInTheLegend(string assetPath)
        {
            ChassisBlueprint bp = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(assetPath);
            if (bp == null)
            {
                Assert.Inconclusive($"Preset asset not found at {assetPath}. Run Robogame → Build Everything to scaffold.");
                return;
            }

            // Library-aware validation isn't needed for the dump itself, but
            // BlueprintPlan needs the same construction PresetBlueprintTests
            // uses so the two suites read one shape.
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            string dump = BlueprintAsciiDump.Dump(plan);

            string legendLine = null;
            foreach (string line in dump.Split('\n'))
            {
                if (line.StartsWith("Legend:"))
                {
                    legendLine = line;
                    break;
                }
            }
            Assert.IsNotNull(legendLine, $"{bp.DisplayName} dump has no Legend line:\n{dump}");

            var legendGlyphs = new HashSet<char>();
            foreach (Match m in Regex.Matches(legendLine, @"(\S)=\S+"))
                legendGlyphs.Add(m.Groups[1].Value[0]);

            List<char> gridGlyphs = ExtractGridGlyphs(dump);
            var unmapped = new List<char>();
            foreach (char g in gridGlyphs)
            {
                if (g == '.') continue; // the documented "empty cell" glyph, not a block
                if (g == '?' || !legendGlyphs.Contains(g))
                    unmapped.Add(g);
            }

            Assert.That(unmapped, Is.Empty,
                $"{bp.DisplayName} dump prints a glyph its own legend doesn't name (or the '?' unmapped fallback): " +
                $"{string.Join(",", unmapped)}\n{dump}");
        }

        /// <summary>
        /// Pulls the single grid-cell character out of every data row a
        /// layer prints. Mirrors BlueprintAsciiDump.Dump's own row format
        /// exactly: <c>sb.AppendFormat("{0,3}  ", z)</c> (a 3-char
        /// right-aligned z coordinate plus 2 spaces = a 5-char prefix,
        /// matching the x-header's 5-space lead-in) followed by one
        /// <c>" glyph "</c> triple per column.
        /// </summary>
        private static List<char> ExtractGridGlyphs(string dump)
        {
            var glyphs = new List<char>();
            foreach (string rawLine in dump.Replace("\r\n", "\n").Split('\n'))
            {
                if (rawLine.Length < 5) continue;
                string prefix = rawLine.Substring(0, 5);
                if (!Regex.IsMatch(prefix, @"^\s*-?\d+\s{2}$")) continue;

                string cells = rawLine.Substring(5);
                for (int i = 1; i < cells.Length; i += 3)
                    glyphs.Add(cells[i]);
            }
            return glyphs;
        }
    }
}
