using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="AeroShape"/> — the per-block-id
    /// dispatcher that decides whether a given aero id reads
    /// <see cref="WingDefaults"/> or <see cref="FoilDefaults"/>. Every
    /// downstream consumer (occupancy, the variant panel, the ghost
    /// factory, the placed-mesh rig) goes through this resolver, so a
    /// wrong dispatch here silently mis-sizes every one of them.
    /// </summary>
    public sealed class AeroShapeTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void IsAeroId_RecognisesAllThreeIds()
        {
            Assert.IsTrue(AeroShape.IsAeroId(BlockIds.Aero));
            Assert.IsTrue(AeroShape.IsAeroId(BlockIds.AeroFin));
            Assert.IsTrue(AeroShape.IsAeroId(BlockIds.Wing));
        }

        [Test]
        public void IsAeroId_RejectsNonAeroId()
        {
            // Guards against the dispatcher over-matching (e.g. a
            // careless string.Contains) and routing unrelated blocks
            // through aero-only sizing.
            Assert.IsFalse(AeroShape.IsAeroId(BlockIds.Cube));
        }

        [Test]
        public void Defaults_Wing_ReadsWingDefaults_NotFoilDefaults()
        {
            // The Wing must resolve to its own authored constants, not
            // silently fall back to the foil's — this is the seam that
            // WingDefaults was introduced to protect (session 140).
            AeroShape.Defaults(BlockIds.Wing, out float span, out float thickness, out float chord);
            Assert.That(span,      Is.EqualTo(WingDefaults.DefaultSpan).Within(Eps));
            Assert.That(thickness, Is.EqualTo(WingDefaults.DefaultThickness).Within(Eps));
            Assert.That(chord,     Is.EqualTo(WingDefaults.DefaultChord).Within(Eps));
            Assert.That(span, Is.Not.EqualTo(FoilDefaults.DefaultSpan).Within(Eps),
                "Fixture sanity: Wing and Foil defaults must actually differ, or this test can't catch a wrong dispatch.");
        }

        [Test]
        public void Defaults_AeroAndAeroFin_BothReadFoilDefaults()
        {
            AeroShape.Defaults(BlockIds.Aero, out float aeroSpan, out float aeroThickness, out float aeroChord);
            AeroShape.Defaults(BlockIds.AeroFin, out float finSpan, out float finThickness, out float finChord);

            Assert.That(aeroSpan,      Is.EqualTo(FoilDefaults.DefaultSpan).Within(Eps));
            Assert.That(aeroThickness, Is.EqualTo(FoilDefaults.DefaultThickness).Within(Eps));
            Assert.That(aeroChord,     Is.EqualTo(FoilDefaults.DefaultChord).Within(Eps));
            Assert.That(finSpan,      Is.EqualTo(aeroSpan).Within(Eps));
            Assert.That(finThickness, Is.EqualTo(aeroThickness).Within(Eps));
            Assert.That(finChord,     Is.EqualTo(aeroChord).Within(Eps));
        }

        [Test]
        public void ResolveDims_ZeroRawDims_FallsBackToPerIdDefaults()
        {
            // Vector3.zero is the sentinel for "unconfigured" (Dims not
            // authored on the blueprint entry). Every component must
            // resolve to the id's default independently.
            AeroShape.ResolveDims(BlockIds.Wing, Vector3.zero, out float wSpan, out float wThickness, out float wChord);
            Assert.That(wSpan,      Is.EqualTo(WingDefaults.DefaultSpan).Within(Eps));
            Assert.That(wThickness, Is.EqualTo(WingDefaults.DefaultThickness).Within(Eps));
            Assert.That(wChord,     Is.EqualTo(WingDefaults.DefaultChord).Within(Eps));

            AeroShape.ResolveDims(BlockIds.Aero, Vector3.zero, out float aSpan, out float aThickness, out float aChord);
            Assert.That(aSpan,      Is.EqualTo(FoilDefaults.DefaultSpan).Within(Eps));
            Assert.That(aThickness, Is.EqualTo(FoilDefaults.DefaultThickness).Within(Eps));
            Assert.That(aChord,     Is.EqualTo(FoilDefaults.DefaultChord).Within(Eps));
        }

        [Test]
        public void ResolveDims_PositiveRawComponents_PassThroughUnchanged()
        {
            // A blueprint that DID author custom dims must get exactly
            // those values back, not the defaults — otherwise per-block
            // scaling (the whole point of Dims) silently gets clobbered.
            Vector3 raw = new Vector3(2.5f, 0.25f, 1.5f);
            AeroShape.ResolveDims(BlockIds.Wing, raw, out float span, out float thickness, out float chord);
            Assert.That(span,      Is.EqualTo(2.5f).Within(Eps));
            Assert.That(thickness, Is.EqualTo(0.25f).Within(Eps));
            Assert.That(chord,     Is.EqualTo(1.5f).Within(Eps));
        }

        [Test]
        public void ResolveDims_MixedZeroAndPositiveComponents_ResolvesPerComponent()
        {
            // Boundary: a partially-authored Dims (e.g. only span
            // customised, thickness/chord left at the zero sentinel)
            // must resolve each axis independently — not fall back to
            // full defaults just because one component is zero.
            Vector3 raw = new Vector3(3.0f, 0f, 0f);
            AeroShape.ResolveDims(BlockIds.Wing, raw, out float span, out float thickness, out float chord);
            Assert.That(span,      Is.EqualTo(3.0f).Within(Eps));
            Assert.That(thickness, Is.EqualTo(WingDefaults.DefaultThickness).Within(Eps));
            Assert.That(chord,     Is.EqualTo(WingDefaults.DefaultChord).Within(Eps));
        }
    }
}
