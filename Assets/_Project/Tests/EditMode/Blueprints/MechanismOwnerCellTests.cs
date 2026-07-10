// =============================================================================
// MechanismOwnerCellTests — EditMode (session 138 rotor pick-redirect fix)
//
// INVARIANTS COVERED
//   • A cube sitting at rotor.cell + rotor.Up IS the rotor's mechanism cube:
//     ResolveMechanismOwnerCell must return the rotor's cell so cursor verbs
//     (eyedropper / tune-bind / removal) land on the rotor the player SEES,
//     not the invisible cube whose collider caught the ray.
//   • The redirect keys on the rotor's spin axis: a rotor adjacent to a cube
//     but NOT pointing at it must not claim the cube.
//   • Non-cube blocks, cube-less cells, and cubes with no adjacent rotor all
//     pass through unchanged — the redirect must never reroute a legitimate
//     pick on a visible block.
//
// WHY THIS MATTERS
//   The rotor's mast visual extends into the mechanism cell, but the collider
//   there belongs to an auto-placed invisible Cube (BuildSession.
//   AutoPlaceCompanionsOf). Before the redirect, middle-clicking the upper
//   half of the mast eyedropped a plain Cube — the player had to aim at the
//   bottom half of the stem to duplicate a rotor (user report, session 138).
//
// PATTERN
//   Pure-static helper on BuildSession, driven with a plain dictionary —
//   same MakeDef / MakeBlock reflection stubs as BuildSessionInstanceEditTests.
// =============================================================================

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// EditMode tests for <see cref="BuildSession.ResolveMechanismOwnerCell"/>.
    /// </summary>
    public sealed class MechanismOwnerCellTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, 100f);
            return def;
        }

        /// <summary>
        /// Minimal BlockBehaviour stub with a real Definition, GridPosition
        /// and Up — the three fields the resolver reads. Same reflection
        /// path as BuildSessionInstanceEditTests.MakeBlock.
        /// </summary>
        private BlockBehaviour MakeBlock(string blockId, Vector3Int cell, Vector3Int up)
        {
            var go = new GameObject($"TestBlock_{blockId}_{cell}");
            _spawned.Add(go);
            BlockBehaviour bb = go.AddComponent<BlockBehaviour>();
            BlockDefinition def = MakeDef(blockId);

            var initMethod = typeof(BlockBehaviour).GetMethod(
                "Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(initMethod,
                "BlockBehaviour.Initialize not found — the resolver test needs " +
                "GridPosition and Up set, so the _definition-only fallback is not enough here.");
            // params: (definition, gridPosition, dims, up, pitchDeg, yaw)
            initMethod.Invoke(bb, new object[] { def, cell, Vector3.zero, up, 0f, 0 });
            return bb;
        }

        private Dictionary<Vector3Int, BlockBehaviour> GridOf(params BlockBehaviour[] blocks)
        {
            var dict = new Dictionary<Vector3Int, BlockBehaviour>();
            foreach (BlockBehaviour b in blocks) dict[b.GridPosition] = b;
            return dict;
        }

        [Test]
        public void CubeAboveRotor_RedirectsToRotorCell()
        {
            var rotorCell = new Vector3Int(0, 0, 0);
            var mechCell  = new Vector3Int(0, 1, 0);
            var blocks = GridOf(
                MakeBlock(BlockIds.Rotor, rotorCell, Vector3Int.up),
                MakeBlock(BlockIds.Cube,  mechCell,  Vector3Int.up));

            Vector3Int resolved = BuildSession.ResolveMechanismOwnerCell(blocks, mechCell);

            Assert.AreEqual(rotorCell, resolved,
                "A cube on the rotor's spin-axis face is the mechanism cube — " +
                "picks on it must land on the rotor the player is looking at.");
        }

        [Test]
        public void SidewaysRotor_ClaimsCubeOnItsSpinAxis()
        {
            // Rotor mounted on a wall, spin axis +X: mechanism cube sits at +X.
            var rotorCell = new Vector3Int(2, 0, 0);
            var mechCell  = new Vector3Int(3, 0, 0);
            var blocks = GridOf(
                MakeBlock(BlockIds.Rotor, rotorCell, Vector3Int.right),
                MakeBlock(BlockIds.Cube,  mechCell,  Vector3Int.right));

            Assert.AreEqual(rotorCell, BuildSession.ResolveMechanismOwnerCell(blocks, mechCell),
                "The redirect must follow the rotor's actual spin axis, not assume +Y.");
        }

        [Test]
        public void CubeBesideRotor_NotOnSpinAxis_IsNotRedirected()
        {
            // Rotor points up; cube sits to its side. The cube is ordinary
            // structure — a pick on it must stay a cube pick.
            var rotorCell = new Vector3Int(0, 0, 0);
            var cubeCell  = new Vector3Int(1, 0, 0);
            var blocks = GridOf(
                MakeBlock(BlockIds.Rotor, rotorCell, Vector3Int.up),
                MakeBlock(BlockIds.Cube,  cubeCell,  Vector3Int.up));

            Assert.AreEqual(cubeCell, BuildSession.ResolveMechanismOwnerCell(blocks, cubeCell),
                "A cube that is merely adjacent to a rotor (not on its spin axis) " +
                "must not be rerouted — the player can see and target it normally.");
        }

        [Test]
        public void NonCubeBlock_PassesThroughUnchanged()
        {
            var rotorCell = new Vector3Int(0, 0, 0);
            var blocks = GridOf(MakeBlock(BlockIds.Rotor, rotorCell, Vector3Int.up));

            Assert.AreEqual(rotorCell, BuildSession.ResolveMechanismOwnerCell(blocks, rotorCell),
                "Picking the rotor itself must resolve to the rotor cell, no redirect.");
        }

        [Test]
        public void PlainCube_NoRotorNeighbour_PassesThroughUnchanged()
        {
            var cubeCell = new Vector3Int(5, 5, 5);
            var blocks = GridOf(MakeBlock(BlockIds.Cube, cubeCell, Vector3Int.up));

            Assert.AreEqual(cubeCell, BuildSession.ResolveMechanismOwnerCell(blocks, cubeCell),
                "An ordinary structural cube must never be rerouted.");
        }

        [Test]
        public void EmptyCellAndNullGrid_PassThroughUnchanged()
        {
            var cell = new Vector3Int(9, 9, 9);
            Assert.AreEqual(cell, BuildSession.ResolveMechanismOwnerCell(
                new Dictionary<Vector3Int, BlockBehaviour>(), cell),
                "An empty cell resolves to itself.");
            Assert.AreEqual(cell, BuildSession.ResolveMechanismOwnerCell(null, cell),
                "A null block map must be tolerated (resolver is called from hot input paths).");
        }
    }
}
