using Robogame.Core;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// "Can this held-fire weapon fire this tick?" — the cooldown + ammo +
    /// empty-click gate shared by <see cref="ProjectileGun"/>,
    /// <see cref="CannonBlock"/>, <see cref="MortarBlock"/> and
    /// <see cref="BombBayBlock"/> (ADR-0003 phase D). The grapple is a
    /// one-shot state machine and does not use this.
    /// </summary>
    /// <remarks>
    /// A mutable struct held as a field on each firer — no allocation, and the
    /// cooldown / dry-click timestamps live with the weapon instance. Call
    /// <see cref="TryFire"/> once per <c>Update</c>; on a true return the
    /// caller spends the round and fires (the cooldown is armed inside on the
    /// accepting tick). The per-weapon fire interval is passed already floored
    /// by the caller, because the floor differs per weapon (SMG derives it from
    /// a fire-rate; cannon/mortar/bomb clamp an authored interval).
    /// </remarks>
    public struct WeaponFireGate
    {
        private float _nextFireTime;
        private float _nextEmptyClickTime;

        /// <summary>
        /// Returns true when the weapon may fire now. Handles the cooldown, the
        /// ammo gate, and a throttled dry-click cue on empty. On a true return
        /// the cooldown is armed and one round is consumed from
        /// <paramref name="ammo"/> (when present); the caller then fires.
        /// </summary>
        /// <param name="fireHeld">The firer's input — fire button held.</param>
        /// <param name="now"><c>Time.time</c> at the call site.</param>
        /// <param name="fireInterval">Seconds between shots, already floored by the caller.</param>
        /// <param name="ammo">Per-chassis ammo state, or null when the weapon is ungated.</param>
        /// <param name="blockId">The firing block's definition id (ammo pool key), or null.</param>
        /// <param name="emptyClickPos">World position for the dry-click cue.</param>
        /// <param name="emptyClickThrottle">Min seconds between dry-click cues.</param>
        public bool TryFire(bool fireHeld, float now, float fireInterval,
                            WeaponAmmoState ammo, string blockId,
                            Vector3 emptyClickPos, float emptyClickThrottle)
        {
            if (!fireHeld) return false;
            if (now < _nextFireTime) return false;

            // Ammo gate. Pool-empty plays a throttled dry-click so the player
            // gets feedback that their held trigger isn't firing.
            if (ammo != null && blockId != null && !ammo.CanFire(blockId))
            {
                if (now >= _nextEmptyClickTime)
                {
                    AudioRouter.PlayOneShot(AudioCue.WeaponEmpty, emptyClickPos);
                    _nextEmptyClickTime = now + emptyClickThrottle;
                }
                return false;
            }

            _nextFireTime = now + fireInterval;
            // Consume the round before firing — Consume is idempotent against
            // an already-empty pool, so the gate above and this can't both fail
            // in the same tick.
            if (ammo != null && blockId != null) ammo.Consume(blockId, 1);
            return true;
        }
    }
}
