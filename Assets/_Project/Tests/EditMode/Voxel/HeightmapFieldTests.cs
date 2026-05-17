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
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
