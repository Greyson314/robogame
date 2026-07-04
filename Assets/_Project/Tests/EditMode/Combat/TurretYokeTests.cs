using NUnit.Framework;
using Robogame.Combat;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.EditMode.Combat
{
    /// <summary>
    /// Pins the shared turret aim math (ADR-0003 phase C). The load-bearing
    /// case is the spherical-aim regression (audit #8): the four forked
    /// bodies yawed with <c>LookRotation(flatXZ, Vector3.up)</c>, which swings
    /// the base toward a world-horizontal projection of the target on the
    /// planet arena instead of a surface-level one. <see cref="TurretYoke"/>
    /// yaws about the local up, so the block stays level with the surface.
    /// </summary>
    public sealed class TurretYokeTests
    {
        private const float Tol = 1e-3f;

        // ---- UpAt -------------------------------------------------------

        [Test]
        public void UpAt_FlatArena_NoGravitySources_IsWorldUp()
        {
            // No registered source → GravityField.SampleAt returns
            // Physics.gravity → up is exactly Vector3.up. The fix is a no-op
            // off the planet.
            Assert.AreEqual(0, GravityField.SourceCount, "test precondition: no sources registered");
            Vector3 up = TurretYoke.UpAt(new Vector3(7f, 3f, -2f));
            Assert.That(Vector3.Distance(up, Vector3.up), Is.LessThan(Tol));
        }

        [Test]
        public void UpAt_OnPlanet_IsOppositeGravity()
        {
            // A source pulling toward -X (gravity points -X) → up points +X.
            var src = new FakeGravity(new Vector3(-9.81f, 0f, 0f));
            GravityField.Register(src);
            try
            {
                Vector3 up = TurretYoke.UpAt(Vector3.zero);
                Assert.That(Vector3.Distance(up, Vector3.right), Is.LessThan(Tol));
            }
            finally
            {
                GravityField.Unregister(src);
            }
        }

        // ---- MountUp ----------------------------------------------------

        [Test]
        public void MountUp_RolledChassis_FollowsTheBlock()
        {
            // A turret on top of a plane that rolls inverted must ride the
            // roll (session 131): its yaw axis is the authored mount up in
            // the CHASSIS frame, so at 180° roll it points world-DOWN. The
            // old gravity-up axis kept the turret world-level, which read as
            // the weapon detaching from its block.
            var chassis = new GameObject("chassis");
            var block = new GameObject("block");
            try
            {
                block.transform.SetParent(chassis.transform, false);
                chassis.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
                Vector3 up = TurretYoke.MountUp(block.transform, Quaternion.identity);
                Assert.That(Vector3.Distance(up, Vector3.down), Is.LessThan(Tol),
                    "mount up must follow the rolled chassis");
            }
            finally { Object.DestroyImmediate(chassis); }
        }

        [Test]
        public void MountUp_SideMountedBlock_UsesAuthoredRestOrientation()
        {
            // A block authored on a side face (rest rotation 90° about Z) has
            // its mount up along the chassis' +X — the yaw axis must come
            // from the authored rest rotation, not the block's current
            // (Track-overwritten) rotation and not world up.
            var chassis = new GameObject("chassis");
            var block = new GameObject("block");
            try
            {
                block.transform.SetParent(chassis.transform, false);
                Quaternion rest = Quaternion.Euler(0f, 0f, -90f); // up -> +X
                Vector3 up = TurretYoke.MountUp(block.transform, rest);
                Assert.That(Vector3.Distance(up, Vector3.right), Is.LessThan(Tol));
            }
            finally { Object.DestroyImmediate(chassis); }
        }

        [Test]
        public void Yaw_RolledChassis_TurretStaysAttached()
        {
            // Full chain for the inverted-plane case: with the mount-up axis,
            // the yawed block's world up must match the (inverted) chassis
            // up, and its forward must stay in the chassis' yaw plane.
            Quaternion parent = Quaternion.Euler(0f, 0f, 180f);
            Vector3 mountUp = parent * Vector3.up; // == world down
            bool ok = TurretYoke.TryYawTargetLocal(
                Vector3.zero, parent,
                aimPoint: new Vector3(4f, -1f, 9f), localUp: mountUp,
                out Quaternion local);

            Assert.IsTrue(ok);
            Vector3 blockUp = (parent * local) * Vector3.up;
            Assert.That(Vector3.Distance(blockUp, mountUp), Is.LessThan(Tol),
                "turret up must ride the chassis roll, not stay world-level");
            Vector3 blockFwd = (parent * local) * Vector3.forward;
            Assert.That(Mathf.Abs(Vector3.Dot(blockFwd, mountUp)), Is.LessThan(Tol),
                "turret forward must stay in the chassis yaw plane");
        }

        // ---- TryYawTargetLocal ------------------------------------------

        [Test]
        public void Yaw_FlatArena_DropsAimElevation_AsBefore()
        {
            // Aim up-and-to-the-+X; flat-arena yaw must face +X, ignoring the
            // vertical component — identical to the pre-refactor flat.y = 0.
            bool ok = TurretYoke.TryYawTargetLocal(
                Vector3.zero, Quaternion.identity,
                aimPoint: new Vector3(10f, 4f, 0f), localUp: Vector3.up,
                out Quaternion q);

            Assert.IsTrue(ok);
            Vector3 fwd = q * Vector3.forward;
            Assert.That(Vector3.Distance(fwd, Vector3.right), Is.LessThan(Tol));
        }

        [Test]
        public void Yaw_OnPlanet_KeepsBlockLevelWithSurface()
        {
            // Surface up = +X (we're on the side of a planet). Target is
            // up-and-forward in world. The corrected yaw must keep the block's
            // up aligned to the SURFACE normal, and its forward perpendicular
            // to that normal — the property the Vector3.up fork violated.
            Vector3 localUp = Vector3.right;
            bool ok = TurretYoke.TryYawTargetLocal(
                Vector3.zero, Quaternion.identity,
                aimPoint: new Vector3(0f, 5f, 10f), localUp: localUp,
                out Quaternion q);

            Assert.IsTrue(ok);
            Vector3 blockUp = q * Vector3.up;
            Assert.That(Vector3.Dot(blockUp, localUp), Is.GreaterThan(1f - Tol),
                "block up should track the surface normal, not world-Y");

            Vector3 blockFwd = q * Vector3.forward;
            Assert.That(Mathf.Abs(Vector3.Dot(blockFwd, localUp)), Is.LessThan(Tol),
                "block forward should lie in the surface plane (no tilt into the ground)");
        }

        [Test]
        public void Yaw_AimAlongUpAxis_IsDegenerate_ReturnsFalse()
        {
            bool ok = TurretYoke.TryYawTargetLocal(
                Vector3.zero, Quaternion.identity,
                aimPoint: new Vector3(0f, 10f, 0f), localUp: Vector3.up,
                out _);
            Assert.IsFalse(ok, "aim straight up the up-axis has no yaw direction");
        }

        [Test]
        public void Yaw_AppliesParentRotation_LocalTimesParentReproducesWorld()
        {
            // The returned rotation is block-LOCAL: parent * local must equal
            // the intended world yaw, so a turret on a banked chassis aims
            // correctly.
            Quaternion parent = Quaternion.Euler(20f, 50f, -15f);
            TurretYoke.TryYawTargetLocal(
                Vector3.zero, parent,
                aimPoint: new Vector3(3f, 1f, 8f), localUp: Vector3.up,
                out Quaternion local);

            Vector3 worldFwd = (parent * local) * Vector3.forward;
            // Expected world forward: aim projected onto the world-XZ plane.
            Vector3 expected = new Vector3(3f, 0f, 8f).normalized;
            Assert.That(Vector3.Distance(worldFwd, expected), Is.LessThan(Tol));
        }

        // ---- PitchDegrees -----------------------------------------------

        [Test]
        public void Pitch_LevelAim_IsZero()
        {
            var go = new GameObject("block");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                float pitch = TurretYoke.PitchDegrees(go.transform, new Vector3(0f, 0f, 10f), Vector3.zero);
                Assert.That(Mathf.Abs(pitch), Is.LessThan(Tol));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Pitch_AimAbove_LooksUp_NegativeDegrees()
        {
            var go = new GameObject("block");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // 45° up: equal forward + up components.
                float pitch = TurretYoke.PitchDegrees(go.transform, new Vector3(0f, 10f, 10f), Vector3.zero);
                Assert.That(pitch, Is.EqualTo(-45f).Within(0.01f),
                    "Unity X-rot: looking up is negative");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- Fake gravity source ----------------------------------------

        private sealed class FakeGravity : IGravitySource
        {
            private readonly Vector3 _g;
            public FakeGravity(Vector3 g) => _g = g;
            public Vector3 GetGravityAt(Vector3 worldPosition) => _g;
            public bool ContainsPoint(Vector3 worldPosition) => true;
        }
    }
}
