using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pure-data tests for <see cref="BlueprintSerializer"/>'s schema-v3
    /// round-trip. v1/v2 backward compatibility lives in the missing-field
    /// fall-throughs of <see cref="BlueprintSerializer.TryFromJson"/>.
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
    }
}
