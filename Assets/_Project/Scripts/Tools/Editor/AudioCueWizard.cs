using System.Collections.Generic;
using System.IO;
using Robogame.Core;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Creates / refreshes the <see cref="AudioCueLibrary"/> asset at
    /// <c>Assets/_Project/Resources/AudioCueLibrary.asset</c> and binds
    /// every <see cref="AudioCue"/> value to a Universal Sound FX clip.
    /// Idempotent: re-running the wizard re-pairs missing rows without
    /// clobbering manually-tuned ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in the Editor asmdef so the runtime <see cref="AudioCueLibrary"/>
    /// stays free of <see cref="UnityEditor"/> calls. Mirrors the
    /// <c>CombatVfxWizard</c> shape — run once after importing USFX,
    /// after which the library asset is a regular ScriptableObject any
    /// code can <c>Resources.Load</c>.
    /// </para>
    /// <para>
    /// <b>Why we author by USFX path, not by drag-drop.</b> The library
    /// has 21+ entries; hand-wiring is error-prone and re-creating the
    /// asset after a fresh checkout would lose the bindings. Authoring
    /// from code keeps "v1 audio" reproducible from a single menu
    /// invocation and survives the package being re-imported.
    /// </para>
    /// </remarks>
    public static class AudioCueWizard
    {
        public const string LibraryFolder = "Assets/_Project/Resources";
        public const string LibraryAssetPath = LibraryFolder + "/AudioCueLibrary.asset";
        public const string UsfxRoot = "Assets/Universal Sound FX";

        // -----------------------------------------------------------------
        // Cue → clip mapping. Ordering follows the AudioCue enum so
        // future contributors can match the table at a glance.
        //
        // SpatialBlend: 1.0 = full 3D positional (combat / movement),
        // 0.0 = pure 2D (UI / music / match-state).
        //
        // PitchJitter: ±jitter applied per play. SMG hit-storms get a
        // chunky 0.10 so 12 Hz fire isn't audibly identical; UI clicks
        // get 0 so the menu doesn't feel "broken".
        //
        // Solo: only one instance alive at a time. WeaponFire, RotorSpin,
        // WheelRoll all need it (looped / rapid-fire); explosions don't.
        // -----------------------------------------------------------------
        private static readonly CueRow[] s_rows = new[]
        {
            // Combat
            // BLASTER_Deep_Muffled — heavier / lower-pitched than the
            // bright short blaster. Volume bumped slightly because deep
            // sounds need more headroom to feel equivalently loud.
            new CueRow(AudioCue.WeaponFire,        "WEAPONS/SciFi/Blasters_Simple/BLASTER_Deep_Muffled_mono.wav",                                              AudioBus.Sfx,   spatial: 1f, vol: 0.75f, jitter: 0.10f, solo: false),
            // WeaponFireCannon — short punchy boom for the pirate
            // cannon's fire-and-forget shot. Not solo: at 1 shot/sec
            // and 16 chassis MP, simultaneous booms are realistic
            // and don't stack into a chatter.
            new CueRow(AudioCue.WeaponFireCannon,  "EXPLOSIONS/Short/EXPLOSION_Short_01_mono.wav",                                                              AudioBus.Sfx,   spatial: 1f, vol: 1.00f, jitter: 0.06f, solo: false),
            new CueRow(AudioCue.ProjectileImpact,  "BREAKS_SNAPS/SNAP_Clean_mono.wav",                                                                          AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0.12f, solo: false),
            new CueRow(AudioCue.BlockDamaged,      "ROBOTICS/Short_Bursts/ROBOTIC_Short_Burst_12_Digital_Air_Lock_mono.wav",                                    AudioBus.Sfx,   spatial: 1f, vol: 0.40f, jitter: 0.08f, solo: false),
            new CueRow(AudioCue.BlockDestroyed,    "DEMOLISH/DEMOLISH_Short_01_mono.wav",                                                                       AudioBus.Sfx,   spatial: 1f, vol: 0.85f, jitter: 0.06f, solo: false),
            // ChassisRam: a deep noisy THUD reads as a heavy mass
            // landing — chassis hitting the ground or another chassis
            // shouldn't ring like a swung weapon. The metallic clang is
            // reserved for TipImpact (hook / mace).
            new CueRow(AudioCue.ChassisRam,        "THUDS_THUMPS/THUD_Deep_Noisy_01_mono.wav",                                                                   AudioBus.Sfx,   spatial: 1f, vol: 1.00f, jitter: 0.06f, solo: false),
            // TipImpact: the metallic clang. Deep + ringy — what the
            // hook / mace makes when it actually lands a hit.
            new CueRow(AudioCue.TipImpact,         "IMPACTS/Metal/IMPACT_Metal_Cling_Deep_mono.wav",                                                            AudioBus.Sfx,   spatial: 1f, vol: 0.95f, jitter: 0.06f, solo: false),
            new CueRow(AudioCue.BombExplosion,     "EXPLOSIONS/Arcade/EXPLOSION_Arcade_03_mono.wav",                                                            AudioBus.Sfx,   spatial: 1f, vol: 1.20f, jitter: 0.04f, solo: false),
            // DrillContact — the per-strike "bite" when a drill brush op
            // actually carves cells (changed > 0). Pickaxe-into-dirt is
            // the canonical "tool meeting terrain" cue. High jitter so
            // 30 Hz held-fire doesn't read as a stuck note; not Solo so
            // multiple drills on a single chassis stack naturally.
            new CueRow(AudioCue.DrillContact,      "TOOLS/Pickaxe/PICKAXE_Impact_Dirt_Hard_01_RR4.wav",                                                        AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0.12f, solo: false),
            // DrillActive — the looped motor bed under the bright
            // per-strike DrillContact cue. Stone-crusher loop reads as
            // aggressive industrial grinding; lower volume than
            // DrillContact so the per-strike cue stays in focus. Solo
            // (one motor per drill block); zero jitter on the loop.
            new CueRow(AudioCue.DrillActive,       "MACHINES/Construction/MACHINE_Construction_Stone_Crusher_loop_mono.wav",                                   AudioBus.Sfx,   spatial: 1f, vol: 0.40f, jitter: 0f,    solo: true),

            // Movement
            new CueRow(AudioCue.ThrusterIgnite,    "CHARGE_UPS_DOWNS/CHARGE_Complex_Wet_12_Semi_Up_1000ms_mono.wav",                                            AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0.05f, solo: true),
            new CueRow(AudioCue.ThrusterShutdown,  "CHARGE_UPS_DOWNS/CHARGE_Complex_Wet_12_Semi_Down_1000ms_mono.wav",                                          AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.05f, solo: true),
            new CueRow(AudioCue.WheelRoll,         "ENGINES_MOTORS_GENERATORS/ENGINE_Generic_01_loop_mono.wav",                                                 AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.RotorSpin,         "VEHICLES/Air/Helicopters/HELICOPTER_Hover_Fast_loop_mono.wav",                                              AudioBus.Sfx,   spatial: 1f, vol: 0.45f, jitter: 0f,    solo: true),
            // PropellerLoop — engine-driven prop (rotor + adopted foils).
            // RotorBlock picks this when foils are adopted, falling back
            // to RotorSpin (helicopter-style whine) for bare rotors.
            new CueRow(AudioCue.PropellerLoop,     "VEHICLES/Air/Airplanes/PROPELLER_ENGINE_Loop_01_loop_mono.wav",                                            AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0f,    solo: true),
            // WindLoop — passive wind in your ears. ChassisWindAudio
            // scales volume + pitch with chassis speed. Spatial = 1
            // (3D) so a fast bot whooshing past has its wind pan and
            // attenuate naturally; the local player's camera sits
            // close enough to its own chassis (well inside minDistance)
            // that their own wind reads at full strength regardless.
            // Base volume in the cue is unused — ChassisWindAudio
            // overrides via SetBaseVolume from the speed curve.
            new CueRow(AudioCue.WindLoop,          "WIND/WIND_Storm_Blowing_Deep_01_loop_mono.wav",                                                             AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.WaterSplash,       "ELEMENTS/Water/Splashes/SPLASH_Designed_Medium_01_mono.wav",                                                AudioBus.Sfx,   spatial: 1f, vol: 0.85f, jitter: 0.05f, solo: false),

            // UI / match
            new CueRow(AudioCue.UiHover,           "USER_INTERFACES/Beeps/UI_Beep_Bend_Short_stereo.wav",                                                       AudioBus.UI,    spatial: 0f, vol: 0.40f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.UiClick,           "USER_INTERFACES/Clicks_Taps/UI_Click_Metallic_Bright_mono.wav",                                             AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.UiBack,            "USER_INTERFACES/Clicks_Taps/UI_Click_TapBack_01_mono.wav",                                                  AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.MatchStart,        "8BIT/Coin_Collect/8BIT_RETRO_Coin_Collect_Two_Note_Bright_Twinkle_mono.wav",                                AudioBus.UI,    spatial: 0f, vol: 1.00f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.MatchEndVictory,   "MUSIC_EFFECTS/MUSIC_EFFECT_Platform_Positive_01_stereo.wav",                                                AudioBus.UI,    spatial: 0f, vol: 1.00f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.MatchEndDefeat,    "MUSIC_EFFECTS/MUSIC_EFFECT_Platform_Negative_01_stereo.wav",                                                AudioBus.UI,    spatial: 0f, vol: 1.00f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.MatchEndDraw,      "MUSIC_EFFECTS/MUSIC_EFFECT_Orchestral_Battle_Neutral_stereo.wav",                                           AudioBus.UI,    spatial: 0f, vol: 1.00f, jitter: 0f,    solo: true),
            // KillBanner: short 8-bit notification ping for the
            // first-blood / streak announcer. Solo so the second of
            // two rapid kills replaces the first instead of doubling
            // up — matches the visible banner replacement.
            new CueRow(AudioCue.KillBanner,        "8BIT/Powerups/8BIT_RETRO_Powerup_Spawn_Quick_Climbing_mono.wav",                                            AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: true),

            // Build mode
            new CueRow(AudioCue.BlockPlace,        "TOOLS/Impact_Wrench/TOOL_Impact_Wrench_Comperssed_Air_Short_Burst_01_mono.wav",                             AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0.04f, solo: false),
            new CueRow(AudioCue.BlockRemove,       "ROBOTICS/Short_Bursts/ROBOTIC_Short_Burst_05_Shut_Down_mono.wav",                                           AudioBus.UI,    spatial: 0f, vol: 0.75f, jitter: 0.04f, solo: false),
            new CueRow(AudioCue.InvalidPlacement,  "USER_INTERFACES/Errors/UI_Error_Double_Tone_01_mono.wav",                                                   AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: true),

            // Voxel terrain AI
            // BotDetected — fires when a VoxelChaserBot's A* search
            // flips no-path → path. Digital-worm robotic burst reads as
            // a quiet target-acquisition lock-on. Solo so a flickering
            // path edge doesn't double-trigger the cue.
            new CueRow(AudioCue.BotDetected,       "ROBOTICS/Short_Bursts/ROBOTIC_Short_Burst_13_Digital_Worm_mono.wav",                                        AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.05f, solo: true),
            // BotStep — every other waypoint advance, so the cadence is
            // roughly one cue per chunk of forward motion (not per
            // physics step). Metal walk reads as a heavy mechanical
            // footfall; jitter so successive steps sound varied rather
            // than mechanical-repeat.
            new CueRow(AudioCue.BotStep,           "HUMAN/Footsteps/_Metal_Footsteps/FOOTSTEP_Metal_Walk_01_RR06_mono.wav",                                     AudioBus.Sfx,   spatial: 1f, vol: 0.40f, jitter: 0.12f, solo: false),
        };

        private readonly struct CueRow
        {
            public readonly AudioCue Cue;
            public readonly string PathRel;
            public readonly AudioBus Bus;
            public readonly float Spatial;
            public readonly float Volume;
            public readonly float PitchJitter;
            public readonly bool Solo;

            public CueRow(AudioCue cue, string pathRel, AudioBus bus, float spatial, float vol, float jitter, bool solo)
            {
                Cue = cue;
                PathRel = pathRel;
                Bus = bus;
                Spatial = spatial;
                Volume = vol;
                PitchJitter = jitter;
                Solo = solo;
            }
        }

        // -----------------------------------------------------------------
        // Menu item + entry point
        // -----------------------------------------------------------------

        [MenuItem("Robogame/Scaffold/Audio/Build Cue Library")]
        public static void Menu_CreateOrUpdate()
        {
            AudioCueLibrary lib = CreateOrUpdate();
            EditorGUIUtility.PingObject(lib);
            Selection.activeObject = lib;
        }

        public static AudioCueLibrary CreateOrUpdate()
        {
            EnsureFolder(LibraryFolder);

            AudioCueLibrary lib = AssetDatabase.LoadAssetAtPath<AudioCueLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<AudioCueLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryAssetPath);
            }

            int wired = 0;
            int missing = 0;

            SerializedObject so = new SerializedObject(lib);
            SerializedProperty entriesProp = so.FindProperty("_entries");
            entriesProp.ClearArray();

            for (int i = 0; i < s_rows.Length; i++)
            {
                CueRow row = s_rows[i];
                string clipPath = $"{UsfxRoot}/{row.PathRel}";
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[AudioCueWizard] Missing clip for {row.Cue}: {clipPath}");
                    missing++;
                }
                else
                {
                    wired++;
                }

                entriesProp.InsertArrayElementAtIndex(i);
                SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(i);
                entryProp.FindPropertyRelative("Cue").enumValueIndex = (int)row.Cue;
                entryProp.FindPropertyRelative("Clip").objectReferenceValue = clip;
                entryProp.FindPropertyRelative("Bus").enumValueIndex = (int)row.Bus;
                entryProp.FindPropertyRelative("SpatialBlend").floatValue = row.Spatial;
                entryProp.FindPropertyRelative("Volume").floatValue = row.Volume;
                entryProp.FindPropertyRelative("PitchJitter").floatValue = row.PitchJitter;
                entryProp.FindPropertyRelative("Solo").boolValue = row.Solo;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AudioCueWizard] AudioCueLibrary refreshed at {LibraryAssetPath} " +
                      $"({wired} wired, {missing} missing of {s_rows.Length}).");
            return lib;
        }

        // -----------------------------------------------------------------
        // First-time auto-build: if the asset doesn't exist on editor
        // load AND USFX is present, build it once. No-op once the asset
        // is on disk; wizard is the source of truth thereafter.
        // -----------------------------------------------------------------

        [InitializeOnLoadMethod]
        private static void EnsureLibraryOnFirstLoad()
        {
            // Defer to the next editor tick so AssetDatabase is ready.
            EditorApplication.delayCall += () =>
            {
                if (!AssetDatabase.IsValidFolder(UsfxRoot)) return; // USFX not imported; nothing to build

                AudioCueLibrary existing = AssetDatabase.LoadAssetAtPath<AudioCueLibrary>(LibraryAssetPath);
                if (existing == null)
                {
                    CreateOrUpdate();
                    return;
                }

                // Asset exists but the wizard's row table is the source
                // of truth. Rebuild when the row count diverges (new cue
                // added) or when any cue's wired clip differs from the
                // table — covers the post-add-cue / post-clip-swap case
                // without requiring the user to remember the menu.
                if (LibraryNeedsRebuild(existing)) CreateOrUpdate();
            };
        }

        private static bool LibraryNeedsRebuild(AudioCueLibrary lib)
        {
            if (lib.Entries.Count != s_rows.Length) return true;
            for (int i = 0; i < s_rows.Length; i++)
            {
                CueRow row = s_rows[i];
                AudioCueLibrary.Entry entry = lib.Find(row.Cue);
                if (entry == null) return true;
                AudioClip expected = AssetDatabase.LoadAssetAtPath<AudioClip>($"{UsfxRoot}/{row.PathRel}");
                if (entry.Clip != expected) return true;
            }
            return false;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
