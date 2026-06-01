using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Input;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Combat
{
    /// <summary>
    /// Wiring + lifecycle tests for the multi-slot module system: each placed
    /// module registers an independently-cooled slot, firing one slot's key
    /// engages only that cooldown, the spring slot is gated on ground (and
    /// launches when it has it), smoke raises the healthbar-hidden flag, and a
    /// destroyed carrier empties its slot.
    /// </summary>
    public sealed class ModuleSystemTests
    {
        private sealed class StubInput : MonoBehaviour, IInputSource
        {
            private readonly bool[] _pressed = new bool[4];
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => 0f;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool GetModulePressed(int slot) => slot >= 0 && slot < 4 && _pressed[slot];
            public void SetPressed(int slot, bool v) { if (slot >= 0 && slot < 4) _pressed[slot] = v; }
        }

        private GameObject _root;
        private GameObject _ground;
        private BlockGrid _grid;
        private StubInput _input;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ground != null) Object.Destroy(_ground);
        }

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, BlockCategory.Module);
            return def;
        }

        // Build a deactivated chassis, place the given module blocks, then
        // activate so every OnEnable fires once with the system present — the
        // deactivated-build dance ChassisAssembler uses. Slot order follows the
        // placement order here.
        private ModuleSystem BuildChassis(params (string id, Vector3Int cell, Vector3Int up)[] modules)
        {
            _root = new GameObject("ModuleChassis");
            _root.SetActive(false);
            Rigidbody rb = _root.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 1f;
            rb.linearDamping = 0f;
            _grid = _root.AddComponent<BlockGrid>();
            _input = _root.AddComponent<StubInput>();
            ModuleSystem sys = _root.AddComponent<ModuleSystem>();

            foreach (var m in modules)
            {
                BlockBehaviour bb = _grid.PlaceBlock(MakeDef(m.id), m.cell, m.up);
                Assert.IsNotNull(bb, $"PlaceBlock failed for {m.id} at {m.cell}");
                bb.gameObject.AddComponent<ModuleBlock>();
            }

            _root.SetActive(true);
            return sys;
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.transform.position = new Vector3(0f, -1.5f, 0f); // top ≈ -1.0, within the 2.5 m probe
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator ModulePresent_SlotIsReady()
        {
            ModuleSystem sys = BuildChassis((BlockIds.ModuleEmp, Vector3Int.zero, Vector3Int.up));
            yield return null;

            Assert.IsTrue(sys.HasAnyModule, "System should see the registered module block.");
            Assert.AreEqual(1, sys.Slots.Count, "One module → one slot.");
            Assert.AreEqual(ModuleKind.EmpBurst, sys.Slots[0].Kind, "Kind is resolved from the block id.");
            Assert.IsTrue(sys.Slots[0].IsAvailable, "A fresh EMP slot should be ready to fire.");
        }

        [UnityTest]
        public IEnumerator TwoModules_FireIndependently()
        {
            // Two EMP modules → two slots with independent cooldowns. Pressing
            // slot 0's key must not touch slot 1.
            ModuleSystem sys = BuildChassis(
                (BlockIds.ModuleEmp, new Vector3Int(0, 0, 0), Vector3Int.up),
                (BlockIds.ModuleEmp, new Vector3Int(1, 0, 0), Vector3Int.up));
            yield return null;
            Assume.That(sys.Slots.Count, Is.EqualTo(2));
            Assume.That(sys.Slots[0].IsAvailable && sys.Slots[1].IsAvailable, Is.True);

            _input.SetPressed(0, true);
            yield return null; // Update polls input + fires slot 0
            _input.SetPressed(0, false);

            Assert.IsFalse(sys.Slots[0].IsAvailable, "Fired slot must be on cooldown.");
            Assert.Less(sys.Slots[0].ReadyFraction, 1f, "Fired slot's ReadyFraction drops below 1.");
            Assert.IsTrue(sys.Slots[1].IsAvailable, "The unfired slot must stay ready (independent cooldown).");
        }

        [UnityTest]
        public IEnumerator DestroyingCarrier_EmptiesSlot()
        {
            ModuleSystem sys = BuildChassis((BlockIds.ModuleEmp, Vector3Int.zero, Vector3Int.up));
            yield return null;
            Assume.That(sys.HasAnyModule, Is.True);

            Object.Destroy(sys.Slots[0].Block.gameObject);
            yield return null; // OnDisable unregisters

            Assert.IsFalse(sys.HasAnyModule, "Destroying the carrier must empty its slot (functional disable).");
        }

        [UnityTest]
        public IEnumerator SpringSlot_GroundedGate_AndLaunch()
        {
            // Underside spring: up = chassis-down → launch = world-up.
            ModuleSystem sys = BuildChassis((BlockIds.Spring, Vector3Int.zero, new Vector3Int(0, -1, 0)));
            yield return null;

            // No ground yet → spring is off-cooldown but contextually blocked.
            Assert.GreaterOrEqual(sys.Slots[0].ReadyFraction, 1f, "Spring starts recharged.");
            Assert.IsFalse(sys.Slots[0].IsAvailable, "Spring must be unavailable with no ground to push off.");

            CreateGround();
            yield return null;
            Assert.IsTrue(sys.Slots[0].IsAvailable, "With ground in range the spring becomes available.");

            Rigidbody rb = _root.GetComponent<Rigidbody>();
            _input.SetPressed(0, true);
            yield return null;                       // Update fires the launch impulse
            _input.SetPressed(0, false);
            yield return new WaitForFixedUpdate();   // integrate

            Assert.Greater(rb.linearVelocity.y, 1f,
                $"Underside spring must launch the chassis up; got {rb.linearVelocity}.");
            Assert.IsFalse(sys.Slots[0].IsAvailable, "After launching, the spring is on cooldown.");
        }

        [UnityTest]
        public IEnumerator SmokeFire_HidesHealthbar()
        {
            ModuleSystem sys = BuildChassis((BlockIds.ModuleSmoke, Vector3Int.zero, Vector3Int.up));
            yield return null;
            Assume.That(sys.HealthbarHidden, Is.False, "Healthbar visible before deploying smoke.");

            _input.SetPressed(0, true);
            yield return null; // Update fires smoke
            _input.SetPressed(0, false);

            Assert.IsTrue(sys.HealthbarHidden, "Deploying smoke must hide the healthbar surrogate.");
        }
    }
}
