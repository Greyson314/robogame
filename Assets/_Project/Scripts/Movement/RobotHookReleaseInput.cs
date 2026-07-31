using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Grapple-release verb: <see cref="Robogame.Input.IInputSource.HookReleasePressed"/>
    /// (R on the player handler) releases every active grapple on this
    /// chassis. Lives on the chassis root (added by
    /// <c>ChassisFactory.Build</c>) and walks the chassis's
    /// <see cref="BlockGrid"/> for any <see cref="HookBlock"/> in a
    /// grappled state, calling <see cref="HookBlock.Release"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We walk the grid (not <c>GetComponentsInChildren</c>) because
    /// adopted hooks are reparented under the rope segment at scene
    /// root, which falls outside the chassis transform hierarchy. The
    /// grid keeps the <see cref="BlockBehaviour"/> reference regardless
    /// of GameObject parent.
    /// </para>
    /// <para>
    /// MP-shape: the verb rides the input source (a serialized bit on
    /// the netcode <c>InputCommand</c>), so this component works
    /// unchanged on a server applying a remote owner's command.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class RobotHookReleaseInput : MonoBehaviour
    {
        private BlockGrid _grid;
        private Robogame.Input.IInputSource _input;

        private void OnEnable()
        {
            _grid = GetComponent<BlockGrid>();
        }

        private void Update()
        {
            if (_grid == null) return;
            // Late-resolve: OnEnable ordering can land this component
            // before the input source (LOG-132 activation-order class).
            if (_input == null)
            {
                _input = GetComponentInParent<Robogame.Input.IInputSource>();
                if (_input == null) return;
            }
            if (!_input.HookReleasePressed) return;

            int released = 0;
            foreach (var kv in _grid.Blocks)
            {
                BlockBehaviour bb = kv.Value;
                if (bb == null) continue;
                HookBlock hook = bb.GetComponent<HookBlock>();
                if (hook == null) continue;
                if (!hook.IsGrappled) continue;
                hook.Release();
                released++;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (released > 0)
                Debug.Log($"[RobotHookReleaseInput] Released {released} grapple(s).", this);
#endif
        }
    }
}
