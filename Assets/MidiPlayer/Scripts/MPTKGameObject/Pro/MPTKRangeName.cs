using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MidiPlayerTK
{
    /// @ingroup algorithmic_music_generation
    /// <summary>
    /// [MPTK PRO] Legacy range identifiers loaded from `GammeDefinition.csv`.
    /// Each enum value maps to one CSV row using the same zero-based index.
    /// Kept for backward compatibility with older range-based APIs.
    /// Some identifiers keep historical spellings from the source library (for example, `Aeolien`, `Dorien`, `Lydien`).
    /// </summary>
    public enum MPTKRangeName
    {
        /// <summary>Range preset "MajorMelodic".</summary>
        MajorMelodic = 0,
        /// <summary>Range preset "MajorHarmonic".</summary>
        MajorHarmonic = 1,
        /// <summary>Range preset "MinorNatural".</summary>
        MinorNatural = 2,
        /// <summary>Range preset "MinorMelodic".</summary>
        MinorMelodic = 3,
        /// <summary>Range preset "MinorHarmonic".</summary>
        MinorHarmonic = 4,
        /// <summary>Range preset "PentatonicMajor".</summary>
        PentatonicMajor = 5,
        /// <summary>Range preset "PentatonicMinor".</summary>
        PentatonicMinor = 6,
        /// <summary>Range preset "Chromatic".</summary>
        Chromatic = 7,
        /// <summary>Range preset "Blues".</summary>
        Blues = 8,
        /// <summary>Range preset "Enigmatic1".</summary>
        Enigmatic1 = 9,
        /// <summary>Range preset "Enigmatic2".</summary>
        Enigmatic2 = 10,
        /// <summary>Range preset "Gitane".</summary>
        Gitane = 11,
        /// <summary>Range preset "Oriental1".</summary>
        Oriental1 = 12,
        /// <summary>Range preset "BebopMajor".</summary>
        BebopMajor = 13,
        /// <summary>Range preset "AeolienB5".</summary>
        AeolienB5 = 14,
        /// <summary>Range preset "Arabic".</summary>
        Arabic = 15,
        /// <summary>Range preset "Augmented".</summary>
        Augmented = 16,
        /// <summary>Range preset "Bahar".</summary>
        Bahar = 17,
        /// <summary>Range preset "Balinaise".</summary>
        Balinaise = 18,
        /// <summary>Range preset "Bartock".</summary>
        Bartock = 19,
        /// <summary>Range preset "BebopDominante".</summary>
        BebopDominante = 20,
        /// <summary>Range preset "Aeolien".</summary>
        Aeolien = 21,
        /// <summary>Range preset "BebopMinor".</summary>
        BebopMinor = 22,
        /// <summary>Range preset "BitonalMajorChromatic".</summary>
        BitonalMajorChromatic = 23,
        /// <summary>Range preset "BitonalMinorChromatic".</summary>
        BitonalMinorChromatic = 24,
        /// <summary>Range preset "BluesDecreased1".</summary>
        BluesDecreased1 = 25,
        /// <summary>Range preset "BluesDecreased2".</summary>
        BluesDecreased2 = 26,
        /// <summary>Range preset "MajorBlues1".</summary>
        MajorBlues1 = 27,
        /// <summary>Range preset "MinorBlues1".</summary>
        MinorBlues1 = 28,
        /// <summary>Range preset "MajorBlues2".</summary>
        MajorBlues2 = 29,
        /// <summary>Range preset "MinorBlues2".</summary>
        MinorBlues2 = 30,
        /// <summary>Range preset "Chinese1".</summary>
        Chinese1 = 31,
        /// <summary>Range preset "Chinese2".</summary>
        Chinese2 = 32,
        /// <summary>Range preset "DemiDecreased".</summary>
        DemiDecreased = 33,
        /// <summary>Range preset "DemiTonNoSixte".</summary>
        DemiTonNoSixte = 34,
        /// <summary>Range preset "Diminish".</summary>
        Diminish = 35,
        /// <summary>Range preset "Dorien".</summary>
        Dorien = 36,
        /// <summary>Range preset "Spanish1".</summary>
        Spanish1 = 37,
        /// <summary>Range preset "Spanish2".</summary>
        Spanish2 = 38,
        /// <summary>Range preset "Spanish8".</summary>
        Spanish8 = 39,
        /// <summary>Range preset "Gypsy".</summary>
        Gypsy = 40,
        /// <summary>Range preset "Hexalydien".</summary>
        Hexalydien = 41,
        /// <summary>Range preset "HexaMelodic".</summary>
        HexaMelodic = 42,
        /// <summary>Range preset "HexaPhrygien".</summary>
        HexaPhrygien = 43,
        /// <summary>Range preset "HexaTritoniqueBinary".</summary>
        HexaTritoniqueBinary = 44,
        /// <summary>Range preset "HexaTritoniqueDecreased1".</summary>
        HexaTritoniqueDecreased1 = 45,
        /// <summary>Range preset "HexaTritoniqueDecreased2".</summary>
        HexaTritoniqueDecreased2 = 46,
        /// <summary>Range preset "HexaTritoniqueDecreased3".</summary>
        HexaTritoniqueDecreased3 = 47,
        /// <summary>Range preset "Hindou".</summary>
        Hindou = 48,
        /// <summary>Range preset "Hirajoshi".</summary>
        Hirajoshi = 49,
        /// <summary>Range preset "HongroiseGitane".</summary>
        HongroiseGitane = 50,
        /// <summary>Range preset "HongroiseMajor".</summary>
        HongroiseMajor = 51,
        /// <summary>Range preset "HongroiseMinor".</summary>
        HongroiseMinor = 52,
        /// <summary>Range preset "Indoustane".</summary>
        Indoustane = 53,
        /// <summary>Range preset "Ionien".</summary>
        Ionien = 54,
        /// <summary>Range preset "Ionien5".</summary>
        Ionien5 = 55,
        /// <summary>Range preset "Iwato".</summary>
        Iwato = 56,
        /// <summary>Range preset "Javanais".</summary>
        Javanais = 57,
        /// <summary>Range preset "KokinJoshi".</summary>
        KokinJoshi = 58,
        /// <summary>Range preset "Kumoi".</summary>
        Kumoi = 59,
        /// <summary>Range preset "Locrien".</summary>
        Locrien = 60,
        /// <summary>Range preset "Locrien6".</summary>
        Locrien6 = 61,
        /// <summary>Range preset "Lydien1".</summary>
        Lydien1 = 62,
        /// <summary>Range preset "Lydien2".</summary>
        Lydien2 = 63,
        /// <summary>Range preset "Lydien3".</summary>
        Lydien3 = 64,
        /// <summary>Range preset "Mixolydien".</summary>
        Mixolydien = 65,
        /// <summary>Range preset "NapolitanMajor".</summary>
        NapolitanMajor = 66,
        /// <summary>Range preset "NapolitanMinor".</summary>
        NapolitanMinor = 67,
        /// <summary>Range preset "Oriental2".</summary>
        Oriental2 = 68,
        /// <summary>Range preset "Oriental3".</summary>
        Oriental3 = 69,
        /// <summary>Range preset "PentatonicHarmonic".</summary>
        PentatonicHarmonic = 70,
        /// <summary>Range preset "PentatonicDominante".</summary>
        PentatonicDominante = 71,
        /// <summary>Range preset "PentatonicEgyptian".</summary>
        PentatonicEgyptian = 72,
        /// <summary>Range preset "PentatonicJapanese".</summary>
        PentatonicJapanese = 73,
        /// <summary>Range preset "PentatonicLocrien1".</summary>
        PentatonicLocrien1 = 74,
        /// <summary>Range preset "PentatonicLocrien2".</summary>
        PentatonicLocrien2 = 75,
        /// <summary>Range preset "PentatonicMauritanian".</summary>
        PentatonicMauritanian = 76,
        /// <summary>Range preset "PentatonicPelog".</summary>
        PentatonicPelog = 77,
        /// <summary>Range preset "Persane1".</summary>
        Persane1 = 78,
        /// <summary>Range preset "Persane2".</summary>
        Persane2 = 79,
        /// <summary>Range preset "Phrygien".</summary>
        Phrygien = 80,
        /// <summary>Range preset "Promethee".</summary>
        Promethee = 81,
        /// <summary>Range preset "RoumanMinor".</summary>
        RoumanMinor = 82,
        /// <summary>Range preset "SuperlocrienBB7".</summary>
        SuperlocrienBB7 = 83,
        /// <summary>Range preset "SuperlocrienAltered".</summary>
        SuperlocrienAltered = 84,
        /// <summary>Range preset "TonByTon".</summary>
        TonByTon = 85,
    }
}
