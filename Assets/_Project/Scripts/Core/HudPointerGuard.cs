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
    /// The project runs two UI families side by side: UGUI (EventSystem
    /// raycasts know about it) and IMGUI (invisible to the EventSystem).
    /// Before this class, cursor capture (<c>FollowCamera</c>), fire
    /// suppression (<c>PlayerInputHandler</c>) and the build-camera drag
    /// gates (<c>OrbitCamera</c>) each called
    /// <c>EventSystem.IsPointerOverGameObject()</c> and were blind to
    /// IMGUI buttons — clicking the match-end "Return to Garage" button
    /// also re-captured the cursor. IMGUI overlays now register their
    /// interactive rects here each OnGUI; queries union both families.
    /// </para>
    /// <para>
    /// IMGUI rects are double-buffered by frame: OnGUI runs after Update,
    /// so an Update-time query reads the previous frame's completed
    /// buffer plus whatever has registered so far this frame. One frame
    /// of staleness errs toward suppression, which is the safe direction
    /// for both capture and fire.
    /// </para>
    /// </remarks>
    public static class HudPointerGuard
    {
        // Front fills during the current frame's OnGUI passes; back holds
        // the previous frame's completed set. Swapped, never reallocated.
        // TRACE[INV-6]: no per-frame allocations — lists are reused.
        private static List<Rect> s_front = new List<Rect>(8);
        private static List<Rect> s_back = new List<Rect>(8);
        private static int s_frontFrame = -1;

        // Modal owners (pause menu, settings, match-end overlay). List not
        // HashSet: N is tiny and List add/remove doesn't allocate.
        private static readonly List<object> s_modalOwners = new List<object>(4);

        // TRACE[DOC:best-practices§statics]: statics survive domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_front.Clear();
            s_back.Clear();
            s_modalOwners.Clear();
            s_frontFrame = -1;
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
        /// Register an interactive IMGUI rect (GUI top-left coordinates)
        /// for the current frame. Call from OnGUI every pass — rects expire
        /// automatically the frame after they stop being registered.
        /// Duplicate registrations within a frame are harmless.
        /// </summary>
        public static void RegisterGuiRect(Rect guiRect) => RegisterGuiRect(guiRect, Time.frameCount);

        /// <summary>Frame-explicit overload — test seam.</summary>
        public static void RegisterGuiRect(Rect guiRect, int frame)
        {
            if (frame != s_frontFrame)
            {
                // New frame: the front buffer is complete — promote it.
                List<Rect> finished = s_front;
                s_front = s_back;
                s_front.Clear();
                s_back = finished;
                s_frontFrame = frame;
            }
            s_front.Add(guiRect);
        }

        /// <summary>
        /// Is the pointer over interactive HUD? Unions the UGUI raycast
        /// state with the registered IMGUI rects.
        /// </summary>
        /// <param name="pointerScreenPos">
        /// Pointer position in screen coordinates (bottom-left origin, as
        /// read from <c>Mouse.current.position</c>).
        /// </param>
        public static bool PointerOverHud(Vector2 pointerScreenPos)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;
            // IMGUI rects live in GUI space (top-left origin) — flip Y.
            return PointerOverGuiRects(new Vector2(pointerScreenPos.x, Screen.height - pointerScreenPos.y));
        }

        /// <summary>
        /// Rect-only hit test in GUI coordinates (top-left origin). Public
        /// so EditMode tests can exercise the buffer logic without an
        /// EventSystem or screen-space conversion.
        /// </summary>
        public static bool PointerOverGuiRects(Vector2 guiPoint)
        {
            for (int i = 0; i < s_back.Count; i++)
                if (s_back[i].Contains(guiPoint)) return true;
            for (int i = 0; i < s_front.Count; i++)
                if (s_front[i].Contains(guiPoint)) return true;
            return false;
        }
    }
}
