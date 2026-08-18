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
        public const string GeneratedRoot = "Assets/_Project/Audio/Generated";

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
            // ThrusterIgnite / ThrusterShutdown intentionally absent —
            // user call (session 101): accel/decel doesn't need a discrete
            // cue. ChassisWindAudio + WheelRoll + RotorSpin already cover
            // movement feedback continuously. The enum entries stay so any
            // code still calling these cues no-ops via the missing-cue
            // logger; the calls themselves are gone from ThrusterBlock.
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
            // SpringLaunch — the "boing" pop when a jump spring fires. The
            // 8-bit upward powerup climb reads as a cartoon spring release;
            // 3D / Sfx so it localises to the spring on the chassis. Not solo
            // (a multi-spring bot fires several at once on one Space tap) and
            // jittered so the stack doesn't sound like one mechanical note.
            new CueRow(AudioCue.SpringLaunch,      "8BIT/Powerups/8BIT_RETRO_Powerup_Spawn_Quick_Climbing_mono.wav",                                            AudioBus.Sfx,   spatial: 1f, vol: 0.35f, jitter: 0.08f, solo: false),
            // WingFlapLoop — looped while a Wing block's flap animation
            // plays (arena only; WingFlapAnimator starts/stops it with the
            // clip). Canvas-in-wind reads as the linen membrane sculling
            // air. Quiet — it's ambience under the flap visual, not a
            // motor. Not solo: each wing carries its own loop and a
            // multi-wing bat build should sound fuller than a one-wing
            // one. Pitch jitter breaks phase-lock between paired wings.
            new CueRow(AudioCue.WingFlapLoop,      "FABRIC_CLOTHING/FABRIC_Flag_or_Fabric_in_Wind_loop_mono.wav",                                               AudioBus.Sfx,   spatial: 1f, vol: 0.35f, jitter: 0.06f, solo: false),

            // Weapon ammo / reload — subtle player-state cues. The user
            // (session 101) asked for sounds on running-out-of-ammo and
            // reload-completing specifically; ReloadStart intentionally
            // stays unwired (logs missing-cue once, no spam) since
            // pressing fire on an empty pool already produces WeaponEmpty,
            // which is the player's "oh, reload starting" tell.
            // ReloadComplete: two-tone clean-up beep at low vol = "ready
            // to fire again" affirmation. Solo so a multi-pool chassis
            // reload finishing at near-identical times doesn't double-up.
            new CueRow(AudioCue.ReloadComplete,    "USER_INTERFACES/Beeps/UI_Beep_Double_Clean_Up_stereo.wav",                                                 AudioBus.UI,    spatial: 0f, vol: 0.30f, jitter: 0f,    solo: true),
            // WeaponEmpty: muffled short error tone, the "dry click" of
            // an empty mag. 3D / Sfx because it fires at the firing
            // block's world position (BombBay / Cannon / ProjectileGun
            // all call PlayOneShot with their transform.position).
            new CueRow(AudioCue.WeaponEmpty,       "USER_INTERFACES/Errors/UI_Error_Double_Note_Down_Muffled_Short_stereo.wav",                                AudioBus.Sfx,   spatial: 1f, vol: 0.35f, jitter: 0.04f, solo: true),

            // Active module (session 101)
            // ModuleActivate: a punchy powered "ka-chunk" when the ability
            // fires. 3D / Sfx — played at the chassis world position so it
            // localises to the bot that triggered it.
            new CueRow(AudioCue.ModuleActivate,    "8BIT/Powerups/8BIT_RETRO_Powerup_Spawn_Quick_Climbing_mono.wav",                                            AudioBus.Sfx,   spatial: 1f, vol: 0.90f, jitter: 0.04f, solo: true),
            // ModuleReady: subtle two-tone "recharged" chirp. UI bus —
            // it's feedback to the local player, not a world event.
            new CueRow(AudioCue.ModuleReady,       "USER_INTERFACES/Beeps/UI_Beep_Double_Clean_Up_stereo.wav",                                                 AudioBus.UI,    spatial: 0f, vol: 0.30f, jitter: 0f,    solo: true),
            // SmokeDeploy (session 105): pressurised "fwoomp" as the cloud
            // erupts. Compressed-air burst, 3D at the bot.
            new CueRow(AudioCue.SmokeDeploy,       "TOOLS/Impact_Wrench/TOOL_Impact_Wrench_Comperssed_Air_Short_Burst_01_mono.wav",                            AudioBus.Sfx,   spatial: 1f, vol: 0.75f, jitter: 0.06f, solo: false),
            // Cloak (session 105): digital "phase" sweep on cloak engage /
            // disengage. Robotic air-lock burst reads as a stealth field.
            new CueRow(AudioCue.Cloak,             "ROBOTICS/Short_Bursts/ROBOTIC_Short_Burst_12_Digital_Air_Lock_mono.wav",                                    AudioBus.Sfx,   spatial: 1f, vol: 0.70f, jitter: 0.05f, solo: false),

            // UI / match
            new CueRow(AudioCue.UiHover,           "USER_INTERFACES/Beeps/UI_Beep_Bend_Short_stereo.wav",                                                       AudioBus.UI,    spatial: 0f, vol: 0.40f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.UiClick,           "USER_INTERFACES/Clicks_Taps/UI_Click_Metallic_Bright_mono.wav",                                             AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: false),
            new CueRow(AudioCue.UiBack,            "USER_INTERFACES/Clicks_Taps/UI_Click_TapBack_01_mono.wav",                                                  AudioBus.UI,    spatial: 0f, vol: 0.85f, jitter: 0f,    solo: false),
            // Ink & Motion UI kit (session 164). Placeholder pack voices —
            // the D-minor re-voice rides the composite layer in UiCues
            // (piano flourish, timpani page-land) and the owed ear pass.
            new CueRow(AudioCue.UiToggleOn,        "MECHANICS/MECHANICS_Metal_Mechanism_01_mono.wav",                                                           AudioBus.UI,    spatial: 0f, vol: 0.50f, jitter: 0.04f, solo: false),
            new CueRow(AudioCue.UiToggleOff,       "MECHANICS/MECHANICS_Metal_Mechanism_03_mono.wav",                                                           AudioBus.UI,    spatial: 0f, vol: 0.45f, jitter: 0.04f, solo: false),
            new CueRow(AudioCue.UiSlideTick,       "MECHANICS/MECHANICS_Metal_Mechanism_08_mono.wav",                                                           AudioBus.UI,    spatial: 0f, vol: 0.28f, jitter: 0.10f, solo: false),
            new CueRow(AudioCue.UiSealStamp,       "THUDS_THUMPS/THUD_Smooth_01_mono.wav",                                                                      AudioBus.UI,    spatial: 0f, vol: 0.65f, jitter: 0.05f, solo: false),
            new CueRow(AudioCue.UiPageTurn,        "WHOOSHES/Air/WHOOSH_Air_Slow_RR1_mono.wav",                                                                 AudioBus.UI,    spatial: 0f, vol: 0.55f, jitter: 0f,    solo: true),
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

            // Musical damage stingers (ADR-0006). Generated placeholder
            // clips (see docs/subsystems/music.md) rooted at the global
            // scale root — Assets/-rooted paths bypass the USFX root.
            // Music bus + 2D; jitter must stay 0 (the director supplies
            // explicit pentatonic pitch). Phrases are solo so a double
            // kill replaces the fanfare instead of doubling it.
            new CueRow(AudioCue.StingerPluckNote,       GeneratedRoot + "/stinger_pluck_note.wav",       AudioBus.Music, spatial: 0f, vol: 0.60f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerPluckFlourish,   GeneratedRoot + "/stinger_pluck_flourish.wav",   AudioBus.Music, spatial: 0f, vol: 0.70f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerPluckPhrase,     GeneratedRoot + "/stinger_pluck_phrase.wav",     AudioBus.Music, spatial: 0f, vol: 0.85f, jitter: 0f, solo: true),
            new CueRow(AudioCue.StingerBrassNote,       GeneratedRoot + "/stinger_brass_note.wav",       AudioBus.Music, spatial: 0f, vol: 0.60f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerBrassFlourish,   GeneratedRoot + "/stinger_brass_flourish.wav",   AudioBus.Music, spatial: 0f, vol: 0.70f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerBrassPhrase,     GeneratedRoot + "/stinger_brass_phrase.wav",     AudioBus.Music, spatial: 0f, vol: 0.85f, jitter: 0f, solo: true),
            new CueRow(AudioCue.StingerPianoNote,       GeneratedRoot + "/stinger_piano_note.wav",       AudioBus.Music, spatial: 0f, vol: 0.60f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerPianoFlourish,   GeneratedRoot + "/stinger_piano_flourish.wav",   AudioBus.Music, spatial: 0f, vol: 0.70f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerPianoPhrase,     GeneratedRoot + "/stinger_piano_phrase.wav",     AudioBus.Music, spatial: 0f, vol: 0.85f, jitter: 0f, solo: true),
            new CueRow(AudioCue.StingerTimpaniNote,     GeneratedRoot + "/stinger_timpani_note.wav",     AudioBus.Music, spatial: 0f, vol: 0.65f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerTimpaniFlourish, GeneratedRoot + "/stinger_timpani_flourish.wav", AudioBus.Music, spatial: 0f, vol: 0.75f, jitter: 0f, solo: false),
            new CueRow(AudioCue.StingerTimpaniPhrase,   GeneratedRoot + "/stinger_timpani_phrase.wav",   AudioBus.Music, spatial: 0f, vol: 0.85f, jitter: 0f, solo: true),

            // Wave-1 FX/audio pass (invariant #8 debt from session 155).
            // WeaponOverheat: the SMG's 4s-sustain lockout tripping — a
            // compressed-air pressure release reads as steam venting off
            // hot metal. Solo: one gun trips at a time per chassis and a
            // double-trip should replace, not stack.
            new CueRow(AudioCue.WeaponOverheat,    "TOOLS/Impact_Wrench/TOOL_Impact_Wrench_Comperssed_Air_Medium_Burst_No_Resistance_01_mono.wav",             AudioBus.Sfx,   spatial: 1f, vol: 0.70f, jitter: 0.05f, solo: true),
            // GyroLoop: quiet electric hum of the always-spinning reaction
            // wheel. GyroBlock swells the volume with steer input via
            // SetBaseVolume, so the library vol is the FULL-steer ceiling.
            // Zero jitter — it's a loop; not solo, each gyro hums.
            new CueRow(AudioCue.GyroLoop,          "MACHINES/Factory/MACHINE_Press_Machine_Electric_Hum_loop_mono.wav",                                        AudioBus.Sfx,   spatial: 1f, vol: 0.30f, jitter: 0f,    solo: false),
            // PogoBounce: actual cartoon spring bounce for the pogo's
            // contact-fire hop (the SpringLaunch 8-bit powerup was the
            // session-155 placeholder). Chunky jitter — sustained hopping
            // is the block's whole identity and must not read as a stuck
            // note. Not solo: the arbiter already caps one bounce per
            // chassis window; simultaneous bounces from DIFFERENT bots
            // should stack naturally.
            new CueRow(AudioCue.PogoBounce,        "CARTOON/CARTOON_Spring_Bounce_01_mono.wav",                                                                 AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.12f, solo: false),
            // ArmorSpikeHit: enemy spike procs its ring-0 ram bonus — a
            // hard metal-on-metal jab layered over the ChassisRam thud so
            // being spiked sounds distinctly nastier than a plain ram.
            new CueRow(AudioCue.ArmorSpikeHit,     "IMPACTS/Metal/IMPACT_Metal_Crowbar_Hard_Surface_mono.wav",                                                  AudioBus.Sfx,   spatial: 1f, vol: 0.90f, jitter: 0.08f, solo: false),
            // ArmorDeflect: wedge sheds a glancing projectile — classic
            // bright ricochet whine. High jitter so sustained SMG fire
            // skating off a wedge sparkles instead of machine-gunning
            // one identical ping.
            new CueRow(AudioCue.ArmorDeflect,      "WEAPONS/Firearms/Ricochets/RICOCHET_Bullet_01_mono.wav",                                                    AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.15f, solo: false),

            // -----------------------------------------------------------
            // Unwired-cue audit (session 162 follow-up). These 13 cues
            // were fired from gameplay code but had no rows, so they
            // silently no-oped. Only ThrusterIgnite/Shutdown and
            // ReloadStart (documented above) are intentional omissions.
            // First-pass filename-picked clips — tune by ear in a live
            // session.
            // -----------------------------------------------------------
            // HoverBladeLoop: low fan whoosh under a lifting blade.
            // HoverBladeBlock drives volume itself via SetBaseVolume
            // (starts at 0, ramps with lift), so the row vol is only the
            // pre-ramp default. Per-blade loops, not solo.
            new CueRow(AudioCue.HoverBladeLoop,        "MACHINES/Industrial/MACHINE_Industrial_Fan_Run_Medium_loop_mono.wav",                                   AudioBus.Sfx,   spatial: 1f, vol: 0.45f, jitter: 0f,    solo: false),
            // HoverBladeContactLost: ground dropped out from under the
            // blade — a short shut-down burst reads as the lift dying.
            new CueRow(AudioCue.HoverBladeContactLost, "ROBOTICS/Short_Bursts/ROBOTIC_Short_Burst_05_Shut_Down_mono.wav",                                       AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.05f, solo: true),
            // FlipActivate: the self-righting "schwop" — one fast air whoosh.
            new CueRow(AudioCue.FlipActivate,          "WHOOSHES/Air/WHOOSH_Air_Fast_Bright_RR1_mono.wav",                                                      AudioBus.Sfx,   spatial: 1f, vol: 0.60f, jitter: 0.08f, solo: false),
            // Repair pad: enter = 1s rising charge ("field engages"),
            // cancel = 500ms falling charge (de-energise), complete =
            // bright 8-bit notification chime, per-block respawn = soft
            // dark pop (jittered — it fires once per rebuilt cell).
            new CueRow(AudioCue.RepairPadEnter,        "CHARGE_UPS_DOWNS/CHARGE_Complex_Wet_12_Semi_Up_1000ms_mono.wav",                                        AudioBus.Sfx,   spatial: 1f, vol: 0.60f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.RepairBlockRespawn,    "CARTOON/POP_Mouth_Darker_mono.wav",                                                                     AudioBus.Sfx,   spatial: 1f, vol: 0.30f, jitter: 0.10f, solo: false),
            new CueRow(AudioCue.RepairComplete,        "NOTIFICATIONS/NOTIFICATION_8bit_01_mono.wav",                                                           AudioBus.Sfx,   spatial: 1f, vol: 0.55f, jitter: 0f,    solo: true),
            new CueRow(AudioCue.RepairCancel,          "CHARGE_UPS_DOWNS/CHARGE_Complex_Wet_12_Semi_Down_500ms_mono.wav",                                       AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0f,    solo: true),
            // Scrap loop: drop = coin clink, collect = bright two-note
            // pickup, depot tick = quiet dry tap (the metronome pulse).
            new CueRow(AudioCue.ScrapDrop,             "MONEY_CASH_CURRENCY/SLOT_MACHINE_Insert_Coin_01_mono.wav",                                              AudioBus.Sfx,   spatial: 1f, vol: 0.50f, jitter: 0.10f, solo: false),
            new CueRow(AudioCue.ScrapCollect,          "8BIT/Coin_Collect/8BIT_RETRO_Coin_Collect_Two_Note_Bright_Fast_mono.wav",                               AudioBus.Sfx,   spatial: 1f, vol: 0.60f, jitter: 0.05f, solo: false),
            new CueRow(AudioCue.ScrapTick,             "USER_INTERFACES/Clicks_Taps/UI_Click_TapBack_01_mono.wav",                                              AudioBus.Sfx,   spatial: 1f, vol: 0.25f, jitter: 0f,    solo: true),
            // LabSave: soft confirmation chime at the Lab bench (3D — it
            // plays at the bench's world position).
            new CueRow(AudioCue.LabSave,               "USER_INTERFACES/Beeps/UI_Beep_Double_Clean_Up_stereo.wav",                                              AudioBus.Sfx,   spatial: 1f, vol: 0.40f, jitter: 0f,    solo: true),
            // LowHealthAlert: fired repeatedly by LowHealthVignetteHud as
            // discrete PlayUI pulses (not a PlayLoop) — a muffled low
            // double-note at whisper volume. Solo so overlapping pulses
            // replace instead of stacking.
            new CueRow(AudioCue.LowHealthAlert,        "USER_INTERFACES/Errors/UI_Error_Double_Note_Down_Muffled_Short_stereo.wav",                             AudioBus.UI,    spatial: 0f, vol: 0.25f, jitter: 0f,    solo: true),
            // RoundClockTick: final-10s countdown — a real clock
            // mechanism tick, dry and urgent without being an alarm.
            new CueRow(AudioCue.RoundClockTick,        "FOLEY/CLOCKS/CLOCK_Grandfather_Clock_01_RR01_mono.wav",                                                 AudioBus.UI,    spatial: 0f, vol: 0.50f, jitter: 0f,    solo: true),
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
                // Assets/-rooted rows (generated music clips) resolve as-is;
                // everything else is USFX-relative.
                string clipPath = row.PathRel.StartsWith("Assets/")
                    ? row.PathRel
                    : $"{UsfxRoot}/{row.PathRel}";
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
                string clipPath = row.PathRel.StartsWith("Assets/")
                    ? row.PathRel
                    : $"{UsfxRoot}/{row.PathRel}";
                AudioClip expected = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
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
