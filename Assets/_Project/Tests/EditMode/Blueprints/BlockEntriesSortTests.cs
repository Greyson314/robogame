using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pin the netcode contract: every path that mutates a
    /// <see cref="ChassisBlueprint"/>'s entries must produce the same
    /// canonical order, regardless of authoring sequence. See
    /// <c>docs/NETCODE_PLAN.md</c> §6 and the §3.1 diagnosis in
    /// <c>docs/BUILDING_ARCHITECTURE_REVIEW.md</c>.
    /// </summary>
    public sealed class BlockEntriesSortTests
    {
        // -----------------------------------------------------------------
        // Compare / IsCanonical primitives
        // -----------------------------------------------------------------

        [Test]
        public void Compare_OrdersByZThenYThenX()
        {
            var a = new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, 0));
            var b = new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, 1));
            var c = new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 1, 0));
            var d = new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, 0));

            // z dominates: (0,0,1) > (0,1,0) > (1,0,0) > (0,0,0).
            Assert.Less(BlockEntries.Compare(a, b), 0);
            Assert.Less(BlockEntries.Compare(d, b), 0, "z=1 must come after z=0 even when x=1.");
            Assert.Less(BlockEntries.Compare(c, b), 0, "z=1 must come after z=0 even when y=1.");
            // y dominates over x within the same z.
            Assert.Less(BlockEntries.Compare(d, c), 0);
        }

        [Test]
        public void Compare_TieBreaksOnBlockId()
        {
            var aero = new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(2, 3, 5));
            var cube = new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(2, 3, 5));
            // Two entries at the same cell are an authoring error caught
            // by the validator; the tie-break exists so SortCanonical
            // remains a total order on malformed input.
            Assert.AreNotEqual(0, BlockEntries.Compare(aero, cube),
                "Same-cell entries must still be totally ordered (BlockId tie-break).");
        }

        [Test]
        public void IsCanonical_TrueForEmptyAndSingleton()
        {
            Assert.IsTrue(BlockEntries.IsCanonical(null));
            Assert.IsTrue(BlockEntries.IsCanonical(System.Array.Empty<ChassisBlueprint.Entry>()));
            Assert.IsTrue(BlockEntries.IsCanonical(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(1, 2, 3)),
            }));
        }

        // -----------------------------------------------------------------
        // SetEntries chokepoint
        // -----------------------------------------------------------------

        [Test]
        public void SetEntries_SortsArrayIntoCanonicalOrder()
        {
            ChassisBlueprint.Entry[] scrambled =
            {
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 2, 0, 1)),
                new ChassisBlueprint.Entry(BlockIds.Cpu,  new Vector3Int( 0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 0, 1, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(-1, 0, 0)),
            };

            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.SetEntries(scrambled);

            Assert.IsTrue(BlockEntries.IsCanonical(bp.Entries),
                "SetEntries must sort entries into canonical order — netcode contract.");
            Object.DestroyImmediate(bp);
        }

        [Test]
        public void SetEntries_IsIdempotent()
        {
            // Two blueprints, same contents, different authoring order —
            // entries must compare equal slot-for-slot.
            var aOrder = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu,  new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 1, 0)),
            };
            var bOrder = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 1, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cpu,  new Vector3Int(0, 0, 0)),
            };

            var bpA = ScriptableObject.CreateInstance<ChassisBlueprint>();
            var bpB = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bpA.SetEntries(aOrder);
            bpB.SetEntries(bOrder);

            Assert.AreEqual(bpA.Entries.Length, bpB.Entries.Length);
            for (int i = 0; i < bpA.Entries.Length; i++)
            {
                Assert.AreEqual(bpA.Entries[i].BlockId, bpB.Entries[i].BlockId);
                Assert.AreEqual(bpA.Entries[i].Position, bpB.Entries[i].Position,
                    $"Slot {i} differs — same blueprint contents must produce the same spawn order.");
            }

            Object.DestroyImmediate(bpA);
            Object.DestroyImmediate(bpB);
        }

        // -----------------------------------------------------------------
        // Round-trip through serializer
        // -----------------------------------------------------------------

        [Test]
        public void SerializerRoundTrip_PreservesCanonicalOrder()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "SortTest";
            bp.Kind = ChassisKind.Ground;
            bp.SetEntries(new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 2, 0, 1)),
                new ChassisBlueprint.Entry(BlockIds.Cpu,  new Vector3Int( 0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(-1, 0, 0)),
            });

            string json = BlueprintSerializer.ToJson(bp, prettyPrint: false);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint loaded, out string error),
                $"Round-trip failed: {error}");

            Assert.IsTrue(BlockEntries.IsCanonical(loaded.Entries),
                "Loaded blueprint must come out canonically sorted.");
            Assert.AreEqual(bp.Entries.Length, loaded.Entries.Length);
            for (int i = 0; i < bp.Entries.Length; i++)
            {
                Assert.AreEqual(bp.Entries[i].Position, loaded.Entries[i].Position,
                    $"Slot {i} drifted across round-trip.");
                Assert.AreEqual(bp.Entries[i].BlockId, loaded.Entries[i].BlockId);
            }

            Object.DestroyImmediate(bp);
            Object.DestroyImmediate(loaded);
        }

        [Test]
        public void BuilderToBlueprint_ProducesCanonicalOrderRegardlessOfAuthoring()
        {
            // Two equivalent builds with different statement orders must
            // produce blueprints whose Entries arrays are identical
            // slot-for-slot once they pass through SetEntries.
            BlueprintPlan firstAuthoring = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Block(BlockIds.Cube, 1, 0, 0)
                .Block(BlockIds.Cube, 0, 1, 0)
                .Build();
            BlueprintPlan secondAuthoring = BlueprintBuilder.Create("X", ChassisKind.Ground)
                .Block(BlockIds.Cube, 0, 1, 0)
                .Block(BlockIds.Cube, 1, 0, 0)
                .Block(BlockIds.Cpu, 0, 0, 0)
                .Build();

            ChassisBlueprint bpA = firstAuthoring.ToBlueprint();
            ChassisBlueprint bpB = secondAuthoring.ToBlueprint();

            Assert.IsTrue(BlockEntries.IsCanonical(bpA.Entries));
            Assert.IsTrue(BlockEntries.IsCanonical(bpB.Entries));
            for (int i = 0; i < bpA.Entries.Length; i++)
            {
                Assert.AreEqual(bpA.Entries[i].Position, bpB.Entries[i].Position,
                    $"Slot {i} differs — authoring order must not affect spawn order.");
            }

            Object.DestroyImmediate(bpA);
            Object.DestroyImmediate(bpB);
        }
    }
}
