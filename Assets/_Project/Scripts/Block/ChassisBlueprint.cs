using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Coarse "what kind of vehicle is this" tag baked into a chassis
    /// blueprint. Drives spawn-time behaviour that doesn't fit naturally
    /// inside the per-block components (e.g. planes need an initial
    /// forward velocity so they don't have to taxi from zero).
    /// </summary>
    public enum ChassisKind
    {
        Ground,
        Plane,
    }

    /// <summary>
    /// Serialisable description of a robot: a list of <c>(blockId, gridPos)</c>
    /// pairs plus a small amount of metadata. Pass A's "what crosses scene
    /// boundaries" payload — owned by <c>GameStateController</c>, consumed
    /// by the runtime <c>ChassisFactory</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored as a <see cref="ScriptableObject"/> so designers can hand-author
    /// default loadouts in the editor; the same shape is used at runtime for
    /// the player's mutable in-progress build (an in-memory instance created
    /// via <see cref="ScriptableObject.CreateInstance{T}()"/>).
    /// </para>
    /// <para>
    /// Block IDs are stable strings (<see cref="BlockDefinition.Id"/>), not
    /// asset references — saved blueprints stay valid across asset moves and
    /// are trivially JSON-serialisable for disk save / netcode.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Blueprint_New",
        menuName = "Robogame/Chassis Blueprint",
        order = 2)]
    public sealed class ChassisBlueprint : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Stable BlockDefinition.Id (e.g. 'block.cube').")]
            public string BlockId;

            [Tooltip("Grid coordinate, integer triple in robot-local space.")]
            public Vector3Int Position;

            public Entry(string blockId, Vector3Int position)
            {
                BlockId = blockId;
                Position = position;
            }
        }

        [Tooltip("Human-readable name shown in the garage UI.")]
        [SerializeField] private string _displayName = "Untitled Chassis";

        [Tooltip("Coarse vehicle category. Drives spawn-time behaviour " +
                 "(e.g. planes are launched with forward velocity).")]
        [SerializeField] private ChassisKind _kind = ChassisKind.Ground;

        [Tooltip("Block placements that make up this chassis.")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        public ChassisKind Kind
        {
            get => _kind;
            set => _kind = value;
        }

        public Entry[] Entries => _entries;

        /// <summary>Replace the entries array. Used by the editor wizard and the in-game garage.</summary>
        public void SetEntries(Entry[] entries) => _entries = entries ?? Array.Empty<Entry>();
    }
}
