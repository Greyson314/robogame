using UnityEditor;
using UnityEditor.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// The one button.
    /// </summary>
    /// <remarks>
    /// <c>Robogame/Build Everything</c> (Ctrl+Shift+B) is the only menu entry
    /// for now — every other scaffolder is callable from code but hidden from
    /// the menu bar to keep the workflow surface area small while the project
    /// is still finding its shape. Steps:
    /// <list type="bullet">
    ///   <item><description>Run <see cref="GameplayScaffolder.BuildAllPassA"/>: block defs, blueprints, post-FX, skybox, materials, all scenes, build-settings scene list.</description></item>
    ///   <item><description>Save every open scene so the user can hit this and trust the project is on disk.</description></item>
    /// </list>
    /// </remarks>
    public static class BuildEverythingMenu
    {
        [MenuItem("Robogame/Build Everything %#b", priority = 0)]
        public static void BuildEverything()
        {
            GameplayScaffolder.BuildAllPassA();
            EditorSceneManager.SaveOpenScenes();
        }
    }
}
