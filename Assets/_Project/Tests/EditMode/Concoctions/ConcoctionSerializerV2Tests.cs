using NUnit.Framework;
using Robogame.Block;

namespace Robogame.Tests.EditMode.Concoctions
{
    /// <summary>
    /// Session-141 schema-v2 tests for <see cref="ConcoctionSerializer"/>: the
    /// speed/spread lever back-compat load path and the full five-lever
    /// round-trip. Split from <c>ConcoctionTests</c> (the pre-141 3-lever
    /// suite) rather than edited in place, so the v1-vs-v2 contract stays a
    /// deliberate addition, not a silent rewrite of an existing assertion.
    /// </summary>
    public sealed class ConcoctionSerializerV2Tests
    {
        // --- v1 back-compat: the load-bearing regression this session exists to prevent ---

        [Test]
        public void TryFromJson_V1SchemaMissingSpeedSpreadFields_LoadsNeutralNotZero()
        {
            // Hand-authored v1 JSON: schemaVersion 1, no speedPct/spreadPct
            // keys at all (pre-141 files on every player's disk look like
            // this). JsonUtility zero-fills absent struct fields, so without
            // the explicit version branch in ConcoctionSerializer this would
            // silently load speed/spread at 0.0 — Multiplier(0) is 0.5×, so
            // every saved recipe would halve its projectile speed the moment
            // this build reads it back.
            string v1Json =
                "{\"schemaVersion\":1,\"id\":\"legacy-1\",\"displayName\":\"Old Mix\"," +
                "\"damagePct\":0.9,\"sizePct\":0.2,\"knockbackPct\":0.7}";

            Assert.IsTrue(ConcoctionSerializer.TryFromJson(v1Json, out Concoction c, out string err), err);
            Assert.AreEqual(Concoction.DefaultPct, c.SpeedPct, 1e-6f,
                "v1 files have no speed lever — must load at neutral (0.5), not the JsonUtility zero-fill.");
            Assert.AreEqual(Concoction.DefaultPct, c.SpreadPct, 1e-6f,
                "v1 files have no spread lever — must load at neutral (0.5), not the JsonUtility zero-fill.");
            // The pre-existing three levers still load verbatim.
            Assert.AreEqual(0.9f, c.DamagePct, 1e-4f);
            Assert.AreEqual(0.2f, c.SizePct, 1e-4f);
            Assert.AreEqual(0.7f, c.KnockbackPct, 1e-4f);
        }

        [Test]
        public void TryFromJson_V1SchemaExplicitZeroFields_StillLoadsNeutral()
        {
            // Same regression, defence-in-depth: even if a v1 DTO round-trips
            // through code that explicitly zero-writes the fields (rather
            // than omitting the keys), the version gate — not the field
            // value — is what decides neutral-vs-loaded.
            string v1Json =
                "{\"schemaVersion\":1,\"id\":\"legacy-2\",\"displayName\":\"Old Mix 2\"," +
                "\"damagePct\":0.5,\"sizePct\":0.5,\"knockbackPct\":0.5,\"speedPct\":0.0,\"spreadPct\":0.0}";

            Assert.IsTrue(ConcoctionSerializer.TryFromJson(v1Json, out Concoction c, out string err), err);
            Assert.AreEqual(Concoction.DefaultPct, c.SpeedPct, 1e-6f,
                "schemaVersion 1 must ignore any speedPct value present in the payload.");
            Assert.AreEqual(Concoction.DefaultPct, c.SpreadPct, 1e-6f,
                "schemaVersion 1 must ignore any spreadPct value present in the payload.");
        }

        // --- v2 round-trip: all five levers survive ------------------------

        [Test]
        public void RoundTrip_V2_PreservesAllFiveLevers()
        {
            var c = new Concoction("v2-id", "Full Mix",
                damagePct: 0.9f, sizePct: 0.1f, knockbackPct: 0.6f,
                speedPct: 0.8f, spreadPct: 0.3f);

            string json = ConcoctionSerializer.ToJson(c);
            Assert.IsTrue(ConcoctionSerializer.TryFromJson(json, out Concoction back, out string err), err);

            Assert.AreEqual("v2-id", back.Id);
            Assert.AreEqual("Full Mix", back.DisplayName);
            Assert.AreEqual(0.9f, back.DamagePct, 1e-4f);
            Assert.AreEqual(0.1f, back.SizePct, 1e-4f);
            Assert.AreEqual(0.6f, back.KnockbackPct, 1e-4f);
            Assert.AreEqual(0.8f, back.SpeedPct, 1e-4f, "Speed lever must survive the v2 round-trip.");
            Assert.AreEqual(0.3f, back.SpreadPct, 1e-4f, "Spread lever must survive the v2 round-trip.");
        }

        [Test]
        public void ToJson_WritesCurrentSchemaVersion()
        {
            // ToJson always stamps the writer's current schema — a v1-loaded
            // recipe that gets re-saved upgrades in place rather than
            // perpetuating the zero-fill trap on its next load.
            var c = new Concoction("id", "x");
            string json = ConcoctionSerializer.ToJson(c, prettyPrint: false);
            StringAssert.Contains($"\"schemaVersion\":{ConcoctionSerializer.CurrentSchemaVersion}", json);
        }
    }
}
