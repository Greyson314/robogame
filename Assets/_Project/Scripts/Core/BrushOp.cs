using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// The closed set of brush operations the voxel system understands.
    /// Wire-stable: order is part of the netcode contract once Phase 6 lands.
    /// </summary>
    /// <remarks>
    /// See [docs/TERRAFORMING_PLAN.md](../../../../../docs/TERRAFORMING_PLAN.md)
    /// §4 "Brush operations" for why these two are sufficient (bombs use
    /// sphere, drills use the swept capsule between FixedUpdate ticks).
    /// </remarks>
    public enum BrushKind : byte
    {
        /// <summary>Unset / invalid. Zero so a default-constructed BrushOp is recognisably bogus.</summary>
        None            = 0,

        /// <summary>Bomb / grenade / explosive shell. p0 = centre, p1 ignored, radiusFixed = blast radius.</summary>
        SphereSubtract  = 1,

        /// <summary>Drill swept from last-tick position to this-tick position. p0 = previous tip, p1 = current tip, radiusFixed = drill-bit radius.</summary>
        CapsuleSubtract = 2,
    }

    /// <summary>
    /// World-space position in 1/256 m fixed-point. Three int16s = 6 bytes.
    /// Range ±128 m per axis, precision ~3.9 mm.
    /// </summary>
    /// <remarks>
    /// Brush math is integer once positions are in this form, which kills
    /// the float-ULP drift that would otherwise let two clients diverge by
    /// a cell over many ops. TERRAFORMING_PLAN.md §2 "Determinism note"
    /// and §4 describe the rationale. Arenas are authored at world origin
    /// (SPHERICAL_ARENAS.md §15 risk S5), so the ±128 m envelope covers
    /// every brush op a v1 arena can emit.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vector3Fixed : IEquatable<Vector3Fixed>
    {
        public const int UnitsPerMeter = 256;
        public const float MetersPerUnit = 1f / UnitsPerMeter;

        public short x;
        public short y;
        public short z;

        public Vector3Fixed(short x, short y, short z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3Fixed FromVector3(Vector3 worldMeters)
        {
            return new Vector3Fixed(
                ToFixed(worldMeters.x),
                ToFixed(worldMeters.y),
                ToFixed(worldMeters.z));
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x * MetersPerUnit, y * MetersPerUnit, z * MetersPerUnit);
        }

        private static short ToFixed(float meters)
        {
            float scaled = Mathf.Round(meters * UnitsPerMeter);
            if (scaled <= short.MinValue) return short.MinValue;
            if (scaled >= short.MaxValue) return short.MaxValue;
            return (short)scaled;
        }

        public bool Equals(Vector3Fixed other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Vector3Fixed o && Equals(o);
        public override int GetHashCode() => ((x * 397) ^ y) * 397 ^ z;
        public static bool operator ==(Vector3Fixed a, Vector3Fixed b) => a.Equals(b);
        public static bool operator !=(Vector3Fixed a, Vector3Fixed b) => !a.Equals(b);

        public override string ToString()
            => $"({x * MetersPerUnit:F3}, {y * MetersPerUnit:F3}, {z * MetersPerUnit:F3})m";
    }

    /// <summary>
    /// One brush operation. 17 bytes on disk / in memory; the wire encoder
    /// will pad to 18 when netcode lands (Phase 6).
    /// </summary>
    /// <remarks>
    /// Min-fold semantics: applying the same op twice is idempotent;
    /// applying ops in any order converges to the same SDF. See
    /// TERRAFORMING_PLAN.md §2 for why this invariant is the load-bearing
    /// simplification of the whole system. Phase 0 ships the type only;
    /// the SDF apply pipeline lands in Phase 1.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BrushOp : IEquatable<BrushOp>
    {
        public BrushKind kind;          // 1 byte
        public ushort serverTick;       // 2 bytes — for ordering / replay determinism
        public Vector3Fixed p0;         // 6 bytes — sphere centre, or capsule start
        public Vector3Fixed p1;         // 6 bytes — capsule end; equals p0 for SphereSubtract
        public ushort radiusFixed;      // 2 bytes — 1/256 m precision, max ~256 m
                                        // Total: 17 bytes.

        public float RadiusMeters => radiusFixed * Vector3Fixed.MetersPerUnit;

        public bool Equals(BrushOp other)
            => kind == other.kind
            && serverTick == other.serverTick
            && p0 == other.p0
            && p1 == other.p1
            && radiusFixed == other.radiusFixed;

        public override bool Equals(object obj) => obj is BrushOp o && Equals(o);

        public override int GetHashCode()
        {
            int h = (int)kind;
            h = (h * 397) ^ serverTick.GetHashCode();
            h = (h * 397) ^ p0.GetHashCode();
            h = (h * 397) ^ p1.GetHashCode();
            h = (h * 397) ^ radiusFixed.GetHashCode();
            return h;
        }

        public static bool operator ==(BrushOp a, BrushOp b) => a.Equals(b);
        public static bool operator !=(BrushOp a, BrushOp b) => !a.Equals(b);
    }

    /// <summary>
    /// A tick's worth of brush ops for one dig zone. Sent as the unit of
    /// replication once Phase 6 lands; mirrors the BlockHitBatch shape from
    /// NETCODE_PLAN.md §7.
    /// </summary>
    /// <remarks>
    /// Phase 0 ships the type only. The batching producer (server side)
    /// and the apply consumer (client side) land in Phases 2 and 6
    /// respectively. <see cref="MaxOpsPerBatch"/> is the hard cap per
    /// TERRAFORMING_PLAN.md §4.
    /// </remarks>
    public struct BrushOpBatch
    {
        public const int MaxOpsPerBatch = 32;

        public ushort digZoneId;
        public ushort serverTick;
        public BrushOp[] ops;
    }
}
