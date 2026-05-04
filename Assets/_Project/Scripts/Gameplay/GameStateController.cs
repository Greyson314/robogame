using System;
using System.Collections.Generic;
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
        WaterArena,
        PlanetArena,
    }

    /// <summary>
    /// Persistent singleton that owns the cross-scene "what is the player
    /// currently doing" data: current state, the working chassis blueprint,
    /// the block-definition library used to materialise it, and the
    /// merged catalog of preset + user-saved blueprints.
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
    /// Blueprint picker model: presets (read-only, designer-authored) and
    /// user blueprints (disk-backed, mutable) are presented as a single
    /// merged list to the HUD. <see cref="SelectPreset"/> takes a merged
    /// index — presets come first, user records second.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameStateController : MonoBehaviour
    {
        public const string GarageSceneName = "Garage";
        public const string ArenaSceneName = "Arena";
        public const string WaterArenaSceneName = "WaterArena";
        public const string PlanetArenaSceneName = "PlanetArena";

        [Header("Data")]
        [Tooltip("Library used to resolve block IDs in the current blueprint.")]
        [SerializeField] private BlockDefinitionLibrary _library;

        [Tooltip("Default chassis blueprint loaded on first bootstrap. Cloned into " +
                 "CurrentBlueprint so edits never mutate the asset.")]
        [SerializeField] private ChassisBlueprint _defaultBlueprint;

        [Tooltip("Designer-authored chassis presets. Always shown first in the HUD picker.")]
        [SerializeField] private ChassisBlueprint[] _presetBlueprints;

        [Tooltip("Input actions asset wired onto each spawned player chassis.")]
        [SerializeField] private InputActionAsset _inputActions;

        public static GameStateController Instance { get; private set; }

        public BlockDefinitionLibrary Library => _library;
        public InputActionAsset InputActions => _inputActions;
        public ChassisBlueprint CurrentBlueprint { get; private set; }
        public GameState State { get; private set; } = GameState.Bootstrap;

        public IReadOnlyList<ChassisBlueprint> PresetBlueprints => _presetBlueprints;

        private readonly List<UserBlueprintLibrary.Record> _userBlueprints = new();
        public IReadOnlyList<UserBlueprintLibrary.Record> UserBlueprints => _userBlueprints;

        /// <summary>
        /// Index into the merged picker list: <c>[0..presets.Count)</c> are
        /// presets, <c>[presets.Count..presets.Count+user.Count)</c> are
        /// user records. <c>-1</c> means "current blueprint isn't on the
        /// list" (e.g. freshly created via <see cref="CreateNewBlueprint"/>).
        /// </summary>
        public int CurrentPresetIndex { get; private set; } = -1;

        /// <summary>
        /// File name (with extension) of the user blueprint currently loaded
        /// into <see cref="CurrentBlueprint"/>, or <c>null</c> if the
        /// current blueprint is a preset clone or freshly created. Used by
        /// <see cref="SaveCurrentBlueprint"/> to decide overwrite vs new.
        /// </summary>
        public string CurrentUserFileName { get; private set; }

        public int PresetCount => _presetBlueprints != null ? _presetBlueprints.Length : 0;
        public int TotalCatalogCount => PresetCount + _userBlueprints.Count;

        /// <summary>Raised after a scene transition completes and <see cref="State"/> has been updated.</summary>
        public event Action<GameState> StateChanged;

        /// <summary>
        /// Raised after the current blueprint changes (preset switch, user
        /// load, "New Robot", or save-as overwrite). Argument is the new
        /// <see cref="CurrentPresetIndex"/> (may be <c>-1</c>).
        /// </summary>
        public event Action<int> PresetChanged;

        /// <summary>
        /// Raised when the on-disk user blueprint catalog changes (save,
        /// delete, or initial load). HUD subscribes to repopulate its
        /// dropdown.
        /// </summary>
        public event Action BlueprintCatalogChanged;

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
            // Try to align CurrentPresetIndex with the default blueprint so
            // the HUD dropdown shows the right starting selection.
            if (_presetBlueprints != null && _defaultBlueprint != null)
            {
                for (int i = 0; i < _presetBlueprints.Length; i++)
                {
                    if (_presetBlueprints[i] == _defaultBlueprint)
                    {
                        CurrentPresetIndex = i;
                        break;
                    }
                }
            }

            // Hydrate the user blueprint catalog from disk on bootstrap.
            RefreshUserBlueprints(notify: false);
            UserBlueprintLibrary.Changed += HandleLibraryChanged;

            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UserBlueprintLibrary.Changed -= HandleLibraryChanged;
        }

        // -----------------------------------------------------------------
        // Scene transitions
        // -----------------------------------------------------------------

        public void EnterGarage() => SceneManager.LoadScene(GarageSceneName, LoadSceneMode.Single);
        public void EnterArena() => SceneManager.LoadScene(ArenaSceneName, LoadSceneMode.Single);
        public void EnterWaterArena() => SceneManager.LoadScene(WaterArenaSceneName, LoadSceneMode.Single);
        public void EnterPlanetArena() => SceneManager.LoadScene(PlanetArenaSceneName, LoadSceneMode.Single);

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GameState next = scene.name switch
            {
                GarageSceneName => GameState.Garage,
                ArenaSceneName => GameState.Arena,
                WaterArenaSceneName => GameState.WaterArena,
                PlanetArenaSceneName => GameState.PlanetArena,
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
            CurrentPresetIndex = -1;
            CurrentUserFileName = null;
        }

        /// <summary>
        /// Swap the current blueprint to one of the merged catalog entries
        /// and notify listeners. <paramref name="mergedIndex"/> spans
        /// presets first, then user blueprints.
        /// </summary>
        public void SelectPreset(int mergedIndex)
        {
            int presetCount = PresetCount;
            if (mergedIndex < 0 || mergedIndex >= presetCount + _userBlueprints.Count) return;

            if (mergedIndex < presetCount)
            {
                ChassisBlueprint src = _presetBlueprints[mergedIndex];
                if (src == null) return;
                CurrentBlueprint = CloneBlueprint(src);
                CurrentUserFileName = null;
            }
            else
            {
                UserBlueprintLibrary.Record rec = _userBlueprints[mergedIndex - presetCount];
                if (rec.Blueprint == null) return;
                // Records are already runtime instances minted by the
                // serializer, but clone defensively so picker re-selects
                // hand back a fresh, isolated copy.
                CurrentBlueprint = CloneBlueprint(rec.Blueprint);
                CurrentUserFileName = rec.FileName;
            }

            CurrentPresetIndex = mergedIndex;
            PresetChanged?.Invoke(mergedIndex);
        }

        /// <summary>
        /// Mint a brand-new blueprint from <see cref="StarterBlueprints"/>
        /// and switch to it. Not yet persisted to disk; call
        /// <see cref="SaveCurrentBlueprint"/> to do that.
        /// </summary>
        public void CreateNewBlueprint()
        {
            CurrentBlueprint = StarterBlueprints.CreateGroundStarter();
            CurrentPresetIndex = -1;
            CurrentUserFileName = null;
            PresetChanged?.Invoke(-1);
        }

        /// <summary>
        /// Persist <see cref="CurrentBlueprint"/> to the user library. If
        /// the current blueprint was loaded from a user file, the existing
        /// file is overwritten; otherwise a new uniquely-named file is
        /// created. After saving, the catalog is refreshed and
        /// <see cref="CurrentUserFileName"/>/<see cref="CurrentPresetIndex"/>
        /// are repointed at the on-disk record so subsequent saves
        /// overwrite cleanly.
        /// </summary>
        public string SaveCurrentBlueprint()
        {
            if (CurrentBlueprint == null) return null;
            string fileName = UserBlueprintLibrary.Save(CurrentBlueprint, CurrentUserFileName);
            CurrentUserFileName = fileName;
            // RefreshUserBlueprints fires BlueprintCatalogChanged; after it
            // returns we re-derive the merged index for the saved file.
            RefreshUserBlueprints(notify: true);
            int idx = FindUserIndex(fileName);
            if (idx >= 0)
            {
                CurrentPresetIndex = PresetCount + idx;
                PresetChanged?.Invoke(CurrentPresetIndex);
            }
            return fileName;
        }

        /// <summary>
        /// Delete the user blueprint currently loaded (if any). After
        /// deletion the current blueprint is left in memory but
        /// <see cref="CurrentUserFileName"/> / <see cref="CurrentPresetIndex"/>
        /// are cleared.
        /// </summary>
        public bool DeleteCurrentUserBlueprint()
        {
            if (string.IsNullOrEmpty(CurrentUserFileName)) return false;
            bool ok = UserBlueprintLibrary.Delete(CurrentUserFileName);
            if (ok)
            {
                CurrentUserFileName = null;
                CurrentPresetIndex = -1;
                // Library.Changed → HandleLibraryChanged → RefreshUserBlueprints → event.
            }
            return ok;
        }

        /// <summary>Re-read the on-disk catalog. Cheap; fires <see cref="BlueprintCatalogChanged"/>.</summary>
        public void RefreshUserBlueprints(bool notify = true)
        {
            _userBlueprints.Clear();
            _userBlueprints.AddRange(UserBlueprintLibrary.LoadAll());
            if (notify) BlueprintCatalogChanged?.Invoke();
        }

        private void HandleLibraryChanged() => RefreshUserBlueprints(notify: true);

        private int FindUserIndex(string fileName)
        {
            for (int i = 0; i < _userBlueprints.Count; i++)
                if (string.Equals(_userBlueprints[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
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
            // Per-blueprint physics flags must round-trip through the
            // clone or the runtime copy silently loses them — e.g. the
            // helicopter blueprint's RotorsGenerateLift would be false
            // on every spawn, leaving rotors cosmetic. Add new flags
            // here when ChassisBlueprint grows them.
            clone.RotorsGenerateLift = source.RotorsGenerateLift;

            ChassisBlueprint.Entry[] src = source.Entries;
            var copy = new ChassisBlueprint.Entry[src.Length];
            // Entry is a struct so Array.Copy is a deep copy of all
            // fields including Up — nothing extra to do for new fields.
            Array.Copy(src, copy, src.Length);
            clone.SetEntries(copy);
            return clone;
        }
    }
}
