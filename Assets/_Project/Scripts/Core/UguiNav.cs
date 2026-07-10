using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// UGUI input-policy helpers. The keyboard-navigation kill switch
    /// lives in <c>Robogame.Gameplay.HudEventSystem</c> (it needs
    /// Unity.InputSystem, which Core deliberately doesn't reference).
    /// </summary>
    public static class UguiNav
    {
        /// <summary>
        /// True while a text field owns the keyboard. The old
        /// "<c>currentSelectedGameObject != null</c>" guards were too
        /// broad — a clicked button or dragged slider stays selected
        /// forever, permanently eating hotkeys (T, R) until something
        /// else got clicked.
        /// </summary>
        public static bool IsTextInputFocused()
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            return es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }
    }
}
