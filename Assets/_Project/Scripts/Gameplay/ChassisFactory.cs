using Robogame.Block;
using Robogame.Combat;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Runtime equivalent of the editor's <c>RobotLayouts</c>: turns a
    /// <see cref="ChassisBlueprint"/> into a fully wired playable chassis
    /// at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adds the always-needed components (<see cref="Rigidbody"/>,
    /// <see cref="BlockGrid"/>, <see cref="Robot"/>, <see cref="RobotDrive"/>,
    /// <see cref="PlayerInputHandler"/>, <see cref="PlayerController"/>),
    /// plus the optional ones implied by the blueprint contents (wheels →
    /// <see cref="GroundDriveSubsystem"/> + <see cref="RobotWheelBinder"/>;
    /// aero → <see cref="PlaneControlSubsystem"/> + <see cref="RobotAeroBinder"/>;
    /// any weapon → <see cref="WeaponMount"/> + <see cref="RobotWeaponBinder"/>).
    /// </para>
    /// <para>
    /// Tuning <see cref="ScriptableObject"/> assets are NOT auto-assigned at
    /// runtime — the inline default values on each subsystem are used. The
    /// editor scaffolder remains the place to wire curated tuning profiles
    /// onto a saved scene. This is fine for Pass A: chassis built in-game
    /// drive and fly using sane defaults.
    /// </para>
    /// </remarks>
    public static class ChassisFactory
    {
        /// <summary>
        /// Build a chassis under <paramref name="root"/> from <paramref name="blueprint"/>.
        /// Wipes any prior blocks on the root's <see cref="BlockGrid"/>. Returns the
        /// configured <see cref="Robot"/>.
        /// </summary>
        public static Robot Build(
            GameObject root,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library,
            InputActionAsset inputActions = null)
        {
            if (root == null)
            {
                Debug.LogError("[Robogame] ChassisFactory.Build: root is null.");
                return null;
            }
            if (blueprint == null)
            {
                Debug.LogError("[Robogame] ChassisFactory.Build: blueprint is null.", root);
                return null;
            }
            if (library == null)
            {
                Debug.LogError("[Robogame] ChassisFactory.Build: library is null.", root);
                return null;
            }

            // --- Always-on components ---
            // Build with the root deactivated so OnEnable on the spawned
            // components runs ONCE, after we've finished wiring serialised
            // references via reflection. PlayerInputHandler in particular
            // looks up its action map in OnEnable and bails if _actions is
            // null at that point.
            bool wasActive = root.activeSelf;
            root.SetActive(false);
            try
            {
                Rigidbody rb = EnsureComponent<Rigidbody>(root);
                rb.useGravity = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                BlockGrid grid = EnsureComponent<BlockGrid>(root);
                Robot robot = EnsureComponent<Robot>(root);
                RobotDrive drive = EnsureComponent<RobotDrive>(root);

                // Input: handler reads from an InputActionAsset; if none is
                // supplied the chassis will simply have no inputs (useful for
                // bots/dummies later).
                PlayerInputHandler inputHandler = EnsureComponent<PlayerInputHandler>(root);
                if (inputActions != null)
                {
                    AssignSerializedReference(inputHandler, "_actions", inputActions);
                }
                EnsureComponent<PlayerController>(root);

                // --- Subsystems implied by the blueprint contents ---
                bool hasWheels = false, hasAero = false, hasWeapon = false;
                foreach (ChassisBlueprint.Entry e in blueprint.Entries)
                {
                    if (e.BlockId == BlockIds.Wheel || e.BlockId == BlockIds.WheelSteer) hasWheels = true;
                    if (e.BlockId == BlockIds.Aero) hasAero = true;
                    if (e.BlockId == BlockIds.Weapon) hasWeapon = true;
                }

                if (hasWheels)
                {
                    EnsureComponent<GroundDriveSubsystem>(root);
                    EnsureComponent<RobotWheelBinder>(root);
                }

                if (hasAero || blueprint.Kind == ChassisKind.Plane)
                {
                    EnsureComponent<PlaneControlSubsystem>(root);
                    EnsureComponent<RobotAeroBinder>(root);
                }

                if (hasWeapon)
                {
                    EnsureWeaponMountAndBinder(root);
                }

                // --- Place the blocks. Subsystems / binders subscribed above
                //     receive BlockPlaced events and self-attach correctly. ---
                grid.Clear();
                foreach (ChassisBlueprint.Entry entry in blueprint.Entries)
                {
                    BlockDefinition def = library.Get(entry.BlockId);
                    if (def == null)
                    {
                        Debug.LogWarning(
                            $"[Robogame] ChassisFactory: blueprint references unknown block id '{entry.BlockId}' — skipping.",
                            root);
                        continue;
                    }
                    grid.PlaceBlock(def, entry.Position);
                }

                robot.RecalculateAggregates();
                return robot;
            }
            finally
            {
                root.SetActive(wasActive);
            }
        }

        /// <summary>
        /// Build a non-player target chassis from a blueprint: <see cref="BlockGrid"/>
        /// and <see cref="Robot"/> only, with a frozen-rotation kinematic-friendly
        /// rigidbody. Used for combat dummies and (later) AI targets.
        /// </summary>
        public static Robot BuildTarget(
            GameObject root,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library)
        {
            if (root == null || blueprint == null || library == null)
            {
                Debug.LogError("[Robogame] ChassisFactory.BuildTarget: missing argument(s).");
                return null;
            }

            Rigidbody rb = EnsureComponent<Rigidbody>(root);
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            BlockGrid grid = EnsureComponent<BlockGrid>(root);
            Robot robot = EnsureComponent<Robot>(root);

            grid.Clear();
            foreach (ChassisBlueprint.Entry entry in blueprint.Entries)
            {
                BlockDefinition def = library.Get(entry.BlockId);
                if (def == null) continue;
                grid.PlaceBlock(def, entry.Position);
            }

            robot.RecalculateAggregates();
            return robot;
        }

        // -----------------------------------------------------------------
        // Helpers (mirror the editor ScaffoldHelpers, but runtime-safe)
        // -----------------------------------------------------------------

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        /// <summary>
        /// Set a private serialized reference field via reflection. Avoids
        /// pulling in <c>SerializedObject</c> (editor-only) just to assign
        /// one field at runtime.
        /// </summary>
        private static void AssignSerializedReference(Object target, string fieldName, Object value)
        {
            if (target == null) return;
            System.Type t = target.GetType();
            System.Reflection.FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                t = t.BaseType;
            }
            if (f != null) f.SetValue(target, value);
        }

        private static void EnsureWeaponMountAndBinder(GameObject robotGO)
        {
            Transform mountT = robotGO.transform.Find("WeaponMount");
            GameObject mountGO;
            if (mountT != null)
            {
                mountGO = mountT.gameObject;
            }
            else
            {
                mountGO = new GameObject("WeaponMount");
                mountGO.transform.SetParent(robotGO.transform, worldPositionStays: false);
                mountGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                mountGO.transform.localRotation = Quaternion.identity;
            }
            WeaponMount mount = mountGO.GetComponent<WeaponMount>();
            if (mount == null) mount = mountGO.AddComponent<WeaponMount>();

            RobotWeaponBinder binder = robotGO.GetComponent<RobotWeaponBinder>();
            if (binder == null) binder = robotGO.AddComponent<RobotWeaponBinder>();
            AssignSerializedReference(binder, "_mount", mount);
        }
    }
}
