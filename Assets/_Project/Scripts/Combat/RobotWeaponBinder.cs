using Robogame.Block;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Sits on a <see cref="Robot"/> alongside its <see cref="WeaponMount"/>
    /// and listens to the <see cref="BlockGrid"/> so that any block of
    /// category <see cref="BlockCategory.Weapon"/> automatically gets a
    /// <see cref="WeaponBlock"/> attached at placement time.
    /// </summary>
    /// <remarks>
    /// Keeps Block / Combat asmdef separation: <c>BlockGrid</c> stays
    /// weapon-agnostic and only emits <c>BlockPlaced</c>. Combat subscribes.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Robot))]
    public sealed class RobotWeaponBinder : MonoBehaviour
    {
        [SerializeField] private WeaponMount _mount;

        private BlockGrid _grid;

        private void Awake()
        {
            if (_mount == null) _mount = GetComponentInChildren<WeaponMount>();
            _grid = GetComponent<BlockGrid>();
        }

        private void OnEnable()
        {
            if (_grid == null) _grid = GetComponent<BlockGrid>();
            if (_grid == null) return;

            _grid.BlockPlaced += HandleBlockPlaced;

            // Re-bind any pre-existing weapon blocks (e.g. after a rebuild).
            foreach (BlockBehaviour b in GetComponentsInChildren<BlockBehaviour>(includeInactive: true))
            {
                HandleBlockPlaced(b);
            }
        }

        private void OnDisable()
        {
            if (_grid != null) _grid.BlockPlaced -= HandleBlockPlaced;
        }

        private void HandleBlockPlaced(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return;
            if (block.Definition.Category != BlockCategory.Weapon) return;

            WeaponBlock weapon = block.GetComponent<WeaponBlock>();
            if (weapon == null) weapon = block.gameObject.AddComponent<WeaponBlock>();
            weapon.Bind(_mount);
        }
    }
}
