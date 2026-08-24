// =============================================================================
// BinderRetrofitTests — pins the LOG-170 rule: block binders must be present
// on every assembled chassis regardless of what the blueprint contained at
// spawn, so a block placed LATER (garage build mode) still gets its
// behaviour component the moment BlockGrid.BlockPlaced fires.
//
// WHY THIS MATTERS: ChassisAssembler used to gate the weapon mount + binder
// on "blueprint has a Weapon-category block" and the wheel binder on
// "blueprint has a Ground-drive block" — both evaluated once, at spawn. The
// first mortar placed on a weaponless bot in the garage therefore got no
// MortarBlock at all: nothing built its rig or hid the host cube, and the
// player saw the bare red BlockMat_Weapon primitive (the session-132 mortar
// bug one level up — 132 fixed the detection LIST, these tests pin that
// detection can never again run only at spawn). A regression here is
// invisible to launch-path tests because relaunching reassembles with the
// block already in the blueprint.
//
// Also pins the drill exclusion: the drill is Weapon-category (hotbar) but
// its behaviour belongs to RobotDrillBinder. Without the ShouldBind skip,
// RobotWeaponBinder's generic fallthrough stacked WeaponBlock (turret aim +
// hitscan fire) onto every drill on an armed bot.
//
// PATTERN: synthetic reflection-built defs + library + blueprint through the
// REAL ChassisAssembler.Assemble path (Bot options), then a post-assembly
// grid.PlaceBlock — the exact garage build-mode sequence.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Gameplay;
using Robogame.Input;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Tests.PlayMode.Gameplay
{
    public class BinderRetrofitTests
    {
        // Bot options add a PlayerController, which errors without an
        // IInputSource on the root — real bot spawns attach an AI input
        // source before assembly, so the rig does the same with a stub.
        private sealed class StubInput : MonoBehaviour, IInputSource
        {
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => 0f;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool FlipPressed => false;
            public bool HookReleasePressed => false;
            public bool GetModulePressed(int slot) => false;
        }

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private static BlockDefinition MakeDef(string id, BlockCategory cat)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, cat);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_mass", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 1f);
            return def;
        }

        private static BlockDefinitionLibrary MakeLibrary(params BlockDefinition[] defs)
        {
            BlockDefinitionLibrary lib = ScriptableObject.CreateInstance<BlockDefinitionLibrary>();
            typeof(BlockDefinitionLibrary).GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(lib, defs);
            return lib;
        }

        private static ChassisBlueprint MakeBlueprint(params ChassisBlueprint.Entry[] entries)
        {
            ChassisBlueprint bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.SetEntries(entries);
            return bp;
        }

        // Assemble a bot chassis through the real assembler, then park it
        // kinematic (no ground plane in these tests — physics is not what is
        // being pinned).
        private BlockGrid AssembleParked(ChassisBlueprint bp, BlockDefinitionLibrary lib)
        {
            _root = new GameObject("BinderRetrofitRig");
            _root.AddComponent<StubInput>();
            ChassisAssembler.Assemble(_root, bp, lib, AssemblyOptions.Bot());
            Rigidbody rb = _root.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            return _root.GetComponent<BlockGrid>();
        }

        [Test]
        public void FirstWeapon_PlacedOnWeaponlessBot_GetsBehaviourImmediately()
        {
            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure);
            BlockDefinition mortar = MakeDef(BlockIds.Mortar, BlockCategory.Weapon);
            BlockDefinitionLibrary lib = MakeLibrary(cube, mortar);
            BlockGrid grid = AssembleParked(
                MakeBlueprint(new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero)), lib);

            Assert.IsNotNull(_root.GetComponent<RobotWeaponBinder>(),
                "A weaponless-at-spawn chassis must still carry the weapon binder, " +
                "or the first weapon placed in the garage stays a dead red host cube.");

            BlockBehaviour placed = grid.PlaceBlock(mortar, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(placed, "PlaceBlock rejected the mortar.");
            Assert.IsNotNull(placed.GetComponent<MortarBlock>(),
                "The binder must attach MortarBlock at placement time, not only at spawn.");
            Assert.IsNotNull(placed.transform.Find("Yoke"),
                "MortarBlock's rig (procedural yoke for a model-less test def) must exist — " +
                "its absence is the visible red-cube symptom.");
        }

        [Test]
        public void FirstWheel_PlacedOnWheellessBot_GetsWheelBlockImmediately()
        {
            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure);
            BlockDefinition wheel = MakeDef(BlockIds.Wheel, BlockCategory.Movement);
            BlockDefinitionLibrary lib = MakeLibrary(cube, wheel);
            BlockGrid grid = AssembleParked(
                MakeBlueprint(new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero)), lib);

            BlockBehaviour placed = grid.PlaceBlock(
                wheel, new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1));
            Assert.IsNotNull(placed, "PlaceBlock rejected the wheel.");
            Assert.IsNotNull(placed.GetComponent<WheelBlock>(),
                "The wheel binder must attach WheelBlock at placement time — a gated " +
                "binder left the first wheel on a wheel-less bot as a bare host cube.");
        }

        [Test]
        public void Drill_PlacedOnArmedBot_IsNotBoundAsAWeapon()
        {
            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure);
            BlockDefinition smg = MakeDef(BlockIds.Weapon, BlockCategory.Weapon);
            BlockDefinition drill = MakeDef(BlockIds.Drill, BlockCategory.Weapon);
            BlockDefinitionLibrary lib = MakeLibrary(cube, smg, drill);
            BlockGrid grid = AssembleParked(
                MakeBlueprint(
                    new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero),
                    new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(1, 0, 0))), lib);

            Assume.That(_root.GetComponent<RobotWeaponBinder>(), Is.Not.Null);

            BlockBehaviour placed = grid.PlaceBlock(drill, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(placed, "PlaceBlock rejected the drill.");
            Assert.IsNull(placed.GetComponent<WeaponBlock>(),
                "The weapon binder must skip the drill (Weapon-category for the hotbar " +
                "only) — WeaponBlock on a drill yaw-tracks the reticle and fires hitscan " +
                "on the dig trigger.");
            Assert.IsNotNull(placed.GetComponent<Robogame.Voxel.DrillBlock>(),
                "RobotDrillBinder still owns the drill's behaviour.");
        }
    }
}
