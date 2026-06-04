namespace Robogame.Combat
{
    /// <summary>
    /// The stats every ammo-gated weapon definition exposes (ADR-0003 phase A).
    /// Implemented by <see cref="WeaponStatsDefinition"/> (and therefore the
    /// four concrete <c>*Definition</c> assets). Consumers that only need the
    /// shared surface — <see cref="WeaponAmmoState"/>'s pool sizing, the ammo
    /// registry — depend on this instead of casting to each concrete type.
    /// </summary>
    /// <remarks>
    /// A block is an ammo weapon iff its
    /// <see cref="Robogame.Block.BlockDefinition.ComponentData"/> implements
    /// this interface — the marker that replaces the hand-synced weapon-id
    /// list. Opting a new weapon in is local: author a definition that derives
    /// from <see cref="WeaponStatsDefinition"/>.
    /// </remarks>
    public interface IWeaponStats
    {
        /// <summary>Seconds between shots while fire is held.</summary>
        float FireInterval { get; }

        /// <summary>Headline damage (HP).</summary>
        float Damage { get; }

        /// <summary>Newton-seconds imparted to the target along the hit direction.</summary>
        float KnockbackImpulse { get; }

        /// <summary>Rounds per clip per weapon instance.</summary>
        int ClipSize { get; }

        /// <summary>Seconds the weapon-type pool is locked during reload.</summary>
        float ReloadDuration { get; }

        /// <summary>Grace window after firing the last round before auto-reload.</summary>
        float AutoReloadDelay { get; }
    }
}
