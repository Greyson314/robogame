using System.Collections.Generic;
using UnityEngine;

namespace MidiPlayerTK
{
    /// @ingroup algorithmic_music_generation
    /// <summary>
    /// Builds chord settings and generated note events.
    /// @version Maestro Pro 
    /// See examples in TestMidiStream.cs and ExtStreamPlayerPro.cs.
    /// </summary>
    public class MPTKChordBuilder
    {
        /// <summary>
        /// Triad quality used for three-note chord structures.
        /// </summary>
        public enum Modifier3
        {
            /// <summary>Major triad (1, 3, 5).</summary>
            Maj,
            /// <summary>Minor triad (1, b3, 5).</summary>
            Min,
            /// <summary>Diminished triad (1, b3, b5).</summary>
            Dim,
            /// <summary>Half-diminished triad variant (1, 3, b5).</summary>
            DimHalf,
            /// <summary>Augmented triad (1, 3, #5).</summary>
            Aug,
            /// <summary>Suspended second triad (1, 2, 5).</summary>
            Sus2,
            /// <summary>Suspended fourth triad (1, 4, 5).</summary>
            Sus4,
        }

        /// <summary>
        /// Four-note chord extensions.
        /// </summary>
        public enum Modifier4
        {
            /// <summary>Triad plus major sixth.</summary>
            Maj6,
            /// <summary>Triad plus minor sixth.</summary>
            Min6,
            /// <summary>Triad plus major seventh.</summary>
            Maj7,
            /// <summary>Triad plus minor seventh.</summary>
            Min7,
        }

        /// <summary>@brief
        /// Root MIDI note for the chord. 48 = C3, 60 = C4, 61 = C#4, 62 = D4, 72 = C5.
        /// </summary>
        public int Tonic;

        /// <summary>@brief
        /// Number of notes used to build the chord. Valid range is 2 to 50.
        /// </summary>
        public int Count;

        /// <summary>@brief
        /// Scale degree from 1 to 7.
        /// @li I   Tonic       First
        /// @li II  Supertonic  Second
        /// @li III Mediant     Major or minor third
        /// @li IV  Subdominant Fourth
        /// @li V   Dominant    Fifth
        /// @li VI  Submediant  Major or minor sixth
        /// @li VII Leading Tone/Subtonic Major or minor seventh
        ///! Additional reading: https://lotusmusic.com/lm_chordnames.html
        /// </summary>
        public int Degree;

        /// <summary>@brief
        /// Index of the chord in `ChordLib.csv` under `Resources/GeneratorTemplate`.
        /// Used by chord playback helpers, for example `MidiStreamPlayer.MPTK_PlayChordFromLib(MPTKChordBuilder chord)`.
        /// </summary>
        public int FromLib;

        /// <summary>@brief
        /// MIDI channel from 0 to 15 (9 for drums).
        /// </summary>
        public int Channel;

        /// <summary>@brief
        /// Velocity between 0 and 127.
        /// </summary>
        public int Velocity;

        /// <summary>@brief
        /// Duration of the chord in milliseconds. Set `-1` to play indefinitely.
        /// </summary>
        public long Duration;

        /// <summary>@brief
        /// Delay in milliseconds before playing the chord.
        /// </summary>
        public long Delay;

        /// <summary>@brief
        /// Delay in milliseconds between each note in the chord (plays an arpeggio).
        /// </summary>
        public long Arpeggio;

        /// <summary>@brief
        /// List of MIDI events played for this chord. This list is built when `MPTK_PlayChord` or `MPTK_PlayChordFromLib` is called; otherwise, null.
        /// </summary>
        public List<MPTKEvent> Events;

        //// https://www.bellandcomusic.com/building-chords.html
        //public bool Alterations;

        private bool logChord;

        /// <summary>@brief
        /// Creates a default chord: tonic = C3, degree = 1, note count = 3.
        /// </summary>
        /// <param name="log">True to enable debug logging.</param>
        public MPTKChordBuilder(bool log = false)
        {
            logChord = log;
            Tonic = 48;
            Degree = 1;
            Count = 3;
            Duration = -1; // indefinitely
            Channel = 0;
            Delay = 0;
            Arpeggio = 0;
            Velocity = 127; // max
        }

        private long Clamp(long val, long min, long max)
        {
            return val > max ? max : val < min ? min : val;
        }

        /// <summary>@brief
        /// Builds a chord from the selected scale.
        /// Tonic and Degree must be defined in this `MPTKChordBuilder` instance.
        /// If `scale` is null, a major scale is used.
        /// After the call, `Events` contains all generated notes for the chord.
        /// @version Maestro Pro 
        /// </summary>
        /// <param name="scale">Scale source used to resolve chord notes. If null, a default major scale is created.</param>
        public void MPTK_BuildFromRange(MPTKScaleLib scale = null)
        {
            if (scale == null) scale = MPTKScaleLib.CreateScale(0, logChord);
            Tonic = Mathf.Clamp(Tonic, 0, 127);
            Count = Mathf.Clamp(Count, 2, 50);
            Degree = Mathf.Clamp(Degree, 1, 7);
            Velocity = Mathf.Clamp(Velocity, 0, 127);
            Duration = Clamp(Duration, -1, 999999);
            Delay = Clamp(Delay, 0, 999999);
            Arpeggio = Clamp(Arpeggio, 0, 1000);

            Events = new List<MPTKEvent>();

            for (int iNote = 0; iNote < Count; iNote++)
            {
                int value = Tonic + scale[Degree - 1 + iNote * 2];
                if (value > 127) break;
                Events.Add(new MPTKEvent()
                {
                    Command = MPTKCommand.NoteOn,
                    Value = value,
                    Delay = Delay + Arpeggio * iNote, // time to start playing the note
                    Channel = Channel,
                    Duration = Duration, // real duration. Set to -1 to indefinitely
                    Velocity = Velocity
                });
            }

            if (logChord)
            {
                string info = string.Format("Tonic:{0} Degree:{1}", HelperNoteLabel.LabelFromMidi(Tonic), Degree);
                foreach (MPTKEvent evnt in Events)
                    info += " " + HelperNoteLabel.LabelFromMidi(evnt.Value);
                Debug.Log(info);
            }
        }


        /// <summary>@brief
        /// Builds a chord from `ChordLib.csv` under `Resources/GeneratorTemplate`.
        /// @version Maestro Pro 
        /// </summary>
        /// <param name="chordName">Name of the chord.</param>
        public void MPTK_BuildFromLib(MPTKChordName chordName)
        {
            MPTK_BuildFromLib((int)chordName);
        }

        /// <summary>@brief
        /// Builds a chord from a zero-based index in `ChordLib.csv`.
        /// @version Maestro Pro 
        /// </summary>
        /// <param name="pindex">Zero-based position in `ChordLib.csv`.</param>
        public void MPTK_BuildFromLib(int pindex)
        {
            int index = Mathf.Clamp(pindex, 0, MPTKChordLib.ChordCount - 1);
            MPTKChordLib chorLib = MPTKChordLib.Chords[index];

            Tonic = Mathf.Clamp(Tonic, 0, 127);
            Velocity = Mathf.Clamp(Velocity, 0, 127);
            Duration = Clamp(Duration, -1, 999999);
            Delay = Clamp(Delay, 0, 999999);
            Arpeggio = Clamp(Arpeggio, 0, 1000);

            Events = new List<MPTKEvent>();

            // Add each notes to compose the chord. 
            for (int iNote = 0; iNote < chorLib.Count; iNote++)
            {
                int value = Tonic + chorLib[iNote];
                Events.Add(new MPTKEvent()
                {
                    Command = MPTKCommand.NoteOn,
                    Value = value,
                    Delay = Delay + Arpeggio * iNote, // time to start playing the note
                    Channel = Channel,
                    Duration = Duration, // real duration. Set to -1 to indefinitely
                    Velocity = Velocity
                });
            }

            if (logChord)
            {
                string info = string.Format("Tonic:{0} Degree:{1}", HelperNoteLabel.LabelFromMidi(Tonic), Degree);
                foreach (MPTKEvent evnt in Events)
                    info += " " + HelperNoteLabel.LabelFromMidi(evnt.Value);
                Debug.Log(info);
            }
        }
    }
}
