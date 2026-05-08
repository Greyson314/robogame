namespace Robogame.Core
{
    /// <summary>
    /// Audio mixer bus. Drives which Tweakables-backed volume slider a
    /// cue is gated by, and which <c>AudioMixerGroup</c> the eventual
    /// audio asset routes through.
    /// </summary>
    /// <remarks>
    /// The four-bus layout (Master + 3 children) is the minimum needed
    /// to ship a sane "mute the menu music while I sort tweaks" UX. We
    /// intentionally do NOT add per-weapon / per-impact buses today —
    /// they add complexity without changing perceived loudness control,
    /// and the AudioMixer's snapshots cover transient ducking when the
    /// time comes (e.g. duck SFX 6 dB during the match-end overlay).
    /// </remarks>
    public enum AudioBus
    {
        /// <summary>The master bus. Always applied on top of the per-bus volume.</summary>
        Master,
        /// <summary>Combat / movement / world cues.</summary>
        Sfx,
        /// <summary>Background music.</summary>
        Music,
        /// <summary>Menu clicks, settings panel, HUD ticks.</summary>
        UI,
    }
}
