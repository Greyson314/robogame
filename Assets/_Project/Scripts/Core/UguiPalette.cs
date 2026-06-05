using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Single source of truth for runtime-built **UGUI** panel colours — the
    /// counterpart to <see cref="HudStyles"/> (which owns the IMGUI overlays).
    /// Every procedurally-built menu / build-mode panel (MainMenu, Settings,
    /// SceneTransition, BuildHotbar, Lab, VariantConfig, …) draws its chrome
    /// from these tokens instead of re-rolling its own hardcoded colours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this exists: before this token set, all eight UGUI panels copy-pasted
    /// the same ~7 literal colours (accent orange, dim white, dark panel bg,
    /// button idle/highlight/pressed). A one-line theme change meant editing
    /// eight files; the alphas had already drifted apart (0.85 / 0.92 / 0.93).
    /// This collapses them to one place — see <c>docs/subsystems/ui-direction.md</c>.
    /// </para>
    /// <para>
    /// Shared semantics (<see cref="Accent"/>, <see cref="Text"/>,
    /// <see cref="Danger"/>, <see cref="Healthy"/>) DERIVE from
    /// <see cref="HudStyles"/> so the IMGUI HUD and the UGUI panels move
    /// together when the theme changes — we never want a third divergent
    /// palette. Panel-only chrome (backdrops, button faces) is defined here and
    /// anchored to the locked <see cref="RuntimePalette"/> tokens where one
    /// applies (e.g. <see cref="PanelBg"/> is the <c>UIBg</c> token).
    /// </para>
    /// </remarks>
    public static class UguiPalette
    {
        // -----------------------------------------------------------------
        // Shared semantics — kept in lockstep with the IMGUI HUD.
        // -----------------------------------------------------------------

        /// <summary>Project accent — hazard orange. Headers, highlights, primary action.</summary>
        public static readonly Color Accent = HudStyles.Accent;

        /// <summary>Darkened accent for the pressed state of an accent button.</summary>
        public static readonly Color AccentPressed = new(0.70f, 0.40f, 0.05f, 1f);

        /// <summary>Primary body text (off-white).</summary>
        public static readonly Color Text = HudStyles.TextPrimary;

        /// <summary>Secondary / muted label text.</summary>
        public static readonly Color TextDim = HudStyles.TextMuted;

        /// <summary>Destructive / error / enemy red.</summary>
        public static readonly Color Danger = HudStyles.Danger;

        /// <summary>Affirmative / healthy / friendly green (mint).</summary>
        public static readonly Color Healthy = HudStyles.Healthy;

        // -----------------------------------------------------------------
        // Panel chrome — UGUI panels are more opaque than the IMGUI overlays.
        // -----------------------------------------------------------------

        /// <summary>Canonical opaque panel background, anchored to the locked UIBg token.</summary>
        public static readonly Color PanelBg = WithAlpha(RuntimePalette.UIBg, 0.93f);

        /// <summary>Full-screen modal scrim behind a focused overlay (Lab, modal menus). Near-opaque, slightly blue-black.</summary>
        public static readonly Color Backdrop = new(0.02f, 0.03f, 0.05f, 0.92f);

        /// <summary>Lighter dim laid over live gameplay (Settings / pause), so the world stays faintly visible.</summary>
        public static readonly Color ScrimDim = new(0f, 0f, 0f, 0.55f);

        /// <summary>Idle face of a structural (non-accent) button.</summary>
        public static readonly Color ButtonIdle = new(0.10f, 0.12f, 0.16f, 0.95f);

        /// <summary>Opaque header strip / sub-panel divider.</summary>
        public static readonly Color Header = new(0.10f, 0.12f, 0.16f, 1f);

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
