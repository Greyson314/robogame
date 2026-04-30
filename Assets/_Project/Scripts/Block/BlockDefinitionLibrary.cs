using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Singleton-style asset that maps block <see cref="BlockDefinition.Id"/>
    /// strings to their <see cref="BlockDefinition"/> instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Saved chassis blueprints store block IDs (stable strings) rather than
    /// direct asset references, so they survive renames / asset moves and can
    /// later be JSON-serialised for save / netcode without dragging
    /// <c>UnityEngine.Object</c> graphs along.
    /// </para>
    /// <para>
    /// Authoring: the library asset is auto-populated by an editor command
    /// (see <c>BlockDefinitionLibraryWizard</c>). Designers don't hand-edit
    /// the entries list — they create new <see cref="BlockDefinition"/>
    /// assets and re-run the populate command.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "BlockDefinitionLibrary",
        menuName = "Robogame/Block Definition Library",
        order = 1)]
    public sealed class BlockDefinitionLibrary : ScriptableObject
    {
        [Tooltip("Every block definition in the project. Auto-populated by the editor wizard.")]
        [SerializeField] private BlockDefinition[] _definitions = System.Array.Empty<BlockDefinition>();

        // Lazy id->def cache; rebuilt on first access or when SetDefinitions
        // is called.
        private Dictionary<string, BlockDefinition> _byId;

        public IReadOnlyList<BlockDefinition> Definitions => _definitions;

        /// <summary>Look up a definition by its stable <see cref="BlockDefinition.Id"/>.</summary>
        public BlockDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureCache();
            _byId.TryGetValue(id, out BlockDefinition def);
            return def;
        }

        /// <summary>True if <paramref name="id"/> resolves to a non-null definition.</summary>
        public bool Contains(string id) => Get(id) != null;

#if UNITY_EDITOR
        /// <summary>Editor-only: replace the entries array. Used by the populate wizard.</summary>
        public void SetDefinitions(BlockDefinition[] defs)
        {
            _definitions = defs ?? System.Array.Empty<BlockDefinition>();
            _byId = null;
        }
#endif

        private void EnsureCache()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, BlockDefinition>(_definitions.Length);
            foreach (BlockDefinition def in _definitions)
            {
                if (def == null) continue;
                if (string.IsNullOrEmpty(def.Id)) continue;
                if (!_byId.ContainsKey(def.Id)) _byId.Add(def.Id, def);
            }
        }

        private void OnValidate() => _byId = null;
    }
}
