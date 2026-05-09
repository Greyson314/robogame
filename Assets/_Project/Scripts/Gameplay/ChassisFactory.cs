using Robogame.Block;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Backwards-compatible facade over <see cref="ChassisAssembler"/>.
    /// Existing call sites (arena controllers, garage, water/planet
    /// arenas, scaffolders) keep their <c>ChassisFactory.Build</c> /
    /// <c>BuildTarget</c> imports; new code should call
    /// <see cref="ChassisAssembler.Assemble"/> directly so the
    /// <see cref="ChassisHandle"/> return value flows through.
    /// </summary>
    public static class ChassisFactory
    {
        /// <summary>
        /// Build a chassis under <paramref name="root"/> from
        /// <paramref name="blueprint"/>. Wipes any prior blocks on the
        /// root's <see cref="BlockGrid"/>. Returns the configured
        /// <see cref="Robot"/>; new callers should prefer
        /// <see cref="ChassisAssembler.Assemble"/> for the
        /// <see cref="ChassisHandle"/> bundle.
        /// </summary>
        public static Robot Build(
            GameObject root,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library,
            InputActionAsset inputActions = null,
            bool addPlayerInputs = true)
        {
            AssemblyOptions options = addPlayerInputs
                ? AssemblyOptions.Player(inputActions)
                : AssemblyOptions.Bot();
            ChassisHandle handle = ChassisAssembler.Assemble(root, blueprint, library, options);
            return handle?.Robot;
        }

        /// <summary>
        /// Build a non-player target chassis: <see cref="BlockGrid"/>
        /// and <see cref="Robot"/> only, with a frozen-rotation
        /// kinematic-friendly rigidbody. Used for combat dummies and
        /// (later) AI targets.
        /// </summary>
        public static Robot BuildTarget(
            GameObject root,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library,
            bool freezeRotation = true)
        {
            ChassisHandle handle = ChassisAssembler.Assemble(
                root, blueprint, library, AssemblyOptions.Target(freezeRotation));
            return handle?.Robot;
        }
    }
}
