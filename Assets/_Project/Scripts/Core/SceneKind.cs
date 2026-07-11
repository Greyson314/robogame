using UnityEngine.SceneManagement;

namespace Robogame.Core
{
    /// <summary>
    /// Scene-kind checks for systems that behave differently in the
    /// garage vs an arena (instanced structure rendering, the Wing's
    /// flap animation, …). One list so every consumer agrees when a new
    /// arena scene ships.
    /// </summary>
    public static class SceneKind
    {
        /// <summary>True in any combat arena scene; false in the garage (and tests).</summary>
        public static bool IsArena()
        {
            string n = SceneManager.GetActiveScene().name;
            return n == "Arena" || n == "WaterArena" || n == "PlanetArena";
        }
    }
}
