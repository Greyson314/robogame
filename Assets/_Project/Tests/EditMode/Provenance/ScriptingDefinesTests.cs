using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Robogame.Tests.EditMode.Provenance
{
    /// <summary>
    /// A scripting define with no consumer is litter that misleads the next
    /// reader of the project's provenance (LAUNCH-READINESS L1): third-party
    /// packs add their own defines through editor scripts, and deleting the
    /// pack (CHG-005) leaves the define behind forever (F-025). Every define on
    /// the Standalone target must be named by at least one source file in
    /// Assets/, Packages/ or the package cache (a `#if`, an asmdef versionDefine, a shader keyword),
    /// or it goes.
    /// </summary>
    public sealed class ScriptingDefinesTests
    {
        private static readonly string[] SourceExtensions = { ".cs", ".shader", ".cginc", ".hlsl", ".asmdef", ".compute" };

        [Test]
        public void StandaloneDefines_AllHaveAConsumer()
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            var defines = new List<string>();
            foreach (string d in raw.Split(';'))
                if (!string.IsNullOrWhiteSpace(d)) defines.Add(d.Trim());
            if (defines.Count == 0) Assert.Pass("No Standalone scripting defines.");

            string root = Path.GetDirectoryName(Application.dataPath) ?? "";
            var consumed = new HashSet<string>();
            foreach (string dir in new[] { Path.Combine(root, "Assets"), Path.Combine(root, "Packages"), Path.Combine(root, "Library", "PackageCache") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (System.Array.IndexOf(SourceExtensions, ext) < 0) continue;
                    string text;
                    try { text = File.ReadAllText(file); } catch { continue; }
                    foreach (string d in defines)
                        if (!consumed.Contains(d) && text.Contains(d)) consumed.Add(d);
                    if (consumed.Count == defines.Count) break;
                }
            }
            var dead = new List<string>();
            foreach (string d in defines) if (!consumed.Contains(d)) dead.Add(d);
            Assert.That(dead, Is.Empty,
                "Standalone scripting defines that no source file under Assets/, Packages/ or Library/PackageCache names (strip them from Project Settings → Player → Scripting Define Symbols):\n  " + string.Join("\n  ", dead));
        }
    }
}
