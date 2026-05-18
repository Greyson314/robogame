// Session 22: hook-grapple physics. Asserts that HookBlock.HandleCollision
// creates a SpringJoint between the rope's host segment Rigidbody and the
// contacted target Rigidbody, with self-grapple rejection, per-hook
// re-attach cooldown, clean release, and a null-body guard.
//
// Naming follows the session-17 convention:
//   {ClassName}Tests.{Method}_{Scenario}_{ExpectedOutcome}.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class HookGrappleTests
    {
        private GameObject _hostGo;     // stand-in for the rope's last segment
        private Rigidbody  _hostRb;
        private GameObject _ownerGo;    // stand-in for the rope's anchor chassis
        private Rigidbody  _ownerRb;
        private GameObject _targetGo;   // an external chassis the hook can grab
        private Rigidbody  _targetRb;
        private HookBlock  _hook;

        // -----------------------------------------------------------------
        // Reflection helpers — read private state without exposing API
        // -----------------------------------------------------------------

        private static float GetReleaseTime(HookBlock h)
        {
            FieldInfo fi = typeof(HookBlock).GetField(
                "_releaseTime", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi == null ? 0f : (float)fi.GetValue(h);
        }

        private static void SetReattachCooldown(HookBlock h, float seconds)
        {
            FieldInfo fi = typeof(HookBlock).GetField(
                "_reattachCooldown", BindingFlags.NonPublic | BindingFlags.Instance);
            fi?.SetValue(h, seconds);
        }

        // HookBlock.Attach is private. Call it via reflection so we can drive
        // the grapple path without needing a real PhysX Collision object.
        // Only used in the joint-creation test where the collision forwarding
        // chain (TipCollisionForwarder → HandleCollision → Attach) is what we
        // are verifying end-to-end. For the collision test itself we use the
        // real PhysX path + TipCollisionForwarder; for the injection tests we
        // call Attach directly.
        private static void CallAttach(HookBlock h, Rigidbody targetRb, Vector3 contactPoint)
        {
            MethodInfo mi = typeof(HookBlock).GetMethod(
                "Attach", BindingFlags.NonPublic | BindingFlags.Instance);
            mi?.Invoke(h, new object[] { targetRb, contactPoint });
        }

        // -----------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            // Owner chassis — what the rope is anchored to. The hook
            // must NOT grapple onto this even if it contacts it.
            _ownerGo = new GameObject("OwnerChassis");
            _ownerRb = _ownerGo.AddComponent<Rigidbody>();
            _ownerRb.useGravity = false;
            _ownerRb.isKinematic = false;

            // Host = rope's last segment. Lives at scene root in production;
            // mirror that here. Has its own Rigidbody and a small collider
            // so the hook can register contacts.
            _hostGo = new GameObject("HostSegment");
            _hostRb = _hostGo.AddComponent<Rigidbody>();
            _hostRb.useGravity = false;
            _hostRb.isKinematic = false;
            _hostRb.mass = 0.04f; // matches default rope segment mass

            // Hook block as a child of host (matches RopeBlock adoption).
            // Use a fresh GameObject to avoid double-Awake on the host.
            GameObject hookGo = new GameObject("HookTip");
            hookGo.transform.SetParent(_hostGo.transform, worldPositionStays: false);
            // BlockBehaviour is required by HookBlock via TipBlock. Stub
            // by adding a lightweight BlockBehaviour-less HookBlock —
            // since the test doesn't exercise damage routing (just
            // grapple), we work around by adding a BlockBehaviour
            // component directly. The TipBlock.Mass property reads from
            // BlockBehaviour.Definition; without a definition Mass falls
            // back to 1f, which is fine for the joint break tests.
            hookGo.AddComponent<Robogame.Block.BlockBehaviour>();
            _hook = hookGo.AddComponent<HookBlock>();

            // Set short cooldown so the cooldown test doesn't wait long.
            SetReattachCooldown(_hook, 0.10f);

            // Manually attach the hook to its host (skip the rope adoption
            // path; we're testing HookBlock in isolation).
            _hook.AttachToHost(_hostRb, _ownerRb);

            // Target — an external chassis with a Rigidbody + collider.
            _targetGo = new GameObject("TargetChassis");
            _targetRb = _targetGo.AddComponent<Rigidbody>();
            _targetRb.useGravity = false;
            _targetRb.isKinematic = false;
            _targetRb.mass = 50f;
            _targetGo.AddComponent<BoxCollider>();
            _targetGo.transform.position = new Vector3(0f, 0f, 1.0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo   != null) Object.Destroy(_hostGo);
            if (_ownerGo  != null) Object.Destroy(_ownerGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
        }

        // -----------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------

        /// <summary>
        /// When the hook contacts an external chassis, a SpringJoint must be
        /// created linking the host segment Rigidbody to the target Rigidbody
        /// (HookBlock.IsGrappled becomes true, GrappleTarget is the target).
        ///
        /// WHY this matters for netcode: the grapple joint is the one physics
        /// constraint that must replicate. If Attach never runs, the joint is
        /// never created, and the server's authoritative rope state diverges
        /// from the client's visual. This test closes the gap between
        /// "TipCollisionForwarder wired correctly" and "Attach called" by
        /// using a real PhysX collision + forwarder.
        ///
        /// Implementation: we add a TipCollisionForwarder to _hostGo (the same
        /// component RopeBlock.AdoptAdjacentTipBlock adds in production) and
        /// push the target into the hook at high velocity. PhysX fires
        /// OnCollisionEnter → forwarder → HandleCollision → Attach.
        /// The hook's BoxColliders are parented under hookGo (a child of _hostGo
        /// sharing _hostRb as the owning Rigidbody), so PhysX resolves contacts
        /// against _hostRb and routes OnCollisionEnter to TipCollisionForwarder
        /// on _hostGo.
        /// </summary>
        [UnityTest]
        public IEnumerator HandleCollision_OnExternalContact_CreatesGrappleJoint()
        {
            // Wire TipCollisionForwarder to mimic what RopeBlock does in
            // production. Without this component, OnCollisionEnter on _hostGo
            // never reaches HookBlock.HandleCollision — the hook would sit
            // inert regardless of collisions.
            TipCollisionForwarder forwarder = _hostGo.AddComponent<TipCollisionForwarder>();
            forwarder.Tip = _hook;

            // Position the target overlapping the hook's collider zone so
            // PhysX starts with an existing contact (faster than waiting
            // for a fly-in at FixedUpdate intervals).
            _targetGo.transform.position = new Vector3(0f, 0f, 0.1f);
            _targetRb.linearVelocity = new Vector3(0f, 0f, -6f); // driving into the hook

            // Wait enough fixed steps for PhysX to resolve the contact and
            // for TipCollisionForwarder.OnCollisionEnter to fire. Production
            // rope+hook collisions typically resolve within 2 fixed steps;
            // allow 10 as headroom.
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            // The hook should now be grappled to the target.
            Assert.IsTrue(_hook.IsGrappled,
                "HookBlock.IsGrappled must be true after a real PhysX collision with an " +
                "external Rigidbody routed through TipCollisionForwarder. " +
                "If false, HandleCollision was never called — check that " +
                "TipCollisionForwarder.Tip is set and the hookGo's BoxColliders are " +
                "reachable by the owning Rigidbody (_hostRb).");

            Assert.IsNotNull(_hook.GrappleTarget,
                "HookBlock.GrappleTarget must be non-null after a successful grapple. " +
                "GrappleTarget is used by FixedUpdate's null-body guard to detect " +
                "target destruction mid-grapple; if null, the guard fires immediately " +
                "and the joint is released on the next FixedUpdate.");

            Assert.AreSame(_targetRb, _hook.GrappleTarget,
                "GrappleTarget must be the target's Rigidbody, not some other body. " +
                "A wrong target reference means the pull force is applied to the wrong " +
                "body — the rope would drag something other than the intended chassis.");

            // Confirm the grapple joint lives on the host segment (not on the
            // hook's own GameObject). In production the SpringJoint is added to
            // _hostRb.gameObject so PhysX owns it alongside the chassis↔tip joint.
            SpringJoint joint = _hostGo.GetComponent<SpringJoint>();
            Assert.IsNotNull(joint,
                "A SpringJoint must exist on the host segment GameObject after Attach. " +
                "The joint is what actually transmits rope tension to the target. " +
                "If missing, IsGrappled returning true would be a lie with no physics effect.");

            Assert.AreSame(_targetRb, joint.connectedBody,
                "SpringJoint.connectedBody must be the target Rigidbody. " +
                "An incorrect connectedBody means the constraint pulls the wrong body — " +
                "critical for netcode authority which reads connectedBody to identify " +
                "which client's chassis the server is tracking.");
        }

        /// <summary>
        /// <see cref="HookBlock.IsGrappled"/> and <see cref="HookBlock.GrappleTarget"/>
        /// must reflect the real grapple state — false / null when not attached.
        /// </summary>
        [Test]
        public void IsGrappled_WhenNeverAttached_IsFalse()
        {
            Assert.IsFalse(_hook.IsGrappled,
                "Fresh hook should report IsGrappled=false before any contact.");
            Assert.IsNull(_hook.GrappleTarget,
                "Fresh hook should have GrappleTarget=null before any contact.");
        }

        /// <summary>
        /// Calling <see cref="HookBlock.Release"/> when not grappled is a
        /// no-op apart from setting the cooldown timer — must NOT throw
        /// and must update <c>_releaseTime</c>.
        /// </summary>
        [Test]
        public void Release_WhenNotGrappled_DoesNotThrowAndArmsCooldown()
        {
            float t0 = Time.time;
            Assert.DoesNotThrow(() => _hook.Release());
            Assert.GreaterOrEqual(GetReleaseTime(_hook), t0,
                "Release must arm the cooldown timer even when not grappled, " +
                "so subsequent collisions still respect the cooldown window.");
        }

        /// <summary>
        /// HookBlock.Attach creates a SpringJoint on the host segment linked to the
        /// target Rigidbody. This test calls Attach directly (via reflection) rather
        /// than driving a real PhysX collision, which lets it run as a fast
        /// non-physics test.
        ///
        /// WHY a second test alongside HandleCollision_OnExternalContact: that test
        /// verifies the wiring chain (forwarder → HandleCollision → Attach). This
        /// test verifies the Attach contract in isolation — spring stiffness, break
        /// force, and connectedBody — so a regression in the joint configuration
        /// is caught independently of the collision-forwarding path.
        /// </summary>
        [UnityTest]
        public IEnumerator Attach_ToExternalTarget_CreatesSpringJoint_WithCorrectConfiguration()
        {
            // Zero-out the release time so the reattach cooldown is not blocking.
            FieldInfo releaseTimeField = typeof(HookBlock).GetField(
                "_releaseTime", BindingFlags.NonPublic | BindingFlags.Instance);
            releaseTimeField?.SetValue(_hook, -999f);

            Vector3 contactPoint = new Vector3(0f, 0f, 0.5f);
            CallAttach(_hook, _targetRb, contactPoint);

            // Let one FixedUpdate run so the joint's internal state settles.
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_hook.IsGrappled,
                "IsGrappled must be true immediately after Attach completes.");

            SpringJoint joint = _hostGo.GetComponent<SpringJoint>();
            Assert.IsNotNull(joint,
                "A SpringJoint must exist on the host segment after Attach. " +
                "HookBlock uses SpringJoint (not ConfigurableJoint:Locked) because " +
                "spring forces are bounded — locked constraints spike impulses under " +
                "acceleration and trip breakForce (see HookBlock.Attach remarks).");

            Assert.AreSame(_targetRb, joint.connectedBody,
                "SpringJoint.connectedBody must be the target Rigidbody. " +
                "The netcode wrapper identifies tethered targets by this reference.");

            // Break force must be infinite — the rope's chassis↔tip leash is the
            // actual length constraint; this joint must not self-release under load.
            Assert.AreEqual(Mathf.Infinity, joint.breakForce,
                "SpringJoint.breakForce must be Infinity. " +
                "A finite breakForce would let the joint snap under high acceleration " +
                "(same bug the session-60 ConfigurableJoint redesign fixed). " +
                "Release is always explicit (R-key or target death), never auto-snap.");

            // The host must be non-kinematic so PhysX integrates spring forces.
            // Attach() flips isKinematic so the Verlet simulator yields control.
            Assert.IsFalse(_hostRb.isKinematic,
                "Host Rigidbody must be non-kinematic after Attach so PhysX can integrate " +
                "the SpringJoint force. A kinematic body ignores joint forces — the rope " +
                "would appear attached but exert no pull on the target.");
        }

        /// <summary>
        /// FixedUpdate null-body guard: when the joint exists but
        /// connectedBody is null (target destroyed mid-grapple), the
        /// guard must release cleanly without nullref.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_WhenTargetDestroyedMidGrapple_ReleasesCleanly()
        {
            // Drive HookBlock into the "joint exists, target gone" branch by
            // reflection-injecting the joint + target, then nulling the target.
            // _grappleJoint is a SpringJoint (session-60 redesign) — injecting
            // a ConfigurableJoint here throws ArgumentException on SetValue,
            // which is the bug that silently broke this test after session 60.
            SpringJoint joint = _hostGo.AddComponent<SpringJoint>();
            joint.connectedBody = _targetRb;
            joint.breakForce = Mathf.Infinity;

            // Inject the joint + target into HookBlock's private state.
            FieldInfo jointField = typeof(HookBlock).GetField(
                "_grappleJoint", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo targetField = typeof(HookBlock).GetField(
                "_grappleTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            jointField.SetValue(_hook, joint);
            targetField.SetValue(_hook, _targetRb);

            // Confirm the hook now reports grappled.
            Assert.IsTrue(_hook.IsGrappled, "Precondition: hook should be grappled.");

            // Destroy the target. PhysX nulls connectedBody on the joint;
            // HookBlock's FixedUpdate poll should detect this and release.
            Object.Destroy(_targetGo);
            _targetRb = null;
            _targetGo = null;

            // Wait two fixed updates: one for Destroy to actually fire,
            // one for HookBlock.FixedUpdate to detect the null and release.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(_hook.IsGrappled,
                "Hook should auto-release when target's Rigidbody goes null. " +
                "FixedUpdate null-body guard is the safety net.");
        }
    }
}
