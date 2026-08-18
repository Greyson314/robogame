namespace Robogame.Core
{
    /// <summary>
    /// Catalogue of audio events the project can play. Each cue is a
    /// stable identifier that game code uses to fire audio without
    /// holding a reference to a clip — the clip + bus + spatialisation
    /// live on an <c>AudioCueDefinition</c> that lookups by enum name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why an enum + a side table, not direct AudioClip references?</b>
    /// Same reason BlockIds is a string and not a BlockDefinition asset
    /// reference (BEST_PRACTICES § 10.2): keeps gameplay code free of
    /// asset dependencies, lets clips swap without touching every
    /// caller, and keeps save / netcode formats portable.
    /// </para>
    /// <para>
    /// Adding a cue is a one-line change here, plus an authored entry
    /// in the AudioCue library asset. Until clips are authored, calls
    /// for an unmapped cue are a no-op (logged once per cue at warning
    /// level so missing audio is visible without spamming).
    /// </para>
    /// </remarks>
    public enum AudioCue
    {
        // Combat
        WeaponFire,
        WeaponFireCannon,  // pirate cannon — single deep boom
        ProjectileImpact,
        BlockDamaged,
        BlockDestroyed,
        ChassisRam,
        TipImpact,         // hook / mace tip striking a target — the "thonk" of a swung weapon
        BombExplosion,
        DrillContact,      // drill tip actually carving SDF cells — sharp "biting" cue, fires when changed > 0
        DrillActive,       // looped while the drill's fire input is held — quiet motorised spin under the contact cue

        // Movement
        ThrusterIgnite,
        ThrusterShutdown,
        WheelRoll,         // looped while grounded + rolling
        RotorSpin,         // looped while rotor active (bare rotor — tail rotor / decorative spinner)
        PropellerLoop,     // looped on a rotor with adopted foils — i.e. an actual propeller producing lift
        WindLoop,          // passive wind, scales with chassis speed — the "rushing past your ears" cue
        WaterSplash,
        FlipActivate,      // snap-rotate self-righting kick — single short "schwop" cue
        HoverBladeLoop,    // looped while a hover blade is producing lift — low whoosh, modulated by lift magnitude
        HoverBladeContactLost, // one-shot when a hover blade's ground raycast goes from hitting → missing (cliff-edge or terraformed pit)
        SpringLaunch,      // one-shot when a spring block fires its jump impulse — sharp percussive "boing" / launch pop
        WingFlapLoop,      // looped while a Wing block's flap animation plays (arena only) — soft rhythmic canvas whoosh

        // Repair pad
        RepairPadEnter,    // chassis crosses into the pad's trigger volume — the "field engages" tone
        RepairBlockRespawn, // per-block during the gradual rebuild — soft pop as a missing cell returns
        RepairComplete,    // rebuild finishes, chassis at full HP again — chime
        RepairCancel,      // chassis leaves the pad mid-rebuild — short de-energising blip

        // Scrap pickups (post-kill collectibles)
        ScrapDrop,         // a scrap pickup spawns from a destroyed chassis — short clink
        ScrapCollect,      // a chassis drives over a scrap pickup — bright pickup chime
        ScrapTick,         // depot ticks one scrap into team score — quiet metronome pulse

        // Laboratory
        LabSave,           // a concoction is saved in the garage Lab — soft confirmation chime (ADR-0004)

        // Weapon ammo / reload
        ReloadStart,       // weapon-pool reload begins — mechanical click / spinner
        ReloadComplete,    // weapon-pool reload finishes — pool refilled
        WeaponEmpty,       // attempted fire on an empty pool — dry click

        // Player state
        LowHealthAlert,    // looped pulse while local chassis HP < ~30% — the "you're about to die" warning

        // Modules (session 101 / 105)
        ModuleActivate,    // a world-mutating module fires (EMP / Blink / Shield) — punchy "ka-chunk" trigger
        ModuleReady,       // a module cooldown finishes — subtle "ready" chirp
        SmokeDeploy,       // Smoke module deploys its cloud — soft pressurised hiss / "fwoomp"
        Cloak,             // Invisibility module engages or disengages — shimmery digital "phase" sweep

        // UI / match
        UiHover,
        UiClick,
        UiBack,
        MatchStart,
        MatchEndVictory,
        MatchEndDefeat,
        MatchEndDraw,
        KillBanner,        // first-blood + streak announcer ping

        // Build mode
        BlockPlace,
        BlockRemove,
        InvalidPlacement,

        // Voxel terrain AI
        BotDetected,       // a VoxelChaserBot acquires a fresh A* path to its target — quiet "I see you" tone
        BotStep,           // a VoxelChaserBot advances a waypoint — soft mechanical footfall

        // -------------------------------------------------------------
        // Append-only past this line. AudioCueLibrary.asset serialises
        // Entry.Cue by enum int value — inserting mid-enum silently
        // remaps every authored row below the insertion point.
        // -------------------------------------------------------------
        RoundClockTick,    // one tick per displayed second during the final-10s round countdown — short dry click, urgency without alarm

        // Musical damage stingers (ADR-0006). One instrument per
        // ProjectileKind, three intensity tiers each. Clips are recorded
        // at the global scale root; MusicalHitDirector supplies explicit
        // pentatonic pitch at play time, so Phrase stays None and
        // jitter 0 on all of these.
        StingerPluckNote,       // SMG — pizzicato pluck
        StingerPluckFlourish,
        StingerPluckPhrase,
        StingerBrassNote,       // cannon — bright brass stab
        StingerBrassFlourish,
        StingerBrassPhrase,
        StingerPianoNote,       // mortar — the inventor-doc piano
        StingerPianoFlourish,
        StingerPianoPhrase,
        StingerTimpaniNote,     // bomb — deep timpani boom
        StingerTimpaniFlourish,
        StingerTimpaniPhrase,

        WeaponOverheat,    // SMG heat lockout trips (session 155) — pressure-release hiss / hot metal tick

        // Wave-1 FX/audio pass (invariant #8 debt from session 155)
        GyroLoop,          // looped electric flywheel hum while a gyro block is live — volume swells with steer input
        PogoBounce,        // one-shot cartoon spring bounce when the winning pogo foot fires — replaces the SpringLaunch placeholder
        ArmorSpikeHit,     // enemy spike armor procs its ram bonus — brutal metal jab on top of the ram thud
        ArmorDeflect,      // wedge armor deflects a glancing projectile — bright ricochet ping

        // Ink & Motion UI kit (session 164). UiConfirm has no row here on
        // purpose — the Begin flourish is a pitched composite of
        // StingerPianoNote, built in UiCues.Confirm().
        UiToggleOn,        // settings toggle commits ON — mechanism clack, up-voiced
        UiToggleOff,       // settings toggle commits OFF — mechanism clack, down-voiced
        UiSlideTick,       // slider crosses a ruler division — dry ratchet tick, rate-capped by the caller
        UiSealStamp,       // wax-seal checkbox / kill-feed splat lands — soft stamp thud (UiCues.Seal adds the piano D under it)
        UiPageTurn,        // full-screen ink wipe launches — brush swish (UiCues.PageTurnLand adds the timpani touch)
    }
}
