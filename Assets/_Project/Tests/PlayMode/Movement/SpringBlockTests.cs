// =============================================================================
// Playmode tests for SpringBlock (session 104) — the jump-spring block.
// Verifies the load-bearing behaviour: rising-edge launch along the mount-
// inward axis, cooldown gating against spam, destruction disarm, and that the
// launch direction tracks the block's mount orientation.
//
// Pattern follows HoverBladeBlockTests.cs — hand-built chassis hierarchy, an
// IInputSource stub on the root, no prefabs / scenes required.
// =============================================================================

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Input;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class SpringBlockTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private JumpInputStub _input;

        // Minimal IInputSource: only Vertical is meaningful for the spring;
        // everything else stays neutral. Vertical is settable so a test can
        // raise / drop the jump input between fixed steps.
        private sealed class JumpInputStub : MonoBehaviour, IInputSource
        {
            public float VerticalValue;
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => VerticalValue;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool ModulePressed => false;
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false;   // assert on the impulse alone
            _chassisRb.isKinematic = false;
            _chassisRb.mass = 1f;            // 1 kg → Δv = impulse
            _chassisRb.linearDamping = 0f;   // velocity persists between steps
            _grid = _root.AddComponent<BlockGrid>();
            // Input stub MUST exist before the SpringBlock so its OnEnable
            // GetComponentInParent<IInputSource> finds it.
            _input = _root.AddComponent<JumpInputStub>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, BlockCategory.Movement);
            return def;
        }

        // Place a spring at the given cell with the given mount-up. PlaceBlock
        // rotates the block so transform.up = the mount-outward world axis;
        // the spring launches along -transform.up.
        private SpringBlock PlaceSpring(Vector3Int cell, Vector3Int up)
        {
            BlockDefinition def = MakeDef(BlockIds.Spring);
            BlockBehaviour bb = _grid.PlaceBlock(def, cell, up);
            Assert.IsNotNull(bb, $"PlaceBlock failed for spring at {cell}");
            return bb.gameObject.AddComponent<SpringBlock>();
        }

        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator SpringBlock_VerticalRisingEdge_LaunchesChassisUp_ForUndersideMount()
        {
            // Underside mount: up = chassis-down → transform.up = world-down →
            // launch = -transform.up = world-up.
            PlaceSpring(Vector3Int.zero, new Vector3Int(0, -1, 0));
            _input.VerticalValue = 1f;       // jump pressed (rising edge this step)

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 v = _chassisRb.linearVelocity;
            Assert.Greater(v.y, 1f, $"Underside spring must launch the chassis up; got velocity {v}.");
            Assert.Greater(v.y, Mathf.Abs(v.x), "Launch must be dominantly +Y, not sideways.");
            Assert.Greater(v.y, Mathf.Abs(v.z), "Launch must be dominantly +Y, not sideways.");
        }

        [UnityTest]
        public IEnumerator SpringBlock_Cooldown_PreventsImmediateRefire()
        {
            PlaceSpring(Vector3Int.zero, new Vector3Int(0, -1, 0));

            // First launch.
            _input.VerticalValue = 1f;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float vAfterFirst = _chassisRb.linearVelocity.y;
            Assert.Greater(vAfterFirst, 1f, "Sanity: first press must launch.");

            // Release, then press again within the cooldown window.
            _input.VerticalValue = 0f;
            yield return new WaitForFixedUpdate();
            _input.VerticalValue = 1f;        // fresh rising edge, but still on cooldown
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // No second impulse → velocity unchanged (no gravity, no drag).
            Assert.AreEqual(vAfterFirst, _chassisRb.linearVelocity.y, 0.5f,
                "Cooldown must swallow a second press before it expires — velocity must not jump again.");
        }

        [UnityTest]
        public IEnumerator SpringBlock_Destroyed_AppliesNoImpulse()
        {
            SpringBlock spring = PlaceSpring(Vector3Int.zero, new Vector3Int(0, -1, 0));
            BlockBehaviour bb = spring.GetComponent<BlockBehaviour>();

            // Destroy the block (production path: HP → 0 fires Destroyed).
            bb.TakeDamage(1000f);
            _chassisRb.linearVelocity = Vector3.zero;
            _chassisRb.angularVelocity = Vector3.zero;

            _input.VerticalValue = 1f;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Less(_chassisRb.linearVelocity.magnitude, 0.01f,
                "A destroyed spring must apply no impulse.");
        }

        [UnityTest]
        public IEnumerator SpringBlock_MountOrientation_LaunchDirFollowsUp()
        {
            // Side mount: up = chassis +X → transform.up = world +X → launch
            // = -transform.up = world -X. Confirms the generic direction model
            // (a side spring dashes the chassis, not jumps it).
            PlaceSpring(Vector3Int.zero, new Vector3Int(1, 0, 0));
            _input.VerticalValue = 1f;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 v = _chassisRb.linearVelocity;
            Assert.Less(v.x, -1f, $"A +X-mounted spring must launch toward -X; got {v}.");
            Assert.Greater(Mathf.Abs(v.x), Mathf.Abs(v.y), "Launch must be dominantly along -X.");
            Assert.Greater(Mathf.Abs(v.x), Mathf.Abs(v.z), "Launch must be dominantly along -X.");
        }
    }
}
