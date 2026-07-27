using Robogame.Core;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Creates / refreshes the combat-music assets (ADR-0006): the
    /// <see cref="MusicTrackDefinition"/> at
    /// <c>Resources/Music/CombatTrack.asset</c> and — via
    /// <see cref="AudioCueWizard"/> — the stinger rows in the cue
    /// library. Mirrors the AudioCueWizard shape: authored from code so
    /// "v1 music" is reproducible from one menu invocation after the
    /// generated clips land in <see cref="AudioCueWizard.GeneratedRoot"/>.
    /// </summary>
    public static class MusicScaffolder
    {
        public const string TrackFolder = "Assets/_Project/Resources/Music";
        public const string TrackAssetPath = TrackFolder + "/CombatTrack.asset";
        public const string TrackClipPath =
            AudioCueWizard.GeneratedRoot + "/track_warpulse_100bpm.wav";

        // TRACE[ADR-0006]: the clip is rendered at exactly
        // bars × beatsPerBar × (60/bpm) seconds — these numbers must
        // match the generator (gen_music_assets.py) or the loop seam
        // breaks the beat grid.
        private const float Bpm = 100f;
        private const int BeatsPerBar = 4;
        private const float Volume = 0.55f;

        [MenuItem("Robogame/Scaffold/Music/Build Combat Music")]
        public static void Menu_Build()
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(TrackClipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[MusicScaffolder] Missing track clip at {TrackClipPath} — " +
                                 "run the generator (docs/subsystems/music.md § assets) and reimport.");
            }

            EnsureFolder(TrackFolder);
            var def = AssetDatabase.LoadAssetAtPath<MusicTrackDefinition>(TrackAssetPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MusicTrackDefinition>();
                AssetDatabase.CreateAsset(def, TrackAssetPath);
            }
            def.Clip = clip;
            def.Bpm = Bpm;
            def.BeatsPerBar = BeatsPerBar;
            def.Volume = Volume;
            // TRACE[ADR-0007]: intensity-layer stems for the FMOD backend,
            // StreamingAssets-relative. Same generator, same exact loop
            // length as the fallback clip. Fade windows are MusicMath.
            // LayerGain semantics: equal endpoints = always on (bed),
            // ascending = riser. Each intensity unit adds one pitched
            // voice + one taiko voice (LOG-147): the drums are the
            // foundation at every level, per the percussionist's brief.
            def.Stems = new[]
            {
                new MusicTrackDefinition.Stem { File = "Music/stem_bed.wav",          FadeStart = 0f, FadeEnd = 0f },
                new MusicTrackDefinition.Stem { File = "Music/stem_taiko_ji.wav",     FadeStart = 0f, FadeEnd = 1f },
                new MusicTrackDefinition.Stem { File = "Music/stem_strings.wav",      FadeStart = 0f, FadeEnd = 1f },
                new MusicTrackDefinition.Stem { File = "Music/stem_taiko_chu.wav",    FadeStart = 1f, FadeEnd = 2f },
                new MusicTrackDefinition.Stem { File = "Music/stem_brass.wav",        FadeStart = 1f, FadeEnd = 2f },
                new MusicTrackDefinition.Stem { File = "Music/stem_taiko_odaiko.wav", FadeStart = 2f, FadeEnd = 3f },
                new MusicTrackDefinition.Stem { File = "Music/stem_lute.wav",         FadeStart = 2f, FadeEnd = 3f },
            };
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();

            // Stinger cue rows live in the cue wizard's table — one
            // rebuild wires them alongside everything else.
            AudioCueWizard.CreateOrUpdate();

            Debug.Log($"[MusicScaffolder] CombatTrack at {TrackAssetPath} " +
                      $"(clip {(clip != null ? "wired" : "MISSING")}, {Bpm} BPM, {BeatsPerBar}/4).");
            EditorGUIUtility.PingObject(def);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
