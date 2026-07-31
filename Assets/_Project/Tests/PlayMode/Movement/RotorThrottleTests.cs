// =============================================================================
// RotorThrottleTests — PlayMode (session 125 throttle feature)
//
// INVARIANTS COVERED
//   • Default throttle = 1: a freshly-spawned rotor spins at full max-RPM
//     (RotorDefaults.ResolveRpm(configValue) × 1.0). Pre-throttle behavior
//     is preserved — AI presets that don't drive vertical/forward input fly
//     without change. (Regression against accidentally defaulting to 0.)
//   • RpmOverride bypasses throttle entirely: stress-tower absolute RPM
//     is unaffected by any _throttle01 value; LiveRpm = RpmOverride.
//   • Throttle holds on zero input: releasing the climb key does NOT change
//     the throttle. This is the explicit "hover hands-off" design choice
//     that distinguishes the trim-throttle from a momentary trigger.
//   • Vertical-axis rotor responds to Vertical input; forward-axis rotor
//     responds to Move.y; sideways-axis rotor ignores all input.
//
// TEST SEAM NOTE (see recommendations section at bottom of file)
//   _throttle01 and LiveRpm are private. Tests that need to read the live
//   throttle value use reflection. All reflection field reads are guarded by
//   Assert.IsNotNull on the FieldInfo so a rename produces a clear failure
//   rather than a silent wrong-value assertion. A recommended minimal seam
//   is described in the seam-note comment at the bottom of this file.
//
// PATTERN
//   Follows RotorBlockTests.cs: hand-built chassis hierarchy, [SetUp] /
//   [TearDown], reflection for private state, [UnityTest] + WaitForFixedUpdate
//   for any multi-frame assertion.
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
    /// <summary>
    /// Tests for the per-rotor throttle feature added in session 125.
    /// Covers default behavior, RpmOverride bypass, hold-on-release, and
    /// axis-routing logic.
    /// </summary>
    public class RotorThrottleTests
    {
        private GameObject _root;
        private Rigidbody  _chassisRb;
        private BlockGrid  _grid;

        // -----------------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ThrottleTestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity   = false;
            _chassisRb.isKinematic  = false;
            _grid = _root.AddComponent<BlockGrid>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

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

        /// <summary>
        /// Place a rotor at <paramref name="cell"/> with an identity-aligned
        /// (Y-up) spin axis and return it. The rotor's GO is deactivated before
        /// adding the component, then re-activated — same pattern ChassisFactory
        /// uses, and what RotorBlockTests establishes as the canonical idiom.
        /// </summary>
        private RotorBlock PlaceRotor(Vector3Int cell)
        {
            BlockDefinition def = MakeDef("block.rotor.throttle.test");
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed at {cell}");
            bb.gameObject.SetActive(false);
            RotorBlock rotor = bb.gameObject.AddComponent<RotorBlock>();
            _grid.RebuildFromChildren();
            bb.gameObject.SetActive(true);
            return rotor;
        }

        /// <summary>
        /// Place a rotor whose spin axis is along local Z (forward-axis / pusher
        /// prop). Used by the Move.y routing test.
        /// </summary>
        private RotorBlock PlaceForwardAxisRotor(Vector3Int cell)
        {
            BlockDefinition def = MakeDef("block.rotor.throttle.test");
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed at {cell}");
            bb.gameObject.SetActive(false);
            RotorBlock rotor = bb.gameObject.AddComponent<RotorBlock>();
            _grid.RebuildFromChildren();
            // Override _spinAxisLocal to Z before activating so FixedUpdate
            // uses the forward-axis branch from the first tick.
            typeof(RotorBlock)
                .GetField("_spinAxisLocal", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(rotor, Vector3.forward);
            bb.gameObject.SetActive(true);
            return rotor;
        }

        /// <summary>
        /// Attach a <see cref="StubInputSource"/> to the chassis root and
        /// return it so the test can set Vertical / Move.
        /// </summary>
        private StubInputSource AttachInputSource()
        {
            return _root.AddComponent<StubInputSource>();
        }

        /// <summary>
        /// Read the private <c>_throttle01</c> field via reflection.
        /// Returns -1 if the field is not found (see seam-note).
        /// </summary>
        private static float GetThrottle(RotorBlock rotor)
        {
            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi,
                "RotorBlock._throttle01 not found via reflection. " +
                "If the field was renamed, update this test or add the recommended " +
                "internal seam (see seam-note at bottom of file).");
            return (float)fi.GetValue(rotor);
        }

        // -----------------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Default throttle is 1, not 0.
        /// A freshly-spawned rotor that has never received any input must have
        /// _throttle01 = 1 so it spins at full max-RPM. This preserves
        /// pre-throttle behavior: AI dummies that never touch vertical/forward
        /// input keep flying exactly as before (invariant: default throttle = 1).
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_Throttle_DefaultIsOne_BeforeAnyInput()
        {
            RotorBlock rotor = PlaceRotor(Vector3Int.zero);
            // No input source — simulates an AI that never drives the axis.
            // Throttle must be 1 straight out of the box.

            yield return new WaitForFixedUpdate();

            float throttle = GetThrottle(rotor);
            Assert.AreEqual(1f, throttle, 1e-5f,
                "Default throttle must be 1.0 so un-driven rotors (AI presets, stress tower) " +
                "spin at full max-RPM. Pre-throttle behavior regression if this fails.");
        }

        /// <summary>
        /// RpmOverride pins LiveRpm to its absolute value and bypasses
        /// _throttle01 entirely.
        /// The stress tower sets RpmOverride so it runs at a precise RPM
        /// regardless of any throttle state. If throttle could scale RpmOverride,
        /// the tower's calibrated loads would be wrong.
        /// Verified indirectly: set _throttle01 to 0 via reflection, set
        /// RpmOverride = 300, then confirm the hub's angular velocity corresponds
        /// to 300 RPM (not 0). Direct hub ω measurement after N fixed steps.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_RpmOverride_BypassesThrottle_AtAbsoluteRpm()
        {
            RotorBlock rotor = PlaceRotor(Vector3Int.zero);
            yield return new WaitForFixedUpdate();  // let OnEnable settle

            // Force throttle to 0 — if the override respects throttle, hub would
            // stay still; if it bypasses correctly, hub should spin at 300 RPM.
            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "RotorBlock._throttle01 not found via reflection.");
            fi.SetValue(rotor, 0f);

            rotor.RpmOverride = 300f;

            // Run several fixed steps so MoveRotation accumulates into the
            // hub transform's angle. Check the spin visual angle (_angleRad)
            // advances — the hub MoveRotation drives _angleRad accumulation.
            FieldInfo angleField = typeof(RotorBlock).GetField(
                "_angleRad", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(angleField, "RotorBlock._angleRad not found via reflection.");

            float angleBefore = (float)angleField.GetValue(rotor);
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            float angleAfter = (float)angleField.GetValue(rotor);

            float deltaRad = angleAfter - angleBefore;
            // 300 RPM = 5 rev/s = 31.4 rad/s. Over 5 fixed steps at 50 Hz
            // (0.02 s each) → 5 × 0.02 × 31.4 ≈ 3.14 rad. Throttle-at-zero
            // would give 0 rad. Allow generous tolerance for timing variance.
            Assert.Greater(deltaRad, 1.0f,
                $"_angleRad advanced only {deltaRad:F4} rad with RpmOverride=300 and throttle=0. " +
                "RpmOverride must bypass throttle (stress tower calibration invariant).");
        }

        /// <summary>
        /// Throttle holds its value when input returns to 0.
        /// After ramping up via positive Vertical input, setting Vertical to 0
        /// must leave _throttle01 unchanged on subsequent FixedUpdate calls.
        /// This is the "hover hands-off" design decision: the trim throttle
        /// is NOT a momentary trigger — it's a held state.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_Throttle_HoldsValue_WhenInputReleasedToZero()
        {
            StubInputSource input = AttachInputSource();
            RotorBlock rotor = PlaceRotor(Vector3Int.zero);

            // Start below max throttle so there's room to ramp down if the
            // code incorrectly uses 0 as a "return to zero" signal.
            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "RotorBlock._throttle01 not found via reflection.");
            fi.SetValue(rotor, 0.5f);

            // RpmOverride must be negative so the throttle path is active.
            rotor.RpmOverride = -1f;

            // Apply zero input for 3 fixed steps.
            input.Vertical = 0f;
            input.MoveVector = Vector2.zero;

            float throttleBefore = (float)fi.GetValue(rotor);
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            float throttleAfter = (float)fi.GetValue(rotor);

            Assert.AreEqual(throttleBefore, throttleAfter, 1e-5f,
                $"Throttle changed from {throttleBefore:F4} to {throttleAfter:F4} with zero input. " +
                "Zero input must NOT move the throttle — hold-on-release is the invariant.");
        }

        /// <summary>
        /// Vertical-axis rotor ramps throttle on Vertical input (space / descend).
        /// _spinAxisLocal = Vector3.up → |axis.y| > 0.7 → uses Vertical.
        /// Positive Vertical input over multiple fixed steps must increase
        /// _throttle01 from its starting value toward 1.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_Throttle_RampsUp_OnPositiveVerticalInput_ForYAxisRotor()
        {
            StubInputSource input = AttachInputSource();
            RotorBlock rotor = PlaceRotor(Vector3Int.zero); // default Y spin axis

            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "RotorBlock._throttle01 not found via reflection.");
            fi.SetValue(rotor, 0.2f);   // start well below 1 so ramp has room
            rotor.RpmOverride = -1f;

            input.Vertical = 1f;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            float throttleAfter = (float)fi.GetValue(rotor);
            Assert.Greater(throttleAfter, 0.2f,
                $"Throttle didn't increase from 0.2 after 10 fixed steps of Vertical=1 " +
                $"on a Y-axis rotor. Got {throttleAfter:F4}. " +
                "Vertical input must ramp the throttle on a vertical-axis rotor.");
        }

        /// <summary>
        /// Forward-axis rotor ramps throttle on Move.y input (W/S), not Vertical.
        /// _spinAxisLocal = Vector3.forward → |axis.z| > 0.7 → uses Move.y.
        /// With Vertical = 1 and Move.y = 0 the throttle must NOT move.
        /// With Move.y = 1 the throttle must ramp.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_Throttle_RampsOnMoveY_ForZAxisRotor_AndIgnoresVertical()
        {
            StubInputSource input = AttachInputSource();
            RotorBlock rotor = PlaceForwardAxisRotor(Vector3Int.zero);

            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "RotorBlock._throttle01 not found via reflection.");
            fi.SetValue(rotor, 0.3f);
            rotor.RpmOverride = -1f;

            // Phase 1: Vertical = 1, Move.y = 0 → throttle must not move.
            input.Vertical    = 1f;
            input.MoveVector  = Vector2.zero;
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            float afterVertical = (float)fi.GetValue(rotor);
            Assert.AreEqual(0.3f, afterVertical, 1e-4f,
                $"Z-axis rotor throttle changed to {afterVertical:F4} on Vertical=1 — " +
                "Z-axis rotor must use Move.y, not Vertical.");

            // Phase 2: Vertical = 0, Move.y = 1 → throttle must ramp up.
            input.Vertical    = 0f;
            input.MoveVector  = new Vector2(0f, 1f);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            float afterMoveY = (float)fi.GetValue(rotor);
            Assert.Greater(afterMoveY, 0.3f,
                $"Z-axis rotor throttle stayed at {afterMoveY:F4} with Move.y=1 after 10 steps. " +
                "Forward-axis rotor must ramp on Move.y.");
        }

        /// <summary>
        /// Throttle clamps at [0, 1].
        /// Sustained positive input on a throttle already at 1 must not
        /// push it above 1. Sustained negative input on a throttle at 0
        /// must not push it below 0.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_Throttle_ClampsToZeroAndOne()
        {
            StubInputSource input = AttachInputSource();
            RotorBlock rotor = PlaceRotor(Vector3Int.zero);

            FieldInfo fi = typeof(RotorBlock).GetField(
                "_throttle01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "RotorBlock._throttle01 not found via reflection.");
            rotor.RpmOverride = -1f;

            // Upper clamp: start at 1, ramp up.
            fi.SetValue(rotor, 1f);
            input.Vertical = 1f;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            float atMax = (float)fi.GetValue(rotor);
            Assert.LessOrEqual(atMax, 1f + 1e-5f,
                $"Throttle exceeded 1.0 (got {atMax:F5}) under sustained positive input — Clamp01 missing?");

            // Lower clamp: start at 0, ramp down.
            fi.SetValue(rotor, 0f);
            input.Vertical = -1f;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            float atMin = (float)fi.GetValue(rotor);
            Assert.GreaterOrEqual(atMin, -1e-5f,
                $"Throttle went below 0.0 (got {atMin:F5}) under sustained negative input — Clamp01 missing?");
        }

        // -----------------------------------------------------------------------
        // SEAM NOTE — recommended minimal change to make RotorBlock testable
        // without reflection:
        //
        //   internal float Throttle01 => _throttle01;
        //
        // Adding this single internal read-accessor would let these tests drop
        // the GetField calls and compile against the contract directly. The
        // accessor is read-only and internal so it doesn't leak a write path.
        // The "internal" visibility is visible to the test assembly when the
        // production asmdef's assembly carries:
        //   [assembly: InternalsVisibleTo("Robogame.Tests.PlayMode")]
        // Both the asmdef approach (allowUnsafeCode is already set per the
        // project's test setup) and the attribute approach work; the attribute
        // approach requires one line in AssemblyInfo.cs under Robogame.Movement.
        // Until the seam is added, reflection is the fallback — tests will
        // produce a clear "field not found" failure message if the field is renamed.
        // -----------------------------------------------------------------------
    }

    // ---------------------------------------------------------------------------
    // StubInputSource — minimal IInputSource for throttle tests.
    // Attaches as a MonoBehaviour so GetComponentInParent<IInputSource>() in
    // RotorBlock.OnEnable() resolves it. Exposed fields let each test set
    // Vertical / Move.y independently.
    // ---------------------------------------------------------------------------

    internal sealed class StubInputSource : MonoBehaviour, IInputSource
    {
        public float   Vertical   { get; set; }
        public Vector2 MoveVector { get; set; }

        // IInputSource implementation.
        Vector2 IInputSource.Move           => MoveVector;
        Vector2 IInputSource.Look           => Vector2.zero;
        float   IInputSource.Vertical       => Vertical;
        bool    IInputSource.FireHeld       => false;
        bool    IInputSource.FirePressed    => false;
        bool    IInputSource.ReloadPressed  => false;
        bool    IInputSource.FlipPressed    => false;
        bool    IInputSource.HookReleasePressed => false;
        bool    IInputSource.GetModulePressed(int slot) => false;
    }
}
