// GrappleMagnetBlock static-swing latch (LOG-171). A flight hit on static
// geometry (no attachedRigidbody — trees, terrain, walls) anchors the tip
// as a KINEMATIC world pin and the chassis↔tip ConfigurableJoint leash
// becomes the entire pendulum constraint. These tests pin that contract:
// no SpringJoint tether exists, the stiffer swing leash constants are the
// ones on the joint, reel input actually changes the constraint radius,
// and destroying the joint on release leaves chassis momentum untouched.
//
// Naming follows the project convention:
//   {ClassName}Tests.{Method}_{Scenario}_{ExpectedOutcome}.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Combat;
using Robogame.Input;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class GrappleSwingTests
    {
        private GameObject _chassisGo;
        private Rigidbody _chassisRb;
        private GameObject _blockGo;
        private GrappleMagnetBlock _block;
        private GameObject _tipGo;
        private Rigidbody _tipRb;

        // ------------------------------------------------------------------
        // Stub input — only Vertical matters here.
        // ------------------------------------------------------------------

        private sealed class StubInput : IInputSource
        {
            public float VerticalValue;
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => VerticalValue;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool GetModulePressed(int slot) => false;
            public bool FlipPressed => false;
            public bool HookReleasePressed => false;
        }

        // ------------------------------------------------------------------
        // Reflection helpers — same pattern as GrappleMagnetTests: drive the
        // private latch path directly instead of the full Fire→flight→hit
        // state machine.
        // ------------------------------------------------------------------

        private static void SetPrivate(object o, string field, object value)
        {
            FieldInfo fi = o.GetType().GetField(
                field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"GrappleMagnetBlock.{field} not found — field renamed? " +
                                  "This test reflects on private state and must track renames.");
            fi.SetValue(o, value);
        }

        private static T GetPrivate<T>(object o, string field)
        {
            FieldInfo fi = o.GetType().GetField(
                field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"GrappleMagnetBlock.{field} not found — field renamed?");
            return (T)fi.GetValue(o);
        }

        private static void CallPrivate(object o, string method)
        {
            MethodInfo mi = o.GetType().GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, $"GrappleMagnetBlock.{method} not found — method renamed?");
            mi.Invoke(o, null);
        }

        // ------------------------------------------------------------------
        // SetUp / TearDown
        // ------------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            // Chassis body the leash connects back to. Gravity off so the
            // momentum assertions see only joint forces.
            _chassisGo = new GameObject("SwingChassis");
            _chassisRb = _chassisGo.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false;
            _chassisRb.mass = 30f;

            // Block parented under the chassis so the muzzle/leash anchor
            // geometry is chassis-local, as in a real bot.
            _blockGo = new GameObject("GrappleMagnetBlock");
            _blockGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            _block = _blockGo.AddComponent<GrappleMagnetBlock>();
            SetPrivate(_block, "_chassisRb", _chassisRb);

            // Tip body pinned where the flight cast "hit a tree": kinematic,
            // 10 m out — exactly the state TickFiring leaves it in before
            // BeginSwingLatch runs.
            _tipGo = new GameObject("SwingTip");
            _tipGo.transform.position = new Vector3(0f, 0f, 10f);
            _tipRb = _tipGo.AddComponent<Rigidbody>();
            _tipRb.isKinematic = true;
            _tipRb.useGravity = false;
            SetPrivate(_block, "_tipGo", _tipGo);
            SetPrivate(_block, "_tipRb", _tipRb);
        }

        [TearDown]
        public void TearDown()
        {
            if (_blockGo   != null) Object.Destroy(_blockGo);
            if (_chassisGo != null) Object.Destroy(_chassisGo);
            if (_tipGo     != null) Object.Destroy(_tipGo);
        }

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        /// <summary>
        /// A swing latch must keep the tip KINEMATIC (it is the immovable
        /// world anchor — non-kinematic would let the anchor itself get
        /// dragged off the tree), must create no SpringJoint (there is no
        /// target body to tether), and must put the stiffer swing constants
        /// on the leash joint rather than the enemy-drag constants.
        ///
        /// WHY: the whole swing design is "kinematic pin + one existing
        /// joint". If the tip goes dynamic or a tether appears, we've
        /// silently regressed into the enemy-latch code path and the
        /// pendulum will feel like bungee (or the anchor will fly away).
        /// </summary>
        [UnityTest]
        public IEnumerator BeginSwingLatch_OnStaticAnchor_KinematicTipSwingLeashNoTether()
        {
            CallPrivate(_block, "BeginSwingLatch");
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_block.IsSwinging,
                "IsSwinging must report true after a static latch — HUD and " +
                "follow-on systems key off this instead of re-deriving it.");
            Assert.IsTrue(_tipRb.isKinematic,
                "Tip must STAY kinematic during a static swing. A dynamic tip " +
                "has nothing holding it to the tree — the anchor itself would " +
                "get yanked toward the chassis on the first taut frame.");
            Assert.IsNull(_tipGo.GetComponent<SpringJoint>(),
                "No SpringJoint may exist during a static swing — the tether " +
                "is the enemy-latch constraint; a world anchor needs none.");

            ConfigurableJoint leash = _tipGo.GetComponent<ConfigurableJoint>();
            Assert.IsNotNull(leash,
                "The chassis↔tip ConfigurableJoint leash must exist — it IS " +
                "the swing constraint; without it the chassis just falls.");
            Assert.AreSame(_chassisRb, leash.connectedBody,
                "Leash must connect the tip anchor to the chassis Rigidbody.");

            float swingSpring = GetPrivate<float>(_block, "_swingLeashSpring");
            float dragSpring  = GetPrivate<float>(_block, "_leashSpring");
            Assert.AreEqual(swingSpring, leash.linearLimitSpring.spring, 1e-3f,
                "Swing latch must use _swingLeashSpring, not the enemy-drag " +
                "_leashSpring. The drag constants were tuned as a cushion for " +
                "towing a chassis; under sustained centripetal load they read " +
                "as bungee and the swing feel dies.");
            Assert.AreNotEqual(dragSpring, leash.linearLimitSpring.spring,
                "Swing and drag leash constants must stay distinct — if they " +
                "converge, either this test's premise or the tuning surface " +
                "collapsed into one knob and the docs are stale.");
        }

        /// <summary>
        /// While swinging, vertical input must actually change the
        /// constraint radius: climb (+1) shortens the joint's linear limit
        /// down to the min-length clamp; dive (−1) lets it back out but
        /// never beyond the length deployed at latch.
        ///
        /// WHY: reel is the skill verb of the swing (shorten to speed up,
        /// lengthen to extend the arc). If the joint limit stops tracking
        /// _swingLength, the player's input silently does nothing — the
        /// rope visual would shrink while the physics radius stays fixed.
        /// </summary>
        [UnityTest]
        public IEnumerator TickSwingReel_ClimbThenDive_LimitClampsBothEnds()
        {
            var input = new StubInput();
            SetPrivate(_block, "_input", input);
            CallPrivate(_block, "BeginSwingLatch");
            yield return new WaitForFixedUpdate();

            ConfigurableJoint leash = _tipGo.GetComponent<ConfigurableJoint>();
            float lenAtLatch = GetPrivate<float>(_block, "_swingLenAtLatch");
            float minLen = GetPrivate<float>(_block, "_swingMinLength");

            // Climb far past the clamp: 400 reel ticks at 6 m/s × 0.02 s
            // = 48 m of reel-in against a ~9.5 m rope.
            input.VerticalValue = 1f;
            for (int i = 0; i < 400; i++) CallPrivate(_block, "TickSwingReel");
            Assert.AreEqual(Mathf.Min(minLen, lenAtLatch), leash.linearLimit.limit, 1e-3f,
                "Reel-in must clamp at _swingMinLength — without the clamp the " +
                "chassis winches itself into the anchor and the joint solver " +
                "explodes at zero radius.");

            // Dive equally far past the other clamp.
            input.VerticalValue = -1f;
            for (int i = 0; i < 400; i++) CallPrivate(_block, "TickSwingReel");
            Assert.AreEqual(lenAtLatch, leash.linearLimit.limit, 1e-3f,
                "Reel-out must clamp at the latch-time deployed length — free " +
                "paying-out past it would be a stealth range extender beyond " +
                "_maxRange.");
        }

        /// <summary>
        /// Releasing a swing (BeginRetract destroys the leash joint) must
        /// not disturb chassis velocity: the whole Spiderman payoff is
        /// keeping the speed you built at the bottom of the arc.
        ///
        /// WHY: "does destroying this joint type apply a residual impulse"
        /// is exactly the kind of thing that is cheap to get quietly wrong
        /// and invisible until a player notices every release feels dead.
        /// The chassis sits INSIDE the limit here so the joint is slack and
        /// the only velocity change a correct release can produce is zero.
        /// </summary>
        [UnityTest]
        public IEnumerator BeginRetract_DuringSlackSwing_ChassisVelocityUnchanged()
        {
            CallPrivate(_block, "BeginSwingLatch");
            yield return new WaitForFixedUpdate();

            Vector3 v0 = new Vector3(5f, 0f, 0f);
            _chassisRb.linearVelocity = v0;

            CallPrivate(_block, "BeginRetract");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(_block.IsSwinging,
                "IsSwinging must drop on release — a stale flag would leak " +
                "swing behaviour (reel, skipped pull field) into the next latch.");
            Assert.AreEqual(0f, (_chassisRb.linearVelocity - v0).magnitude, 1e-3f,
                "Chassis velocity must be unchanged after release: no gravity, " +
                "no drag, slack joint — any delta is a residual impulse from " +
                "the joint teardown, which kills swing momentum-carry.");
        }
    }
}
