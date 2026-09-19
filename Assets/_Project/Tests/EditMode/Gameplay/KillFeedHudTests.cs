// =============================================================================
// KillFeedHudTests — EditMode (CHG-039)
//
// WHAT THIS COVERS
//   KillFeedHud.FormatKillEntry — the pure static formatter PushKill uses to
//   build a named kill-feed line (extracted so it's testable outside OnGUI).
//   With a null/empty weapon name its output must be byte-identical to the
//   pre-CHG-039 text ("{killer}  ->  {victim}", double space either side of
//   the arrow) so a bare-weapon kill still reads exactly as it did before
//   this change; with a weapon name it appends the "(name)" suffix so a
//   tinkerer's named mix shows up as authorship. Reached via reflection
//   (BindingFlags.NonPublic | Static) rather than widened visibility — same
//   convention BuildRowShellTests uses for SettingsHud's private static
//   BuildRowShell (no InternalsVisibleTo is set up in this project).
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Gameplay;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class KillFeedHudTests
    {
        private static string Format(string killerName, string victimName, string weaponName)
        {
            MethodInfo method = typeof(KillFeedHud).GetMethod(
                "FormatKillEntry", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "KillFeedHud.FormatKillEntry not found — has it been extracted from PushKill yet (CHG-039)?");
            return (string)method.Invoke(null, new object[] { killerName, victimName, weaponName });
        }

        [Test]
        public void FormatKillEntry_NullWeaponName_MatchesPreChg039Text()
        {
            string result = Format("YOU", "BOT 1", null);
            Assert.AreEqual("YOU  →  BOT 1", result,
                "No weapon name must render byte-identical to the original two-space-arrow text — no input, no visible change.");
        }

        [Test]
        public void FormatKillEntry_EmptyWeaponName_MatchesPreChg039Text()
        {
            string result = Format("YOU", "BOT 1", string.Empty);
            Assert.AreEqual("YOU  →  BOT 1", result,
                "An empty weapon name must be treated the same as null — no suffix, no stray parens.");
        }

        [Test]
        public void FormatKillEntry_WithWeaponName_AppendsSuffix()
        {
            string result = Format("YOU", "BOT 1", "Dark Madder Concoction");
            Assert.AreEqual("YOU  →  BOT 1  (Dark Madder Concoction)", result,
                "A named concoction must show up as authorship on the kill line (Concoction Identity, ADR-0005).");
        }
    }
}
