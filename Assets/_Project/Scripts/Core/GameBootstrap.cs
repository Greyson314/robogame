using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Core
{
    /// <summary>
    /// Entry point of the game. Lives in <c>Bootstrap.unity</c> and is the only
    /// scene that should be opened directly in the editor.
    /// </summary>
    /// <remarks>
    /// Responsibilities (current scope is intentionally minimal):
    /// <list type="bullet">
    /// <item>Initialise core services / event bus (TBD).</item>
    /// <item>Load the first gameplay scene (Garage by default).</item>
    /// </list>
    /// As the project grows, this is the place to register a DI container,
    /// stand up the event bus, and run any one-shot bootstrap tasks.
    /// </remarks>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Scene to load after bootstrap completes. Must be in Build Profiles.")]
        [SerializeField] private string _firstScene = "Garage";

        [Tooltip("If true, the bootstrap scene survives across loads (useful for persistent services).")]
        [SerializeField] private bool _persistAcrossScenes = true;

        private void Awake()
        {
            if (_persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            Debug.Log("[Robogame] Bootstrap starting…");
        }

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(_firstScene))
            {
                Debug.LogWarning("[Robogame] Bootstrap: no first scene configured; staying here.");
                return;
            }

            Debug.Log($"[Robogame] Loading first scene: {_firstScene}");
            SceneManager.LoadScene(_firstScene, LoadSceneMode.Single);
        }
    }
}
