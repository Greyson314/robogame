// =============================================================================
// Playmode tests for HoverBladeBlock — verifies the load-bearing invariants
// from session-99's design: zero stratosphere propulsion, terraformed-pit
// fall-through, N² lift scaling, and per-corner failure on destruction.
//
// Pattern follows RotorBlockTests.cs — minimal chassis hierarchy built by
// hand, ground collider as a separate GameObject in the test scene, no
// prefabs / scenes required.
// =============================================================================

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class HoverBladeBlockTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false; // tests assert on lift alone, not net of gravity
            _chassisRb.isKinematic = false;
            _chassisRb.mass = 1f; // 1 kg → easy v = F·dt arithmetic
            _grid = _root.AddComponent<BlockGrid>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ground != null) Object.Destroy(_ground);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, 100f);
            typeof(BlockDefinition)
                .GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, BlockCategory.Movement);
            return def;
        }

        // Place a hover blade at the given cell. Position is at world-space
        // gridPos × cellSize relative to chassis root. Returns the block
        // component (caller can mutate Dims for size tests).
        private HoverBladeBlock PlaceHoverBlade(Vector3Int cell, int size = 2)
        {
            BlockDefinition def = MakeDef(BlockIds.HoverBlade);
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed for hover blade at {cell}");
            // Set the size via Dims.x — must happen before HoverBladeBlock
            // is added (its OnEnable reads Dims to compute lift scale).
            bb.SetDims(new Vector3(size, 0f, 0f));
            HoverBladeBlock blade = bb.gameObject.AddComponent<HoverBladeBlock>();
            return blade;
        }

        // Create a flat ground plane at the given world y. Sits on the
        // default layer so the blade's _groundMask (all layers) hits it.
        private void CreateGround(float worldY)
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, worldY - 0.5f, 0f);
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);
            // Strip the Rigidbody check — the blade's RaycastIgnoringSelf
            // filters by chassis Rigidbody, and the ground has none, so
            // it always hits.
        }

        // -----------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------

        /// <summary>
        /// No-stratosphere invariant: when the gap to ground is >= target
        /// altitude (2.5 m), the blade contributes zero force. The spring
        /// term clamps to zero on the upper side, so a chassis sitting at
        /// (or above) the target hover height has zero added velocity from
        /// hover blades — only gravity can pull it back into range.
        /// </summary>
        [UnityTest]
        public IEnumerator HoverBladeBlock_AppliesZeroForce_WhenGapExceedsTargetAltitude()
        {
            // Blade at origin, ground 3 m below — within max raycast range
            // (4 m) but beyond target altitude (2.5 m). Spring force must
            // clamp to zero.
            PlaceHoverBlade(Vector3Int.zero);
            CreateGround(worldY: -3f);
            // Two fixed updates: one for OnEnable to settle, one for
            // FixedUpdate to run with stabilised refs.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(_chassisRb.linearVelocity.magnitude, Is.LessThan(0.01f),
                "Blade should produce zero lift when above target altitude — chassis must not accelerate.");
        }

        /// <summary>
        /// Terraformed-pit invariant: when the raycast finds no ground
        /// within max range, the blade contributes zero force. The
        /// chassis simply falls; no fallback clamp keeps it suspended in
        /// midair.
        /// </summary>
        [UnityTest]
        public IEnumerator HoverBladeBlock_AppliesZeroForce_WhenRaycastMisses()
        {
            // No ground in the scene — ray misses entirely.
            PlaceHoverBlade(Vector3Int.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(_chassisRb.linearVelocity.magnitude, Is.LessThan(0.01f),
                "Blade should produce zero lift when raycast misses ground.");
        }

        /// <summary>
        /// N² scaling invariant: lift scales quadratically with blade size.
        /// A size-4 blade produces 4× the lift of a size-2 blade at the
        /// same gap. We compare two separate test rigs (size-2 vs size-4)
        /// instead of one rig with multiple blades because the chassis
        /// mass is shared, and per-instance comparison is the cleaner
        /// way to express the contract.
        /// </summary>
        [UnityTest]
        public IEnumerator HoverBladeBlock_SpringForceScalesWithNSquared()
        {
            // Rig 1: size-2 blade, ground at gap = 1.0 m (well below
            // target 2.5 m → strong spring force).
            PlaceHoverBlade(Vector3Int.zero, size: 2);
            CreateGround(worldY: -1f);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float v_size2 = _chassisRb.linearVelocity.magnitude;

            // Rig 2: tear down, rebuild with size-4 blade, same gap.
            TearDown();
            SetUp();
            PlaceHoverBlade(Vector3Int.zero, size: 4);
            CreateGround(worldY: -1f);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float v_size4 = _chassisRb.linearVelocity.magnitude;

            // Size-4 = (4/2)² = 4× spring constant. With the same gap,
            // same chassis mass, same dt, accumulated velocity over the
            // first few ticks should be ~4× as well. Damping eats into
            // this proportionally but the *initial* impulse ratio holds.
            Assert.That(v_size2, Is.GreaterThan(0.01f),
                "Size-2 blade should produce measurable lift.");
            float ratio = v_size4 / v_size2;
            Assert.That(ratio, Is.InRange(3.0f, 5.0f),
                $"Size-4 lift should be ~4× size-2 lift (got {ratio:F2}× — out of [3.0, 5.0] tolerance).");
        }

        /// <summary>
        /// Per-corner failure invariant: a destroyed blade applies no
        /// force, even with ground in range. The chassis loses lift on
        /// the destroyed corner (intended dramatic behavior — no
        /// load redistribution to surviving blades).
        /// </summary>
        [UnityTest]
        public IEnumerator HoverBladeBlock_DestroyedBlade_AppliesNoForce()
        {
            HoverBladeBlock blade = PlaceHoverBlade(Vector3Int.zero);
            CreateGround(worldY: -1f);

            // Baseline: confirm the blade DOES apply force before destruction.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float baselineSpeed = _chassisRb.linearVelocity.magnitude;
            Assert.That(baselineSpeed, Is.GreaterThan(0.01f),
                "Sanity check: a live blade with ground in range should lift the chassis.");

            // Fire the BlockBehaviour.Destroyed event via reflection
            // (TakeDamage(MaxHealth) is the production-equivalent path).
            BlockBehaviour bb = blade.GetComponent<BlockBehaviour>();
            bb.TakeDamage(1000f);

            // Reset velocity so the assertion below measures post-destroy
            // contribution only.
            _chassisRb.linearVelocity = Vector3.zero;
            _chassisRb.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(_chassisRb.linearVelocity.magnitude, Is.LessThan(0.01f),
                "Destroyed blade should apply zero force — observed velocity change indicates lift still active.");
        }
    }
}
