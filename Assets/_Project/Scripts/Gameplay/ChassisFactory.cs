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
                bool hasWheels = false, hasAero = false, hasThruster = false, hasRudder = false, hasWeapon = false;
                foreach (ChassisBlueprint.Entry e in blueprint.Entries)
                {
                    if (e.BlockId == BlockIds.Wheel || e.BlockId == BlockIds.WheelSteer) hasWheels = true;
                    if (e.BlockId == BlockIds.Aero || e.BlockId == BlockIds.AeroFin) hasAero = true;
                    if (e.BlockId == BlockIds.Thruster) hasThruster = true;
                    if (e.BlockId == BlockIds.Rudder) hasRudder = true;
                    // Match any weapon-category block by id, so new weapon
                    // variants (BombBay, future rocket pods, …) trigger the
                    // weapon-mount + binder path without needing to be
                    // listed individually here.
                    if (e.BlockId == BlockIds.Weapon || e.BlockId == BlockIds.BombBay) hasWeapon = true;
                }

                if (hasWheels)
                {
                    EnsureComponent<GroundDriveSubsystem>(root);
                    EnsureComponent<RobotWheelBinder>(root);
                }

                // The aero binder turns Thruster / Aero / AeroFin / Rudder block
                // primitives into their behaviour components (jet rig +
                // force-applying ThrusterBlock, lifting AeroSurfaceBlock,
                // yaw-applying RudderBlock). It's needed any time those
                // blocks exist, regardless of chassis kind — e.g. a
                // Ground-kind boat with a thruster + rudder.
                if (hasAero || hasThruster || hasRudder || blueprint.Kind == ChassisKind.Plane)
                {
                    EnsureComponent<RobotAeroBinder>(root);
                }

                // PlaneControlSubsystem owns pitch/roll/yaw authority and
                // only makes sense on aircraft. Skip it on a thruster-only
                // ground/boat chassis so we don't fight gravity with control
                // surfaces that aren't there.
                if (hasAero || blueprint.Kind == ChassisKind.Plane)
                {
                    EnsureComponent<PlaneControlSubsystem>(root);
                }

                if (hasWeapon)
                {
                    EnsureWeaponMountAndBinder(root);
                }

                // Rope binder is unconditional: ropes are commonly added
                // mid-build (player drags one onto an existing plane), and
                // the binder is just a BlockPlaced subscriber — zero cost
                // when no rope blocks are present, and avoids an "I added
                // a rope and nothing happened" trap when the blueprint
                // didn't originally contain one.
                EnsureComponent<RobotRopeBinder>(root);

                // Rotor binder — same reasoning as the rope binder. Rotors
                // are Cosmetic-tab build-mode blocks; players drop them
                // onto an existing chassis and expect them to start
                // spinning. Zero per-frame cost when no rotor blocks are
                // present (the binder is just a BlockPlaced subscriber).
                EnsureComponent<RobotRotorBinder>(root);

                // Ramming damage (kinetic-energy based). Lives on every
                // player chassis so plane-vs-dummy / plane-vs-plane
                // collisions both deal mutual damage scaled by reduced
                // mass and relative speed.
                EnsureComponent<Robogame.Combat.MomentumImpactHandler>(root);

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
                    grid.PlaceBlock(def, entry.Position, entry.EffectiveUp);
                }

                robot.RecalculateAggregates();

                return robot;
            }
            finally
            {
                root.SetActive(wasActive);

                // Per-blueprint opt-in: helicopters flip their cosmetic
                // rotors into lift mode (kinematic hub + ring of aerofoil
                // blades). MUST run AFTER SetActive(true) — the
                // RobotRotorBinder only attaches RotorBlock components
                // during its OnEnable re-bind pass, so before activation
                // there are no RotorBlocks to find. See
                // RotorBlock.GeneratesLift and docs/PHYSICS_PLAN.md §2.
                if (wasActive && blueprint.RotorsGenerateLift)
                {
                    foreach (Robogame.Movement.RotorBlock rotor in root.GetComponentsInChildren<Robogame.Movement.RotorBlock>(true))
                    {
                        rotor.GeneratesLift = true;
                    }
                }
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

            // Target chassis is non-player but still a valid ramming
            // victim — a plane crashing into the dummy must do (and
            // receive) damage exactly as it would against another
            // player. Lives on the same root Rigidbody.
            EnsureComponent<Robogame.Combat.MomentumImpactHandler>(root);

            // Passive targets (CombatDummy, StressRotorTower, future AI)
            // must mirror the player's per-block attach hooks: BlockGrid
            // raises BlockPlaced as cells are added, and these binders
            // are what actually attach the RopeBlock / RotorBlock
            // MonoBehaviours that build the visual rig + dynamic
            // segments. Without them, blueprint-authored rope/rotor
            // cells appear as bare host cubes — exactly the StressRotor
            // tower symptom in v0.5.
            EnsureComponent<Robogame.Movement.RobotRopeBinder>(root);
            EnsureComponent<Robogame.Movement.RobotRotorBinder>(root);

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
