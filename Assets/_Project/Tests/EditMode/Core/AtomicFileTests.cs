// =============================================================================
// AtomicFileTests — EditMode (CHG-006)
//
// WHAT THIS COVERS
//   Robogame.Core.AtomicFile.WriteAllText: the `.tmp` + File.Replace/.Move
//   pattern that UserBlueprintLibrary, ConcoctionLibrary and Tweakables use
//   to save player data (best-practices.md § 11.3) — write to a `.tmp`
//   sibling, then swap it in so a reader never observes a partial file, and
//   a crash or a locked destination leaves the original untouched.
//
//   Everything here runs inside a throwaway temp directory created in
//   SetUp and removed in TearDown. It never touches
//   Application.persistentDataPath — on this machine that folder holds
//   Grey's own blueprints, concoctions and tweakables.json, so no test may
//   write there, and neither library exposes a directory-override seam to
//   redirect it safely.
//
//   The last two tests prove the claim the spec leans on for "library
//   enumeration ignores .bak/.tmp": Directory.GetFiles with each library's
//   exact search pattern (read from the library's own Extension constant)
//   returns only the real save file when a stray .bak/.tmp sibling sits
//   next to it. They read the constant only — they never call Save/Load/
//   Delete, so they can't touch the real save folder.
// =============================================================================

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Core;

namespace Robogame.Tests.EditMode.Core
{
    public sealed class AtomicFileTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RobogameAtomicFileTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Test]
        public void WriteAllText_NewFile_WritesContentAndLeavesNoTmp()
        {
            string path = Path.Combine(_dir, "new.json");

            AtomicFile.WriteAllText(path, "hello", Encoding.UTF8);

            Assert.AreEqual("hello", File.ReadAllText(path), "the destination must hold the written text");
            Assert.IsFalse(File.Exists(path + ".tmp"), "no .tmp should survive a successful write");
        }

        [Test]
        public void WriteAllText_ExistingFile_OverwritesAndBacksUpPrevious()
        {
            string path = Path.Combine(_dir, "existing.json");
            File.WriteAllText(path, "old");

            AtomicFile.WriteAllText(path, "new", Encoding.UTF8);

            Assert.AreEqual("new", File.ReadAllText(path), "the live file must hold the new content");
            Assert.AreEqual("old", File.ReadAllText(path + ".bak"), "the .bak sibling must hold what was overwritten");
            Assert.IsFalse(File.Exists(path + ".tmp"), "no .tmp should survive a successful write");
        }

        [Test]
        public void WriteAllText_DestinationLocked_OriginalSurvivesAndExceptionSurfaces()
        {
            // A save the player would cry over losing must never be corrupted
            // by a write that can't complete (e.g. another process/AV holding
            // the file open). The spec requires the original to survive and
            // the exception to reach the caller's existing error handling.
            string path = Path.Combine(_dir, "locked.json");
            File.WriteAllText(path, "original");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => AtomicFile.WriteAllText(path, "new", Encoding.UTF8));
            }

            Assert.AreEqual("original", File.ReadAllText(path), "a failed swap must never touch the original");
            Assert.IsFalse(File.Exists(path + ".tmp"), "the failed .tmp must be cleaned up best-effort");
        }

        [Test]
        public void BlueprintLibraryEnumeration_IgnoresBackupAndTmpSiblings()
        {
            string real = Path.Combine(_dir, "robot" + UserBlueprintLibrary.Extension);
            File.WriteAllText(real, "{}");
            File.WriteAllText(real + ".bak", "{}");
            File.WriteAllText(real + ".tmp", "{}");

            string[] found = Directory.GetFiles(_dir, "*" + UserBlueprintLibrary.Extension, SearchOption.TopDirectoryOnly);

            Assert.AreEqual(1, found.Length, "a .bak/.tmp sibling must not appear as a phantom blueprint entry");
            Assert.AreEqual(real, found[0]);
        }

        [Test]
        public void ConcoctionLibraryEnumeration_IgnoresBackupAndTmpSiblings()
        {
            string real = Path.Combine(_dir, "mix" + ConcoctionLibrary.Extension);
            File.WriteAllText(real, "{}");
            File.WriteAllText(real + ".bak", "{}");
            File.WriteAllText(real + ".tmp", "{}");

            string[] found = Directory.GetFiles(_dir, "*" + ConcoctionLibrary.Extension, SearchOption.TopDirectoryOnly);

            Assert.AreEqual(1, found.Length, "a .bak/.tmp sibling must not appear as a phantom concoction entry");
            Assert.AreEqual(real, found[0]);
        }
    }
}
