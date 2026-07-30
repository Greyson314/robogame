using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Robogame.Core
{
    /// <summary>
    /// Single source of truth for two questions every mouse-consuming
    /// system keeps answering independently: "is the pointer over
    /// interactive HUD?" and "is a modal screen open?".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before this class, cursor capture (<c>FollowCamera</c>), fire
    /// suppression (<c>PlayerInputHandler</c>) and the build-camera drag
    /// gates (<c>OrbitCamera</c>) each called
    /// <c>EventSystem.IsPointerOverGameObject()</c> independently
    /// (session 128). IMGUI overlays are invisible to the EventSystem;
    /// any interactive IMGUI screen must claim suppression via
    /// <see cref="SetModalOpen"/> (the match-end overlay is the example).
    /// A per-rect IMGUI registration path used to exist here but had no
    /// production callers and was deleted — don't put gameplay-critical
    /// non-modal buttons in IMGUI (docs/changes/architecture.md gotchas).
    /// </para>
    /// </remarks>
    public static class HudPointerGuard
    {
        // Modal owners (pause menu, settings, match-end overlay). List not
        // HashSet: N is tiny and List add/remove doesn't allocate.
        private static readonly List<object> s_modalOwners = new List<object>(4);

        // TRACE[DOC:best-practices§statics]: statics survive domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_modalOwners.Clear();
        }

        /// <summary>
        /// True while any registered modal (pause menu, settings panel,
        /// match-end overlay) is open. Cursor capture and fire should stay
        /// suppressed regardless of where the pointer is.
        /// </summary>
        public static bool AnyModalOpen => s_modalOwners.Count > 0;

        /// <summary>
        /// Register / unregister a modal screen. Idempotent per owner —
        /// callers can safely re-assert their state every toggle.
        /// </summary>
        public static void SetModalOpen(object owner, bool open)
        {
            if (owner == null) return;
            bool present = s_modalOwners.Contains(owner);
            if (open && !present) s_modalOwners.Add(owner);
            else if (!open && present) s_modalOwners.Remove(owner);
        }

        /// <summary>
        /// Is the pointer over interactive HUD? (UGUI raycast state; IMGUI
        /// modals are covered by <see cref="AnyModalOpen"/>.)
        /// </summary>
        /// <param name="pointerScreenPos">
        /// Pointer position in screen coordinates (bottom-left origin, as
        /// read from <c>Mouse.current.position</c>).
        /// </param>
        public static bool PointerOverHud(Vector2 pointerScreenPos)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
