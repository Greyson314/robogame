using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// EditMode tests for the planned <c>ring0Multiplier</c> parameter on
    /// <see cref="BlockGrid.ApplySplashDamage"/> and the Fuse
    /// (<c>BlockIds.Fuse</c>, id <c>"block.structure.fuse"</c>)
    /// propagation-stop rule. Both land in the same change: a splash hit's
    /// direct-hit ring can be scaled independently of its falloff rings, and
    /// a Fuse block sacrifices itself to shield whatever is behind it in the
    /// block graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Interface assumption.</b> <c>BlockIds.Fuse</c> does not exist yet
    /// at the time these tests are written — it lands with the
    /// implementation. Until then this file fails to compile, which is the
    /// expected/intended state per the test-drafter contract (write to the
    /// spec, not the current code).
    /// </para>
    /// <para>
    /// <b>Fixture pattern.</b> Mirrors <c>ModuleSystemTests.MakeDef</c> /
    /// <c>MechanismOwnerCellTests.MakeDef</c>: a <see cref="BlockDefinition"/>
    /// built via <c>ScriptableObject.CreateInstance</c> with its private
    /// <c>_id</c> / <c>_maxHealth</c> fields set by reflection, so no asset
    /// file is needed. Blocks are placed through the real
    /// <see cref="BlockGrid.PlaceBlock(BlockDefinition,Vector3Int)"/> path
    /// (not a hand-built dictionary) because <c>ApplySplashDamage</c> reads
    /// the grid's private <c>_blocks</c> map directly — the BFS and the
    /// Fuse check only see blocks that actually went through PlaceBlock.
    /// </para>
    /// </remarks>
    public sealed class SplashMultiplierAndFuseTests
    {
        private readonly List<GameObject> _spawnedRoots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _spawnedRoots)
                if (root != null) Object.DestroyImmediate(root);
            _spawnedRoots.Clear();
        }

        private static BlockDefinition MakeDef(string id, float maxHealth = 100f)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition)
                .GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(def, maxHealth);
            return def;
        }

        private BlockGrid BuildGrid()
        {
            var root = new GameObject("SplashTestGrid");
            _spawnedRoots.Add(root);
            return root.AddComponent<BlockGrid>();
        }

        // -----------------------------------------------------------------
        // ring0Multiplier
        // -----------------------------------------------------------------

        [Test]
        public void ApplySplashDamage_Ring0MultiplierAboveOne_ScalesOnlyTheDirectHit()
        {
            // WHY: ring0Multiplier exists so a "sweet spot" splash weapon can
            // reward a direct hit without also buffing its area damage. If
            // the scaling leaked into ring 1+, every splash weapon tuned
            // this way becomes a stealth AoE buff instead of a precision
            // incentive.
            BlockGrid grid = BuildGrid();
            BlockBehaviour center = grid.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(0, 0, 0));
            BlockBehaviour neighbor = grid.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(1, 0, 0));
            Assert.IsNotNull(center);
            Assert.IsNotNull(neighbor);

            grid.ApplySplashDamage(new Vector3Int(0, 0, 0), new float[] { 10f, 5f }, ring0Multiplier: 2f);

            Assert.AreEqual(80f, center.CurrentHealth, 1e-3f,
                "Ring 0 (direct hit) must take ringDamage[0] * ring0Multiplier (10 * 2 = 20 dealt).");
            Assert.AreEqual(95f, neighbor.CurrentHealth, 1e-3f,
                "Ring 1 damage must pass through unscaled — the multiplier is direct-hit-only.");
        }

        [Test]
        public void ApplySplashDamage_OmittedRing0Multiplier_MatchesExplicitOne()
        {
            // WHY: every existing splash call site (SMG / cannon / mortar /
            // bomb impacts) uses the pre-existing 2-arg call. If the new
            // parameter's default weren't an exact no-op, landing this
            // change would silently retune every already-shipping weapon's
            // damage the moment it compiled.
            BlockGrid gridOmitted = BuildGrid();
            BlockBehaviour a1 = gridOmitted.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(0, 0, 0));
            BlockBehaviour b1 = gridOmitted.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(1, 0, 0));

            BlockGrid gridExplicit = BuildGrid();
            BlockBehaviour a2 = gridExplicit.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(0, 0, 0));
            BlockBehaviour b2 = gridExplicit.PlaceBlock(MakeDef(BlockIds.Cube), new Vector3Int(1, 0, 0));

            float[] ringDamage = { 10f, 5f };
            gridOmitted.ApplySplashDamage(new Vector3Int(0, 0, 0), ringDamage);        // param omitted
            gridExplicit.ApplySplashDamage(new Vector3Int(0, 0, 0), ringDamage, 1f);   // explicit default

            Assert.AreEqual(a2.CurrentHealth, a1.CurrentHealth, 1e-3f,
                "Omitting ring0Multiplier must be behaviourally identical to passing 1f.");
            Assert.AreEqual(b2.CurrentHealth, b1.CurrentHealth, 1e-3f);
        }

        // -----------------------------------------------------------------
        // Fuse propagation stop
        // -----------------------------------------------------------------

        [Test]
        public void ApplySplashDamage_FuseInPath_StopsPropagationPastFuse()
        {
            // WHY: Fuse's entire purpose is to sacrifice itself so blocks
            // further down the line are shielded from a splash hit. If the
            // BFS still walks through a Fuse's neighbours, Fuse is a
            // strictly-worse Cube (dies just as fast, protects nothing) and
            // no one would ever build with it.
            BlockGrid grid = BuildGrid();
            Vector3Int posA = new Vector3Int(0, 0, 0);
            Vector3Int posB = new Vector3Int(1, 0, 0);
            Vector3Int posC = new Vector3Int(2, 0, 0);
            Vector3Int posD = new Vector3Int(3, 0, 0);

            BlockBehaviour a = grid.PlaceBlock(MakeDef(BlockIds.Cube), posA);
            BlockBehaviour b = grid.PlaceBlock(MakeDef(BlockIds.Fuse), posB);
            BlockBehaviour c = grid.PlaceBlock(MakeDef(BlockIds.Cube), posC);
            BlockBehaviour d = grid.PlaceBlock(MakeDef(BlockIds.Cube), posD);
            Assert.IsNotNull(a); Assert.IsNotNull(b); Assert.IsNotNull(c); Assert.IsNotNull(d);

            // 4 rings so, absent the fuse rule, C (ring 2) and D (ring 3)
            // would both take damage.
            grid.ApplySplashDamage(posA, new float[] { 40f, 30f, 20f, 10f });

            Assert.AreEqual(60f, a.CurrentHealth, 1e-3f, "Ring 0 (direct hit) always takes its listed damage.");
            Assert.AreEqual(70f, b.CurrentHealth, 1e-3f, "The fuse itself still takes its own ring's damage.");
            Assert.AreEqual(100f, c.CurrentHealth, 1e-3f,
                "The fuse must not enqueue its neighbours — nothing propagates past it, so C is untouched.");
            Assert.AreEqual(100f, d.CurrentHealth, 1e-3f,
                "D is unreachable once the fuse blocks the only path to it.");
        }

        [Test]
        public void ApplySplashDamage_DirectHitOnFuse_DamagesFuseOnlyNotNeighbours()
        {
            // WHY: the "stop propagation" rule applies to blocks BEYOND the
            // fuse, not the fuse's own ring — a hit landing directly ON the
            // fuse must still damage it normally, then simply go no further.
            BlockGrid grid = BuildGrid();
            Vector3Int fusePos = new Vector3Int(0, 0, 0);
            Vector3Int neighborPos = new Vector3Int(1, 0, 0);

            BlockBehaviour fuse = grid.PlaceBlock(MakeDef(BlockIds.Fuse), fusePos);
            BlockBehaviour neighbor = grid.PlaceBlock(MakeDef(BlockIds.Cube), neighborPos);
            Assert.IsNotNull(fuse);
            Assert.IsNotNull(neighbor);

            grid.ApplySplashDamage(fusePos, new float[] { 15f, 15f });

            Assert.AreEqual(85f, fuse.CurrentHealth, 1e-3f, "Fuse takes its own ring-0 damage normally.");
            Assert.AreEqual(100f, neighbor.CurrentHealth, 1e-3f,
                "A hit landing directly on the fuse must not reach ring 1 at all.");
        }
    }
}
