using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Sits on the robot root and listens to <see cref="BlockGrid.BlockPlaced"/>.
    /// Any block whose definition matches a known wheel ID gets a
    /// <see cref="WheelBlock"/> attached with the appropriate
    /// <see cref="WheelKind"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class RobotWheelBinder : MonoBehaviour
    {
        // Mirrors BlockDefinitionWizard ids so we don't add a Movement→Tools dep.
        public const string IdWheelDrive = "block.movement.wheel";
        public const string IdWheelSteer = "block.movement.wheel.steer";

        private BlockGrid _grid;

        private void OnEnable()
        {
            _grid = GetComponent<BlockGrid>();
            if (_grid == null) return;

            _grid.BlockPlaced += HandleBlockPlaced;

            // Re-bind any pre-existing wheel blocks (template rebuild path).
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
            if (block.Definition.Category != BlockCategory.Movement) return;

            WheelKind kind;
            switch (block.Definition.Id)
            {
                case IdWheelSteer: kind = WheelKind.Steer; break;
                case IdWheelDrive: kind = WheelKind.Drive; break;
                default: return; // Not a wheel we know how to handle.
            }

            WheelBlock wheel = block.GetComponent<WheelBlock>();
            if (wheel == null) wheel = block.gameObject.AddComponent<WheelBlock>();
            wheel.Kind = kind;
        }
    }
}
