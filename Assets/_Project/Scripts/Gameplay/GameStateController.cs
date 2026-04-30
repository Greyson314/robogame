using System;
using Robogame.Block;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Robogame.Gameplay
{
    /// <summary>High-level state of the game's scene flow.</summary>
    public enum GameState
    {
        Bootstrap,
        Garage,
        Arena,
    }

    /// <summary>
    /// Persistent singleton that owns the cross-scene "what is the player
    /// currently doing" data: current state, the working chassis blueprint,
    /// and the block-definition library used to materialise it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives on the Bootstrap scene's root object and survives scene loads
    /// via <see cref="UnityEngine.Object.DontDestroyOnLoad(UnityEngine.Object)"/>.
    /// Garage / Arena scene controllers read <see cref="CurrentBlueprint"/>
    /// to spawn the player chassis, and call <see cref="EnterGarage"/> /
    /// <see cref="EnterArena"/> to transition.
    /// </para>
    /// <para>
    /// Pass A scope: scene transitions, current blueprint storage, default
    /// blueprint hydrated on first bootstrap. Pass B will plug an in-garage
    /// editor on top of <see cref="CurrentBlueprint"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameStateController : MonoBehaviour
    {
        public const string GarageSceneName = "Garage";
        public const string ArenaSceneName = "Arena";

        [Header("Data")]
        [Tooltip("Library used to resolve block IDs in the current blueprint.")]
        [SerializeField] private BlockDefinitionLibrary _library;

        [Tooltip("Default chassis blueprint loaded on first bootstrap. Cloned into " +
                 "CurrentBlueprint so edits never mutate the asset.")]
        [SerializeField] private ChassisBlueprint _defaultBlueprint;

        [Tooltip("Input actions asset wired onto each spawned player chassis.")]
        [SerializeField] private InputActionAsset _inputActions;

        public static GameStateController Instance { get; private set; }

        public BlockDefinitionLibrary Library => _library;
        public InputActionAsset InputActions => _inputActions;
        public ChassisBlueprint CurrentBlueprint { get; private set; }
        public GameState State { get; private set; } = GameState.Bootstrap;

        /// <summary>Raised after a scene transition completes and <see cref="State"/> has been updated.</summary>
        public event Action<GameState> StateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentBlueprint = CloneBlueprint(_defaultBlueprint);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        // -----------------------------------------------------------------
        // Scene transitions
        // -----------------------------------------------------------------

        public void EnterGarage() => SceneManager.LoadScene(GarageSceneName, LoadSceneMode.Single);
        public void EnterArena() => SceneManager.LoadScene(ArenaSceneName, LoadSceneMode.Single);

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GameState next = scene.name switch
            {
                GarageSceneName => GameState.Garage,
                ArenaSceneName => GameState.Arena,
                _ => State,
            };
            if (next != State)
            {
                State = next;
                StateChanged?.Invoke(State);
            }
        }

        // -----------------------------------------------------------------
        // Blueprint helpers
        // -----------------------------------------------------------------

        /// <summary>Replace the current working blueprint (e.g. after loading a save).</summary>
        public void SetCurrentBlueprint(ChassisBlueprint source)
        {
            CurrentBlueprint = CloneBlueprint(source);
        }

        /// <summary>
        /// Clone a blueprint into a fresh runtime <see cref="ScriptableObject"/>
        /// so edits never write back to the source asset. Returns null if
        /// <paramref name="source"/> is null.
        /// </summary>
        public static ChassisBlueprint CloneBlueprint(ChassisBlueprint source)
        {
            if (source == null) return null;
            ChassisBlueprint clone = ScriptableObject.CreateInstance<ChassisBlueprint>();
            clone.name = source.name + " (Runtime)";
            clone.DisplayName = source.DisplayName;
            clone.Kind = source.Kind;

            ChassisBlueprint.Entry[] src = source.Entries;
            var copy = new ChassisBlueprint.Entry[src.Length];
            Array.Copy(src, copy, src.Length);
            clone.SetEntries(copy);
            return clone;
        }
    }
}
