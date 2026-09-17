using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using Robogame.Tests.PlayMode.Perf;
using Robogame.Voxel;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Robogame.Tests.PlayMode.Voxel
{
    /// <summary>
    /// Phase 1c machine gate per docs/subsystems/terraforming.md §12. Two assertions
    /// the Burst-compiled mesher must satisfy in steady state:
    /// <list type="bullet">
    ///   <item><description>Median remesh time at dim=34 (the production chunk meshing
    ///   extent: 32 cells + 2 apron) under 1 ms, judged on the best of three
    ///   50-iteration windows.</description></item>
    ///   <item><description>Zero managed GC allocations across 50 consecutive Mesh() calls.</description></item>
    /// </list>
    /// Failure on either means the Burst port isn't paying for itself and
    /// the plan's perf budget (docs/subsystems/performance.md §7 cascade) is at risk.
    /// Every run of the median test appends a <c>[SURFACENETS-BENCH]</c> row to
    /// docs/perf-captures/harness-log.txt so drift is a number in the log.
    /// </summary>
    public sealed class SurfaceNetsBenchmarkTests
    {
        // dim=34 matches the production chunk meshing extent after Phase 2b
        // (chunkSizeCells + 2 = 32 + 2 = 34, where the +2 covers the own-region
        // 33 samples plus the 1-cell apron rim used for seam-free meshing).
        private const int Dim = 34;
        private const float SphereRadius = 12f;
        private const int Iterations = 50;

        // Ten untimed calls, not three, and three timed windows judged on the
        // best median: on the test rig this gate read 1.005 ms once, straight
        // after a full recompile, and 0.36–0.61 ms on every other run
        // (docs/loop/FINDINGS.md F-021; docs/loop/SPIKES.md L3, L3b). A real
        // regression is slower in all three windows; a transient (a late
        // compile, a CPU power-state dip, a neighbouring test's tail) is not.
        private const int WarmupIterations = 10;
        private const int Windows = 3;

        [Test]
        public void Mesh_Dim34Sphere_BurstMedianUnderOneMillisecond()
        {
            using var sdf = MakeSphereSdf(Dim, SphereRadius);
            using var buffers = SurfaceNetsMesher.Allocate(Dim, Allocator.Persistent);

            // Warm-up: the first call triggers the Burst JIT compile in-editor
            // and primes any one-time caches in the job scheduler. Burst compiles
            // asynchronously by default, so with a cold Library/BurstCache the
            // whole timed window would run on the managed fallback (measured
            // 1.8–2.0 ms medians, 3 of 3 cold runs failing the gate; FINDINGS
            // F-021, the CHG-012 record). Compile synchronously for the warm-up
            // so the first call pays for the compile and the timed windows
            // measure the Burst code, warm cache or cold.
            bool syncBefore = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            try
            {
                for (int i = 0; i < WarmupIterations; i++)
                    SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);
            }
            finally
            {
                BurstCompiler.Options.EnableBurstCompileSynchronously = syncBefore;
            }

            double[] medians = new double[Windows];
            double minMs = double.MaxValue;
            double maxMs = 0.0;
            long[] ticks = new long[Iterations];
            var sw = new Stopwatch();
            for (int w = 0; w < Windows; w++)
            {
                for (int i = 0; i < Iterations; i++)
                {
                    sw.Restart();
                    SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);
                    sw.Stop();
                    ticks[i] = sw.ElapsedTicks;
                }

                System.Array.Sort(ticks);
                medians[w] = ToMs(ticks[Iterations / 2]);
                minMs = System.Math.Min(minMs, ToMs(ticks[0]));
                maxMs = System.Math.Max(maxMs, ToMs(ticks[Iterations - 1]));
            }

            double bestMs = medians[0];
            for (int w = 1; w < Windows; w++)
                bestMs = System.Math.Min(bestMs, medians[w]);

            string medianList = string.Join("/", System.Array.ConvertAll(medians,
                m => m.ToString("F3", CultureInfo.InvariantCulture)));
            string row = string.Format(CultureInfo.InvariantCulture,
                "[SURFACENETS-BENCH] dim={0} warmup={1} windows={2}x{3} medians={4}ms best={5:F3}ms min={6:F3}ms max={7:F3}ms",
                Dim, WarmupIterations, Windows, Iterations, medianList, bestMs, minMs, maxMs);
            Debug.Log(row);
            PerfBaselineHarness.AppendToLog(row);

            Assert.That(bestMs, Is.LessThan(1.0),
                $"Phase 1c machine gate: the best of {Windows} median remesh times must be < 1 ms " +
                $"(medians {medianList} ms).");
        }

        [Test]
        public void Mesh_RepeatedCalls_ZeroGCAllocations()
        {
            using var sdf = MakeSphereSdf(Dim, SphereRadius);
            using var buffers = SurfaceNetsMesher.Allocate(Dim, Allocator.Persistent);

            // Warm-up outside the measurement window so any first-call
            // allocations (Burst caches, scheduler one-time init) don't
            // contaminate the steady-state delta.
            for (int i = 0; i < 5; i++)
                SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; i++)
                SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);
            long after = System.GC.GetAllocatedBytesForCurrentThread();

            long deltaBytes = after - before;
            Assert.AreEqual(0L, deltaBytes,
                $"Phase 1c machine gate: steady-state mesher must not allocate managed memory " +
                $"(got {deltaBytes} bytes across {Iterations} iters; ~{(double)deltaBytes / Iterations:F1} B/call). " +
                "Common culprits: Schedule() instead of Run(), string interpolation in hot path, " +
                "managed wrapper around a struct that grew over the inline-budget threshold.");
        }

        private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static NativeArray<sbyte> MakeSphereSdf(int dim, float radius)
        {
            var sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent);
            Vector3 centre = new Vector3(dim * 0.5f, dim * 0.5f, dim * 0.5f);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                float d = Vector3.Distance(new Vector3(x, y, z), centre) - radius;
                int scaled = Mathf.RoundToInt(d * 64f);
                if (scaled < -128) scaled = -128;
                else if (scaled > 127) scaled = 127;
                sdf[z * dim * dim + y * dim + x] = (sbyte)scaled;
            }
            return sdf;
        }
    }
}
