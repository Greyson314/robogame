using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Robogame.Tests.EditMode.Gameplay
{
    /// <summary>
    /// CHG-024 (F-047): the Stress.* dev-dummy lifecycle (stress tower, tank
    /// dummy, air dummy) moves off <c>ArenaController</c> onto a dedicated
    /// <c>ArenaDevDummies</c> component. Parses Arena.unity as text — no
    /// scene load — and checks the scene actually carries the new
    /// component with the stress tower's scene-authored wiring (the
    /// scaffolder's default position, 40/0.5/18, not the code default
    /// 55/0.5/30 — see ArenaController's old <c>_stressTowerPosition</c>
    /// tooltip history), and that ArenaController's own MonoBehaviour
    /// block no longer carries the moved field.
    /// </summary>
    /// <remarks>
    /// RED on main: <c>ArenaDevDummies.cs</c> (and its .meta) don't exist
    /// yet, so <see cref="ReadGuid"/> fails the meta-file existence assert
    /// before the scene is even parsed.
    /// </remarks>
    public sealed class ArenaDevDummiesSceneWiringTests
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? "";
        private const string ArenaScenePath = "Assets/_Project/Scenes/Arena.unity";
        private const string ArenaDevDummiesMetaPath = "Assets/_Project/Scripts/Gameplay/ArenaDevDummies.cs.meta";
        private const string ArenaControllerMetaPath = "Assets/_Project/Scripts/Gameplay/ArenaController.cs.meta";

        private static string AbsPath(string projectRelativePath)
            => Path.Combine(ProjectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Reads the <c>guid:</c> line out of a .meta file (2-line format: fileFormatVersion, guid).</summary>
        private static string ReadGuid(string metaRelativePath)
        {
            string abs = AbsPath(metaRelativePath);
            Assert.IsTrue(File.Exists(abs), metaRelativePath + " does not exist.");
            foreach (string line in File.ReadAllLines(abs))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("guid:")) return trimmed.Substring("guid:".Length).Trim();
            }
            Assert.Fail(metaRelativePath + " has no guid: line.");
            return null;
        }

        /// <summary>
        /// Scene YAML is a sequence of documents, each starting with a
        /// <c>--- !u!&lt;classId&gt; &amp;&lt;fileId&gt;</c> marker line.
        /// Splits on that marker so each returned block is one object's
        /// full text (marker line included).
        /// </summary>
        private static List<string> SplitObjectBlocks(string sceneText)
        {
            var blocks = new List<string>();
            var current = new List<string>();
            foreach (string line in sceneText.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith("--- !u!"))
                {
                    if (current.Count > 0) blocks.Add(string.Join("\n", current));
                    current = new List<string>();
                }
                current.Add(line);
            }
            if (current.Count > 0) blocks.Add(string.Join("\n", current));
            return blocks;
        }

        private static string FindMonoBehaviourBlockForScriptGuid(List<string> blocks, string scriptGuid)
        {
            string needle = "guid: " + scriptGuid;
            return blocks.FirstOrDefault(b => b.Contains("MonoBehaviour:") && b.Contains("m_Script:") && b.Contains(needle));
        }

        [Test]
        public void ArenaScene_CarriesArenaDevDummies_WithTheStressTowerWiring()
        {
            string devDummiesGuid = ReadGuid(ArenaDevDummiesMetaPath);
            string arenaControllerGuid = ReadGuid(ArenaControllerMetaPath);

            string sceneAbs = AbsPath(ArenaScenePath);
            Assert.IsTrue(File.Exists(sceneAbs), ArenaScenePath + " does not exist.");
            List<string> blocks = SplitObjectBlocks(File.ReadAllText(sceneAbs));

            string devDummiesBlock = FindMonoBehaviourBlockForScriptGuid(blocks, devDummiesGuid);
            Assert.IsNotNull(devDummiesBlock,
                "Arena.unity has no MonoBehaviour block whose m_Script guid matches " +
                "ArenaDevDummies.cs.meta (" + devDummiesGuid + "). The Editor-side move " +
                "(AddComponent<ArenaDevDummies> + SerializedObject copy + save) hasn't landed.");

            StringAssert.Contains(
                "_stressTowerBlueprint: {fileID: 11400000, guid: 45a8582379d5bfb41a77567294417bca",
                devDummiesBlock,
                "ArenaDevDummies' scene block is missing the stress tower blueprint wiring.");
            StringAssert.Contains(
                "_stressTowerPosition: {x: 40, y: 0.5, z: 18}",
                devDummiesBlock,
                "ArenaDevDummies' scene block is missing the stress tower position wiring " +
                "(40, 0.5, 18 — the scene-authored value, not the code default 55/0.5/30).");

            string arenaControllerBlock = FindMonoBehaviourBlockForScriptGuid(blocks, arenaControllerGuid);
            Assert.IsNotNull(arenaControllerBlock, "Arena.unity has no ArenaController MonoBehaviour block.");
            StringAssert.DoesNotContain(
                "_stressTowerBlueprint",
                arenaControllerBlock,
                "ArenaController's scene block still carries _stressTowerBlueprint — it should have moved to ArenaDevDummies.");
        }
    }
}
