using System;
using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Tools.Editor;
using UnityEditor;

namespace Robogame.Tests.EditMode.Tools
{
    /// <summary>
    /// Guards the two hand-authored tables the audio pass depends on:
    /// <see cref="AudioCueWizard"/>'s row list (cue → USFX clip path) and
    /// the serialized <see cref="AudioCueLibrary"/> asset. A cue that
    /// skips one of these two steps doesn't fail loudly — it just plays
    /// nothing (AudioRouter's missing-cue logger, or a null-clip no-op) —
    /// so these tests exist to catch the gap at authoring time instead.
    /// CHG-004 / F-008.
    /// </summary>
    /// <remarks>
    /// Reflection reaches <see cref="AudioCueWizard"/>'s private row table
    /// and the nested (also private) CueRow struct's fields — the
    /// project's established pattern for reaching non-public members from
    /// another test assembly (see RotorThrottleTests' SEAM NOTE,
    /// CommandLineMuteAudioTests). No InternalsVisibleTo exists in this
    /// project; reflection is the road, not a language-level seam.
    /// </remarks>
    public sealed class AudioCueWizardTests
    {
        // Documented, deliberate gaps — AudioCueWizard.cs's own comments
        // explain each one (search the cue name there for the full note):
        //   ThrusterIgnite / ThrusterShutdown — session 101 call: accel/
        //     decel doesn't need a discrete cue: ChassisWindAudio +
        //     WheelRoll + RotorSpin already cover movement continuously.
        //   ReloadStart — stays unwired on purpose: WeaponEmpty already
        //     gives the player the "reload is starting" tell.
        // Pre-existing on main, independent of CHG-004; not this change's
        // to fix. If EveryAudioCue_HasAWizardRow ever reports a cue NOT in
        // this set, that's a real gap — add the row, don't add the cue here.
        private static readonly HashSet<AudioCue> s_intentionallyUnwired = new HashSet<AudioCue>
        {
            AudioCue.ThrusterIgnite,
            AudioCue.ThrusterShutdown,
            AudioCue.ReloadStart,
        };

        private static List<AudioCue> WizardRowCues()
        {
            System.Reflection.FieldInfo rowsField = typeof(AudioCueWizard).GetField(
                "s_rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(rowsField, "AudioCueWizard.s_rows not found by reflection — has the field been renamed?");

            Array rows = (Array)rowsField.GetValue(null);
            Assert.IsNotNull(rows, "AudioCueWizard.s_rows was null.");

            Type rowType = rows.GetType().GetElementType();
            System.Reflection.FieldInfo cueField = rowType.GetField(
                "Cue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(cueField, "AudioCueWizard.CueRow.Cue not found by reflection — has the field been renamed?");

            var cues = new List<AudioCue>(rows.Length);
            foreach (object row in rows)
                cues.Add((AudioCue)cueField.GetValue(row));
            return cues;
        }

        [Test]
        public void EveryAudioCue_HasAWizardRow()
        {
            List<AudioCue> wired = WizardRowCues();
            var missing = new List<AudioCue>();
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                if (s_intentionallyUnwired.Contains(cue)) continue;
                if (!wired.Contains(cue)) missing.Add(cue);
            }
            Assert.IsEmpty(missing,
                "AudioCue values with no AudioCueWizard row (add a CueRow, or if the " +
                "omission is deliberate, add it to s_intentionallyUnwired with a one-line " +
                "reason): " + string.Join(", ", missing));
        }

        [Test]
        public void EveryWizardRow_HasALibraryEntry()
        {
            // The clip itself is never asserted non-null here: Assets/Universal
            // Sound FX is gitignored and absent on a fresh clone / the test rig,
            // so even a correctly-authored library entry's guid reference can't
            // resolve to a real AudioClip there.
            AudioCueLibrary lib = AssetDatabase.LoadAssetAtPath<AudioCueLibrary>(AudioCueWizard.LibraryAssetPath);
            Assert.IsNotNull(lib, "No AudioCueLibrary asset at " + AudioCueWizard.LibraryAssetPath);

            var missing = new List<AudioCue>();
            foreach (AudioCue cue in WizardRowCues())
            {
                if (lib.Find(cue) == null) missing.Add(cue);
            }
            Assert.IsEmpty(missing,
                "AudioCueWizard rows with no matching AudioCueLibrary entry (by AudioCue " +
                "int — see AudioCueLibrary.asset): " + string.Join(", ", missing));
        }
    }
}
