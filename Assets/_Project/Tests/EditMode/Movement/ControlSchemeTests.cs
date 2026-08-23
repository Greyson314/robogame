using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Tests.EditMode.Movement
{
    /// <summary>
    /// ADR-0009 intent layer: the control scheme is the ONLY place keys are
    /// interpreted, and a foil's control deflection is decided by geometry.
    /// These tests pin (a) how a blueprint's composition resolves to a scheme
    /// — the hybrid cases are the ones that matter — (b) the key → intent
    /// mapping per scheme, (c) the sign every surface gets from its position
    /// relative to the CoM, and (d) that the override survives both save
    /// formats. A wrong sign here is a plane that pitches DOWN on Space.
    /// </summary>
    public sealed class ControlSchemeTests
    {
        private static ChassisBlueprint Bp(ChassisKind kind, bool rotorsLift, params ChassisBlueprint.Entry[] entries)
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Scheme Test";
            bp.Kind = kind;
            bp.RotorsGenerateLift = rotorsLift;
            bp.SetEntries(entries);
            return bp;
        }

        private static ChassisBlueprint.Entry E(string id, Vector3Int up) =>
            new ChassisBlueprint.Entry(id, Vector3Int.zero, up);

        private static readonly Vector3Int Up = Vector3Int.up;
        private static readonly Vector3Int Fwd = new Vector3Int(0, 0, 1);
        private static readonly Vector3Int Right = new Vector3Int(1, 0, 0);

        // ---------------------------------------------------------------
        // Resolution
        // ---------------------------------------------------------------

        [Test]
        public void Resolve_ExplicitOverride_Wins()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Plane, false, E(BlockIds.Thruster, Up));
            bp.ControlScheme = ControlScheme.Helicopter;
            Assert.AreEqual(ControlScheme.Helicopter, ControlSchemes.Resolve(bp, false, false, true),
                "An explicit per-blueprint scheme must beat every composition rule (hybrids need the override).");
        }

        [Test]
        public void Resolve_LiftRotorWithoutForwardThrust_IsHelicopter()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, rotorsLift: true, E(BlockIds.Rotor, Up), E(BlockIds.Aero, Right));
            Assert.AreEqual(ControlScheme.Helicopter, ControlSchemes.Resolve(bp, false, false, hasAero: true),
                "Vertical lift rotor + adopted blades is the Helicopter preset shape.");
        }

        [Test]
        public void Resolve_LiftRotorPlusThruster_IsPlane()
        {
            // A lift rotor AND forward thrust: the forward thrust wins the
            // tie — W/S as throttle, Space as pitch; the rotor still takes
            // heave through its own axis rule.
            ChassisBlueprint bp = Bp(ChassisKind.Ground, rotorsLift: true,
                E(BlockIds.Rotor, Up), E(BlockIds.Thruster, Up), E(BlockIds.Aero, Right));
            Assert.AreEqual(ControlScheme.Plane, ControlSchemes.Resolve(bp, false, false, hasAero: true));
        }

        [Test]
        public void Resolve_KindPlane_IsPlane_EvenWithoutAero()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Plane, false, E(BlockIds.Cube, Up));
            Assert.AreEqual(ControlScheme.Plane, ControlSchemes.Resolve(bp, false, false, false));
        }

        [Test]
        public void Resolve_TankWithSpoiler_StaysGround()
        {
            // Wings on a wheeled chassis with no forward thrust: the user
            // built a tank with a spoiler, not a plane. A/D must stay yaw.
            ChassisBlueprint bp = Bp(ChassisKind.Ground, false, E(BlockIds.Wheel, Up), E(BlockIds.Aero, Right));
            Assert.AreEqual(ControlScheme.Ground, ControlSchemes.Resolve(bp, hasWheels: true, hasHover: false, hasAero: true));
        }

        [Test]
        public void Resolve_WingedPropCarWithWheels_IsPlane()
        {
            // The user's session-166 build: Ground kind, wheels, big wings,
            // forward-axis rotor. Forward thrust + aero → Plane.
            ChassisBlueprint bp = Bp(ChassisKind.Ground, rotorsLift: true,
                E(BlockIds.Wheel, Up), E(BlockIds.Aero, Right), E(BlockIds.Rotor, Fwd));
            Assert.AreEqual(ControlScheme.Plane, ControlSchemes.Resolve(bp, hasWheels: true, hasHover: false, hasAero: true));
        }

        [Test]
        public void Resolve_GliderWithoutWheels_IsPlane()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, false, E(BlockIds.Aero, Right));
            Assert.AreEqual(ControlScheme.Plane, ControlSchemes.Resolve(bp, false, false, hasAero: true));
        }

        [Test]
        public void Resolve_NothingSpecial_IsGround()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, false, E(BlockIds.Cube, Up));
            Assert.AreEqual(ControlScheme.Ground, ControlSchemes.Resolve(bp, false, false, false));
            Assert.AreEqual(ControlScheme.Ground, ControlSchemes.Resolve(null, false, false, false));
        }

        [Test]
        public void ResolveFromIds_ClassifiesByBlockId()
        {
            Assert.AreEqual(ControlScheme.Ground,
                ControlSchemes.ResolveFromIds(Bp(ChassisKind.Ground, false, E(BlockIds.Wheel, Up), E(BlockIds.Aero, Right))));
            Assert.AreEqual(ControlScheme.Plane,
                ControlSchemes.ResolveFromIds(Bp(ChassisKind.Ground, false, E(BlockIds.Aero, Right), E(BlockIds.Thruster, Up))));
            Assert.AreEqual(ControlScheme.Ground, ControlSchemes.ResolveFromIds(null));
        }

        // ---------------------------------------------------------------
        // Key → intent
        // ---------------------------------------------------------------

        [Test]
        public void FromScheme_Plane_MapsThrottlePitchRollAndCoordinatedYaw()
        {
            DriveIntent i = DriveIntent.FromScheme(ControlScheme.Plane, new Vector2(0.5f, 1f), 0.7f);
            Assert.AreEqual(1f,   i.Surge, 1e-6f, "W/S = throttle");
            Assert.AreEqual(0.7f, i.Pitch, 1e-6f, "Space/Shift = pitch");
            Assert.AreEqual(0.5f, i.Roll,  1e-6f, "A/D = roll");
            Assert.AreEqual(0.5f, i.Yaw,   1e-6f, "A/D also asks any rudder for a coordinated turn");
            Assert.AreEqual(0f,   i.Heave, 1e-6f);
        }

        [Test]
        public void FromScheme_Ground_MapsSurgeYawHeave()
        {
            DriveIntent i = DriveIntent.FromScheme(ControlScheme.Ground, new Vector2(0.5f, 1f), 0.7f);
            Assert.AreEqual(1f,   i.Surge, 1e-6f);
            Assert.AreEqual(0.5f, i.Yaw,   1e-6f, "A/D = yaw on a ground bot");
            Assert.AreEqual(0.7f, i.Heave, 1e-6f, "Space = jump / hover lift");
            Assert.AreEqual(0f,   i.Pitch, 1e-6f, "no pitch demand: a spoiler on a tank must not flap on Space");
            Assert.AreEqual(0f,   i.Roll,  1e-6f);
        }

        [Test]
        public void FromScheme_Helicopter_MapsCollectiveAndNoseForward()
        {
            DriveIntent i = DriveIntent.FromScheme(ControlScheme.Helicopter, new Vector2(0.5f, 1f), 0.7f);
            Assert.AreEqual(0.7f, i.Heave, 1e-6f, "Space = collective");
            Assert.AreEqual(-1f,  i.Pitch, 1e-6f, "W = nose FORWARD (down) on a heli");
            Assert.AreEqual(0.5f, i.Yaw,   1e-6f, "A/D = yaw");
            Assert.AreEqual(0f,   i.Surge, 1e-6f, "no surge demand — nothing on a pure heli serves it");
        }

        [Test]
        public void FromScheme_Auto_FallsBackToGround()
        {
            DriveIntent i = DriveIntent.FromScheme(ControlScheme.Auto, new Vector2(0f, 1f), 0f);
            Assert.AreEqual(1f, i.Surge, 1e-6f);
            Assert.AreEqual(0f, i.Pitch, 1e-6f);
        }

        // ---------------------------------------------------------------
        // Deflection sign by geometry (chassis frame: +Z fwd, +Y up, +X right)
        // ---------------------------------------------------------------

        private static readonly DriveIntent PitchUp   = new DriveIntent(0, 0, 0, pitch: 1f, roll: 0f, yaw: 0f);
        private static readonly DriveIntent BankRight = new DriveIntent(0, 0, 0, pitch: 0f, roll: 1f, yaw: 0f);
        private static readonly DriveIntent NoseRight = new DriveIntent(0, 0, 0, pitch: 0f, roll: 0f, yaw: 1f);
        private const float Max = 0.2f;

        [Test]
        public void Deflection_TailElevator_DeflectsNegative_ForPitchUp()
        {
            // Behind the CoM, lift up: pitching up means LESS lift at the
            // tail (tail drops, nose rises).
            float d = AeroControl.Deflection(PitchUp, new Vector3(0f, 0f, -2f), Vector3.up, Max);
            Assert.Less(d, -0.5f * Max, "tail surface must shed lift to pitch the nose up");
        }

        [Test]
        public void Deflection_Canard_DeflectsPositive_ForPitchUp()
        {
            float d = AeroControl.Deflection(PitchUp, new Vector3(0f, 0f, 1.5f), Vector3.up, Max);
            Assert.Greater(d, 0.5f * Max, "a surface ahead of the CoM must ADD lift to pitch the nose up");
        }

        [Test]
        public void Deflection_Ailerons_OpposeEachOther()
        {
            float right = AeroControl.Deflection(BankRight, new Vector3(2f, 0f, 0f), Vector3.up, Max);
            float left  = AeroControl.Deflection(BankRight, new Vector3(-2f, 0f, 0f), Vector3.up, Max);
            Assert.Less(right, -0.5f * Max, "bank right: right wing sheds lift");
            Assert.Greater(left, 0.5f * Max, "bank right: left wing adds lift");
            Assert.AreEqual(-right, left, 1e-5f, "mirror-symmetric wings must deflect by equal and opposite amounts");
        }

        [Test]
        public void Deflection_FinBehindCom_YawsNoseRight()
        {
            // Vertical fin above and behind the CoM with lift along +X:
            // nose-right needs a -X force at the tail → negative deflection.
            float d = AeroControl.Deflection(NoseRight, new Vector3(0f, 2f, -2f), Vector3.right, Max);
            Assert.Less(d, 0f, "a fin behind the CoM must push the tail left to yaw the nose right");
        }

        [Test]
        public void Deflection_SurfaceOnCom_HasNoAuthority()
        {
            Assert.AreEqual(0f, AeroControl.Deflection(PitchUp, Vector3.zero, Vector3.up, Max), 1e-6f,
                "zero lever arm → no rotational authority, whatever the demand");
        }

        [Test]
        public void Deflection_NoRotationalDemand_IsZero()
        {
            var surgeOnly = new DriveIntent(1f, 0f, 0f, 0f, 0f, 0f);
            Assert.AreEqual(0f, AeroControl.Deflection(surgeOnly, new Vector3(0f, 0f, -2f), Vector3.up, Max), 1e-6f);
        }

        [Test]
        public void Deflection_SaturatesAtMax()
        {
            var all = new DriveIntent(0f, 0f, 0f, 1f, 1f, 1f);
            float d = AeroControl.Deflection(all, new Vector3(2f, 1f, -2f), Vector3.up, Max);
            Assert.LessOrEqual(Mathf.Abs(d), Max + 1e-6f);
        }

        // ---------------------------------------------------------------
        // Persistence
        // ---------------------------------------------------------------

        [Test]
        public void ControlScheme_RoundTrips_ThroughJson_AndDefaultsToAuto()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, false, E(BlockIds.Cube, Up));
            bp.ControlScheme = ControlScheme.Helicopter;
            string json = BlueprintSerializer.ToJson(bp, prettyPrint: false);
            Assert.IsTrue(BlueprintSerializer.TryFromJson(json, out ChassisBlueprint loaded, out string err), err);
            Assert.AreEqual(ControlScheme.Helicopter, loaded.ControlScheme);

            // A pre-v10 save has no controlScheme field → Auto.
            string v9 = json.Replace("\"controlScheme\":\"Helicopter\",", string.Empty).Replace("\"schemaVersion\":10", "\"schemaVersion\":9");
            Assert.IsTrue(BlueprintSerializer.TryFromJson(v9, out ChassisBlueprint old, out string err2), err2);
            Assert.AreEqual(ControlScheme.Auto, old.ControlScheme, "older saves must resolve by composition");
        }

        [Test]
        public void ControlScheme_RoundTrips_ThroughBlob_WithoutDisturbingRotorFlag()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, rotorsLift: true, E(BlockIds.Cube, Up));
            bp.ControlScheme = ControlScheme.Plane;
            byte[] blob = BlueprintBlob.Encode(bp);
            Assert.IsTrue(BlueprintBlob.TryDecode(blob, out ChassisBlueprint decoded, out string err), err);
            Assert.AreEqual(ControlScheme.Plane, decoded.ControlScheme);
            Assert.IsTrue(decoded.RotorsGenerateLift, "scheme bits must not clobber the rotor-lift flag bit");
        }

        [Test]
        public void ControlScheme_Change_BumpsRevision()
        {
            ChassisBlueprint bp = Bp(ChassisKind.Ground, false, E(BlockIds.Cube, Up));
            int r0 = bp.Revision;
            bp.ControlScheme = ControlScheme.Auto;  // unchanged
            Assert.AreEqual(r0, bp.Revision);
            bp.ControlScheme = ControlScheme.Plane; // a saveable edit
            Assert.AreEqual(r0 + 1, bp.Revision);
        }
    }
}
