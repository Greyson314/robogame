using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Factories that mint fresh runtime <see cref="ChassisBlueprint"/>s
    /// for the "+ New Robot" flow. Each starter is a known-good, drivable
    /// minimum — a sensible blank canvas for the player to edit.
    /// </summary>
    /// <remarks>
    /// Mirrors the shape used by the default ground rover preset (see
    /// <c>GameplayScaffolder.BuildDefaultGroundBlueprint</c>): 3×3 floor
    /// of cubes with a CPU at centre and a hitscan weapon on top, plus
    /// six wheels on the side faces of the outermost cubes (two steering
    /// at the front, two drive at the middle, two drive at the rear).
    /// </remarks>
    public static class StarterBlueprints
    {
        public const string DefaultName = "New Custom Robot";

        /// <summary>Mint a fresh ground-rover starter as a runtime ScriptableObject.</summary>
        public static ChassisBlueprint CreateGroundStarter(string displayName = DefaultName)
        {
            var list = new List<ChassisBlueprint.Entry>(16);

            // 3×3 floor of cubes (CPU overrides the centre).
            const int xMin = -1, xMax = 1, zMin = -1, zMax = 1;
            for (int x = xMin; x <= xMax; x++)
                for (int z = zMin; z <= zMax; z++)
                    if (!(x == 0 && z == 0))
                        list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, 0, z)));

            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu,    new Vector3Int(0, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, 0)));

            // Wheels mount on the ±X faces of the outermost floor cubes;
            // stem extends outward, tyre at the cell beyond. Steering at
            // the front (zMax), drive everywhere else.
            Vector3Int upRight = new Vector3Int( 1, 0, 0);
            Vector3Int upLeft  = new Vector3Int(-1, 0, 0);
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMin - 1, 0, zMax), upLeft));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMax + 1, 0, zMax), upRight));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel,      new Vector3Int(xMin - 1, 0, 0),    upLeft));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel,      new Vector3Int(xMax + 1, 0, 0),    upRight));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel,      new Vector3Int(xMin - 1, 0, zMin), upLeft));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel,      new Vector3Int(xMax + 1, 0, zMin), upRight));

            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.name = displayName + " (Runtime)";
            bp.DisplayName = displayName;
            bp.Kind = ChassisKind.Ground;
            bp.SetEntries(list.ToArray());
            return bp;
        }
    }
}
