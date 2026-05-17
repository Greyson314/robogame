using Robogame.Robots;

namespace Robogame.Player
{
    /// <summary>
    /// Decides whether a robot's chassis is "locally relevant" and so
    /// should render the ink-line outline. The SP implementation
    /// (inside <see cref="OutlineRelevanceManager"/>) = local player's
    /// own chassis OR the player's current aim target. The seam exists
    /// so MP can later swap in a source that reads a server-pushed
    /// "you are targeting this robot" hint without touching the manager
    /// or the per-chassis controller.
    /// </summary>
    public interface IRelevanceSource
    {
        bool IsRelevant(Robot robot);
    }
}
