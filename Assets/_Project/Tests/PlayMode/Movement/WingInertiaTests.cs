using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    /// <summary>
    /// Session-106 wing physics: a bigger wing must weigh more and add more
    /// rotational inertia, so a wide-winged plane resists roll. Verifies the
    /// mass + box-inertia scaling in <see cref="Robot.RecalculateAggregates"/>,
    /// and that a non-aero (cube) chassis is unchanged by the new model.
    /// </summary>
    public sealed class WingInertiaTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private static BlockDefinition MakeDef(string id, BlockCategory cat, float mass)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, cat);
            typeof(BlockDefinition).GetField("_mass", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, mass);
            return def;
        }

        // A CPU at origin plus two symmetric wings on the ±X cells with the
        // given span (chord/thickness left at the foil defaults). Returns the
        // recalculated Robot.
        private Robot BuildWingedChassis(float span)
        {
            _root = new GameObject("WingChassis");
            _root.AddComponent<Rigidbody>();
            BlockGrid grid = _root.AddComponent<BlockGrid>();
            Robot robot = _root.AddComponent<Robot>(); // Awake caches _rb + _grid

            grid.PlaceBlock(MakeDef(BlockIds.Cpu, BlockCategory.Cpu, 2f), new Vector3Int(0, 0, 0), Vector3Int.up);
            Vector3 dims = new Vector3(span, BlockOccupancy.FoilDefaultThickness, BlockOccupancy.FoilDefaultChord);
            BlockDefinition aero = MakeDef(BlockIds.Aero, BlockCategory.Movement, 0.6f);
            grid.PlaceBlock(aero, new Vector3Int(1, 0, 0), Vector3Int.up, dims, 0f);
            grid.PlaceBlock(aero, new Vector3Int(-1, 0, 0), Vector3Int.up, dims, 0f);

            robot.RecalculateAggregates();
            return robot;
        }

        [UnityTest]
        public IEnumerator BiggerWing_RaisesMassAndInertia()
        {
            Robot small = BuildWingedChassis(BlockOccupancy.FoilDefaultSpan);
            float smallMass = small.TotalBlockMass;
            float smallInertia = _root.GetComponent<Rigidbody>().inertiaTensor.magnitude;
            Object.Destroy(_root);
            yield return null;

            Robot big = BuildWingedChassis(BlockOccupancy.FoilDefaultSpan * 3f);
            float bigMass = big.TotalBlockMass;
            float bigInertia = _root.GetComponent<Rigidbody>().inertiaTensor.magnitude;

            Assert.Greater(bigMass, smallMass * 1.5f,
                "A 3x-span wing must weigh substantially more — mass scales with foil volume.");
            Assert.Greater(bigInertia, smallInertia * 1.5f,
                "A 3x-span wing must add substantially more rotational inertia (this is what makes wide wings roll slower).");
        }

        [UnityTest]
        public IEnumerator DefaultWing_MassEqualsAuthored()
        {
            // At default dims the foil volume ratio is 1, so effective mass ==
            // Definition.Mass — existing chassis are unaffected.
            Robot robot = BuildWingedChassis(BlockOccupancy.FoilDefaultSpan);
            yield return null;
            // CPU 2.0 + two default wings 0.6 each = 3.2 kg.
            Assert.AreEqual(3.2f, robot.TotalBlockMass, 1e-3f,
                "Default-dimension wings must not change mass — the scaling is anchored at default.");
        }

        [UnityTest]
        public IEnumerator CubeChassis_InertiaMatchesAnalyticFormula()
        {
            // Two unit cubes on ±X about the COM: non-aero path must still be a
            // cellSize cube (Izz self = m·s²/6, plus parallel axis m·d²).
            _root = new GameObject("CubeChassis");
            _root.AddComponent<Rigidbody>();
            BlockGrid grid = _root.AddComponent<BlockGrid>();
            Robot robot = _root.AddComponent<Robot>();

            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure, 1f);
            grid.PlaceBlock(cube, new Vector3Int(-1, 0, 0), Vector3Int.up);
            grid.PlaceBlock(cube, new Vector3Int(1, 0, 0), Vector3Int.up);
            robot.RecalculateAggregates();
            yield return null;

            // cellSize 1, each cube m=1 at x=±1 from COM (x=0). Izz per cube =
            // self s²/6 + m·(x²+y²) = 1/6 + 1 = 7/6. Two cubes → 7/3 ≈ 2.333.
            float izz = _root.GetComponent<Rigidbody>().inertiaTensor.z;
            Assert.AreEqual(7f / 3f, izz, 0.05f,
                "Non-aero cube inertia must match the historical cube formula — the box model reduces to it.");
        }
    }
}
