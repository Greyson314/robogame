// =============================================================================
// HudPointerGuardTests — EditMode
//
// What this suite covers
// -----------------------
// The IMGUI-rect half of the pointer guard: rects registered for a frame
// hit-test that frame AND the following one (double-buffer union — Update
// queries run before the frame's OnGUI registration, so one frame of
// staleness must err toward "over HUD"), then expire; modal open/close is
// idempotent per owner and independent across owners. These rules are the
// contract that keeps a HUD click from doubling as a cursor re-capture or
// a weapon shot — a rect that expires too early re-introduces the exact
// bug the guard exists to fix (session 128).
// =============================================================================

using NUnit.Framework;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class HudPointerGuardTests
    {
        private static readonly Rect Panel = new Rect(100f, 200f, 50f, 40f);
        private static readonly Vector2 Inside = new Vector2(120f, 220f);
        private static readonly Vector2 Outside = new Vector2(400f, 400f);

        [SetUp]
        public void ResetGuard()
        {
            // Statics persist across tests in the same domain — flush by
            // advancing two frames with no registrations so both buffers
            // drain, and clear any modal owners left by a failed test.
            HudPointerGuard.SetModalOpen(this, false);
            HudPointerGuard.RegisterGuiRect(new Rect(-1f, -1f, 0f, 0f), frame: 9_000);
            HudPointerGuard.RegisterGuiRect(new Rect(-1f, -1f, 0f, 0f), frame: 9_001);
        }

        // -----------------------------------------------------------------
        // Rect lifetime
        // -----------------------------------------------------------------

        [Test]
        public void RegisteredRect_HitsSameFrame()
        {
            HudPointerGuard.RegisterGuiRect(Panel, frame: 10_000);
            Assert.IsTrue(HudPointerGuard.PointerOverGuiRects(Inside),
                "A rect registered this frame must hit immediately — OnGUI events (button clicks) are processed the same frame they draw.");
        }

        [Test]
        public void RegisteredRect_StillHitsNextFrame()
        {
            HudPointerGuard.RegisterGuiRect(Panel, frame: 10_000);
            // A new frame begins: something else registers, promoting the
            // previous frame's buffer.
            HudPointerGuard.RegisterGuiRect(new Rect(0f, 0f, 1f, 1f), frame: 10_001);
            Assert.IsTrue(HudPointerGuard.PointerOverGuiRects(Inside),
                "Update-time queries run before this frame's OnGUI re-registration — last frame's rects must still count or every capture/fire gate reads one frame blind.");
        }

        [Test]
        public void RegisteredRect_ExpiresAfterTwoFrames()
        {
            HudPointerGuard.RegisterGuiRect(Panel, frame: 10_000);
            HudPointerGuard.RegisterGuiRect(new Rect(0f, 0f, 1f, 1f), frame: 10_001);
            HudPointerGuard.RegisterGuiRect(new Rect(0f, 0f, 1f, 1f), frame: 10_002);
            Assert.IsFalse(HudPointerGuard.PointerOverGuiRects(Inside),
                "A rect whose overlay stopped drawing must expire — otherwise a dismissed panel keeps suppressing capture/fire forever.");
        }

        [Test]
        public void PointOutsideAllRects_DoesNotHit()
        {
            HudPointerGuard.RegisterGuiRect(Panel, frame: 10_000);
            Assert.IsFalse(HudPointerGuard.PointerOverGuiRects(Outside),
                "Empty screen must stay clickable-to-capture — over-suppression would make the camera feel unresponsive.");
        }

        // -----------------------------------------------------------------
        // Modal owners
        // -----------------------------------------------------------------

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
