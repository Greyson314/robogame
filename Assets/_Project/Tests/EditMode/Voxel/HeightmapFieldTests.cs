using NUnit.Framework;
using Robogame.Tools.Editor;
using Robogame.Voxel;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Pins the shared runtime heightmap sampler that both the visual
    /// grass mesh (HillsGround) and the diggable voxel surface (DigZone)
    /// read through. The single-source-of-truth property is what keeps
    /// the two layers aligned (docs/changes/83).
    /// </summary>
    public sealed class HeightmapFieldTests
    {
        private static HeightmapParams Hills(float ampLow = 6f) => new HeightmapParams
        {
            Enabled = true,
            NoiseOffset = new Vector2(137.31f, 91.47f),
            HillFreqLow = 0.025f,
            HillAmpLow = ampLow,
            HillFreqHigh = 0.08f,
            HillAmpHigh = 1f,
            FlatRadius = 25f,
            RampOuter = 55f,
            EdgeFlatStart = 80f,
            EdgeFlatEnd = 100f,
        };

        [Test]
        public void Disabled_ReturnsZeroEverywhere()
        {
            var p = HeightmapParams.Disabled; // Enabled == false
            Assert.AreEqual(0f, HeightmapField.Sample(p, 0f, 0f));
            Assert.AreEqual(0f, HeightmapField.Sample(p, 12.5f, -33.7f));
            Assert.AreEqual(0f, HeightmapField.Sample(p, 200f, 200f));
        }

        [Test]
        public void InsideFlatRadius_IsExactlyFlat()
        {
            var p = Hills();
            // Inside FlatRadius the inner smoothstep is 0, so the spawn /
            // obstacle-course area must be dead flat regardless of noise.
            Assert.AreEqual(0f, HeightmapField.Sample(p, 0f, 0f), 1e-6f);
            Assert.AreEqual(0f, HeightmapField.Sample(p, 10f, 10f), 1e-6f);
            Assert.AreEqual(0f, HeightmapField.Sample(p, 0f, 24f), 1e-6f);
        }

        [Test]
        public void BeyondEdgeFlatEnd_RampsBackToFlat()
        {
            var p = Hills();
            // Past EdgeFlatEnd the outer smoothstep is 0 → flat again so
            // the wall / mountain ring sits on level ground.
            Assert.AreEqual(0f, HeightmapField.Sample(p, 120f, 0f), 1e-6f);
            Assert.AreEqual(0f, HeightmapField.Sample(p, 0f, -150f), 1e-6f);
        }

        [Test]
        public void MidBand_IsNonFlat_AndDeterministic()
        {
            var p = Hills();
            // Somewhere in the hill band (FlatRadius..EdgeFlatStart) the
            // surface must actually undulate, and be reproducible.
            float a = HeightmapField.Sample(p, 60f, 5f);
            float b = HeightmapField.Sample(p, 60f, 5f);
            Assert.AreEqual(a, b, 0f, "Same input must give identical output (determinism).");

            bool anyNonZero = false;
            for (float x = 30f; x <= 78f && !anyNonZero; x += 3f)
            for (float z = -78f; z <= 78f; z += 3f)
            {
                if (Mathf.Abs(HeightmapField.Sample(p, x, z)) > 0.05f) { anyNonZero = true; break; }
            }
            Assert.IsTrue(anyNonZero, "Hill band must have measurable relief.");
        }

        [Test]
        public void HigherAmplitude_ProducesTallerRelief()
        {
            // Monotone response to HillAmpLow at a fixed sample where the
            // low-frequency octave is non-zero.
            const float x = 60f, z = 0f;
            float small = Mathf.Abs(HeightmapField.Sample(Hills(2f), x, z));
            float big = Mathf.Abs(HeightmapField.Sample(Hills(12f), x, z));
            Assert.Greater(big, small, "More amplitude must mean taller hills at the same point.");
        }

        [Test]
        public void HillsGroundProjection_RoundTrips()
        {
            // The editor-side projection used by EnvironmentBuilder must
            // carry every authoring knob into the runtime struct so the
            // voxel surface and grass mesh sample identical math.
            var s = ScriptableObject.CreateInstance<HillsSettings>();
            try
            {
                HeightmapParams hp = HillsGround.ToHeightmapParams(s);
                Assert.IsTrue(hp.Enabled);
                Assert.AreEqual(s.hillFreqLow, hp.HillFreqLow);
                Assert.AreEqual(s.hillAmpLow, hp.HillAmpLow);
                Assert.AreEqual(s.flatRadius, hp.FlatRadius);
                Assert.AreEqual(s.edgeFlatEnd, hp.EdgeFlatEnd);
                Assert.AreEqual(s.noiseOffset, hp.NoiseOffset);
                // Structural knobs (session 119) must round-trip too, or the
                // voxel surface and grass mesh would disagree on the ridges /
                // valley / bowls.
                Assert.AreEqual(s.ridgeAmp, hp.RidgeAmp);
                Assert.AreEqual(s.valleyDepth, hp.ValleyDepth);
                Assert.AreEqual(s.bowlAmp, hp.BowlAmp);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // -----------------------------------------------------------------
        // Structure — the "Sunken Crossing" layout (session 119). Arena-scale
        // params so `field` is ≈1 where the ridges / valley / bowls live.
        // -----------------------------------------------------------------

        private static HeightmapParams Arena() => new HeightmapParams
        {
            Enabled = true,
            NoiseOffset = new Vector2(137.31f, 91.47f),
            HillFreqLow = 0.018f,
            HillAmpLow = 5.5f,
            HillFreqHigh = 0.07f,
            HillAmpHigh = 1.2f,
            FlatRadius = 38f,
            RampOuter = 90f,
            EdgeFlatStart = 145f,
            EdgeFlatEnd = 170f,
            RidgeAmp = 9.5f,
            ValleyDepth = 2.5f,
            BowlAmp = 6.5f,
        };

        [Test]
        public void StructuralTerms_DefaultZero_MatchLegacyRolling()
        {
            // A params with all structural amps 0 must equal the legacy
            // detail-only height at the same point (the fast-out path), so
            // every pre-119 consumer is byte-for-byte unaffected.
            var legacy = Hills();                  // RidgeAmp/ValleyDepth/BowlAmp == 0
            var structured = legacy; structured.RidgeAmp = 0f; // explicit, same struct
            for (float x = -90f; x <= 90f; x += 30f)
            for (float z = -90f; z <= 90f; z += 30f)
                Assert.AreEqual(HeightmapField.Sample(legacy, x, z),
                                HeightmapField.Sample(structured, x, z), 0f);
        }

        [Test]
        public void SpawnCentre_StaysFlat_EvenWithStructure()
        {
            // The flat spawn pad must survive the new terms: inside FlatRadius
            // the master `field` is 0, which gates ridges, valley AND bowls.
            var p = Arena();
            Assert.AreEqual(0f, HeightmapField.Sample(p, 0f, 0f), 1e-4f);
            Assert.AreEqual(0f, HeightmapField.Sample(p, 10f, 8f), 1e-4f);
        }

        [Test]
        public void Ridge_RaisesDiagonalCrown_AboveCentreline()
        {
            // The diagonal ridges (lines z = ±x) must lift the crown well
            // above the on-axis approach at the same distance — that's the
            // whole "X frames the open centre" idea, and it's what keeps the
            // depot-to-depot sightline (x = 0) clear.
            var p = Arena();
            float crown = HeightmapField.Sample(p, 80f, 80f);   // on z = x
            float onAxis = HeightmapField.Sample(p, 0f, 113f);  // same radius, x = 0
            Assert.Greater(crown, onAxis + 5f,
                "Ridge crown must stand clearly above the on-axis centreline at equal radius.");
        }

        [Test]
        public void Valley_LowersCentralLine()
        {
            // Enabling the valley must drop the z≈0 midline relative to the
            // identical params without it (the no-man's-land depression).
            var withValley = Arena();
            var noValley = withValley; noValley.ValleyDepth = 0f;
            float a = HeightmapField.Sample(withValley, 100f, 0f);
            float b = HeightmapField.Sample(noValley, 100f, 0f);
            Assert.Less(a, b - 2f, "Valley term must measurably lower the central east-west line.");
        }

        [Test]
        public void Bowls_AreMirrorSymmetric_AndFloorNearZero()
        {
            // North and south base bowls must be identical (fair sides), and
            // the bowl floor must sit near y=0 so the depot pads (spawned at
            // y≈0.2) land flush rather than buried or floating.
            var p = Arena();
            float north = HeightmapField.Sample(p, 0f, 92f);
            float south = HeightmapField.Sample(p, 0f, -92f);
            Assert.AreEqual(north, south, 1e-3f, "Base bowls must mirror north↔south.");
            Assert.Less(Mathf.Abs(north), 1.5f, "Bowl floor must sit near y=0 for a flush depot pad.");
        }

        [Test]
        public void AllStructure_StaysWithinVoxelCeiling()
        {
            // Nothing the heightmap produces may poke out the top of the
            // single-chunk-tall voxel volume (TERRAFORMING_PLAN §7). The
            // clamp guarantees ≤ 13.5 m of diggable relief; taller drama is
            // the non-diggable backdrop range. Scan the whole playfield.
            var p = Arena();
            for (float x = -170f; x <= 170f; x += 5f)
            for (float z = -170f; z <= 170f; z += 5f)
            {
                float h = HeightmapField.Sample(p, x, z);
                Assert.LessOrEqual(h, 13.5f + 1e-3f, $"Surface above voxel ceiling at ({x},{z}).");
                Assert.GreaterOrEqual(h, -10f - 1e-3f, $"Surface below voxel floor at ({x},{z}).");
            }
        }
    }
}
