// =============================================================================
// HudPointerGuardTests — EditMode
//
// What this suite covers
// -----------------------
// Modal open/close is idempotent per owner and independent across owners.
// These rules are the contract that keeps a modal HUD click from doubling
// as a cursor re-capture or a weapon shot (session 128). The old IMGUI
// rect-registration half of the guard was deleted as dead code — modal
// overlays claim suppression via SetModalOpen instead.
// =============================================================================

using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class HudPointerGuardTests
    {
        [SetUp]
        public void ResetGuard()
        {
            // Statics persist across tests in the same domain — clear any
            // modal owner left by a failed test.
            HudPointerGuard.SetModalOpen(this, false);
        }

        [Test]
        public void ModalOpen_TracksOwnerLifecycle()
        {
            var ownerA = new object();
            var ownerB = new object();
            Assert.IsFalse(HudPointerGuard.AnyModalOpen);

            HudPointerGuard.SetModalOpen(ownerA, true);
            HudPointerGuard.SetModalOpen(ownerB, true);
            Assert.IsTrue(HudPointerGuard.AnyModalOpen);

            // One modal closing must not clear another's claim (settings
            // closing over the pause menu).
            HudPointerGuard.SetModalOpen(ownerA, false);
            Assert.IsTrue(HudPointerGuard.AnyModalOpen,
                "Owner B is still open — modal claims are per-owner, not a global toggle.");

            HudPointerGuard.SetModalOpen(ownerB, false);
            Assert.IsFalse(HudPointerGuard.AnyModalOpen);
        }

        [Test]
        public void ModalOpen_IsIdempotentPerOwner()
        {
            var owner = new object();
            HudPointerGuard.SetModalOpen(owner, true);
            HudPointerGuard.SetModalOpen(owner, true); // re-assert, e.g. every SetOpen(true)
            HudPointerGuard.SetModalOpen(owner, false);
            Assert.IsFalse(HudPointerGuard.AnyModalOpen,
                "Double-open must not require double-close — panels re-assert their state on every toggle.");
        }

        [Test]
        public void ModalOpen_NullOwner_IsIgnored()
        {
            HudPointerGuard.SetModalOpen(null, true);
            Assert.IsFalse(HudPointerGuard.AnyModalOpen);
        }
    }
}
