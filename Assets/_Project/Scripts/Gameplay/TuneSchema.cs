using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Which per-block "next placement" cache a tune field writes. The
    /// panel maps these onto the <see cref="BuildSession"/> variant
    /// setters (dims components, pitch, teeter, scalar config).
    /// </summary>
    public enum TuneTarget
    {
        DimsX,
        DimsY,
        DimsZ,
        Pitch,
        Teeter,
        Config,
    }

    public enum TuneFieldKind
    {
        /// <summary>Continuous slider; commit snap via <see cref="TuneField.Snap"/>.</summary>
        Slider,
        /// <summary>Slider with wholeNumbers set for integer drag feedback.</summary>
        IntSlider,
    }

    public enum TuneFieldGroup
    {
        /// <summary>Always-visible row.</summary>
        Primary,
        /// <summary>Row inside the collapsed-by-default Advanced expander.</summary>
        Advanced,
    }

    /// <summary>
    /// Read context handed to schema resolvers and readouts: the active
    /// block id plus null-safe accessors over the session variant caches.
    /// </summary>
    public readonly struct TuneContext
    {
        public readonly string BlockId;
        public readonly BuildSession Session;

        public TuneContext(string blockId, BuildSession session)
        {
            BlockId = blockId;
            Session = session;
        }

        public Vector3 Dims => Session != null ? Session.GetVariantDims(BlockId) : Vector3.zero;
        public float Pitch => Session != null ? Session.GetVariantPitch(BlockId) : 0f;
        public float Teeter => Session != null ? Session.GetVariantTeeter(BlockId) : 0f;
        public float Config => Session != null ? Session.GetVariantConfig(BlockId) : 0f;
    }

    /// <summary>Section readout line: text plus a warning tint (stall red).</summary>
    public readonly struct TuneReadout
    {
        public readonly string Text;
        public readonly bool Warn;

        public TuneReadout(string text, bool warn = false)
        {
            Text = text;
            Warn = warn;
        }
    }

    /// <summary>
    /// One slider row in a variant-config section. Min/Max are functions
    /// of the block id because families share a schema with per-id bounds
    /// (Wing vs Aero). <see cref="Resolve"/> maps the raw cache to the
    /// DISPLAY value — it must respect the 0-means-"use default" sentinel
    /// (display the default without writing it back), so an untouched
    /// block keeps the sentinel in its blueprint entry.
    /// </summary>
    public sealed class TuneField
    {
        public TuneFieldKind Kind = TuneFieldKind.Slider;
        public TuneFieldGroup Group = TuneFieldGroup.Primary;
        public string Label;
        public string Tip;
        /// <summary>Value-text number format ("F0" / "F1" / "F2").</summary>
        public string Format = "F2";
        /// <summary>Appended after the formatted value ("°").</summary>
        public string Suffix = "";
        public TuneTarget Target;
        public System.Func<string, float> Min;
        public System.Func<string, float> Max;
        /// <summary>Commit snap applied on every slider change.</summary>
        public System.Func<float, float> Snap;
        /// <summary>Cache → display value (sentinel-aware; see class doc).</summary>
        public System.Func<TuneContext, float> Resolve;
        /// <summary>Optional warning predicate — tints the value text (foil stall).</summary>
        public System.Func<float, bool> Warn;
    }

    /// <summary>
    /// A named role snapshot: one button that writes several caches at
    /// once, then re-syncs the section from the caches.
    /// </summary>
    public sealed class TunePreset
    {
        public string Label;
        public (TuneTarget target, float value)[] Writes;
    }

    /// <summary>
    /// Declarative description of one variant-config section: title,
    /// optional preset row, slider fields (primary + advanced), and an
    /// optional live consequence readout. One schema instance may serve
    /// several block ids (the aero family) — everything per-id is a
    /// function of the block id.
    /// </summary>
    public sealed class TuneSchema
    {
        /// <summary>Per-id display name ("Wing", "Tail fin", "EMP").</summary>
        public System.Func<string, string> Title;
        /// <summary>Title lead when not instance-editing ("Variant" / "Module").</summary>
        public string IdleLead = "Variant";
        public TuneField[] Fields;
        public TunePreset[] Presets;
        /// <summary>Optional section readout, recomputed on any field change.</summary>
        public System.Func<TuneContext, TuneReadout> Readout;
    }
}
