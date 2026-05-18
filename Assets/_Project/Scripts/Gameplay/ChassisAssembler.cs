using System.Collections.Generic;
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
    /// Per-call options for <see cref="ChassisAssembler.Assemble"/>.
    /// One struct unifies the player / bot / target spawn paths that
    /// used to live in <c>ChassisFactory.Build</c> and
    /// <c>ChassisFactory.BuildTarget</c> as separate methods.
    /// </summary>
    public readonly struct AssemblyOptions
    {
        public readonly bool AddPlayerInputs;
        public readonly bool AddDriveSubsystems;
        public readonly bool AddCombatSubsystems;
        public readonly bool FreezeRotation;
        public readonly bool ApplyRotorsLiftFromBlueprint;
        public readonly InputActionAsset InputActions;

        public AssemblyOptions(
            bool addPlayerInputs,
            bool addDriveSubsystems,
            bool addCombatSubsystems,
            bool freezeRotation,
            bool applyRotorsLiftFromBlueprint,
            InputActionAsset inputActions)
        {
            AddPlayerInputs = addPlayerInputs;
            AddDriveSubsystems = addDriveSubsystems;
            AddCombatSubsystems = addCombatSubsystems;
            FreezeRotation = freezeRotation;
            ApplyRotorsLiftFromBlueprint = applyRotorsLiftFromBlueprint;
            InputActions = inputActions;
        }

        /// <summary>The full player chassis: drive subsystems + combat + inputs + helicopter lift.</summary>
        public static AssemblyOptions Player(InputActionAsset inputActions)
            => new AssemblyOptions(
                addPlayerInputs: true,
                addDriveSubsystems: true,
                addCombatSubsystems: true,
                freezeRotation: false,
                applyRotorsLiftFromBlueprint: true,
                inputActions: inputActions);

        /// <summary>An AI bot chassis: drive subsystems + combat, no player inputs.</summary>
        public static AssemblyOptions Bot()
            => new AssemblyOptions(
                addPlayerInputs: false,
                addDriveSubsystems: true,
                addCombatSubsystems: true,
                freezeRotation: false,
                applyRotorsLiftFromBlueprint: true,
                inputActions: null);

        /// <summary>A passive combat target: rig binders only, no drive subsystems.</summary>
        public static AssemblyOptions Target(bool freezeRotation = true)
            => new AssemblyOptions(
                addPlayerInputs: false,
                addDriveSubsystems: false,
                addCombatSubsystems: true,
                freezeRotation: freezeRotation,
                applyRotorsLiftFromBlueprint: false,
                inputActions: null);
    }

    /// <summary>
    /// Bundle of references the assembler returns. Replaces the prior
    /// sidechannel of stashing <c>Robot.Blueprint</c> /
    /// <c>Robot.Library</c> directly onto the <see cref="Robot"/> —
    /// repair-style consumers (<see cref="RepairPad"/>) can read from
    /// the handle without re-walking <see cref="GameStateController"/>.
    /// </summary>
    public sealed class ChassisHandle
    {
        public readonly GameObject Root;
        public readonly Robot Robot;
        public readonly BlockGrid Grid;
        public readonly ChassisBlueprint Blueprint;
        public readonly BlockDefinitionLibrary Library;

        public ChassisHandle(GameObject root, Robot robot, BlockGrid grid,
            ChassisBlueprint blueprint, BlockDefinitionLibrary library)
        {
            Root = root; Robot = robot; Grid = grid;
            Blueprint = blueprint; Library = library;
        }
    }

    /// <summary>
    /// Single entry point for building a chassis from a blueprint at
    /// runtime. Replaces the prior <c>ChassisFactory.Build</c> /
    /// <c>ChassisFactory.BuildTarget</c> split with one
    /// <see cref="Assemble"/> method + an <see cref="AssemblyOptions"/>
    /// flag set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phased subsystem inference.</b> Subsystems are added in three
    /// declared phases (substrate → drive/aero/weapon → rig binders)
    /// with explicit ordering inside each. The prior "ORDER MATTERS"
    /// comment that gated tip-binder before rope-binder is now a phase
    /// constraint: phase 3 reliably runs after phases 1–2, and within
    /// phase 3 the registration order is the contract.
    /// </para>
    /// <para>
    /// <b>OnEnable timing.</b> The root is deactivated for the duration
    /// of the build so every <c>OnEnable</c> fires once after wiring
    /// completes, not piecemeal as components are added. Same dance the
    /// prior <c>ChassisFactory.Build</c> used; documented in CLAUDE.md.
    /// </para>
    /// </remarks>
    public static class ChassisAssembler
    {
        public static ChassisHandle Assemble(
            GameObject root,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library,
            AssemblyOptions options)
        {
            using var _scope = Robogame.Core.PerfMarkers.ChassisFactoryBuild.Auto();
            if (root == null) { Debug.LogError("[Robogame] ChassisAssembler.Assemble: root is null."); return null; }
            if (blueprint == null) { Debug.LogError("[Robogame] ChassisAssembler.Assemble: blueprint is null.", root); return null; }
            if (library == null) { Debug.LogError("[Robogame] ChassisAssembler.Assemble: library is null.", root); return null; }

            // Build with the root deactivated so OnEnable on spawned
            // components runs ONCE, after we've wired serialised refs
            // via reflection. PlayerInputHandler in particular looks up
            // its action map in OnEnable and bails if _actions is null
            // at that point.
            bool wasActive = root.activeSelf;
            root.SetActive(false);
            BlockGrid grid;
            Robot robot;
            try
            {
                // Phase 1 — always-on substrate.
                Rigidbody rb = EnsureComponent<Rigidbody>(root);
                rb.useGravity = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.constraints = options.FreezeRotation
                    ? RigidbodyConstraints.FreezeRotation
                    : RigidbodyConstraints.None;

                grid = EnsureComponent<BlockGrid>(root);
                robot = EnsureComponent<Robot>(root);
                // Stash the blueprint + library on the Robot for
                // back-compat with consumers that read robot.Blueprint
                // (e.g. RepairPad). Future migration: read from the
                // ChassisHandle returned below.
                robot.Blueprint = blueprint;
                robot.Library = library;
                // RobotDrive carries the blueprint for the Movement-tier
                // subsystems (Movement must not reference Robot — Robots →
                // Movement asmdef edge). Set before the root activates so
                // RobotDrive.Awake reads server-authoritative damping.
                EnsureComponent<RobotDrive>(root).Blueprint = blueprint;
                EnsureComponent<Movement.ChassisWindAudio>(root);

                if (options.AddPlayerInputs)
                {
                    PlayerInputHandler inputHandler = EnsureComponent<PlayerInputHandler>(root);
                    if (options.InputActions != null)
                        AssignSerializedReference(inputHandler, "_actions", options.InputActions);
                }

                // PlayerController is the IInputSource → IMovementProvider
                // bridge that ticks RobotDrive each FixedUpdate. Bots have
                // their own IInputSource (GroundBot / AirBot input source)
                // attached BEFORE Build runs; without PlayerController the
                // bot's Move output is computed every Update but never
                // applied to the rigidbody. Add it whenever drive
                // subsystems exist — i.e. anything that isn't a frozen
                // passive target.
                if (options.AddDriveSubsystems)
                {
                    EnsureComponent<PlayerController>(root);
                }

                // Phase 2 — drive / aero / weapon subsystems implied by
                // blueprint contents. Skipped on passive targets.
                if (options.AddDriveSubsystems)
                {
                    bool hasWheels = false, hasAero = false, hasWeapon = false;
                    foreach (ChassisBlueprint.Entry e in blueprint.Entries)
                    {
                        if (e.BlockId == BlockIds.Wheel || e.BlockId == BlockIds.WheelSteer) hasWheels = true;
                        if (e.BlockId == BlockIds.Aero || e.BlockId == BlockIds.AeroFin) hasAero = true;
                        if (e.BlockId == BlockIds.Weapon
                            || e.BlockId == BlockIds.BombBay
                            || e.BlockId == BlockIds.Cannon
                            || e.BlockId == BlockIds.GrappleMagnet)
                            hasWeapon = true;
                    }

                    if (hasWheels)
                    {
                        EnsureComponent<GroundDriveSubsystem>(root);
                        EnsureComponent<RobotWheelBinder>(root);
                    }

                    // Aero binder is unconditional — players drag wings
                    // onto an existing chassis and expect them to work
                    // without a respawn. Zero per-frame cost when no
                    // aero blocks are present.
                    EnsureComponent<RobotAeroBinder>(root);

                    if (hasAero || blueprint.Kind == ChassisKind.Plane)
                        EnsureComponent<PlaneControlSubsystem>(root);

                    if (hasWeapon)
                        EnsureWeaponMountAndBinder(root);
                }

                // Phase 3 — rig binders. Tip-binder MUST come before
                // rope-binder: both fire OnEnable in AddComponent order
                // when SetActive(true) cascades, and RopeBlock.Build
                // looks for HookBlock / MaceBlock components added by
                // the tip binder. Phase 3 is the contract; the order
                // within Phase 3 is the contract.
                EnsureComponent<RobotTipBlockBinder>(root);
                EnsureComponent<RobotRopeBinder>(root);
                EnsureComponent<RobotRotorBinder>(root);
                // Phase 3b: attach DrillBlock components to placed
                // "block.tool.drill" cells. Lives in Robogame.Voxel.
                EnsureComponent<Voxel.RobotDrillBinder>(root);

                if (options.AddPlayerInputs)
                {
                    EnsureComponent<RobotHookReleaseInput>(root);
                    EnsureComponent<FlipController>(root);
                }

                if (options.AddCombatSubsystems)
                {
                    EnsureComponent<MomentumImpactHandler>(root);
                    EnsureComponent<ScrapDropper>(root);
                    EnsureComponent<ScrapCarryMovementPenalty>(root);
                    EnsureComponent<WeaponAmmoState>(root);
                }

                // Instanced renderer for the frozen in-arena chassis
                // (PERFORMANCE.md §8.2). Unconditional — it self-gates
                // to arena scenes and to full-health Structure blocks,
                // so it is dormant in the garage and never touches
                // moving/special blocks. Added here (root inactive); it
                // builds its batch one frame after activation.
                EnsureComponent<ChassisInstancedRenderer>(root);

                // Phase 4 — block placement. Subsystems / binders
                // subscribed above receive BlockPlaced events and
                // self-attach correctly.
                grid.Clear();
                foreach (ChassisBlueprint.Entry entry in blueprint.Entries)
                {
                    BlockDefinition def = library.Get(entry.BlockId);
                    if (def == null)
                    {
                        Debug.LogWarning(
                            $"[Robogame] ChassisAssembler: blueprint references unknown block id '{entry.BlockId}' — skipping.",
                            root);
                        continue;
                    }
                    BlockBehaviour placed = grid.PlaceBlock(
                        def, entry.Position, entry.EffectiveUp, entry.Dims, entry.Pitch);
                    // Per-block server-authoritative scalar (thruster
                    // thrust / rudder authority / rotor RPM). 0 = use the
                    // block's authored default. Rides the same Entry the
                    // Dims/Pitch above do; not part of the canonical sort.
                    if (placed != null) placed.ConfigValue = entry.BlockConfig;
                }

                robot.RecalculateAggregates();
            }
            finally
            {
                root.SetActive(wasActive);

                // Phase 5 — post-activation. Helicopters flip their
                // cosmetic rotors into lift mode (kinematic hub + ring
                // of aerofoil blades). MUST run AFTER SetActive(true) —
                // RobotRotorBinder only attaches RotorBlock components
                // during its OnEnable re-bind pass, so before activation
                // there are no RotorBlocks to find. See
                // docs/PHYSICS_PLAN.md §2.
                if (wasActive && options.ApplyRotorsLiftFromBlueprint && blueprint.RotorsGenerateLift)
                {
                    foreach (RotorBlock rotor in root.GetComponentsInChildren<RotorBlock>(true))
                    {
                        rotor.GeneratesLift = true;
                    }
                }
            }

            return new ChassisHandle(root, robot, grid, blueprint, library);
        }

        // -----------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

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
