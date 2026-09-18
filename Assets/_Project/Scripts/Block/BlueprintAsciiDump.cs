using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Render a <see cref="BlueprintPlan"/> as a human-readable ASCII map,
    /// one Y-layer at a time. Lets reviewers (and the AI authoring this
    /// thing) see exactly what shape a blueprint produces without having
    /// to run the game.
    /// </summary>
    /// <remarks>
    /// Output convention:
    /// <list type="bullet">
    /// <item><description>Layers print top-down (highest Y first).</description></item>
    /// <item><description>Within a layer, the +Z direction is "up" on the page (top row), -Z is the bottom.</description></item>
    /// <item><description>+X is right, -X is left.</description></item>
    /// <item><description>Empty cells render as <c>.</c>; unknown ids as <c>?</c>.</description></item>
    /// </list>
    /// </remarks>
    public static class BlueprintAsciiDump
    {
        // One-character glyph per known block id. Add a new id here when
        // a new BlockIds.X ships — fall back to '?' is safe but loses
        // signal in the dump. The legend (see s_legend below) prints
        // itself from this map, one entry per glyph, in this order, so a
        // glyph and its legend entry can no longer drift apart the way
        // they did before CHG-025 (F-035: n=Magnet, X=GrappleMagnet and
        // HoverBlade were all missing from the hand-typed legend, and the
        // Hover Tank preset printed an unmapped '?' for every HoverBlade
        // cell because HoverBlade had never been added here either). The
        // map had also simply never caught up with five other ids the
        // shipped presets use (Cannon, Mortar, Drill, Spring, ModuleEmp) —
        // BlueprintAsciiDumpTests.EveryGlyphInAPresetDump_IsInTheLegend
        // (CHG-025) is the oracle: it names any block id a shipped preset
        // uses that still isn't here.
        private static readonly Dictionary<string, char> s_glyphs = new Dictionary<string, char>
        {
            { BlockIds.Cpu,        'C' },
            { BlockIds.Cube,       '#' },
            { BlockIds.Wheel,      'W' },
            { BlockIds.WheelSteer, 'S' },
            { BlockIds.Thruster,   'T' },
            { BlockIds.Aero,       'A' },
            { BlockIds.AeroFin,    'F' },
            { BlockIds.Wing,       'w' },   // 'W' is Wheel
            { BlockIds.Rudder,     'R' },
            { BlockIds.Weapon,     'G' },
            { BlockIds.BombBay,    'B' },
            { BlockIds.Rope,       '|' },
            { BlockIds.Rotor,      'O' },
            { BlockIds.Hook,       'h' },
            { BlockIds.Mace,       'm' },
            { BlockIds.Magnet,     'n' },
            { BlockIds.GrappleMagnet, 'X' },
            { BlockIds.HoverBlade, 'H' },
            { BlockIds.Cannon,     'c' },   // 'C' is Cpu
            { BlockIds.Mortar,     'M' },   // 'm' is Mace
            { BlockIds.Drill,      'D' },
            { BlockIds.Spring,     's' },   // 'S' is WheelSteer
            { BlockIds.ModuleEmp,  'E' },
        };

        // glyph -> the BlockIds constant's own field name (e.g. "Weapon",
        // not the old hand-typed "Gun"), read via reflection once so the
        // legend never needs a second, hand-typed name table to drift
        // against the glyph map.
        private static readonly Dictionary<string, string> s_shortNames = BuildShortNames();

        // "Legend: " + one "glyph=Name" per s_glyphs entry, in map
        // declaration order, plus the "?=unmapped" fallback.
        private static readonly string s_legend = BuildLegend();

        private static Dictionary<string, string> BuildShortNames()
        {
            var names = new Dictionary<string, string>();
            foreach (FieldInfo f in typeof(BlockIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType != typeof(string) || !f.IsLiteral) continue;
                names[(string)f.GetRawConstantValue()] = f.Name;
            }
            return names;
        }

        private static string BuildLegend()
        {
            StringBuilder sb = new StringBuilder("Legend: ");
            foreach (KeyValuePair<string, char> kv in s_glyphs)
            {
                string name = s_shortNames.TryGetValue(kv.Key, out string n) ? n : kv.Key;
                sb.Append(kv.Value).Append('=').Append(name).Append("  ");
            }
            sb.Append("?=unmapped");
            return sb.ToString();
        }

        public static string Dump(BlueprintPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Blueprint '").Append(plan.DisplayName).Append("' (")
              .Append(plan.Kind).Append(", ").Append(plan.Entries.Length).Append(" cells)\n");
            sb.Append("RotorsGenerateLift: ").Append(plan.RotorsGenerateLift).Append('\n');

            if (plan.Entries.Length == 0)
            {
                sb.Append("(empty)\n");
                return sb.ToString();
            }

            Vector3Int min = plan.Entries[0].Position;
            Vector3Int max = min;
            Dictionary<Vector3Int, string> byPos = new Dictionary<Vector3Int, string>(plan.Entries.Length);
            foreach (ChassisBlueprint.Entry e in plan.Entries)
            {
                min = Vector3Int.Min(min, e.Position);
                max = Vector3Int.Max(max, e.Position);
                byPos[e.Position] = e.BlockId;
            }

            sb.Append("Bounds: x[").Append(min.x).Append("..").Append(max.x)
              .Append("] y[").Append(min.y).Append("..").Append(max.y)
              .Append("] z[").Append(min.z).Append("..").Append(max.z).Append("]\n\n");

            for (int y = max.y; y >= min.y; y--)
            {
                sb.Append("Layer y=").Append(y).Append(":\n");
                // X header.
                sb.Append("     ");
                for (int x = min.x; x <= max.x; x++) sb.AppendFormat("{0,3}", x);
                sb.Append('\n');
                for (int z = max.z; z >= min.z; z--)
                {
                    sb.AppendFormat("{0,3}  ", z);
                    for (int x = min.x; x <= max.x; x++)
                    {
                        char glyph = '.';
                        if (byPos.TryGetValue(new Vector3Int(x, y, z), out string id))
                        {
                            if (!s_glyphs.TryGetValue(id, out glyph)) glyph = '?';
                        }
                        sb.Append(' ').Append(glyph).Append(' ');
                    }
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            sb.Append(s_legend).Append('\n');
            return sb.ToString();
        }
    }
}
