namespace Robogame.Core
{
    /// <summary>
    /// Catalogue of one-shot VFX bursts the project ships. Mapped to a
    /// procedural <c>ParticleSystem</c> by <see cref="VfxSpawner"/>; each
    /// kind has an opinionated palette + shape so call sites don't have
    /// to plumb colour / size knobs through every gameplay event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adding a new kind is a two-step change: add a value here, then
    /// extend <see cref="VfxSpawner.BuildKindPrefab"/> with the
    /// procedural recipe. Don't drift the recipes off the locked palette
    /// in <see cref="RuntimePalette"/> — see <c>docs/ART_DIRECTION.md</c>
    /// "Forbidden List" for what off-palette FX cost us.
    /// </para>
    /// </remarks>
    public enum VfxKind
    {
        /// <summary>Bright muzzle flash at a weapon's barrel tip on fire.</summary>
        MuzzleFlash,

        /// <summary>Tight burst of warm sparks at a projectile's hit point on a chassis or prop.</summary>
        HitSpark,

        /// <summary>Heavier ramming-impact burst — denser sparks + a small puff.</summary>
        RamSpark,

        /// <summary>Procedural shockwave + fragment ring used to augment the CFXR bomb explosion (or stand alone).</summary>
        BombShockwave,

        /// <summary>Slate-coloured dust + small cube fragments thrown when a chassis block detaches.</summary>
        DebrisDust,

        /// <summary>Bright outward kick at the chassis centre on a self-righting flip — reads as a brief mechanical pulse.</summary>
        FlipBurst,

        /// <summary>Tall rising column at a repair pad — long-lifetime palette-bright streamers, stays on while the rebuild ticks.</summary>
        RepairGlow,

        /// <summary>Tight bright pop at a single block respawning during a gradual repair.</summary>
        BlockRespawn,

        /// <summary>Warm pop at a scrap pickup — used both on drop (low scale) and on collect (full scale).</summary>
        ScrapBurst,
    }
}
