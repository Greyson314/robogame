using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Concoctions
{
    /// <summary>
    /// Pure-logic tests for the concoction data layer (ADR-0004): the slider →
    /// multiplier curve, the monotonic CPU surcharge, range clamping, JSON
    /// round-trip, and the blueprint v7 round-trip that carries the chosen
    /// concoction id. These encode the contracts the rest of the feature leans
    /// on — change the curve and these must change with intent, not by accident.
    /// </summary>
    public sealed class ConcoctionTests
    {
        // --- multiplier curve: 0% → 0.5×, 50% → 1.0×, 100% → 2.0× -----------

        [Test]
        public void Multiplier_NeutralSlider_IsBaseline()
        {
            // The whole point of "50% = today's bomb": default must be 1.0×.
            Assert.AreEqual(1f, Concoction.Multiplier(0.5f), 1e-4f);
        }

        [Test]
        public void Multiplier_Endpoints_HitMinAndMax()
        {
            Assert.AreEqual(Concoction.MinMultiplier, Concoction.Multiplier(0f), 1e-4f);
            Assert.AreEqual(Concoction.MaxMultiplier, Concoction.Multiplier(1f), 1e-4f);
        }

        [Test]
        public void Multiplier_IsMonotonicIncreasing()
        {
            float prev = Concoction.Multiplier(0f);
            for (float p = 0.05f; p <= 1f; p += 0.05f)
            {
                float m = Concoction.Multiplier(p);
                Assert.GreaterOrEqual(m, prev, $"multiplier dropped at pct={p}");
                prev = m;
            }
        }

        [Test]
        public void Multiplier_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(Concoction.MinMultiplier, Concoction.Multiplier(-3f), 1e-4f);
            Assert.AreEqual(Concoction.MaxMultiplier, Concoction.Multiplier(5f), 1e-4f);
        }

        // --- CPU surcharge: monotonic, zero at all-min, capped at all-max ---

        [Test]
        public void CpuSurcharge_AllMin_IsZero()
        {
            // v2 (session 141): "all-min" means all FIVE levers — the two
            // added levers default to neutral 0.5, which is NOT free.
            var c = new Concoction("id", "weak", 0f, 0f, 0f, 0f, 0f);
            Assert.AreEqual(0, c.CpuSurcharge(40));
        }

        [Test]
        public void CpuSurcharge_AllMax_IsOnePointFiveBase()
        {
            var c = new Concoction("id", "max", 1f, 1f, 1f, 1f, 1f);
            // v2: base * (1+1+1+1+1) * 0.3 = base * 1.5 — same anchor the
            // v1 3-lever formula (× 0.5) priced at all-max. See ADR-0005.
            Assert.AreEqual(60, c.CpuSurcharge(40));
        }

        [Test]
        public void CpuSurcharge_RaisingASliderRaisesCost()
        {
            var lo = new Concoction("a", "lo", 0.4f, 0.5f, 0.5f);
            var hi = new Concoction("b", "hi", 0.9f, 0.5f, 0.5f);
            Assert.Greater(hi.CpuSurcharge(40), lo.CpuSurcharge(40));
        }

        [Test]
        public void CpuSurcharge_ZeroBaseBlock_NoSurcharge()
        {
            var c = new Concoction("id", "x", 1f, 1f, 1f);
            Assert.AreEqual(0, c.CpuSurcharge(0));
        }

        // --- validation -----------------------------------------------------

        [Test]
        public void Validate_ClampsSlidersToUnitRange()
        {
            var c = new Concoction("id", "bad", -2f, 7f, 0.5f);
            c.Validate();
            Assert.AreEqual(0f, c.DamagePct, 1e-4f);
            Assert.AreEqual(1f, c.SizePct, 1e-4f);
            Assert.AreEqual(0.5f, c.KnockbackPct, 1e-4f);
        }

        // --- JSON round-trip ------------------------------------------------

        [Test]
        public void Serializer_RoundTripsIdentity()
        {
            var c = new Concoction("abc123", "OBLITERATOR", 0.8f, 0.3f, 1f);
            string json = ConcoctionSerializer.ToJson(c);
            Assert.IsTrue(ConcoctionSerializer.TryFromJson(json, out Concoction back, out string err), err);
            Assert.AreEqual("abc123", back.Id);
            Assert.AreEqual("OBLITERATOR", back.DisplayName);
            Assert.AreEqual(0.8f, back.DamagePct, 1e-4f);
            Assert.AreEqual(0.3f, back.SizePct, 1e-4f);
            Assert.AreEqual(1f, back.KnockbackPct, 1e-4f);
        }

        [Test]
        public void Serializer_RejectsMissingId()
        {
            string json = "{\"schemaVersion\":1,\"displayName\":\"x\",\"damagePct\":0.5}";
            Assert.IsFalse(ConcoctionSerializer.TryFromJson(json, out _, out _));
        }

        // --- blueprint v7 carries the concoction id -------------------------

        [Test]
        public void Blueprint_RoundTripsConcoctionId()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Bomber";
            bp.SetEntries(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.BombBay, new Vector3Int(0, 1, 0),
                    Vector3Int.up, Vector3.zero, 0f, 0f, "my-concoction-id"),
            });

            string json = BlueprintSerializer.ToJson(bp);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint back, out string err), err);

            bool found = false;
            foreach (ChassisBlueprint.Entry e in back.Entries)
            {
                if (e.BlockId == BlockIds.BombBay)
                {
                    Assert.AreEqual("my-concoction-id", e.EffectiveConcoctionId);
                    found = true;
                }
            }
            Assert.IsTrue(found, "bomb entry survived the round-trip");
            Object.DestroyImmediate(bp);
            Object.DestroyImmediate(back);
        }

        [Test]
        public void Blueprint_DefaultEntry_HasEmptyConcoction()
        {
            var e = new ChassisBlueprint.Entry(BlockIds.BombBay, new Vector3Int(0, 0, 0));
            Assert.AreEqual(string.Empty, e.EffectiveConcoctionId);
        }

        [Test]
        public void Blueprint_PreV7Json_LoadsEmptyConcoction()
        {
            // A v6 entry has no concoctionId key; JsonUtility leaves it "".
            string v6 = "{\"schemaVersion\":6,\"displayName\":\"Old\",\"kind\":\"Ground\"," +
                        "\"entries\":[{\"id\":\"" + BlockIds.BombBay + "\",\"x\":0,\"y\":0,\"z\":0,\"uy\":1}]}";
            Assert.IsTrue(BlueprintSerializer.TryFromJson(v6, out ChassisBlueprint bp, out string err), err);
            Assert.AreEqual(string.Empty, bp.Entries[0].EffectiveConcoctionId);
            Object.DestroyImmediate(bp);
        }
    }
}
