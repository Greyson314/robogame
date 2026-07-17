using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MidiPlayerTK
{
    /// @ingroup algorithmic_music_generation
    /// <summary>
    /// [MPTK PRO] Range/scale interval library loaded from `GammeDefinition.csv`.
    /// Can be used in any music-generation workflow.
    /// See examples in TestMidiStream.cs and ExtStreamPlayerPro.cs.
    /// @code
    ///
    ///     // Example target: a MidiStreamPlayer prefab in the scene hierarchy.
    ///     public MidiStreamPlayer midiStreamPlayer;
    ///     
    ///     new void Start()
    ///     {
    ///         // Find MidiStreamPlayer. It can also be assigned directly in the Inspector.
    ///         midiStreamPlayer = FindObjectOfType<MidiStreamPlayer>();
    ///     }
    ///
    ///     private void PlayScale()
    ///     {
    ///         // Get the currently selected range/scale.
    ///         MPTKRangeLib range = MPTKRangeLib.Range(CurrentScale, true);
    ///         for (int ecart = 0; ecart < range.Count; ecart++)
    ///         {
    ///             NotePlaying = new MPTKEvent()
    ///             {
    ///                 Command = MPTKCommand.NoteOn, // MIDI command
    ///                 Value = CurrentNote + range[ecart], // 0..127, 48 = C3, 60 = C4
    ///                 Channel = StreamChannel, // 0..15, channel 9 reserved for drums
    ///                 Duration = DelayPlayScale, // milliseconds, -1 for indefinite note
    ///                 Velocity = Velocity, // 0..127
    ///                 Delay = ecart * DelayPlayScale, // delay in milliseconds before note start
    ///             };
    ///             midiStreamPlayer.MPTK_PlayEvent(NotePlaying);
    ///         }
    ///     }
    /// @endcode
    /// </summary>
    public class MPTKRangeLib
    {
        /// <summary>@brief
        /// Zero-based index in the loaded library.
        /// </summary>
        public int Index;

        /// <summary>@brief
        /// Full range/scale name.
        /// </summary>
        public string Name;

        /// <summary>@brief
        /// Short range/scale name.
        /// </summary>
        public string Short;

        /// <summary>@brief
        /// Family indicator when available.
        /// @li M = major family
        /// @li m = minor family
        /// @li _ = unspecified/other
        /// </summary>
        public string Flag;

        /// <summary>@brief
        /// True for a commonly used range/scale, otherwise more exotic.
        /// </summary>
        public bool Main;

        /// <summary>@brief
        /// Number of notes in the range/scale.
        /// </summary>
        public int Count;

        /// <summary>@brief
        /// Interval in semitones from the tonic.
        /// The first position (`index = 0`) always returns `0` regardless of the selected range.
        /// </summary>
        /// <param name="index">Position in the range. When greater than `Count`, intervals continue in the next octave(s).</param>
        /// <returns>Interval in semitones from the tonic.</returns>
        public int this[int index]
        {
            get
            {
                if (Count == 0) return 0;
                if (octave == null) BuildOctave();
                int delta = 0;
                try
                {
                    delta = octave[index % Count] + ((index / Count) * 12);

                }
                catch (System.Exception ex)
                {
                    MidiPlayerGlobal.ErrorDetail(ex);
                }
                return delta;
            }
        }

        private int[] octave;

        /// <summary>@brief
        /// Source pitch-class markers for one octave (12 semitones).
        /// </summary>
        private string[] position;

        private static List<MPTKRangeLib> scales;

        /// <summary>@brief
        /// Gets a range from its zero-based index.
        /// Data is read from `GammeDefinition.csv` in `Resources/GeneratorTemplate`.
        /// </summary>
        /// <param name="index">Zero-based range index.</param>
        /// <param name="log">True to log debug details while loading/building.</param>
        /// <returns>The matching `MPTKRangeLib` instance, or null when the index is invalid.</returns>
        public static MPTKRangeLib Range(int index, bool log = false)
        {
            if (scales == null) Init(log);
            if (index < 0 && index >= scales.Count) return null;
            scales[index].BuildOctave(log);
            return scales[index];
        }

        /// <summary>@brief
        /// Gets a range from its enum identifier.
        /// Data is read from `GammeDefinition.csv` in `Resources/GeneratorTemplate`.
        /// </summary>
        /// <param name="index">Enum identifier in `MPTKRangeName`.</param>
        /// <param name="log">True to log debug details while loading/building.</param>
        /// <returns>The matching `MPTKRangeLib` instance.</returns>
        public static MPTKRangeLib Range(MPTKRangeName index, bool log = false)
        {
            if (scales == null) Init(log);
            scales[(int)index].BuildOctave(log);
            return scales[(int)index];
        }

        /// <summary>@brief
        /// Number of ranges/scales available in `GammeDefinition.csv` (`Resources/GeneratorTemplate`).
        /// </summary>
        public static int RangeCount
        {
            get
            {
                if (scales == null) Init();
                return scales.Count;
            }
        }

        private static void Init(bool log = false)
        {
            if (scales == null)
            {
                scales = new List<MPTKRangeLib>();
                TextAsset mytxtData = Resources.Load<TextAsset>("GeneratorTemplate/GammeDefinition");
                string text = System.Text.Encoding.UTF8.GetString(mytxtData.bytes);
                string[] list1 = text.Split('\r');
                if (list1.Length >= 1)
                {
                    for (int i = 1; i < list1.Length; i++)
                    {
                        string[] c = list1[i].Split(';');
                        if (c.Length >= 15)
                        {
                            MPTKRangeLib scale = new MPTKRangeLib();
                            try
                            {
                                scale.Index = scales.Count;
                                scale.Name = c[0];
                                if (scale.Name[0] == '\n') scale.Name = scale.Name.Remove(0, 1);
                                scale.Short = c[1];
                                scale.Flag = c[2];
                                scale.Main = (c[3].ToUpper() == "X") ? true : false;
                                scale.Count = Convert.ToInt32(c[4]);
                                scale.position = new string[12];
                                for (int j = 5; j <= 16; j++)
                                {
                                    scale.position[j - 5] = c[j];
                                }
                            }
                            catch (System.Exception ex)
                            {
                                MidiPlayerGlobal.ErrorDetail(ex);
                            }
                            scales.Add(scale);
                        }
                    }

                }
                if (log)
                    Debug.Log("Ranges loaded: " + MPTKRangeLib.scales.Count);
            }
        }

        private void BuildOctave(bool log = false)
        {
            if (octave == null)
            {
                try
                {
                    octave = new int[Count];
                    int iEcart = 0;
                    int vEcart = 1;
                    octave[0] = 0;
                    iEcart++;
                    for (int i = 1; i < position.Length; i++)
                    {
                        if (position[i].Trim().Length == 0)
                        {
                            vEcart++;
                        }
                        else
                        {
                            octave[iEcart] = vEcart;
                            iEcart++;
                            vEcart += 1;
                        }
                    }
                    //octave[octave.Length - 1] = 12;
                }
                catch (System.Exception ex)
                {
                    MidiPlayerGlobal.ErrorDetail(ex);
                }

                if (log)
                {
                    string info = string.Format("Range:{0} '{1}'", Flag, Name);
                    foreach (int e in octave)
                        info += string.Format(" [{0} {1}]", e, HelperNoteLabel.LabelFromMidi(48 + e));
                    Debug.Log(info);
                }
            }
        }
    }
}
