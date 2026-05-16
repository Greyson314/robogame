using NUnit.Framework;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Phase 6 machine gate (server-validation half): the stateless
    /// rules in <see cref="BrushOpValidator"/> accept honest brushes and
    /// reject malformed / out-of-bounds / suspiciously-large ones.
    /// </summary>
    public sealed class BrushOpValidatorTests
    {
        private static readonly Bounds Zone = new Bounds(
            center: new Vector3(0f, 0f, 0f),
            size: new Vector3(16f, 16f, 16f));

        // ------------------------------------------------------------------
        // Kind
        // ------------------------------------------------------------------

        [Test]
        public void Validate_DefaultUnsetKind_RejectsAsNone()
        {
            BrushOp op = new BrushOp { kind = BrushKind.None, radiusFixed = 128 };
            Assert.IsFalse(BrushOpValidator.Validate(op, Zone),
                "Default-constructed BrushOp has kind=None and must be rejected.");
        }

        [Test]
        public void Validate_KnownKind_AcceptsKindCheck()
        {
            BrushOp sphere = MakeSphere(Vector3.zero, radius: 2f);
            BrushOp capsule = MakeCapsule(Vector3.zero, new Vector3(1f, 0f, 0f), radius: 2f);
            Assert.IsTrue(BrushOpValidator.ValidateKind(sphere));
            Assert.IsTrue(BrushOpValidator.ValidateKind(capsule));
        }

        // ------------------------------------------------------------------
        // Radius
        // ------------------------------------------------------------------

        [Test]
        public void Validate_ZeroRadius_Rejects()
        {
            BrushOp op = new BrushOp { kind = BrushKind.SphereSubtract, radiusFixed = 0 };
            Assert.IsFalse(BrushOpValidator.Validate(op, Zone));
            Assert.IsFalse(BrushOpValidator.ValidateRadius(op));
        }

        [Test]
        public void Validate_RadiusAtMax_Accepts()
        {
            BrushOp op = MakeSphere(Vector3.zero, BrushOpValidator.MaxRadiusMeters);
            Assert.IsTrue(BrushOpValidator.ValidateRadius(op),
                "Radius exactly at the max should be accepted (closed upper bound).");
        }

        [Test]
        public void Validate_RadiusAboveMax_Rejects()
        {
            // 17m radius — bigger than the cap. Anti-cheat surface area.
            BrushOp op = MakeSphere(Vector3.zero, BrushOpValidator.MaxRadiusMeters + 1f);
            Assert.IsFalse(BrushOpValidator.ValidateRadius(op));
            Assert.IsFalse(BrushOpValidator.Validate(op, Zone),
                "Oversized brush must fail the full validator pipeline.");
        }

        // ------------------------------------------------------------------
        // Zone overlap
        // ------------------------------------------------------------------

        [Test]
        public void Validate_BrushInsideZone_AcceptsOverlap()
        {
            BrushOp op = MakeSphere(Vector3.zero, radius: 2f);
            Assert.IsTrue(BrushOpValidator.ValidateZoneOverlap(op, Zone));
        }

        [Test]
        public void Validate_BrushGrazingZoneEdge_AcceptsOverlap()
        {
            // Centre at zone face plane, radius 1m — bounding sphere
            // dips inside by ~0.5m. Should pass.
            BrushOp op = MakeSphere(new Vector3(8f, 0f, 0f), radius: 1f);
            Assert.IsTrue(BrushOpValidator.ValidateZoneOverlap(op, Zone));
        }

        [Test]
        public void Validate_BrushFarFromZone_RejectsOverlap()
        {
            // Centre 1000m away, radius 2m — no chance of grazing.
            BrushOp op = MakeSphere(new Vector3(1000f, 0f, 0f), radius: 2f);
            Assert.IsFalse(BrushOpValidator.ValidateZoneOverlap(op, Zone),
                "A brush 1000m from the zone with radius 2m cannot overlap.");
            Assert.IsFalse(BrushOpValidator.Validate(op, Zone));
        }

        [Test]
        public void Validate_CapsuleSweepingThroughZone_AcceptsOverlap()
        {
            // Capsule from p0 outside zone, p1 outside zone, but the
            // segment passes through the zone. Should overlap.
            BrushOp op = MakeCapsule(
                p0: new Vector3(-12f, 0f, 0f),
                p1: new Vector3( 12f, 0f, 0f),
                radius: 1f);
            Assert.IsTrue(BrushOpValidator.ValidateZoneOverlap(op, Zone),
                "A capsule that sweeps through the zone must pass overlap.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static BrushOp MakeSphere(Vector3 center, float radius) => new BrushOp
        {
            kind = BrushKind.SphereSubtract,
            serverTick = 1,
            p0 = Vector3Fixed.FromVector3(center),
            p1 = Vector3Fixed.FromVector3(center),
            radiusFixed = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(radius * Vector3Fixed.UnitsPerMeter),
                0, ushort.MaxValue),
        };

        private static BrushOp MakeCapsule(Vector3 p0, Vector3 p1, float radius) => new BrushOp
        {
            kind = BrushKind.CapsuleSubtract,
            serverTick = 1,
            p0 = Vector3Fixed.FromVector3(p0),
            p1 = Vector3Fixed.FromVector3(p1),
            radiusFixed = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(radius * Vector3Fixed.UnitsPerMeter),
                0, ushort.MaxValue),
        };
    }
}
