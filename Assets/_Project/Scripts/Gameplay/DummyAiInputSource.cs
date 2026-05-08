using System;

namespace Robogame.Gameplay
{
    /// <summary>
    /// <b>Deprecated.</b> Use <see cref="GroundBotInputSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Session 28 (Pillar 1) renamed the dev-only "stationary tank dummy"
    /// AI into the production-grade <see cref="GroundBotInputSource"/>
    /// (with Patrol / Engage / Retreat / Dead states). This class survives
    /// only as a thin subclass so any prefab / scene / asset that still
    /// references the old type GUID keeps loading without errors. Behaviour
    /// is now identical to <see cref="GroundBotInputSource"/>.
    /// </para>
    /// <para>
    /// Delete this file in a follow-up session once we've confirmed nothing
    /// in the project references it. The compiler warning surfaces every
    /// remaining call site.
    /// </para>
    /// </remarks>
    [Obsolete("DummyAiInputSource has been replaced by GroundBotInputSource. " +
              "This subclass remains only for legacy scene / prefab GUID compatibility.")]
    public sealed class DummyAiInputSource : GroundBotInputSource
    {
    }
}
