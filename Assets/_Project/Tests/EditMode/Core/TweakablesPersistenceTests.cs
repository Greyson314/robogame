using System.IO;
using NUnit.Framework;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.EditMode
{
    /// <summary>
    /// F-063: the test rig, the live Editor and the player's own game share
    /// one <c>persistentDataPath</c>, so a test that calls
    /// <see cref="Tweakables.Set"/> used to rewrite the player's real
    /// settings file (and could leave a dev toggle switched on if the run
    /// died mid-test). These pin the guard: every test run suspends
    /// persistence, and a suspended Set never reaches the disk.
    /// </summary>
    public sealed class TweakablesPersistenceTests
    {
        private static string RealSavePath
            => Path.Combine(Application.persistentDataPath, "tweakables.json");

        [Test]
        public void TestRun_SuspendsPersistence()
        {
            // Fails if the assembly's TweakablesPersistenceGuard fixture is
            // deleted or stops running before the tests.
            Assert.IsTrue(Tweakables.PersistenceSuspended,
                "A test run must never write the real tweakables.json: the assembly-level " +
                "TweakablesPersistenceGuard [SetUpFixture] should have suspended persistence.");
        }

        [Test]
        public void Set_WhileSuspended_ChangesMemoryButNeverTheSaveFile()
        {
            bool existed = File.Exists(RealSavePath);
            byte[] before = existed ? File.ReadAllBytes(RealSavePath) : null;
            System.DateTime stampBefore = existed ? File.GetLastWriteTimeUtc(RealSavePath) : default;

            float original = Tweakables.Get(Tweakables.AudioUI);
            float changed = original > 0.5f ? 0.25f : 0.75f;
            try
            {
                Tweakables.Set(Tweakables.AudioUI, changed);

                Assert.AreEqual(changed, Tweakables.Get(Tweakables.AudioUI), 1e-6f,
                    "A suspended Set must still change the in-memory value (tests rely on it).");
                Assert.AreEqual(existed, File.Exists(RealSavePath),
                    "A suspended Set must not create or delete the save file.");
                if (existed)
                {
                    Assert.AreEqual(stampBefore, File.GetLastWriteTimeUtc(RealSavePath),
                        "A suspended Set must not rewrite the player's tweakables.json.");
                    CollectionAssert.AreEqual(before, File.ReadAllBytes(RealSavePath));
                }
            }
            finally
            {
                Tweakables.Set(Tweakables.AudioUI, original);
            }
        }
    }
}
