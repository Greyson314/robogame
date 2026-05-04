using Robogame.Block;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Movement
{
    /// <summary>
    /// Player-facing hotkey wrapper: pressing <c>R</c> releases every
    /// active grapple on this chassis. Lives on the chassis root (added
    /// by <c>ChassisFactory.Build</c>) and walks the chassis's
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
    /// Reads <see cref="Keyboard.current"/> directly so we don't have
    /// to edit the project's <c>InputSystem_Actions.inputactions</c>.
    /// Same pattern <see cref="Player.FollowCamera"/> uses for cursor
    /// release.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class RobotHookReleaseInput : MonoBehaviour
    {
        [SerializeField] private Key _releaseKey = Key.R;

        private BlockGrid _grid;

        private void OnEnable()
        {
            _grid = GetComponent<BlockGrid>();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || _grid == null) return;
            if (!kb[_releaseKey].wasPressedThisFrame) return;

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
