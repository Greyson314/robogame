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

        // Repair pad
        RepairPadEnter,    // chassis crosses into the pad's trigger volume — the "field engages" tone
        RepairBlockRespawn, // per-block during the gradual rebuild — soft pop as a missing cell returns
        RepairComplete,    // rebuild finishes, chassis at full HP again — chime
        RepairCancel,      // chassis leaves the pad mid-rebuild — short de-energising blip

        // Scrap pickups (post-kill collectibles)
        ScrapDrop,         // a scrap pickup spawns from a destroyed chassis — short clink
        ScrapCollect,      // a chassis drives over a scrap pickup — bright pickup chime
        ScrapTick,         // depot ticks one scrap into team score — quiet metronome pulse

        // Weapon ammo / reload
        ReloadStart,       // weapon-pool reload begins — mechanical click / spinner
        ReloadComplete,    // weapon-pool reload finishes — pool refilled
        WeaponEmpty,       // attempted fire on an empty pool — dry click

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
    }
}
