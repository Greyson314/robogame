using System.Diagnostics;
using NUnit.Framework;
using Robogame.Voxel;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Robogame.Tests.PlayMode.Voxel
{
    /// <summary>
    /// Phase 1c machine gate per TERRAFORMING_PLAN.md §12. Two assertions
    /// the Burst-compiled mesher must satisfy in steady state:
    /// <list type="bullet">
    ///   <item><description>Median remesh time at dim=33 (production chunk size) under 1 ms.</description></item>
    ///   <item><description>Zero managed GC allocations across 50 consecutive Mesh() calls.</description></item>
    /// </list>
    /// Failure on either means the Burst port isn't paying for itself and
    /// the plan's perf budget (PERFORMANCE.md §7 cascade) is at risk.
    /// </summary>
    public sealed class SurfaceNetsBenchmarkTests
    {
        private const int Dim = 33;
        private const float SphereRadius = 12f;
        private const int Iterations = 50;

        [Test]
        public void Mesh_Dim33Sphere_BurstMedianUnderOneMillisecond()
        {
            using var sdf = MakeSphereSdf(Dim, SphereRadius);
            using var buffers = SurfaceNetsMesher.Allocate(Dim, Allocator.Persistent);

            // Warm-up: first call triggers Burst JIT compile in-editor and
            // primes any one-time caches in the job scheduler.
            for (int i = 0; i < 3; i++)
                SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);

            long[] ticks = new long[Iterations];
            var sw = new Stopwatch();
            for (int i = 0; i < Iterations; i++)
            {
                sw.Restart();
                SurfaceNetsMesher.Mesh(sdf, Dim, buffers, out _, out _);
                sw.Stop();
                ticks[i] = sw.ElapsedTicks;
            }

            System.Array.Sort(ticks);
            double medianMs = ticks[Iterations / 2] * 1000.0 / Stopwatch.Frequency;
            double minMs    = ticks[0]              * 1000.0 / Stopwatch.Frequency;
            double maxMs    = ticks[Iterations - 1] * 1000.0 / Stopwatch.Frequency;

            Debug.Log($"[SurfaceNets dim={Dim}] {Iterations} iters: " +
                      $"median={medianMs:F3} ms, min={minMs:F3} ms, max={maxMs:F3} ms.");

            Assert.That(medianMs, Is.LessThan(1.0),
                $"Phase 1c machine gate: median remesh must be < 1 ms (got {medianMs:F3} ms).");
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
