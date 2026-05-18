// GrappleMagnetBlock latch-tether physics. The Latched phase creates a
// SpringJoint between the rope tip body and the contacted target — the
// same netcode-replication-sensitive constraint HookBlock.Attach creates.
// Session 84-followup (pre-netcode sweep): GrappleMagnetBlock had zero
// test coverage; this pins the tether contract.
//
// Naming follows the project convention:
//   {ClassName}Tests.{Method}_{Scenario}_{ExpectedOutcome}.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Combat;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class GrappleMagnetTests
    {
        private GameObject _blockGo;   // the GrappleMagnetBlock host
        private GrappleMagnetBlock _block;
        private GameObject _tipGo;     // stand-in for the spawned rope tip
        private Rigidbody _tipRb;
        private GameObject _targetGo;  // external chassis the magnet latches
        private Rigidbody _targetRb;

        // -----------------------------------------------------------------
        // Reflection helpers — drive the private latch path without the
        // full Fire→flight→hit state machine (mirrors HookGrappleTests).
        // -----------------------------------------------------------------

        private static void SetPrivate(object o, string field, object value)
        {
            FieldInfo fi = o.GetType().GetField(
                field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"GrappleMagnetBlock.{field} not found — field renamed? " +
                                  "This test reflects on private state and must track renames.");
            fi.SetValue(o, value);
        }

        private static void CallBuildTargetTether(GrappleMagnetBlock b, Rigidbody targetRb, Vector3 contactPoint)
        {
            MethodInfo mi = typeof(GrappleMagnetBlock).GetMethod(
                "BuildTargetTether", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "GrappleMagnetBlock.BuildTargetTether not found — method renamed?");
            mi.Invoke(b, new object[] { targetRb, contactPoint });
        }

        // -----------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            _blockGo = new GameObject("GrappleMagnetBlock");
            // AddComponent runs Awake synchronously; GrappleMagnetBlock's
            // Awake only caches refs and does not require a chassis here.
            _block = _blockGo.AddComponent<GrappleMagnetBlock>();

            // The rope tip body. In production GrappleMagnetBlock spawns
            // this during Fire; we inject it directly to isolate the tether.
            _tipGo = new GameObject("RopeTip");
            _tipRb = _tipGo.AddComponent<Rigidbody>();
            _tipRb.useGravity = false;
            _tipRb.isKinematic = false;
            _tipGo.transform.position = Vector3.zero;

            SetPrivate(_block, "_tipGo", _tipGo);
            SetPrivate(_block, "_tipRb", _tipRb);

            // External target chassis.
            _targetGo = new GameObject("TargetChassis");
            _targetRb = _targetGo.AddComponent<Rigidbody>();
            _targetRb.useGravity = false;
            _targetRb.isKinematic = false;
            _targetRb.mass = 50f;
            _targetGo.transform.position = new Vector3(0f, 0f, 2f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_blockGo  != null) Object.Destroy(_blockGo);
            if (_tipGo    != null) Object.Destroy(_tipGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
        }

        // -----------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------

        /// <summary>
        /// BuildTargetTether must create a SpringJoint on the tip body, wired
        /// to the contacted target, with an unbreakable break force and a
        /// zero rest-distance pull (the magnet drags the target to the tip).
        ///
        /// WHY this matters for netcode: this SpringJoint is the one physics
        /// constraint the Latched state introduces that crosses two bodies.
        /// The server must own it; a client that recreates it with a finite
        /// breakForce or non-zero rest distance produces a visibly different
        /// drag. connectedBody is also how the netcode layer identifies which
        /// chassis the magnet is tethering. A regression in any of these
        /// silently desyncs grapple gameplay the moment a second client exists.
        /// </summary>
        [UnityTest]
        public IEnumerator BuildTargetTether_OnExternalTarget_CreatesConfiguredSpringJoint()
        {
            Vector3 contact = new Vector3(0f, 0f, 1f);
            CallBuildTargetTether(_block, _targetRb, contact);

            // One physics step so the joint's internal state settles.
            yield return new WaitForFixedUpdate();

            SpringJoint joint = _tipGo.GetComponent<SpringJoint>();
            Assert.IsNotNull(joint,
                "A SpringJoint must exist on the rope tip GameObject after BuildTargetTether. " +
                "If missing, the Latched state has no tether — the magnet would 'latch' " +
                "visually but exert no pull, and IsLatched would be a lie.");

            Assert.AreSame(_targetRb, joint.connectedBody,
                "SpringJoint.connectedBody must be the contacted target's Rigidbody. " +
                "The netcode layer reads connectedBody to identify the tethered chassis; " +
                "a wrong reference drags the wrong body.");

            Assert.AreEqual(Mathf.Infinity, joint.breakForce,
                "Tether breakForce must be Infinity. Release is always explicit (retract " +
                "or target death) — a finite break force would let the tether snap under " +
                "acceleration, the exact failure the session-60 SpringJoint redesign fixed.");

            Assert.AreEqual(0f, joint.minDistance, 1e-4f,
                "minDistance must be 0 — the magnet pulls the target all the way to the tip.");
            Assert.AreEqual(0f, joint.maxDistance, 1e-4f,
                "maxDistance must be 0 — a non-zero rest distance would leave the target " +
                "floating off the magnet instead of stuck to it.");
        }

        /// <summary>
        /// BuildTargetTether must no-op (not throw, not create a joint) when
        /// the target Rigidbody is null — the flight-miss path calls into the
        /// latch sequence with whatever the raycast returned.
        ///
        /// WHY: a NullReferenceException here would abort the Fire coroutine
        /// mid-state-transition, leaving the block stuck out of Ready and
        /// unable to fire again for the rest of the match.
        /// </summary>
        [Test]
        public void BuildTargetTether_WithNullTarget_NoJointNoThrow()
        {
            Assert.DoesNotThrow(() => CallBuildTargetTether(_block, null, Vector3.zero));
            Assert.IsNull(_tipGo.GetComponent<SpringJoint>(),
                "No SpringJoint may be created for a null target — the guard at the top " +
                "of BuildTargetTether must reject it before AddComponent.");
        }
    }
}
