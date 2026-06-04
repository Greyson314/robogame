namespace Robogame.Combat
{
    /// <summary>
    /// Marker for a firer component the server disables on every non-server
    /// client (NGO Phase 1 — clients must not spawn authoritative projectiles
    /// or compute hits). <c>NetworkRobotCombat</c> silences via a single
    /// <c>GetComponentsInChildren&lt;IClientSilenceable&gt;</c> walk, so a new
    /// weapon opts in locally by implementing this — no central list to keep
    /// in sync (ADR-0003 phase B).
    /// </summary>
    /// <remarks>
    /// Fixes audit #4: the hand-synced silence loop enumerated
    /// <c>ProjectileGun</c> / <c>CannonBlock</c> / <c>BombBayBlock</c> and
    /// missed <c>MortarBlock</c> and <c>GrappleMagnetBlock</c>, so a client
    /// could still drive those two locally. Implemented by every firer; the
    /// marker is empty because the silence action (disable the
    /// <see cref="UnityEngine.Behaviour"/>) is uniform.
    /// </remarks>
    public interface IClientSilenceable { }
}
