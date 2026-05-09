using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// EditMode tests for the consolidated BFS / orphan / CPU-locator
    /// primitives. The runtime placement editor, removal validator,
    /// blueprint validator, and damage-detachment paths all share these,
    /// so a regression here cascades — pin the contract.
    /// </summary>
    public sealed class BlockGraphTests
    {
        [Test]
        public void BfsFromPositions_ReachesEveryFaceAdjacentCell()
        {
            HashSet<Vector3Int> positions = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(2, 1, 0),
            };
            BlockGraph.Buffers buffers = new BlockGraph.Buffers();
            BlockGraph.BfsFrom(positions, new Vector3Int(0, 0, 0), buffers);
            Assert.AreEqual(4, buffers.Visited.Count);
        }

        [Test]
        public void BfsFromPositions_StopsAtDiagonalGap()
        {
            // (0,0,0) and (1,1,0) share an edge, not a face. BFS must
            // not bridge them.
            HashSet<Vector3Int> positions = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 1, 0),
            };
            BlockGraph.Buffers buffers = new BlockGraph.Buffers();
            BlockGraph.BfsFrom(positions, new Vector3Int(0, 0, 0), buffers);
            Assert.AreEqual(1, buffers.Visited.Count);
            Assert.IsFalse(buffers.Visited.Contains(new Vector3Int(1, 1, 0)));
        }

        [Test]
        public void BfsFromPositions_HonoursIgnoreCell()
        {
            HashSet<Vector3Int> positions = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0), // bridge
                new Vector3Int(2, 0, 0),
            };
            BlockGraph.Buffers buffers = new BlockGraph.Buffers();
            BlockGraph.BfsFrom(positions, new Vector3Int(0, 0, 0), buffers,
                ignoreCell: new Vector3Int(1, 0, 0));
            Assert.AreEqual(1, buffers.Visited.Count, "Ignored bridge must isolate the far cell.");
        }

        [Test]
        public void BfsFromPositions_NoOpWhenRootMissing()
        {
            HashSet<Vector3Int> positions = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
            };
            BlockGraph.Buffers buffers = new BlockGraph.Buffers();
            BlockGraph.BfsFrom(positions, new Vector3Int(5, 5, 5), buffers);
            Assert.AreEqual(0, buffers.Visited.Count);
        }

        [Test]
        public void BfsFromPositions_BuffersAreReusable()
        {
            // Same buffers, two calls — second result must reflect only
            // the second call (no leakage of first call's state).
            HashSet<Vector3Int> first = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            };
            HashSet<Vector3Int> second = new HashSet<Vector3Int>
            {
                new Vector3Int(5, 5, 5),
            };
            BlockGraph.Buffers buffers = new BlockGraph.Buffers();

            BlockGraph.BfsFrom(first, new Vector3Int(0, 0, 0), buffers);
            Assert.AreEqual(2, buffers.Visited.Count);

            BlockGraph.BfsFrom(second, new Vector3Int(5, 5, 5), buffers);
            Assert.AreEqual(1, buffers.Visited.Count);
            Assert.IsTrue(buffers.Visited.Contains(new Vector3Int(5, 5, 5)));
            Assert.IsFalse(buffers.Visited.Contains(new Vector3Int(0, 0, 0)),
                "Buffers must clear at the start of each call.");
        }
    }
}
