using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode
{
    /// <summary>
    /// TRACE[F-062]: <c>-factory-mute</c> is what a headless player run
    /// (LAUNCH-READINESS B3) passes to go quiet without touching the
    /// player's own <see cref="Tweakables.AudioMute"/> save (INV-1). These
    /// pin the pure parse so a rename or a loosened match can't silently
    /// widen or narrow what counts as "muted".
    /// </summary>
    public sealed class CommandLineMuteTests
    {
        [Test]
        public void Parse_FlagPresent_ReturnsTrue()
        {
            Assert.IsTrue(CommandLineMute.Parse(new[] { "Robogame.exe", "-batchmode", "-factory-mute" }));
        }

        [Test]
        public void Parse_FlagAbsent_ReturnsFalse()
        {
            Assert.IsFalse(CommandLineMute.Parse(new[] { "Robogame.exe", "-batchmode", "-nographics" }));
        }

        [Test]
        public void Parse_OtherCase_StillMatches()
        {
            Assert.IsTrue(CommandLineMute.Parse(new[] { "Robogame.exe", "-Factory-Mute" }));
        }

        [Test]
        public void Parse_SubstringIsNotAMatch()
        {
            // A longer or shorter token must not accidentally satisfy the flag.
            Assert.IsFalse(CommandLineMute.Parse(new[] { "Robogame.exe", "-factory-muted" }));
            Assert.IsFalse(CommandLineMute.Parse(new[] { "Robogame.exe", "-factory-mut" }));
        }

        [Test]
        public void Parse_NullArgs_ReturnsFalse()
        {
            Assert.IsFalse(CommandLineMute.Parse(null));
        }

        [Test]
        public void Parse_EmptyArgs_ReturnsFalse()
        {
            Assert.IsFalse(CommandLineMute.Parse(new string[0]));
        }
    }
}
