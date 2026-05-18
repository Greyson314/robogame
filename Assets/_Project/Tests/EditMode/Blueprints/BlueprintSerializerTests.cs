using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlueprintSerializer"/>'s schema-v4
    /// round-trip. v1–v3 backward compatibility lives in the missing-field
    /// fall-throughs of <see cref="BlueprintSerializer.TryFromJson"/> — the
    /// netcode-critical guarantee that an old save loads behaviour-identical.
    /// </summary>
    public sealed class BlueprintSerializerTests
    {
        [Test]
        public void RoundTrip_PreservesPitch()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Pitched Wing";
            bp.Kind = ChassisKind.Plane;
            bp.SetEntries(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu,  new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Aero,
                    new Vector3Int(1, 0, 0),
                    new Vector3Int(1, 0, 0),
                    new Vector3(4f, 0.08f, 0.9f),
                    pitch: 8f),
            });

            string json = BlueprintSerializer.ToJson(bp, prettyPrint: false);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint loaded, out string error),
                $"Round-trip failed: {error}");

            ChassisBlueprint.Entry foil = System.Array.Find(loaded.Entries, e => e.BlockId == BlockIds.Aero);
            Assert.AreEqual(8f, foil.Pitch, 1e-4f, "Pitch must survive a JSON round-trip.");
        }

        [Test]
        public void LegacyV2Json_LoadsWithDefaultPitch()
        {
            // Hand-rolled v2 JSON (no pitch field). Loader should default to 0.
            string v2Json = @"{
                ""schemaVersion"": 2,
                ""displayName"": ""Legacy"",
                ""kind"": ""Ground"",
                ""createdUtc"": ""2026-05-08T00:00:00Z"",
                ""rotorsGenerateLift"": false,
                ""entries"": [
                    { ""id"": ""block.cpu.standard"", ""x"": 0, ""y"": 0, ""z"": 0,
                      ""ux"": 0, ""uy"": 1, ""uz"": 0,
                      ""dx"": 0, ""dy"": 0, ""dz"": 0 }
                ]
            }";
            Assert.IsTrue(BlueprintSerializer.TryFromJson(v2Json, out ChassisBlueprint bp, out string error),
                $"v2 load failed: {error}");
            Assert.AreEqual(0f, bp.Entries[0].Pitch,
                "v2 entries should default to pitch=0 (no AoA offset; preserves prior behaviour).");
        }

        [Test]
        public void RoundTrip_V4_PreservesChassisTuningAndBlockConfig()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Tuned";
            bp.Kind = ChassisKind.Plane;
            bp.PlaneTuning = new PlaneTuningConfig { PitchPower = 12.5f, YawDamping = 0.9f };
            bp.GroundTuning = new GroundTuningConfig { MaxSpeed = 22f };
            bp.ChassisDamping = new ChassisDampingConfig { LinearDamping = 1.3f };
            bp.ThrusterTuning = new ThrusterTuningConfig { IdleThrottle = 0.7f };
            bp.SetEntries(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Thruster,
                    new Vector3Int(0, 0, -1), new Vector3Int(0, 0, -1),
                    Vector3.zero, pitch: 0f, blockConfig: 850f),
            });

            string json = BlueprintSerializer.ToJson(bp, prettyPrint: false);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint loaded, out string error),
                $"v4 round-trip failed: {error}");

            Assert.AreEqual(12.5f, loaded.PlaneTuning.PitchPower, 1e-4f,
                "Chassis-level plane tuning must survive the round-trip — it is now " +
                "server-authoritative, not a per-machine Tweakable.");
            Assert.AreEqual(0.9f, loaded.PlaneTuning.YawDamping, 1e-4f);
            Assert.AreEqual(22f, loaded.GroundTuning.MaxSpeed, 1e-4f);
            Assert.AreEqual(1.3f, loaded.ChassisDamping.LinearDamping, 1e-4f);
            Assert.AreEqual(0.7f, loaded.ThrusterTuning.IdleThrottle, 1e-4f);

            ChassisBlueprint.Entry thr = System.Array.Find(loaded.Entries, e => e.BlockId == BlockIds.Thruster);
            Assert.AreEqual(850f, thr.BlockConfig, 1e-4f,
                "Per-block config (thruster max thrust) must survive — wrong value here " +
                "means a tuned thruster reverts to default on every reload.");
        }

        [Test]
        public void LegacyV3Json_LoadsWithDefaultTuning()
        {
            // Hand-rolled v3 JSON: no tuning objects, no blockConfig. The
            // loaded blueprint MUST carry the historical Tweakable defaults
            // (PitchPower 7.5, MaxSpeed 13.5, LinearDamping 0.2,
            // IdleThrottle 0.4) and blockConfig 0 — anything else silently
            // changes how every pre-v4 save flies/drives the moment netcode
            // starts reading the blueprint instead of the local Tweakable.
            string v3Json = @"{
                ""schemaVersion"": 3,
                ""displayName"": ""LegacyV3"",
                ""kind"": ""Plane"",
                ""createdUtc"": ""2026-05-10T00:00:00Z"",
                ""rotorsGenerateLift"": false,
                ""entries"": [
                    { ""id"": ""block.cpu.standard"", ""x"": 0, ""y"": 0, ""z"": 0,
                      ""ux"": 0, ""uy"": 1, ""uz"": 0,
                      ""dx"": 0, ""dy"": 0, ""dz"": 0, ""pitch"": 0 }
                ]
            }";
            Assert.IsTrue(BlueprintSerializer.TryFromJson(v3Json, out ChassisBlueprint bp, out string error),
                $"v3 load failed: {error}");

            Assert.AreEqual(7.5f, bp.PlaneTuning.PitchPower, 1e-4f,
                "v3 save must load with the pre-migration Plane.PitchPower default (7.5).");
            Assert.AreEqual(2.0f, bp.PlaneTuning.YawFromBank, 1e-4f);
            Assert.AreEqual(13.5f, bp.GroundTuning.MaxSpeed, 1e-4f,
                "v3 save must load with the pre-migration Ground.MaxSpeed default (13.5).");
            Assert.AreEqual(0.2f, bp.ChassisDamping.LinearDamping, 1e-4f,
                "v3 save must load with the pre-migration Chassis.LinearDamping default (0.2).");
            Assert.AreEqual(0.4f, bp.ThrusterTuning.IdleThrottle, 1e-4f,
                "v3 save must load with the pre-migration Thruster.IdleThrottle default (0.4).");
            Assert.AreEqual(0f, bp.Entries[0].BlockConfig,
                "v3 entries have no blockConfig → 0 → 'use the block's authored default'.");
        }
    }
}
