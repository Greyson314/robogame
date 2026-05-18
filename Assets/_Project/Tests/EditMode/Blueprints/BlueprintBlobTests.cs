using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Wire-codec tests for <see cref="BlueprintBlob"/>. The netcode-critical
    /// guarantees: (1) the blob round-trips every gameplay-observable field
    /// the JSON form round-trips, so a peer reconstructs the identical
    /// chassis from <c>SpawnRobotPayload</c>; (2) the content hash is stable
    /// across re-serializes (the <c>createdUtc</c> trap that would otherwise
    /// fail the connect-time content guard); (3) a malformed / newer blob is
    /// rejected, not silently misread, on the receive path.
    /// </summary>
    public sealed class BlueprintBlobTests
    {
        private static ChassisBlueprint MakeRichBlueprint()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Wire Test Rig";
            bp.Kind = ChassisKind.Plane;
            bp.RotorsGenerateLift = true;
            bp.PlaneTuning = new PlaneTuningConfig { PitchPower = 12.5f, YawDamping = 0.9f };
            bp.GroundTuning = new GroundTuningConfig { MaxSpeed = 22f };
            bp.ChassisDamping = new ChassisDampingConfig { LinearDamping = 1.3f };
            bp.ThrusterTuning = new ThrusterTuningConfig { IdleThrottle = 0.7f };
            bp.SetEntries(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Aero,
                    new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0),
                    new Vector3(4f, 0.08f, 0.9f), pitch: 8f),
                new ChassisBlueprint.Entry(BlockIds.Thruster,
                    new Vector3Int(0, 0, -1), new Vector3Int(0, 0, -1),
                    Vector3.zero, 0f, blockConfig: 420f),
            });
            return bp;
        }

        private static void AssertGameplayFieldsEqual(ChassisBlueprint a, ChassisBlueprint b, string ctx)
        {
            // displayName is intentionally NOT on the wire (architect decision,
            // handoff §5.3) — every OTHER gameplay-observable field must match.
            Assert.AreEqual(a.Kind, b.Kind, $"{ctx}: kind");
            Assert.AreEqual(a.RotorsGenerateLift, b.RotorsGenerateLift, $"{ctx}: rotorsGenerateLift");
            Assert.AreEqual(a.PlaneTuning.PitchPower, b.PlaneTuning.PitchPower, 1e-5f, $"{ctx}: plane.PitchPower");
            Assert.AreEqual(a.PlaneTuning.YawDamping, b.PlaneTuning.YawDamping, 1e-5f, $"{ctx}: plane.YawDamping");
            Assert.AreEqual(a.GroundTuning.MaxSpeed, b.GroundTuning.MaxSpeed, 1e-5f, $"{ctx}: ground.MaxSpeed");
            Assert.AreEqual(a.ChassisDamping.LinearDamping, b.ChassisDamping.LinearDamping, 1e-5f, $"{ctx}: damping.Linear");
            Assert.AreEqual(a.ThrusterTuning.IdleThrottle, b.ThrusterTuning.IdleThrottle, 1e-5f, $"{ctx}: thruster.Idle");

            Assert.AreEqual(a.Entries.Length, b.Entries.Length, $"{ctx}: entry count");
            for (int i = 0; i < a.Entries.Length; i++)
            {
                ChassisBlueprint.Entry ea = a.Entries[i];
                ChassisBlueprint.Entry eb = b.Entries[i];
                Assert.AreEqual(ea.BlockId, eb.BlockId, $"{ctx}: entry[{i}].BlockId (canonical order must match)");
                Assert.AreEqual(ea.Position, eb.Position, $"{ctx}: entry[{i}].Position");
                Assert.AreEqual(ea.EffectiveUp, eb.EffectiveUp, $"{ctx}: entry[{i}].EffectiveUp");
                Assert.AreEqual(ea.Dims, eb.Dims, $"{ctx}: entry[{i}].Dims");
                Assert.AreEqual(ea.Pitch, eb.Pitch, 1e-5f, $"{ctx}: entry[{i}].Pitch");
                Assert.AreEqual(ea.BlockConfig, eb.BlockConfig, 1e-5f, $"{ctx}: entry[{i}].BlockConfig");
            }
        }

        [Test]
        public void RoundTrip_PreservesEveryGameplayField()
        {
            ChassisBlueprint src = MakeRichBlueprint();
            byte[] blob = BlueprintBlob.Encode(src);
            Assert.IsTrue(BlueprintBlob.TryDecode(blob, out ChassisBlueprint decoded, out string error),
                $"Decode failed: {error}");
            AssertGameplayFieldsEqual(src, decoded, "blob round-trip");
        }

        [Test]
        public void BlobRoundTrip_MatchesJsonRoundTrip()
        {
            // Handoff §3.2 exit gate: decoding the blob yields the same
            // ChassisBlueprint as decoding the JSON of the same source.
            ChassisBlueprint src = MakeRichBlueprint();

            string json = BlueprintSerializer.ToJson(src, prettyPrint: false);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint fromJson, out string je),
                $"JSON decode failed: {je}");

            byte[] blob = BlueprintBlob.Encode(src);
            Assert.IsTrue(BlueprintBlob.TryDecode(blob, out ChassisBlueprint fromBlob, out string be),
                $"Blob decode failed: {be}");

            AssertGameplayFieldsEqual(fromJson, fromBlob, "blob vs json");
        }

        [Test]
        public void ContentHash_StableAcrossReserialize()
        {
            // The createdUtc trap: JSON of the same blueprint differs every
            // call (DateTime.UtcNow). The blob hash must NOT — the content
            // guard compares server vs client hashes of an identical build.
            ChassisBlueprint src = MakeRichBlueprint();
            uint h1 = BlueprintBlob.ContentHash(src);
            uint h2 = BlueprintBlob.ContentHash(src);
            Assert.AreEqual(h1, h2, "Content hash must be stable across re-serializes of the same blueprint.");

            string j1 = BlueprintSerializer.ToJson(src, prettyPrint: false);
            string j2 = BlueprintSerializer.ToJson(src, prettyPrint: false);
            Assert.AreNotEqual(j1, j2,
                "Sanity: JSON DOES vary per-serialize (createdUtc) — this is exactly why the blob excludes it.");
        }

        [Test]
        public void ContentHash_DiffersWhenGameplayStateDiffers()
        {
            ChassisBlueprint a = MakeRichBlueprint();
            uint baseHash = BlueprintBlob.ContentHash(a);

            ChassisBlueprint b = MakeRichBlueprint();
            b.PlaneTuning = new PlaneTuningConfig { PitchPower = 99f };
            Assert.AreNotEqual(baseHash, BlueprintBlob.ContentHash(b), "Changing tuning must change the hash.");

            ChassisBlueprint c = MakeRichBlueprint();
            c.SetEntries(new[] { new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)) });
            Assert.AreNotEqual(baseHash, BlueprintBlob.ContentHash(c), "Changing block layout must change the hash.");
        }

        [Test]
        public void ContentHash_IgnoresDisplayName()
        {
            // displayName is Bucket E cosmetic and off-wire: two builds that
            // differ only by name must hash identically for the content guard.
            ChassisBlueprint a = MakeRichBlueprint();
            ChassisBlueprint b = MakeRichBlueprint();
            b.DisplayName = "A Completely Different Name";
            Assert.AreEqual(BlueprintBlob.ContentHash(a), BlueprintBlob.ContentHash(b),
                "displayName must not affect the content hash.");
        }

        [Test]
        public void TryDecode_RejectsNewerVersion()
        {
            byte[] blob = BlueprintBlob.Encode(MakeRichBlueprint());
            blob[0] = (byte)(BlueprintBlob.CurrentBlobVersion + 1);
            Assert.IsFalse(BlueprintBlob.TryDecode(blob, out _, out string error),
                "A blob from a newer build must be rejected, not misread.");
            StringAssert.Contains("newer", error);
        }

        [Test]
        public void TryDecode_RejectsTruncatedBlobWithoutThrowing()
        {
            byte[] blob = BlueprintBlob.Encode(MakeRichBlueprint());
            byte[] truncated = new byte[blob.Length - 5];
            System.Array.Copy(blob, truncated, truncated.Length);
            Assert.IsFalse(BlueprintBlob.TryDecode(truncated, out _, out string error),
                "A truncated blob must fail gracefully (the receive path must not crash on a bad peer blob).");
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryDecode_RejectsNullAndEmpty()
        {
            Assert.IsFalse(BlueprintBlob.TryDecode(null, out _, out _));
            Assert.IsFalse(BlueprintBlob.TryDecode(System.Array.Empty<byte>(), out _, out _));
        }
    }
}
