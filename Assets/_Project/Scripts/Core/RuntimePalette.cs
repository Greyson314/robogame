using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Runtime-accessible mirror of the locked 12-token art palette
    /// (see <c>docs/ART_DIRECTION.md</c> and the editor-only
    /// <c>WorldPalette</c>). Lives in <see cref="Robogame.Core"/> so any
    /// runtime asmdef can pick up palette colours for VFX, gizmos, or
    /// procedural materials without taking an editor dependency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers MUST stay in lockstep with <c>WorldPalette</c> and the
    /// art doc — every authored colour comes from this list. If you change
    /// a value, change it here, in <c>WorldPalette.cs</c>, and in the
    /// <c>Palette</c> table inside <c>docs/ART_DIRECTION.md</c> in the
    /// same commit.
    /// </para>
    /// </remarks>
    public static class RuntimePalette
    {
        // Structure & environment
        public static readonly Color Slate       = HexRGB(0x2A, 0x32, 0x3C);
        public static readonly Color SlateLight  = HexRGB(0x52, 0x5B, 0x66);
        public static readonly Color Concrete    = HexRGB(0x3F, 0x43, 0x48);
        public static readonly Color Grass       = HexRGB(0x4D, 0x73, 0x40);
        public static readonly Color SkyDay      = HexRGB(0x8C, 0xB7, 0xE0);

        // Action accents
        public static readonly Color Hazard      = HexRGB(0xF2, 0x8C, 0x1A);
        public static readonly Color Caution     = HexRGB(0xE6, 0xCC, 0x33);
        public static readonly Color Alert       = HexRGB(0xBF, 0x33, 0x3F);

        // Tech / energy
        public static readonly Color Cyan        = HexRGB(0x33, 0xD9, 0xF2);
        public static Color CyanEmit             => Cyan * 4f; // HDR boost
        public static readonly Color Plasma      = HexRGB(0xA1, 0x55, 0xF2);
        public static readonly Color Mint        = HexRGB(0x34, 0xA6, 0x59);

        // UI / chrome
        public static readonly Color UIBg        = HexRGB(0x0F, 0x12, 0x19);
        public static readonly Color UIText      = Color.white;

        // Common derived tones VFX authors reach for. Kept here so a
        // particle system that wants "hot core" doesn't reach for an
        // ad-hoc literal that drifts off-palette.
        public static readonly Color HotCore     = HexRGB(0xFF, 0xF1, 0xC8); // bright cream — muzzle/explosion core
        public static readonly Color SmokeDark   = HexRGB(0x1A, 0x1D, 0x22); // near-black tint for late-stage smoke
        public static readonly Color DustLight   = HexRGB(0xC9, 0xB3, 0x86); // dust kicked off destroyed structure

        private static Color HexRGB(int r, int g, int b)
            => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
