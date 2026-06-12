using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    // Why these tests exist
    // ---------------------
    // RotorDefaults is the single source of truth for the RPM→CPU pricing
    // curve.  The game-design constraint driving quadratic pricing is that
    // blade lift scales with tip-speed² — doubling RPM quadruples lift AND
    // quadruples CPU cost, keeping lift-per-CPU constant.  If the curve
    // drifts, high-RPM rotors become either pay-to-win (under-priced) or
    // unusable (over-priced).  These tests pin the mathematical identity;
    // the authored sticker prices on disk are verified separately via the
    // library integration tests (RotorCpuBudgetIntegrationTests).
    //
    // INV-5 (zero-baseline) is also exercised here: an authored-free rotor
    // (CpuCost 0 in the BlockDefinition) must stay free at any RPM.

    public sealed class RotorDefaultsTests
    {
        // -----------------------------------------------------------------
        // ResolveRpm
        // -----------------------------------------------------------------

        [Test]
        public void ResolveRpm_ZeroConfig_ReturnsDefaultRpm()
        {
            // A blueprint entry with no authored RPM (BlockConfig 0) must
            // spin at DefaultRpm — the value the UI variant panel always
            // displayed before per-block config was introduced.
            Assert.AreEqual(RotorDefaults.DefaultRpm, RotorDefaults.ResolveRpm(0f), 1e-4f,
                "BlockConfig 0 must map to DefaultRpm (240) so pre-schema saves fly at the advertised rate.");
        }

        [Test]
        public void ResolveRpm_NegativeConfig_ReturnsDefaultRpm()
        {
            // Negative configs are not valid authored values but may arrive
            // from corrupt saves; the method must clamp to default rather
            // than propagate garbage into the physics or pricing paths.
            Assert.AreEqual(RotorDefaults.DefaultRpm, RotorDefaults.ResolveRpm(-1f), 1e-4f,
                "Negative BlockConfig must fall back to DefaultRpm (not produce negative RPM).");
            Assert.AreEqual(RotorDefaults.DefaultRpm, RotorDefaults.ResolveRpm(-999f), 1e-4f);
        }

        [Test]
        public void ResolveRpm_PositiveConfig_ReturnsItself()
        {
            // An explicitly authored RPM is returned unchanged; the schema
            // value IS the rpm.
            Assert.AreEqual(600f, RotorDefaults.ResolveRpm(600f), 1e-4f);
            Assert.AreEqual(30f,  RotorDefaults.ResolveRpm(30f),  1e-4f,
                "Explicitly authored RPM must pass through unchanged.");
        }

        // -----------------------------------------------------------------
        // CpuCostFor — mathematical invariants (no asset load required)
        // -----------------------------------------------------------------

        [Test]
        public void CpuCostFor_DefaultRpm_ReturnsExactBaseCost()
        {
            // At DefaultRpm the scale factor is (DefaultRpm / CpuReferenceRpm)²
            // = 1.0, so the result must equal the sticker price.  This is the
            // most important invariant: an untouched rotor costs exactly what
            // the block library says.
            const int sticker = 10;
            int cost = RotorDefaults.CpuCostFor(sticker, 0f); // config 0 → DefaultRpm
            Assert.AreEqual(sticker, cost,
                "A rotor at DefaultRpm (config 0) must cost exactly its authored sticker price.");
        }

        [Test]
        public void CpuCostFor_MaxRpm600_CostsApproximately625PercentOfSticker()
        {
            // 600 RPM / 240 RPM = 2.5; 2.5² = 6.25.  The rotor lifting 6.25×
            // as much must cost 6.25× the CPU — lift-per-CPU stays constant.
            // Mathf.RoundToInt introduces ≤0.5 CPU of rounding error, which is
            // negligible at realistic sticker values (≥10).
            const int sticker = 10;
            int cost = RotorDefaults.CpuCostFor(sticker, 600f);
            // 10 × 6.25 = 62.5 → rounds to 63.
            Assert.AreEqual(Mathf.RoundToInt(sticker * 6.25f), cost,
                "600 RPM rotor must cost ≈6.25× sticker (tip-speed² pricing, lift-per-CPU constant).");
        }

        [Test]
        public void CpuCostFor_HalfRpm120_CostsOneQuarterOfSticker()
        {
            // 120 / 240 = 0.5; 0.5² = 0.25.  A slow decorative rotor at
            // 120 RPM lifts 25% as much and should pay 25% of the sticker.
            // Floor at 1 applies, so sticker must be ≥ 4 for the 25% math
            // to dominate.
            const int sticker = 100;
            int cost = RotorDefaults.CpuCostFor(sticker, 120f);
            Assert.AreEqual(Mathf.Max(1, Mathf.RoundToInt(sticker * 0.25f)), cost,
                "120 RPM rotor (half default) must cost ¼ of sticker (quadratic).");
        }

        [Test]
        public void CpuCostFor_VerySlowRpm_FloorsAtOne()
        {
            // Even at the minimum authored RPM (30), a sticker-1 rotor must
            // cost at least 1 CPU — a truly free tier for decorative spinners
            // is only for blocks with an authored 0 sticker (INV-5 carve-out).
            const int sticker = 1;
            int cost = RotorDefaults.CpuCostFor(sticker, RotorDefaults.MinRpm);
            Assert.GreaterOrEqual(cost, 1,
                "Non-zero sticker rotor must never undercut 1 CPU regardless of RPM (floor applies).");
        }

        [Test]
        [Description("INV-5: zero-baseline cost must remain zero at any RPM.")]
        public void CpuCostFor_ZeroBaseCost_StaysFreeAtAnyRpm()
        {
            // INV-5 (zero-baseline invariant): an authored-free block must
            // never incur CPU cost from RPM scaling or any other per-instance
            // surcharge.  If this breaks, adding rotors to a chassis suddenly
            // consumes budget that the player and UI said was free.
            Assert.AreEqual(0, RotorDefaults.CpuCostFor(0, 0f),   "Free at default RPM.");
            Assert.AreEqual(0, RotorDefaults.CpuCostFor(0, 600f),  "Free at max RPM.");
            Assert.AreEqual(0, RotorDefaults.CpuCostFor(0, 30f),   "Free at min RPM.");
        }

        [Test]
        public void CpuCostFor_NegativeBaseCost_TreatedAsZero()
        {
            // Negative sticker is not a valid authored value but must not
            // produce negative CPU cost (which would increase the effective
            // budget, a security/balance exploit).
            Assert.AreEqual(0, RotorDefaults.CpuCostFor(-5, 240f),
                "Negative baseCost must be clamped to zero, not produce negative CPU cost.");
        }

        // -----------------------------------------------------------------
        // Quadratic monotonicity
        // -----------------------------------------------------------------

        [Test]
        public void CpuCostFor_HigherRpm_NeverCostsLess()
        {
            // The pricing curve must be monotonically non-decreasing in RPM
            // (faster always costs at least as much).  Test against a
            // representative sticker large enough that rounding can't reverse
            // the order.
            const int sticker = 20;
            int costSlow   = RotorDefaults.CpuCostFor(sticker, 120f);
            int costMedium = RotorDefaults.CpuCostFor(sticker, 240f);
            int costFast   = RotorDefaults.CpuCostFor(sticker, 480f);
            int costMax    = RotorDefaults.CpuCostFor(sticker, 600f);
            Assert.LessOrEqual(costSlow,   costMedium, "120 RPM must not cost more than 240 RPM.");
            Assert.LessOrEqual(costMedium, costFast,   "240 RPM must not cost more than 480 RPM.");
            Assert.LessOrEqual(costFast,   costMax,    "480 RPM must not cost more than 600 RPM.");
        }
    }

    // =====================================================================

    /// <summary>
    /// Integration tests that load the real BlockDefinitionLibrary and verify
    /// that CpuBudget.EffectiveCpuCost routes rotor entries through
    /// RotorDefaults, and that TrimToFit prioritises expensive high-RPM
    /// rotors for removal before cheaper blocks at equal BFS distance.
    /// </summary>
    public sealed class RotorCpuBudgetIntegrationTests
    {
        private const string LibraryAssetPath =
            "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        private BlockDefinitionLibrary _lib;

        [SetUp]
        public void SetUp()
        {
            _lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (_lib == null) Assert.Inconclusive("BlockDefinitionLibrary not found; run Build Everything first.");

            BlockDefinition rotorDef = _lib.Get(BlockIds.Rotor);
            if (rotorDef == null) Assert.Inconclusive("Rotor block definition not found in library.");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private ChassisBlueprint.Entry MakeRotorEntry(Vector3Int pos, float rpm)
        {
            var e = new ChassisBlueprint.Entry(BlockIds.Rotor, pos);
            e.BlockConfig = rpm;
            return e;
        }

        private int RotorStickerCost => _lib.Get(BlockIds.Rotor).CpuCost;
        private int RotorCostAt600 => RotorDefaults.CpuCostFor(RotorStickerCost, 600f);

        // -----------------------------------------------------------------
        // EffectiveCpuCost routing
        // -----------------------------------------------------------------

        [Test]
        public void EffectiveCpuCost_RotorAtDefaultRpm_EqualsLibraryStickerCost()
        {
            // Blueprint entry with config 0 → ResolveRpm → DefaultRpm →
            // scale = 1 → EffectiveCpuCost == sticker.
            // Confirms the routing gate (blockId == BlockIds.Rotor) fires and
            // that the pricing core agrees with the raw sticker at 1× scale.
            int sticker = RotorStickerCost;
            Assume.That(sticker, Is.GreaterThan(0),
                "Rotor sticker must be > 0 for this routing test to be meaningful.");

            var entry = MakeRotorEntry(new Vector3Int(1, 0, 0), 0f);
            BlockDefinition def = _lib.Get(BlockIds.Rotor);

            int effective = CpuBudget.EffectiveCpuCost(entry, def);

            Assert.AreEqual(sticker, effective,
                "A rotor at config 0 (DefaultRpm) must be charged exactly its library sticker price.");
        }

        [Test]
        public void EffectiveCpuCost_RotorAtMaxRpm_IsHigherThanDefaultRpm()
        {
            // At 600 RPM the scale is 6.25 — effective cost must be
            // substantially above sticker to preserve the lift-per-CPU
            // constant (design rationale).  Test against ratio rather than
            // absolute so it survives a sticker retune.
            Assume.That(RotorStickerCost, Is.GreaterThan(0));

            var defaultEntry = MakeRotorEntry(new Vector3Int(1, 0, 0), 0f);
            var maxRpmEntry  = MakeRotorEntry(new Vector3Int(2, 0, 0), 600f);
            BlockDefinition def = _lib.Get(BlockIds.Rotor);

            int costDefault = CpuBudget.EffectiveCpuCost(defaultEntry, def);
            int costMaxRpm  = CpuBudget.EffectiveCpuCost(maxRpmEntry,  def);

            Assert.Greater(costMaxRpm, costDefault,
                "A 600-RPM rotor must cost more CPU than the same rotor at DefaultRpm.");
        }

        [Test]
        public void EffectiveCpuCost_NonRotorBlock_IsUnaffectedByBlockConfig()
        {
            // Blocks that are not BlockIds.Rotor must not be routed through
            // RotorDefaults regardless of BlockConfig value.  If this breaks,
            // setting a RPM-style config on a weapon or thruster would
            // accidentally multiply its CPU cost.
            BlockDefinition weaponDef = _lib.Get(BlockIds.Weapon);
            Assume.That(weaponDef, Is.Not.Null, "Weapon block definition must exist.");
            Assume.That(weaponDef.CpuCost, Is.GreaterThan(0));

            var baseEntry = new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 0, 1));
            // BlockConfig starts at 0 from the two-arg constructor.

            var highConfigEntry = new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 0, 2));
            highConfigEntry.BlockConfig = 600f;

            int costBase       = CpuBudget.EffectiveCpuCost(baseEntry,      weaponDef);
            int costHighConfig = CpuBudget.EffectiveCpuCost(highConfigEntry, weaponDef);

            Assert.AreEqual(costBase, costHighConfig,
                "Non-rotor block CPU cost must be invariant to BlockConfig (rotor RPM scaling must not leak).");
        }

        // -----------------------------------------------------------------
        // TrimToFit sort priority
        // -----------------------------------------------------------------

        [Test]
        public void TrimToFit_PrefersDroppingHighRpmRotorBeforeCheaperBlock_AtEqualBfsDistance()
        {
            // Design intent: TrimToFit ranks removable blocks furthest-from-CPU
            // first, then pricier-first within the same BFS distance.  At equal
            // distance, a 600-RPM rotor (cost ≈6.25× sticker) must be stripped
            // before a cheap cube (cost 1) so the player's fastest-spinning
            // rotors are the sacrifice, not their structure.
            //
            // Fixture: CPU at origin; N cubes in a z-line at distances 1..N
            // consume almost all budget; then at distance N+1 we add both a
            // 600-RPM rotor and a cheap cube at the same BFS step.
            // TrimToFit must drop the rotor, not the cube.
            //
            // The fill count is computed from live sticker costs so the test
            // survives a cost retune.

            int cubeCost     = _lib.Get(BlockIds.Cube)?.CpuCost ?? 1;
            int rotorCost600 = RotorCostAt600;

            Assume.That(rotorCost600, Is.GreaterThan(cubeCost),
                "600-RPM rotor must cost more than a cube for the sort-priority test to be meaningful.");
            Assume.That(rotorCost600, Is.GreaterThan(0));

            // Fill with cubes until adding the two distance-(N+1) blocks would
            // push over budget.  We want:
            //   fillCost + rotorCost600 + cubeCost > BudgetPerCpuBlock
            //   fillCost + cubeCost <= BudgetPerCpuBlock
            // Pick fillCost = BudgetPerCpuBlock - rotorCost600, then the
            // over-budget amount is exactly (rotorCost600 - 0) after removing
            // the cube — i.e. the rotor alone is enough to tip the scale.
            int fillBudget  = CpuBudget.BudgetPerCpuBlock - rotorCost600;
            int fillCount   = fillBudget / cubeCost; // rounds down → fill ≤ fillBudget

            // Check feasibility: if cubes are so expensive fillCount == 0
            // we can't build the fixture in a meaningful way.
            Assume.That(fillCount, Is.GreaterThan(0),
                "Need at least one fill cube; adjust if cube sticker cost changed.");

            // Build the entry list.
            var entries = new List<ChassisBlueprint.Entry>
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
            };
            // Fill cubes at z=1..fillCount (all at distance 1..fillCount).
            // Use a simple z-line so every entry is distance z from the CPU.
            for (int z = 1; z <= fillCount; z++)
                entries.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, z)));

            // At distance fillCount+1: a 600-RPM rotor (expensive) and a cube
            // (cheap) both adjacent to the last fill cube (z = fillCount).
            int tipZ   = fillCount + 1;
            entries.Add(MakeRotorEntry(new Vector3Int(0, 0, tipZ), 600f)); // expensive
            entries.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, tipZ - 1))); // cheap, same BFS dist as rotor

            // Confirm the fixture is actually over budget before trimming.
            Assert.IsTrue(CpuBudget.IsOverBudget(entries.ToArray(), _lib),
                "Fixture must start over budget for TrimToFit to do anything.");

            ChassisBlueprint.Entry[] trimmed = CpuBudget.TrimToFit(entries.ToArray(), _lib, out int removed);

            Assert.Greater(removed, 0, "Over-budget chassis must have blocks removed.");
            // The expensive 600-RPM rotor at tipZ must be gone.
            Assert.IsFalse(System.Array.Exists(trimmed, e =>
                    e.BlockId == BlockIds.Rotor && e.Position.z == tipZ),
                "The 600-RPM rotor (priciest block at its BFS distance) must be stripped first.");
            // The cheap cube at distance (tipZ-1) — same BFS level — must survive.
            Assert.IsTrue(System.Array.Exists(trimmed, e =>
                    e.BlockId == BlockIds.Cube && e.Position == new Vector3Int(1, 0, tipZ - 1)),
                "The cheaper cube at equal BFS distance must survive — pricier blocks go first.");
        }

        [Test]
        public void TrimToFit_RotorAtDefaultRpm_BudgetUsageMatchesStickerCost()
        {
            // Validate that the blueprint-entry pricing used by TrimToFit
            // agrees with the sticker for a default-RPM rotor, so the
            // player's spend bar (which calls the same EffectiveCpuCost) and
            // the server's trim decision always report identical values.
            int sticker = RotorStickerCost;
            Assume.That(sticker, Is.GreaterThan(0));
            Assume.That(sticker, Is.LessThan(CpuBudget.BudgetPerCpuBlock));

            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)),
                MakeRotorEntry(new Vector3Int(1, 0, 0), 0f), // default RPM
            };

            int usedCpu = CpuBudget.UsedCpu(entries, _lib);

            // CPU block costs 0 (verified by its authored _cpuCost); rotor
            // at DefaultRpm costs exactly sticker (scale = 1.0).
            Assert.AreEqual(sticker, usedCpu,
                "UsedCpu for [CPU + one default-RPM rotor] must equal the rotor's sticker cost. " +
                "Disagreement means garage spend bar and server TrimToFit would charge different amounts.");
        }
    }
}
