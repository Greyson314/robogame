using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Shared policy applied to the scene's single <see cref="EventSystem"/>
    /// by every HUD's <c>EnsureEventSystem</c> bootstrap.
    /// </summary>
    public static class HudEventSystem
    {
        /// <summary>
        /// Kill keyboard/gamepad UI navigation. WASD is camera movement
        /// everywhere in this game, but the default
        /// <see cref="InputSystemUIInputModule"/> binds it (via the
        /// UI/Navigate action) to walk the selection highlight across HUD
        /// buttons — which reads as random button flicker while flying
        /// the build cam (session 138 playtest). The UI is mouse-driven
        /// by design; pointer, click and scroll stay active.
        /// </summary>
        public static void DisableKeyboardNavigation(EventSystem es)
        {
            if (es == null) return;
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module != null) module.move = null;
        }
    }
}
