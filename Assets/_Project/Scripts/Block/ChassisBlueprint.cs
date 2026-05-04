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

            [Tooltip("Mount orientation: the direction the block's local +Y points in chassis space " +
                     "(i.e. the face normal of the cube it's attached to). Default (0,0,0) is treated " +
                     "as +Y so legacy entries stay upright. Most cosmetic blocks ignore this; rotors, " +
                     "aerofoils, weapons, and thrusters use it to face outward from the mount face.")]
            public Vector3Int Up;

            /// <summary>Returns <see cref="Up"/> with the legacy zero → +Y fallback applied.</summary>
            public Vector3Int EffectiveUp => Up == Vector3Int.zero ? Vector3Int.up : Up;

            public Entry(string blockId, Vector3Int position)
            {
                BlockId = blockId;
                Position = position;
                Up = Vector3Int.up;
            }

            public Entry(string blockId, Vector3Int position, Vector3Int up)
            {
                BlockId = blockId;
                Position = position;
                Up = up;
            }
        }

        [Tooltip("Human-readable name shown in the garage UI.")]
        [SerializeField] private string _displayName = "Untitled Chassis";

        [Tooltip("Coarse vehicle category. Drives spawn-time behaviour " +
                 "(e.g. planes are launched with forward velocity).")]
        [SerializeField] private ChassisKind _kind = ChassisKind.Ground;

        [Tooltip("If true, every Rotor cell on this chassis spawns a 4-blade " +
                 "aerofoil ring producing real lift via the standard AeroSurfaceBlock " +
                 "math (helicopter / rotorcraft). When false (default) rotors are " +
                 "purely cosmetic. Per-rotor opt-in lands when the blueprint format " +
                 "supports per-cell config.")]
        [SerializeField] private bool _rotorsGenerateLift = false;

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

        /// <summary>
        /// Whether <see cref="Block.BlockIds.Rotor"/> cells on this chassis
        /// are propulsion rotors (spawn aerofoils, generate lift) or pure
        /// cosmetic spinners. See <c>docs/PHYSICS_PLAN.md</c> §2 — this
        /// is the temporary blueprint-level switch until per-cell config
        /// lands; until then, "this chassis is a helicopter" is the right
        /// granularity.
        /// </summary>
        public bool RotorsGenerateLift
        {
            get => _rotorsGenerateLift;
            set => _rotorsGenerateLift = value;
        }

        public Entry[] Entries => _entries;

        /// <summary>Replace the entries array. Used by the editor wizard and the in-game garage.</summary>
        public void SetEntries(Entry[] entries) => _entries = entries ?? Array.Empty<Entry>();
    }
}
