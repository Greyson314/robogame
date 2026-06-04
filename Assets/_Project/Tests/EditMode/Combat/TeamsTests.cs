using NUnit.Framework;
using Robogame.Robots;

namespace Robogame.Tests.EditMode.Combat
{
    /// <summary>
    /// Pins the single friendly-fire rule (ADR-0003 / audit #15). The forked
    /// version let EMP disable teammates; this is the truth table both the
    /// projectile path and EMP now route through.
    /// </summary>
    public sealed class TeamsTests
    {
        [Test]
        public void SameNonNeutralTeam_IsFriendlyFire()
        {
            Assert.IsTrue(Teams.IsFriendlyFire(TeamId.Player, TeamId.Player));
            Assert.IsTrue(Teams.IsFriendlyFire(TeamId.Enemy, TeamId.Enemy));
        }

        [Test]
        public void DifferentTeams_AreNotFriendly()
        {
            Assert.IsFalse(Teams.IsFriendlyFire(TeamId.Player, TeamId.Enemy));
            Assert.IsFalse(Teams.IsFriendlyFire(TeamId.Enemy, TeamId.Player));
        }

        [Test]
        public void NeutralIsNeverFriendly_SoDummiesStayDamageable()
        {
            // None on either side → not friendly → damageable by everyone.
            Assert.IsFalse(Teams.IsFriendlyFire(TeamId.None, TeamId.None));
            Assert.IsFalse(Teams.IsFriendlyFire(TeamId.None, TeamId.Player));
            Assert.IsFalse(Teams.IsFriendlyFire(TeamId.Player, TeamId.None));
        }

        [Test]
        public void NullChassis_AreNotFriendly()
        {
            Assert.IsFalse(Teams.IsFriendlyFire(null, null));
        }
    }
}
