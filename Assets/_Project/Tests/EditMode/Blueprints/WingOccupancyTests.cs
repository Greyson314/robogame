using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Swept-volume tests for <see cref="BlockIds.Wing"/> — the bat-wing
    /// aero part (session 140). Sibling of <see cref="BlockOccupancyTests"/>'
    /// foil coverage; mirrors its structure and assertions so the two
    /// families stay comparable. The Wing shares the foil's span/anchor
    /// geometry (<c>ComputeFoilSweptBoundsLocal</c>) but additionally
    /// inflates the camber axis to reserve the airspace its flap
    /// animation visits — that inflation is the behaviour under test
    /// here, not repeated in <see cref="BlockOccupancyTests"/>.
    /// </summary>
    public sealed class WingOccupancyTests
    {
        private const float Eps = 1e-4f;

        // -----------------------------------------------------------------
        // Flap-sweep camber inflation — the load-bearing difference
        // between Wing and a rigid foil. The flap is visual-only (no
        // physics object moves) but placement must still reserve the
        // airspace it sweeps, or a neighbour block placed there would
        // visibly clip through the animating flap.
        // -----------------------------------------------------------------

        [Test]
        public void Wing_DefaultDims_CamberExtentIsSweepEnvelope_NotRestThickness()
        {
            // Rest thickness (0.195) is far smaller than the flap's
            // travel; the swept camber half-extent is governed by
            // span * SweepHalfExtentPerSpan, not thickness/2. If this
            // regresses to using rest thickness, a block placed at the
            // flap's peak deflection would silently clip through it —
            // this test exists to keep the reservation, not the mesh,
            // authoritative for placement.
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);

            float expectedCamberExtent = 2f * WingDefaults.DefaultSpan * WingDefaults.SweepHalfExtentPerSpan;
            Assert.That(b.size.x, Is.EqualTo(expectedCamberExtent).Within(Eps));
            Assert.That(b.size.x, Is.GreaterThan(WingDefaults.DefaultThickness),
                "Swept camber envelope must exceed rest thickness — that's the whole point of reserving flap airspace.");
        }

        [Test]
        public void Wing_SweepEnvelope_ScalesLinearlyWithSpan()
        {
            // The flap is an angular motion: sweep distance scales with
            // span (longer wing, wider arc at the tip). Doubling span
            // must double the reserved camber extent — if the placement
            // math ever hardcodes a span or truncates rather than
            // scaling, longer wings would under-reserve and short wings
            // would over-reserve.
            Vector3 dimsBase   = new Vector3(WingDefaults.DefaultSpan,       WingDefaults.DefaultThickness, WingDefaults.DefaultChord);
            Vector3 dimsDouble = new Vector3(WingDefaults.DefaultSpan * 2f,  WingDefaults.DefaultThickness, WingDefaults.DefaultChord);

            Bounds baseBounds   = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, dimsBase, cellSize: 1f);
            Bounds doubleBounds = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, dimsDouble, cellSize: 1f);

            Assert.That(doubleBounds.size.x, Is.EqualTo(baseBounds.size.x * 2f).Within(Eps));
        }

        [Test]
        public void Wing_ThicknessLargerThanSweepEnvelope_CamberExtentFallsBackToThickness()
        {
            // Boundary: if a future variant is authored with unusually
            // thick rest geometry that exceeds the sweep envelope, the
            // reservation must not shrink below the physical mesh — the
            // dispatcher takes max(thickness/2, span*sweepFactor), so a
            // very small span with large thickness should report the
            // thickness-driven extent, not the (now smaller) sweep one.
            Vector3 dims = new Vector3(0.01f, 5f, WingDefaults.DefaultChord); // tiny span, huge thickness
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(b.size.x, Is.EqualTo(5f).Within(Eps),
                "When rest thickness exceeds the sweep envelope, thickness must win — the reservation is a max(), not the sweep term alone.");
        }

        // -----------------------------------------------------------------
        // Span-axis anchoring — must match the foil contract exactly:
        // mount face at the cell's inner face, bounds extend outward by
        // span. Reuses the same assertions BlockOccupancyTests uses for
        // Aero, applied to Wing, to guard the shared dispatch path.
        // -----------------------------------------------------------------

        [Test]
        public void Wing_TopMount_Span2_ExtendsUpward_SameAnchorAsFoil()
        {
            // up=+Y → span aligns with chassis +Y. Outward shift = 0.5.
            // y-center: 1 + 0.5 = 1.5. y-range: [0.5, 2.5]. Identical
            // anchor math to Foil_TopMount_Span2_ExtendsUpward — the Wing
            // must not get its own, divergent shift formula.
            Vector3 dims = new Vector3(2f, WingDefaults.DefaultThickness, WingDefaults.DefaultChord);
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, dims, cellSize: 1f);
            Assert.That(b.min.y, Is.EqualTo(0.5f).Within(Eps));
            Assert.That(b.max.y, Is.EqualTo(2.5f).Within(Eps));
        }

        [Test]
        public void Wing_DefaultDims_SpanAboveOne_AlreadyShiftsOutward()
        {
            // Unlike the foil (DefaultSpan = 1.0, sits flush at default
            // dims), the Wing's DefaultSpan (1.828) exceeds 1, so even
            // an un-configured Wing already shifts outward from its
            // mount cell. Regression guard for that divergence: the
            // shift magnitude is max(0, span/2 - 0.5), evaluated here at
            // the Wing's actual default rather than a hand-picked dims=2.
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            float expectedShift = WingDefaults.DefaultSpan * 0.5f - 0.5f;
            Assert.That(b.center.y, Is.EqualTo(1f + expectedShift).Within(Eps));
            Assert.That(expectedShift, Is.GreaterThan(0f),
                "Sanity check on the fixture: this test is only meaningful because DefaultSpan > 1.");
        }

        // -----------------------------------------------------------------
        // Foils are unaffected by the Wing dispatch branch — regression
        // guard against ComputeFoilSweptBoundsLocal's signature/behaviour
        // changing for non-Wing ids. Existing BlockOccupancyTests already
        // exercises the full foil geometry (anchor, lateral extent, scale
        // invariance, Aero/AeroFin parity); not duplicated here.
        // -----------------------------------------------------------------

        [Test]
        public void Foil_DefaultDims_CamberAxisStaysAtRestThickness_NoSweepInflation()
        {
            // The one assertion worth adding here rather than in
            // BlockOccupancyTests: a foil's camber (thickness) axis must
            // NOT pick up the Wing's sweep-inflation branch just because
            // both ids share ComputeFoilSweptBoundsLocal.
            Bounds b = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(2, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Assert.That(b.size.x, Is.EqualTo(FoilDefaults.DefaultThickness).Within(Eps),
                "Foil camber extent must stay at rest thickness — sweep inflation is Wing-only.");
        }

        // -----------------------------------------------------------------
        // Behavioural payoff: a Wing reserves airspace a rigid foil
        // doesn't. Same neighbour position, same mount, default dims for
        // each id — Wing must reject placement there, foil must allow it.
        // -----------------------------------------------------------------

        [Test]
        public void Wing_DefaultDims_OverlapsNeighbourAlongCamberAxis_WhereEquivalentFoilDoesNot()
        {
            Bounds wing = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Wing, new Vector3Int(0, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Bounds foil = BlockOccupancy.ComputeSweptBoundsLocal(
                BlockIds.Aero, new Vector3Int(0, 1, 0), Vector3Int.up, Vector3.zero, cellSize: 1f);
            Bounds neighbour = BlockOccupancy.DefaultUnitCellBoundsLocal(new Vector3Int(1, 1, 0), 1f);

            Assert.IsTrue(BlockOccupancy.StrictOverlap(wing, neighbour),
                "Wing's flap-sweep envelope must reach into the adjacent cell along the camber axis; " +
                "a block placed there would clip through the flap at full deflection.");
            Assert.IsFalse(BlockOccupancy.StrictOverlap(foil, neighbour),
                "A rigid foil never sweeps, so the same adjacent cell must remain placeable.");
        }
    }
}
