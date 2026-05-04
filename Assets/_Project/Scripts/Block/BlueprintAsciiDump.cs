using System.Collections.Generic;
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
        // signal in the dump.
        private static readonly Dictionary<string, char> s_glyphs = new Dictionary<string, char>
        {
            { BlockIds.Cpu,        'C' },
            { BlockIds.Cube,       '#' },
            { BlockIds.Wheel,      'W' },
            { BlockIds.WheelSteer, 'S' },
            { BlockIds.Thruster,   'T' },
            { BlockIds.Aero,       'A' },
            { BlockIds.AeroFin,    'F' },
            { BlockIds.Rudder,     'R' },
            { BlockIds.Weapon,     'G' },
            { BlockIds.BombBay,    'B' },
            { BlockIds.Rope,       '|' },
            { BlockIds.Rotor,      'O' },
            { BlockIds.Hook,       'h' },
            { BlockIds.Mace,       'm' },
        };

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

            sb.Append("Legend: C=Cpu  #=Cube  W=Wheel  S=WheelSteer  T=Thruster  ")
              .Append("A=Aero  F=AeroFin  R=Rudder  G=Gun  B=BombBay  |=Rope  O=Rotor  ")
              .Append("h=Hook  m=Mace\n");
            return sb.ToString();
        }
    }
}
