using UnityEngine;

namespace Robogame.Input
{
    /// <summary>
    /// Abstract input source for any controller (player, AI, replay, network).
    /// </summary>
    /// <remarks>
    /// The contract is intentionally minimal. Anything driving a robot
    /// should be expressible as: a planar move vector, a vertical scalar,
    /// and a few discrete action flags. AI bots will implement this exactly
    /// the same way as the player.
    /// </remarks>
    public interface IInputSource
    {
        /// <summary>Normalised planar movement. <c>x</c> = strafe/turn, <c>y</c> = forward/back.</summary>
        Vector2 Move { get; }

        /// <summary>Mouse delta or right-stick look. <c>x</c> = yaw, <c>y</c> = pitch.</summary>
        Vector2 Look { get; }

        /// <summary>Vertical intent in [-1, 1] (jump / jet thrust / dive).</summary>
        float Vertical { get; }

        /// <summary>True while the primary fire button is held.</summary>
        bool FireHeld { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// fire button. Edge-triggered companion to <see cref="FireHeld"/>.
        /// Consumed by single-shot weapons whose firing cadence is
        /// player-pace rather than a fire-rate timer — notably the
        /// grapple magnet (fire once → wait for retract → fire again).
        /// Bots: stub to false until they author single-shot logic.
        /// </summary>
        bool FirePressed { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// reload key. Consumed by <c>WeaponAmmoState</c> to start a
        /// manual reload on every non-full weapon pool. Bots return false
        /// — they rely on auto-reload-on-empty.
        /// </summary>
        bool ReloadPressed { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// ability key for module slot <paramref name="slot"/> (0→Q, 1→W,
        /// 2→E, 3→R). Consumed by the chassis-root <c>ModuleSystem</c> to fire
        /// the module in that slot when off cooldown and available. Out-of-range
        /// indices return false. Bots stub to false until they author module
        /// logic.
        /// </summary>
        bool GetModulePressed(int slot);

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// self-right flip key (H). Consumed by <c>FlipController</c>,
        /// which owns the cooldown + rotate. Routed through the input
        /// source (not a local keyboard poll) so the verb rides the
        /// netcode input command like every other action. Bots stub to
        /// false.
        /// </summary>
        bool FlipPressed { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// grapple-release key (R — deliberately shared with
        /// <see cref="ReloadPressed"/>). Consumed by
        /// <c>RobotHookReleaseInput</c> to release every grappled hook on
        /// the chassis. Bots stub to false.
        /// </summary>
        bool HookReleasePressed { get; }
    }

    /// <summary>
    /// Implemented by delegating input sources (e.g. the netcode's
    /// <c>NetworkInputSource</c>) so consumers that care WHO is behind
    /// the input — not just its values — can unwrap to the inner source.
    /// <c>WeaponAmmoState</c> uses this to keep its "is this the local
    /// player's chassis" audio gate working once ownership wraps the
    /// live <c>PlayerInputHandler</c>.
    /// </summary>
    public interface IInputSourceWrapper
    {
        /// <summary>The wrapped source, or null when none is bound.</summary>
        IInputSource InnerSource { get; }
    }
}
