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

        // TRACE[DOC:research/ui-design-handoff]: inventor + painter tokens.

        /// <summary>Project accent — indigo wash. Headers, highlights, selected state.</summary>
        public static readonly Color Accent = HudStyles.Accent;

        /// <summary>Darkened accent for the pressed state of an accent button.</summary>
        public static readonly Color AccentPressed = new(0.227f, 0.345f, 0.400f, 1f);

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

        /// <summary>Canonical panel background — paper (#F6F0E0), near-opaque.
        /// No longer anchored to <see cref="RuntimePalette.UIBg"/>: the 12-token
        /// lock is suspended per art-direction.md's status banner, and the UI
        /// paper ground has no equivalent among the world tokens.</summary>
        public static readonly Color PanelBg = new(0.965f, 0.941f, 0.878f, 0.97f);

        /// <summary>Full-screen modal ground behind a focused overlay (Lab, modal menus). Opaque paper, mid falloff tone.</summary>
        public static readonly Color Backdrop = new(0.929f, 0.894f, 0.800f, 0.98f);

        /// <summary>Ink dim laid over live gameplay (Settings / pause), so the world stays faintly visible.</summary>
        public static readonly Color ScrimDim = new(0.149f, 0.129f, 0.102f, 0.45f);

        /// <summary>Idle face of a structural (non-accent) button — parchment, one step
        /// below the paper ground so ink <see cref="Text"/> labels stay readable.</summary>
        public static readonly Color ButtonIdle = new(0.918f, 0.875f, 0.773f, 1f);

        /// <summary>Hovered face of a structural button — ink at ~8% over parchment.</summary>
        public static readonly Color ButtonHover = new(0.855f, 0.812f, 0.702f, 1f);

        /// <summary>Header strip / sub-panel divider — deeper parchment.</summary>
        public static readonly Color Header = new(0.882f, 0.835f, 0.714f, 1f);

        /// <summary>Solid ink (#26211A) — primary "Begin"-style buttons, brush fills. Pair with <see cref="CreamText"/>.</summary>
        public static readonly Color Ink = HudStyles.Ink;

        /// <summary>Hovered ink surface (#322B21).</summary>
        public static readonly Color InkHover = new(0.196f, 0.169f, 0.129f, 1f);

        /// <summary>Cream text (#F1E9D4) for labels on ink surfaces.</summary>
        public static readonly Color CreamText = HudStyles.CreamText;

        /// <summary>Vermilion (#C33D1F). STRICTLY RATIONED: needles, ticks, strike-throughs, splats, seals — never large fills.</summary>
        public static readonly Color Vermilion = HudStyles.Danger;

        /// <summary>Indigo label text (#5B7280) on wash panels.</summary>
        public static readonly Color IndigoText = new(0.357f, 0.447f, 0.502f, 1f);

        /// <summary>1px rules and ticks — frame-line ink @ 0.5.</summary>
        public static readonly Color FrameLine = new(0.180f, 0.157f, 0.125f, 0.5f);

        /// <summary>Drafting-grid tint for <see cref="InkKit.GridTile"/> overlays — ink @ 3%.</summary>
        public static readonly Color GridLine = new(0.200f, 0.173f, 0.129f, 0.03f);
    }
}
