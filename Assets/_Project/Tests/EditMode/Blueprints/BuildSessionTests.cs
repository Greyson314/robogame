using NUnit.Framework;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Exercise the plain-C# build-mode model. The session owns the
    /// variant cache + mirror state + selected block id; the
    /// MonoBehaviour drivers (editor, mirror toggle, variant panel)
    /// all read/write through it. These tests run without a scene
    /// because that's the whole point.
    /// </summary>
    public sealed class BuildSessionTests
    {
        [Test]
        public void VariantCache_RoundTripsDimsAndPitch()
        {
            var session = new BuildSession();
            session.SetVariantDims(BlockIds.Aero, new Vector3(2f, 0.1f, 1f));
            session.SetVariantPitch(BlockIds.Aero, 5f);

            Assert.AreEqual(new Vector3(2f, 0.1f, 1f), session.GetVariantDims(BlockIds.Aero));
            Assert.AreEqual(5f, session.GetVariantPitch(BlockIds.Aero), 1e-4f);
        }

        [Test]
        public void VariantCache_DefaultsForUnknownBlock()
        {
            var session = new BuildSession();
            Assert.AreEqual(Vector3.zero, session.GetVariantDims("never.set"));
            Assert.AreEqual(0f, session.GetVariantPitch("never.set"));
        }

        [Test]
        public void ResetVariantCaches_ClearsBoth()
        {
            var session = new BuildSession();
            session.SetVariantDims(BlockIds.Aero, new Vector3(3f, 0f, 0f));
            session.SetVariantPitch(BlockIds.Rotor, 7f);
            session.ResetVariantCaches();
            Assert.AreEqual(Vector3.zero, session.GetVariantDims(BlockIds.Aero));
            Assert.AreEqual(0f, session.GetVariantPitch(BlockIds.Rotor));
        }

        [Test]
        public void Mirror_ToggleAndAxisRaiseChangedEvent()
        {
            var session = new BuildSession();
            int changes = 0;
            session.MirrorChanged += () => changes++;

            session.ToggleMirror();
            Assert.IsTrue(session.MirrorEnabled);
            Assert.AreEqual(1, changes);

            session.SetMirrorAxis(MirrorAxis.Z);
            Assert.AreEqual(MirrorAxis.Z, session.MirrorAxis);
            Assert.AreEqual(2, changes);

            // Idempotent — same axis should not re-fire.
            session.SetMirrorAxis(MirrorAxis.Z);
            Assert.AreEqual(2, changes, "Same-axis set must not re-fire MirrorChanged.");
        }

        [Test]
        public void SelectedBlockChanged_FiresOnceOnTransition()
        {
            var session = new BuildSession();
            int changes = 0;
            session.SelectedBlockChanged += _ => changes++;

            session.SetSelectedBlock(BlockIds.Cube);
            Assert.AreEqual(1, changes);
            session.SetSelectedBlock(BlockIds.Cube);
            Assert.AreEqual(1, changes, "Re-selecting the same block must not re-fire.");
            session.SetSelectedBlock(BlockIds.Aero);
            Assert.AreEqual(2, changes);
        }
    }
}
