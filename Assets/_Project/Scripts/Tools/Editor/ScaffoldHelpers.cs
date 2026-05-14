using Robogame.Player;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor utility shared by <see cref="SceneScaffolder"/> and
    /// <see cref="GameplayScaffolder"/>. Session 62 stripped this down
    /// to a single helper — every other method that used to live here
    /// (component-ensure, tuning-assign, input wiring, binder ensure)
    /// was deleted alongside its only caller, and
    /// <see cref="Gameplay.ChassisAssembler"/> carries private versions
    /// for the runtime path.
    /// </summary>
    internal static class ScaffoldHelpers
    {
        /// <summary>
        /// Destroy every player-controlled chassis EXCEPT the one named
        /// <paramref name="keepName"/>. Keeps the "one player at a time"
        /// invariant true across consecutive scaffold runs.
        /// </summary>
        public static void ClearPlayerChassis(string keepName)
        {
            PlayerController[] all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            foreach (PlayerController pc in all)
            {
                if (pc == null) continue;
                if (pc.gameObject.name == keepName) continue;
                Object.DestroyImmediate(pc.gameObject);
            }
            // Stale loose cube from a long-retired BuildSimpleGarage path.
            GameObject legacyPlayer = GameObject.Find("Player");
            if (legacyPlayer != null && legacyPlayer.name != keepName)
            {
                Object.DestroyImmediate(legacyPlayer);
            }
        }
    }
}
