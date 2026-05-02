using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// The one button.
    /// </summary>
    /// <remarks>
    /// Top-level <c>Robogame/Build Everything</c> (Ctrl+Shift+B) — the
    /// canonical "rebuild the whole project from code" entry point. Delegates
    /// to <see cref="GameplayScaffolder.BuildAllPassA"/> which:
    /// <list type="bullet">
    ///   <item><description>Populates the BlockDefinition library + default blueprints.</description></item>
    ///   <item><description>Builds post-FX volume profiles, skybox, outline renderer feature, block materials.</description></item>
    ///   <item><description>Rebuilds Bootstrap, Garage, and Arena scenes.</description></item>
    ///   <item><description>Syncs the Build Settings scene list and leaves Bootstrap.unity open.</description></item>
    /// </list>
    /// If you only want a slice (just block defs, just one scene, etc.), the
    /// numbered steps under <c>Robogame/Scaffold/Gameplay/</c> still work.
    /// </remarks>
    public static class BuildEverythingMenu
    {
        // Priority 0 + a separator below puts this at the very top of the
        // Robogame menu, before any submenu. Shortcut: Ctrl+Shift+B.
        [MenuItem("Robogame/Build Everything %#b", priority = 0)]
        public static void BuildEverything()
        {
            GameplayScaffolder.BuildAllPassA();
        }

        // Visual separator between the catch-all and the rest of the menu.
        [MenuItem("Robogame/", priority = 1)] private static void Separator() { }
    }
}
