namespace Robogame.Block
{
    /// <summary>
    /// Opt-out contract for block components that are built to keep
    /// running after their block is detached from the chassis as debris.
    /// </summary>
    /// <remarks>
    /// Default policy (enforced by <c>Robot.DetachAsDebris</c>): every
    /// MonoBehaviour on a detached block is disabled, because block
    /// gameplay components cache the chassis Rigidbody and keep acting on
    /// the LIVE chassis from the ground (a detached pogo foot kept
    /// claiming the chassis's bounce window and kicking it for the whole
    /// debris lifetime — spring-cleaning review finding). A component
    /// that genuinely handles detached life (e.g. <c>RopeBlock</c>, which
    /// rebuilds its chain against the debris body on reparent) implements
    /// this interface to stay enabled; the callback fires at detach time.
    /// </remarks>
    public interface IDetachAware
    {
        /// <summary>Called once when the owning block becomes debris.</summary>
        void OnDetachedAsDebris();
    }
}
