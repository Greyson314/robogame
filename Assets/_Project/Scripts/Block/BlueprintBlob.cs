using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Compact binary wire form for <see cref="ChassisBlueprint"/>, used by
    /// the netcode <c>SpawnRobotPayload</c> (NETCODE_PLAN §6 Bucket B / §7a)
    /// and the connect-time content guard. It lives <em>alongside</em>
    /// <see cref="BlueprintSerializer"/>'s JSON — JSON stays the human /
    /// debug / on-disk form; this is the bytes-on-the-wire form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <c>BrushOpCodec</c>'s discipline (session 81): a versioned
    /// header, fixed-width little-endian fields, direct <c>byte[]</c> +
    /// offset arithmetic (no <c>BinaryReader</c>/<c>BinaryWriter</c> — both
    /// allocate). The one structural difference is the string table:
    /// <see cref="ChassisBlueprint.Entry.BlockId"/> is variable-length, so
    /// the blob interns every distinct id once into a table and each entry
    /// stores a 2-byte table index (architect-approved — handoff §5.1).
    /// Entries are kept fixed-width so a decoder is a straight stride walk.
    /// </para>
    /// <para>
    /// The blob carries exactly the gameplay-observable fields JSON
    /// round-trips: per-entry <see cref="ChassisBlueprint.Entry.EffectiveUp"/>
    /// (the same zero→+Y normalisation <see cref="BlueprintSerializer"/>
    /// applies on serialize), position, dims, pitch, blockConfig,
    /// concoctionId (v3 — ADR-0004 gameplay-observable); plus the
    /// four chassis tuning configs, kind, and rotorsGenerateLift.
    /// <c>displayName</c> and the JSON-only <c>createdUtc</c> are
    /// <em>deliberately excluded</em> (architect decision — handoff §5.3):
    /// displayName is cosmetic (Bucket E), createdUtc is
    /// <c>DateTime.UtcNow</c> stamped per-serialize so including it would
    /// make two byte-identical builds hash differently. Excluding both makes
    /// <see cref="ContentHash"/> stable across re-serializes.
    /// </para>
    /// <para>
    /// Block ordering: <see cref="ChassisBlueprint.SetEntries"/> already
    /// canonicalises (sort by <see cref="BlockEntries"/>), and
    /// <see cref="TryDecode"/> runs the decoded entries back through
    /// <c>SetEntries</c>, so the block index a peer derives is identical on
    /// every machine (invariant #2, NETCODE_PLAN §6).
    /// </para>
    /// </remarks>
    public static class BlueprintBlob
    {
        /// <summary>Wire format version. Bump whenever the byte layout or a
        /// tuning-config field set changes — <see cref="TryDecode"/> rejects
        /// a newer blob rather than silently misreading it.</summary>
        public const byte CurrentBlobVersion = 3;

        // Header: version(1) + kind(1) + flags(1) + entryCount(u16) + tableCount(u16).
        private const int HeaderSize = 7;

        // 13 chassis-tuning floats in a fixed order (see WriteConfigs).
        private const int ConfigFloatCount = 13;
        private const int ConfigBytes = ConfigFloatCount * 4;

        // Per entry: tableIdx(u16) + pos(short*3) + up(short*3) + dims(float*3) + pitch(float) + blockConfig(float) + yaw(short, v2)
        //            + concoctionIdx(u16, v3 — string-table index, NoConcoctionIndex when unset).
        private const int EntrySizeV2 = 2 + 6 + 6 + 12 + 4 + 4 + 2; // = 32
        private const int EntrySize = EntrySizeV2 + 2;              // = 34 (v3)

        // v3: sentinel concoction index for "no concoction" — keeps the
        // string table free of an interned empty string. ADR-0004: the
        // dialed concoction is gameplay-observable (damage / blast size /
        // CPU surcharge) so it MUST ride the wire, or client-built chassis
        // arrive stripped while the server keeps them.
        private const ushort NoConcoctionIndex = ushort.MaxValue;

        private const byte FlagRotorsGenerateLift = 0x01;
        // Bits 1–2 of the flags byte carry the ControlScheme override (ADR-0009,
        // session 167). Previously always zero → older blobs decode as Auto.
        private const int  SchemeShift = 1;
        private const byte SchemeMask  = 0x03;
        private const int MaxIdByteLength = 255;   // string-table length prefix is one byte

        // -----------------------------------------------------------------
        // Encode
        // -----------------------------------------------------------------

        /// <summary>
        /// Encode <paramref name="blueprint"/> into a freshly allocated
        /// byte array. Allocating is acceptable: encode happens once per
        /// robot at spawn, never per frame (mirrors
        /// <c>BrushOpCodec.DecodeBatch</c>'s spawn-time array alloc).
        /// </summary>
        public static byte[] Encode(ChassisBlueprint blueprint)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

            ChassisBlueprint.Entry[] entries = blueprint.Entries ?? Array.Empty<ChassisBlueprint.Entry>();
            if (entries.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(blueprint),
                    $"Blueprint has {entries.Length} entries; max {ushort.MaxValue}.");

            // Build the interned string table in first-seen order over the
            // already-canonical entry list — deterministic across machines.
            var table = new List<string>();
            var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var idBytes = new List<byte[]>();
            int tableByteTotal = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                Intern(entries[i].BlockId ?? string.Empty, "BlockId");
                // v3: non-empty concoction ids share the same table —
                // they're strings under the same length cap.
                string cid = entries[i].EffectiveConcoctionId;
                if (cid.Length > 0) Intern(cid, "ConcoctionId");
            }

            void Intern(string id, string fieldLabel)
            {
                if (idToIndex.ContainsKey(id)) return;
                byte[] b = Encoding.UTF8.GetBytes(id);
                if (b.Length > MaxIdByteLength)
                    throw new ArgumentOutOfRangeException(nameof(blueprint),
                        $"{fieldLabel} '{id}' is {b.Length} UTF-8 bytes; max {MaxIdByteLength}.");
                idToIndex[id] = table.Count;
                table.Add(id);
                idBytes.Add(b);
                tableByteTotal += 1 + b.Length;
            }
            if (table.Count > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(blueprint),
                    $"Blueprint references {table.Count} distinct block ids; max {ushort.MaxValue}.");

            int total = HeaderSize + tableByteTotal + ConfigBytes + entries.Length * EntrySize;
            byte[] buffer = new byte[total];
            int o = 0;

            buffer[o++] = CurrentBlobVersion;
            buffer[o++] = (byte)blueprint.Kind;
            buffer[o++] = (byte)((blueprint.RotorsGenerateLift ? FlagRotorsGenerateLift : (byte)0)
                                 | (((byte)blueprint.ControlScheme & SchemeMask) << SchemeShift));
            WriteUShort(buffer, ref o, (ushort)entries.Length);
            WriteUShort(buffer, ref o, (ushort)table.Count);

            for (int i = 0; i < idBytes.Count; i++)
            {
                byte[] b = idBytes[i];
                buffer[o++] = (byte)b.Length;
                Buffer.BlockCopy(b, 0, buffer, o, b.Length);
                o += b.Length;
            }

            WriteConfigs(buffer, ref o, blueprint);

            for (int i = 0; i < entries.Length; i++)
            {
                ChassisBlueprint.Entry e = entries[i];
                Vector3Int up = e.EffectiveUp; // same normalisation BlueprintSerializer applies
                WriteUShort(buffer, ref o, (ushort)idToIndex[e.BlockId ?? string.Empty]);
                WriteShort(buffer, ref o, (short)e.Position.x);
                WriteShort(buffer, ref o, (short)e.Position.y);
                WriteShort(buffer, ref o, (short)e.Position.z);
                WriteShort(buffer, ref o, (short)up.x);
                WriteShort(buffer, ref o, (short)up.y);
                WriteShort(buffer, ref o, (short)up.z);
                WriteFloat(buffer, ref o, e.Dims.x);
                WriteFloat(buffer, ref o, e.Dims.y);
                WriteFloat(buffer, ref o, e.Dims.z);
                WriteFloat(buffer, ref o, e.Pitch);
                WriteFloat(buffer, ref o, e.BlockConfig);
                WriteShort(buffer, ref o, (short)e.EffectiveYaw); // v2
                string cid = e.EffectiveConcoctionId;              // v3
                WriteUShort(buffer, ref o, cid.Length > 0 ? (ushort)idToIndex[cid] : NoConcoctionIndex);
            }

            return buffer;
        }

        private static void WriteConfigs(byte[] b, ref int o, ChassisBlueprint bp)
        {
            PlaneTuningConfig p = bp.PlaneTuning;
            WriteFloat(b, ref o, p.PitchPower);
            WriteFloat(b, ref o, p.RollPower);
            WriteFloat(b, ref o, p.YawFromBank);
            WriteFloat(b, ref o, p.PitchDamping);
            WriteFloat(b, ref o, p.RollDamping);
            WriteFloat(b, ref o, p.YawDamping);

            GroundTuningConfig g = bp.GroundTuning;
            WriteFloat(b, ref o, g.Acceleration);
            WriteFloat(b, ref o, g.MaxSpeed);
            WriteFloat(b, ref o, g.TurnRate);

            ChassisDampingConfig d = bp.ChassisDamping;
            WriteFloat(b, ref o, d.LinearDamping);
            WriteFloat(b, ref o, d.AngularDamping);

            ThrusterTuningConfig t = bp.ThrusterTuning;
            WriteFloat(b, ref o, t.IdleThrottle);
            WriteFloat(b, ref o, t.ThrottleResponse);
        }

        // -----------------------------------------------------------------
        // Decode
        // -----------------------------------------------------------------

        /// <summary>
        /// Decode a blob into a fresh runtime <see cref="ChassisBlueprint"/>.
        /// Returns false with a human-readable <paramref name="error"/> on
        /// malformed / truncated / newer-version input rather than throwing
        /// (the caller is the netcode receive path — a bad blob from a peer
        /// must not crash the receiver). <c>DisplayName</c> is intentionally
        /// not on the wire and is left at the SO default.
        /// </summary>
        public static bool TryDecode(byte[] buffer, out ChassisBlueprint blueprint, out string error)
        {
            blueprint = null;
            error = null;

            if (buffer == null) { error = "Blob is null."; return false; }
            if (buffer.Length < HeaderSize) { error = $"Blob too short ({buffer.Length} < {HeaderSize})."; return false; }

            int o = 0;
            byte version = buffer[o++];
            if (version == 0) { error = "Invalid blob version 0."; return false; }
            if (version > CurrentBlobVersion)
            {
                error = $"Blob version {version} is newer than this build (v{CurrentBlobVersion}). Update the game?";
                return false;
            }

            byte kindByte = buffer[o++];
            if (kindByte > (byte)ChassisKind.Plane) { error = $"Unknown chassis kind byte {kindByte}."; return false; }
            byte flags = buffer[o++];
            ushort entryCount = ReadUShort(buffer, ref o);
            ushort tableCount = ReadUShort(buffer, ref o);

            // String table.
            var table = new string[tableCount];
            for (int i = 0; i < tableCount; i++)
            {
                if (o + 1 > buffer.Length) { error = $"Truncated string table at entry {i}."; return false; }
                int len = buffer[o++];
                if (o + len > buffer.Length) { error = $"Truncated string-table id {i} (len {len})."; return false; }
                table[i] = Encoding.UTF8.GetString(buffer, o, len);
                o += len;
            }

            if (o + ConfigBytes > buffer.Length) { error = "Truncated chassis config block."; return false; }
            ReadConfigs(buffer, ref o,
                out PlaneTuningConfig plane, out GroundTuningConfig ground,
                out ChassisDampingConfig damping, out ThrusterTuningConfig thruster);

            int entrySize = version >= 3 ? EntrySize : EntrySizeV2;
            long entriesBytes = (long)entryCount * entrySize;
            if (o + entriesBytes > buffer.Length)
            {
                error = $"Truncated entries: need {entriesBytes} bytes for {entryCount} entries, {buffer.Length - o} left.";
                return false;
            }

            var decoded = new ChassisBlueprint.Entry[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                ushort idIdx = ReadUShort(buffer, ref o);
                if (idIdx >= tableCount) { error = $"Entry {i} references string-table index {idIdx} (table size {tableCount})."; return false; }
                int px = ReadShort(buffer, ref o);
                int py = ReadShort(buffer, ref o);
                int pz = ReadShort(buffer, ref o);
                int ux = ReadShort(buffer, ref o);
                int uy = ReadShort(buffer, ref o);
                int uz = ReadShort(buffer, ref o);
                float dx = ReadFloat(buffer, ref o);
                float dy = ReadFloat(buffer, ref o);
                float dz = ReadFloat(buffer, ref o);
                float pitch = ReadFloat(buffer, ref o);
                float blockConfig = ReadFloat(buffer, ref o);
                int yaw = ReadShort(buffer, ref o); // v2
                string concoctionId = string.Empty; // v3; pre-v3 blobs carry none
                if (version >= 3)
                {
                    ushort cidIdx = ReadUShort(buffer, ref o);
                    if (cidIdx != NoConcoctionIndex)
                    {
                        if (cidIdx >= tableCount) { error = $"Entry {i} references concoction string-table index {cidIdx} (table size {tableCount})."; return false; }
                        concoctionId = table[cidIdx];
                    }
                }
                decoded[i] = new ChassisBlueprint.Entry(
                    table[idIdx],
                    new Vector3Int(px, py, pz),
                    new Vector3Int(ux, uy, uz),
                    new Vector3(dx, dy, dz),
                    pitch,
                    blockConfig,
                    concoctionId);
                decoded[i].Yaw = yaw;
            }

            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.Kind = (ChassisKind)kindByte;
            bp.RotorsGenerateLift = (flags & FlagRotorsGenerateLift) != 0;
            bp.ControlScheme = (ControlScheme)((flags >> SchemeShift) & SchemeMask);
            bp.PlaneTuning = plane;
            bp.GroundTuning = ground;
            bp.ChassisDamping = damping;
            bp.ThrusterTuning = thruster;
            bp.SetEntries(decoded); // re-canonicalise → identical block index on every peer

            blueprint = bp;
            return true;
        }

        private static void ReadConfigs(byte[] b, ref int o,
            out PlaneTuningConfig plane, out GroundTuningConfig ground,
            out ChassisDampingConfig damping, out ThrusterTuningConfig thruster)
        {
            plane = new PlaneTuningConfig
            {
                PitchPower   = ReadFloat(b, ref o),
                RollPower    = ReadFloat(b, ref o),
                YawFromBank  = ReadFloat(b, ref o),
                PitchDamping = ReadFloat(b, ref o),
                RollDamping  = ReadFloat(b, ref o),
                YawDamping   = ReadFloat(b, ref o),
            };
            ground = new GroundTuningConfig
            {
                Acceleration = ReadFloat(b, ref o),
                MaxSpeed     = ReadFloat(b, ref o),
                TurnRate     = ReadFloat(b, ref o),
            };
            damping = new ChassisDampingConfig
            {
                LinearDamping  = ReadFloat(b, ref o),
                AngularDamping = ReadFloat(b, ref o),
            };
            thruster = new ThrusterTuningConfig
            {
                IdleThrottle     = ReadFloat(b, ref o),
                ThrottleResponse = ReadFloat(b, ref o),
            };
        }

        // -----------------------------------------------------------------
        // Content hash (Bucket A connect-time guard, NETCODE_PLAN §6 / §13)
        // -----------------------------------------------------------------

        /// <summary>
        /// CRC-32 of the encoded blob. Stable across re-serializes of the
        /// same blueprint because the blob excludes displayName / createdUtc
        /// (the only per-serialize-varying fields). Not a security hash —
        /// just a cheap mismatch detector for the content guard.
        /// </summary>
        public static uint ContentHash(ChassisBlueprint blueprint) => Crc32(Encode(blueprint));

        /// <summary>CRC-32 of an arbitrary byte span (used by the content guard).</summary>
        public static uint Crc32(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = (uint)(-(int)(crc & 1));
                    crc = (crc >> 1) ^ (0xEDB88320u & mask);
                }
            }
            return ~crc;
        }

        // -----------------------------------------------------------------
        // Low-level read/write — little-endian, mirrors BrushOpCodec.
        // -----------------------------------------------------------------

        private static void WriteShort(byte[] b, ref int o, short v)
        {
            b[o++] = (byte)(v & 0xFF);
            b[o++] = (byte)((v >> 8) & 0xFF);
        }

        private static short ReadShort(byte[] b, ref int o)
        {
            short v = (short)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        private static void WriteUShort(byte[] b, ref int o, ushort v)
        {
            b[o++] = (byte)(v & 0xFF);
            b[o++] = (byte)((v >> 8) & 0xFF);
        }

        private static ushort ReadUShort(byte[] b, ref int o)
        {
            ushort v = (ushort)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        private static void WriteFloat(byte[] b, ref int o, float v)
        {
            int bits = BitConverter.SingleToInt32Bits(v);
            b[o++] = (byte)(bits & 0xFF);
            b[o++] = (byte)((bits >> 8) & 0xFF);
            b[o++] = (byte)((bits >> 16) & 0xFF);
            b[o++] = (byte)((bits >> 24) & 0xFF);
        }

        private static float ReadFloat(byte[] b, ref int o)
        {
            int bits = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
            o += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
