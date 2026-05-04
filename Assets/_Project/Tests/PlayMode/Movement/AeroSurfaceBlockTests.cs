using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    /// <summary>
    /// Playmode tests for <see cref="AeroSurfaceBlock"/> rotor-mode wiring.
    ///
    /// These tests exercise <see cref="AeroSurfaceBlock.ConfigureRotorMode"/>
    /// in isolation — without a full <see cref="RotorBlock"/> present — to
    /// verify the contract independently of the adoption pipeline.
    ///
    /// The primary concern is the R3 regression: if <c>OnEnable</c> re-resolves
    /// <c>_velocityRb</c> and <c>_forceTargetRb</c> AFTER <c>ConfigureRotorMode</c>
    /// ran (e.g. because the foil was enabled late), the hub-at-scene-root has no
    /// non-kinematic ancestor so <c>_forceTargetRb</c> goes null and lift is
    /// silently lost. The guard <c>if (_rotorMode &amp;&amp; _forceTargetRb != null) return;</c>
    /// in <c>OnEnable</c> must survive.
    ///
    /// Change C tests (session 18) cover the tweakable-driven visual resize:
    ///   • _wingMesh.localScale tracks Aero.WingSpan / WingChord / WingThickness.
    ///   • Vertical fins swap the span and thickness axes.
    ///   • PHYSICS_PLAN §1.5 contract: changing visual dims must not change lift output.
    /// </summary>
    public class AeroSurfaceBlockTests
    {
        private GameObject _hubGo;
        private Rigidbody  _hubRb;
        private GameObject _chassisGo;
        private Rigidbody  _chassisRb;
        private GameObject _foilGo;
        private AeroSurfaceBlock _foil;

        // -----------------------------------------------------------------------
        // Helpers — field reflection
        // -----------------------------------------------------------------------

        private static Rigidbody GetVelocityRb(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_velocityRb", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(aero) as Rigidbody;
        }

        private static Rigidbody GetForceTargetRb(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_forceTargetRb", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(aero) as Rigidbody;
        }

        private static bool GetRotorMode(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_rotorMode", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi != null && (bool)fi.GetValue(aero);
        }

        private static Transform GetWingMesh(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_wingMesh", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(aero) as Transform;
        }

        // -----------------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            // Kinematic hub at scene root (mirrors what RotorBlock spawns).
            _hubGo = new GameObject("TestHub");
            _hubRb = _hubGo.AddComponent<Rigidbody>();
            _hubRb.isKinematic  = true;
            _hubRb.useGravity   = false;

            // Dynamic chassis — represents the robot body.
            _chassisGo = new GameObject("TestChassis");
            _chassisRb = _chassisGo.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false;

            // Foil parented under the hub (post-adoption topology).
            _foilGo = new GameObject("TestFoil");
            _foilGo.transform.SetParent(_hubGo.transform, worldPositionStays: false);
            _foilGo.transform.localPosition = new Vector3(1f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hubGo     != null) Object.Destroy(_hubGo);
            if (_chassisGo != null) Object.Destroy(_chassisGo);
            // _foilGo is a child of _hubGo, destroyed with it.

            // Reset aero tweakables so cross-test pollution doesn't carry over.
            Tweakables.Reset(Tweakables.AeroWingSpan);
            Tweakables.Reset(Tweakables.AeroWingChord);
            Tweakables.Reset(Tweakables.AeroWingThickness);
        }

        // -----------------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Calling <see cref="AeroSurfaceBlock.ConfigureRotorMode"/> must set
        /// <c>_velocityRb</c> to the hub and <c>_forceTargetRb</c> to the chassis.
        /// This test calls ConfigureRotorMode before the foil's OnEnable runs
        /// (the factory-early-call path).
        /// </summary>
        [UnityTest]
        public IEnumerator AeroSurfaceBlock_ConfigureRotorMode_BeforeOnEnable_SetsHubAndChassis()
        {
            // Add the component while the GO is inactive so OnEnable hasn't fired.
            _foilGo.SetActive(false);
            _foil = _foilGo.AddComponent<AeroSurfaceBlock>();

            // Call ConfigureRotorMode before activation — mirrors the RotorBlock
            // adoption path where the factory calls Configure then SetActive.
            _foil.ConfigureRotorMode(_hubRb, _chassisRb);

            // Now enable — OnEnable should respect the _rotorMode guard.
            _foilGo.SetActive(true);

            yield return new WaitForFixedUpdate();

            Assert.AreSame(_hubRb,     GetVelocityRb(_foil),
                "_velocityRb should be the hub after ConfigureRotorMode (pre-enable call).");
            Assert.AreSame(_chassisRb, GetForceTargetRb(_foil),
                "_forceTargetRb should be the chassis after ConfigureRotorMode (pre-enable call).");
            Assert.IsTrue(GetRotorMode(_foil),
                "_rotorMode should be true after ConfigureRotorMode.");
        }

        /// <summary>
        /// Calling <see cref="AeroSurfaceBlock.ConfigureRotorMode"/> AFTER the
        /// foil's OnEnable has already run (i.e. the late-call path where the
        /// adoption happens on an already-active foil) must still correctly
        /// override <c>_velocityRb</c> and <c>_forceTargetRb</c>.
        /// </summary>
        [UnityTest]
        public IEnumerator AeroSurfaceBlock_ConfigureRotorMode_AfterOnEnable_OverridesVelocityAndForceTarget()
        {
            // Start active so OnEnable fires during AddComponent.
            _foil = _foilGo.AddComponent<AeroSurfaceBlock>();

            // OnEnable has now run and resolved from the parent chain (hub is
            // kinematic, no non-kinematic ancestor at scene root → _forceTargetRb
            // would be null in the pre-fix state). Now call Configure to inject.
            _foil.ConfigureRotorMode(_hubRb, _chassisRb);

            yield return new WaitForFixedUpdate();

            Assert.AreSame(_hubRb,     GetVelocityRb(_foil),
                "_velocityRb should be the hub after late ConfigureRotorMode call.");
            Assert.AreSame(_chassisRb, GetForceTargetRb(_foil),
                "_forceTargetRb should be the chassis after late ConfigureRotorMode call.");
        }

        /// <summary>
        /// The R3 guard: once ConfigureRotorMode has been called and the foil is
        /// active, a subsequent OnEnable (e.g. caused by toggling the GameObject
        /// off/on) must NOT clobber the injected hub/chassis references.
        /// The guard <c>if (_rotorMode &amp;&amp; _forceTargetRb != null) return;</c>
        /// in OnEnable is the fix for the most likely R3 cause (session-16 §R3
        /// bullet 3). This test will fail if the guard is removed.
        /// </summary>
        [UnityTest]
        public IEnumerator AeroSurfaceBlock_OnEnable_AfterConfigureRotorMode_DoesNotClobberHubReference()
        {
            _foilGo.SetActive(false);
            _foil = _foilGo.AddComponent<AeroSurfaceBlock>();
            _foil.ConfigureRotorMode(_hubRb, _chassisRb);
            _foilGo.SetActive(true);

            yield return null; // let OnEnable run

            // Simulate a mid-game toggle (e.g. block disabled by damage, then re-enabled).
            _foilGo.SetActive(false);
            yield return null;
            _foilGo.SetActive(true);
            yield return new WaitForFixedUpdate();

            Assert.AreSame(_hubRb,     GetVelocityRb(_foil),
                "_velocityRb was clobbered by a second OnEnable call — the _rotorMode guard is missing or broken (R3).");
            Assert.AreSame(_chassisRb, GetForceTargetRb(_foil),
                "_forceTargetRb was clobbered by a second OnEnable call — lift will be dropped after any toggle (R3).");
        }

        // -----------------------------------------------------------------------
        // Change C — visual size driven by Aero.* tweakables (session 18)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Change C contract: setting <c>Aero.WingSpan</c>, <c>Aero.WingChord</c>,
        /// or <c>Aero.WingThickness</c> via <see cref="Tweakables.Set"/> must
        /// immediately update <c>_wingMesh.localScale</c> on every active foil.
        /// Horizontal foil: span → scale.x, thickness → scale.y, chord → scale.z.
        /// </summary>
        [UnityTest]
        public IEnumerator WingMeshScale_TracksAeroWingSpanTweakable_Live()
        {
            // Start with defaults so any prior test pollution is cleared.
            Tweakables.Reset(Tweakables.AeroWingSpan);
            Tweakables.Reset(Tweakables.AeroWingChord);
            Tweakables.Reset(Tweakables.AeroWingThickness);

            // Use a standalone foil parented directly to the dynamic chassis
            // (not the hub) so OnEnable resolves cleanly without rotor-mode.
            GameObject foilGo = new GameObject("VisualFoil");
            foilGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            AeroSurfaceBlock foil = foilGo.AddComponent<AeroSurfaceBlock>();
            // Vertical = false (default) — horizontal wing mapping.

            yield return null; // let Awake/OnEnable fire

            Transform mesh = GetWingMesh(foil);
            Assert.IsNotNull(mesh, "Precondition: _wingMesh must be non-null after Awake.");

            // --- Span → scale.x ---
            Tweakables.Set(Tweakables.AeroWingSpan, 2.5f);
            yield return null; // Changed event fires synchronously; yield lets scale flush
            Assert.AreEqual(2.5f, mesh.localScale.x, 0.001f,
                "scale.x should track AeroWingSpan (horizontal foil).");

            // --- Chord → scale.z ---
            Tweakables.Set(Tweakables.AeroWingChord, 1.8f);
            yield return null;
            Assert.AreEqual(1.8f, mesh.localScale.z, 0.001f,
                "scale.z should track AeroWingChord (horizontal foil).");

            // --- Thickness → scale.y ---
            Tweakables.Set(Tweakables.AeroWingThickness, 0.15f);
            yield return null;
            Assert.AreEqual(0.15f, mesh.localScale.y, 0.001f,
                "scale.y should track AeroWingThickness (horizontal foil).");
        }

        /// <summary>
        /// Change C contract (vertical fin): when <c>Vertical = true</c>,
        /// <c>ApplyOrientationToVisual</c> swaps span and thickness so the long
        /// axis points up. span → scale.y, thickness → scale.x.
        /// </summary>
        [UnityTest]
        public IEnumerator VerticalFin_SwapsSpanAndThickness()
        {
            Tweakables.Reset(Tweakables.AeroWingSpan);
            Tweakables.Reset(Tweakables.AeroWingThickness);

            GameObject foilGo = new GameObject("VerticalFoil");
            foilGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            foilGo.SetActive(false);
            AeroSurfaceBlock foil = foilGo.AddComponent<AeroSurfaceBlock>();
            foil.Vertical = true; // set before first enable
            foilGo.SetActive(true);

            yield return null; // Awake/OnEnable fires with Vertical already true

            Tweakables.Set(Tweakables.AeroWingSpan,      1.5f);
            Tweakables.Set(Tweakables.AeroWingThickness, 0.1f);
            yield return null;

            Transform mesh = GetWingMesh(foil);
            Assert.IsNotNull(mesh, "Precondition: _wingMesh must be non-null after Awake.");

            Assert.AreEqual(0.1f, mesh.localScale.x, 0.001f,
                "Vertical fin: scale.x should be thickness (the narrow axis).");
            Assert.AreEqual(1.5f, mesh.localScale.y, 0.001f,
                "Vertical fin: scale.y should be span (the tall axis).");
        }

        /// <summary>
        /// PHYSICS_PLAN §1.5 contract: changing aerofoil visual dimensions must
        /// not change the lift output of a wing in FixedUpdate.
        ///
        /// The lift formula in <see cref="AeroSurfaceBlock"/> FixedUpdate is:
        ///   lift = speed² × _liftCoef × liftFactor
        /// It has no wing-dimension term. This test confirms that contract holds
        /// by measuring the chassis velocity delta over two FixedUpdate ticks:
        /// once at default dims, then at max span (3 m), and asserting equality.
        ///
        /// If a future PR introduces a span or chord term in the lift path, this
        /// test will fail and surface the coupling before it ships.
        /// </summary>
        [UnityTest]
        public IEnumerator LiftForce_DoesNotChange_WhenAeroDimsChange()
        {
            // Reset dims to the registered defaults so test starts clean.
            Tweakables.Reset(Tweakables.AeroWingSpan);
            Tweakables.Reset(Tweakables.AeroWingChord);
            Tweakables.Reset(Tweakables.AeroWingThickness);

            // Build a standalone foil directly on the chassis (not a hub child)
            // so _velocityRb == _forceTargetRb == the chassis — the simplest
            // topology for measuring lift without rotor-mode complications.
            GameObject foilGo = new GameObject("LiftTestFoil");
            foilGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            // Orient the foil so its local +Z (forward) aligns with chassis +Z.
            // AeroSurfaceBlock.FixedUpdate requires forward > 0.05 m/s to compute AoA.
            foilGo.transform.localRotation = Quaternion.identity;
            foilGo.transform.localPosition = new Vector3(1f, 0f, 0f); // offset from COM
            AeroSurfaceBlock foil = foilGo.AddComponent<AeroSurfaceBlock>();

            // Let Awake/OnEnable settle — chassis is dynamic, _velocityRb resolves.
            yield return new WaitForFixedUpdate();

            // Precondition: _forceTargetRb must be the chassis.
            Assert.AreSame(_chassisRb, GetForceTargetRb(foil),
                "Precondition: _forceTargetRb should resolve to chassis in standalone topology.");

            // Disable gravity so the only force acting is the foil's lift.
            _chassisRb.useGravity = false;

            // --- Sample 1: default dims ---
            // Give the chassis a forward velocity so the foil sees airflow.
            _chassisRb.linearVelocity = new Vector3(0f, 0f, 10f);
            _chassisRb.angularVelocity = Vector3.zero;
            Vector3 velBefore1 = _chassisRb.linearVelocity;

            yield return new WaitForFixedUpdate(); // one FixedUpdate applies the lift

            Vector3 delta1 = _chassisRb.linearVelocity - velBefore1;

            // --- Sample 2: max span ---
            Tweakables.Set(Tweakables.AeroWingSpan, 3.0f); // max value

            // Reset chassis to identical initial conditions.
            _chassisRb.linearVelocity = new Vector3(0f, 0f, 10f);
            _chassisRb.angularVelocity = Vector3.zero;
            Vector3 velBefore2 = _chassisRb.linearVelocity;

            yield return new WaitForFixedUpdate();

            Vector3 delta2 = _chassisRb.linearVelocity - velBefore2;

            // The velocity deltas should be identical: same lift, same mass.
            // Tolerance 1e-4 m/s handles floating-point variation between steps
            // (chassis position drifts slightly between the two samples, giving a
            // tiny lever-arm difference; irrelevant for the §1.5 check).
            Assert.AreEqual(delta1.x, delta2.x, 1e-4f,
                "Lift X changed when AeroWingSpan changed — visual dim is coupled to gameplay (§1.5 violation).");
            Assert.AreEqual(delta1.y, delta2.y, 1e-4f,
                "Lift Y changed when AeroWingSpan changed — visual dim is coupled to gameplay (§1.5 violation).");
            Assert.AreEqual(delta1.z, delta2.z, 1e-4f,
                "Lift Z changed when AeroWingSpan changed — visual dim is coupled to gameplay (§1.5 violation).");
        }
    }
}
