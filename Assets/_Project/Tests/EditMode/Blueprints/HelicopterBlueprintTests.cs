using System.Linq;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Asserts shape-level invariants on the default helicopter
    /// blueprint that the user explicitly asked for in session 19:
    /// foils are the absolute topmost cells, two guns total.
    /// </summary>
    public sealed class HelicopterBlueprintTests
    {
        private const string HelicopterPath = "Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultHelicopter.asset";

        private static ChassisBlueprint LoadHelicopter()
            => AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(HelicopterPath);

        [Test]
        public void Helicopter_FoilsAreAtAbsoluteTopmostLayer()
        {
            ChassisBlueprint bp = LoadHelicopter();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the helicopter first.");
            int maxY = bp.Entries.Max(e => e.Position.y);
            var topCells = bp.Entries.Where(e => e.Position.y == maxY).ToList();
            int foilCount = topCells.Count(e => e.BlockId == BlockIds.Aero);
            Assert.AreEqual(4, foilCount,
                $"Expected 4 foils at the topmost layer (y={maxY}); found {foilCount}. " +
                $"Topmost cells: {string.Join(", ", topCells.Select(c => c.BlockId + "@" + c.Position))}");
        }

        [Test]
        public void Helicopter_HasExactlyTwoWeaponBlocks()
        {
            ChassisBlueprint bp = LoadHelicopter();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the helicopter first.");
            int weaponCount = bp.Entries.Count(e => e.BlockId == BlockIds.Weapon);
            Assert.AreEqual(2, weaponCount,
                $"Expected 2 Weapon blocks on the helicopter; found {weaponCount}.");
        }

        [Test]
        public void Helicopter_HasMirroredGunsAcrossXAxis()
        {
            ChassisBlueprint bp = LoadHelicopter();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the helicopter first.");
            var guns = bp.Entries.Where(e => e.BlockId == BlockIds.Weapon).ToList();
            Assert.AreEqual(2, guns.Count, "Expected exactly 2 guns.");
            Assert.AreEqual(guns[0].Position.y, guns[1].Position.y, "Guns should sit at the same Y.");
            Assert.AreEqual(guns[0].Position.z, guns[1].Position.z, "Guns should sit at the same Z.");
            Assert.AreEqual(guns[0].Position.x, -guns[1].Position.x, "Guns should mirror across X.");
        }

        [Test]
        public void Helicopter_PassesFullValidation()
        {
            ChassisBlueprint bp = LoadHelicopter();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the helicopter first.");
            BlueprintPlan plan = new BlueprintPlan(bp.DisplayName, bp.Kind, bp.Entries, bp.RotorsGenerateLift);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            Assert.IsTrue(result.IsValid, $"Helicopter validation failed:\n{result}");
        }

        [Test]
        public void Helicopter_RotorsGenerateLiftIsTrue()
        {
            ChassisBlueprint bp = LoadHelicopter();
            if (bp == null) Assert.Inconclusive("Run Robogame → Build Everything to scaffold the helicopter first.");
            Assert.IsTrue(bp.RotorsGenerateLift,
                "Helicopter blueprint must have RotorsGenerateLift = true so the main rotor produces lift.");
        }
    }
}
