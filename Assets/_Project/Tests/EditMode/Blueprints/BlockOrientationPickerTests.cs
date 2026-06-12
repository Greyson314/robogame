using NUnit.Framework;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    // Why these tests exist
    // ---------------------
    // The middle-click eyedropper (BlockEditor.TryPickBlock) reads the
    // stored local pitch from a blueprint entry and must invert it back to
    // world-intent before populating the variant panel and the re-place
    // path.  The inversion relies on NormalizePitchForUp being involutive
    // (applying it twice to the same pitch+up pair returns the original
    // value).
    //
    // Two specific failure modes this guards against:
    //
    //   1. Zero-up legacy entries.  Blueprints authored before the Up field
    //      was added store Vector3Int.zero; the method must silently treat
    //      that as +Y and remain involutive, otherwise picking any pre-v3
    //      foil entry and re-placing it would silently flip its pitch.
    //
    //   2. Rotor / non-foil blocks bypass NormalizePitchForUp entirely via
    //      BlockOrientation.UsesWorldIntentPitch.  If that gate is wrong,
    //      picking a rotor and re-placing it applies an unintended sign flip
    //      on its collective pitch, changing blade incidence without user
    //      input.
    //
    // BlockOrientationTests.cs already covers the axis-sign rule and the
    // general involution for the six canonical up vectors.  This companion
    // file covers the picker-specific edge cases that sit above that layer.

    public sealed class BlockOrientationPickerTests
    {
        private const string LibraryAssetPath =
            "Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset";

        private BlockDefinitionLibrary _lib;

        [SetUp]
        public void SetUp()
        {
            _lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (_lib == null) Assert.Inconclusive("BlockDefinitionLibrary not found; run Build Everything first.");
        }

        // -----------------------------------------------------------------
        // Zero-up (legacy) involution
        // -----------------------------------------------------------------

        [Test]
        public void NormalizePitchForUp_ZeroUp_TreatedAsPositiveY_IsInvolutive()
        {
            // An entry saved before the Up field existed has Up == (0,0,0).
            // NormalizePitchForUp must fall back to +Y and be involutive,
            // so that picking an old foil and re-placing it does not change
            // the stored pitch.
            const float pitch = 12f;
            float once  = BlockOrientation.NormalizePitchForUp(pitch, Vector3Int.zero);
            float twice = BlockOrientation.NormalizePitchForUp(once,  Vector3Int.zero);
            Assert.AreEqual(pitch, twice, 1e-4f,
                "Zero-up (legacy pre-Up-field saves) must be treated as +Y and remain involutive — " +
                "picking an old foil entry must not silently alter its pitch on re-place.");
        }

        [Test]
        public void NormalizePitchForUp_ZeroUp_ProducesSameResultAsExplicitPlusY()
        {
            // Confirm the zero → +Y fallback is literal, not just involutive
            // by coincidence.  The eyedropper calls
            // NormalizePitchForUp(stored, entry.EffectiveUp); if zero and +Y
            // produce different results, legacy and modern +Y entries are
            // inverted differently and re-placement produces the wrong pitch.
            const float pitch = 7f;
            float viaZero      = BlockOrientation.NormalizePitchForUp(pitch, Vector3Int.zero);
            float viaExplicitY = BlockOrientation.NormalizePitchForUp(pitch, Vector3Int.up);
            Assert.AreEqual(viaExplicitY, viaZero, 1e-4f,
                "Zero-up must produce identical output to explicit +Y — legacy and modern +Y " +
                "entries must be inverted identically by the eyedropper.");
        }

        // -----------------------------------------------------------------
        // UsesWorldIntentPitch gate — keeps rotors and other non-foil
        // blocks out of the sign-conversion path
        // -----------------------------------------------------------------

        [Test]
        public void UsesWorldIntentPitch_ReturnsTrueForAero()
        {
            // The eyedropper must apply world-intent inversion only to blocks
            // that use it.  Aero is a declared foil type; verify the gate is
            // open for it using the real library definition so the Id string
            // matches what shipping assets carry.
            BlockDefinition aeroDef = _lib.Get(BlockIds.Aero);
            Assume.That(aeroDef, Is.Not.Null, "Aero block definition must exist in the library.");

            Assert.IsTrue(BlockOrientation.UsesWorldIntentPitch(aeroDef),
                "Aero block must be identified as using world-intent pitch — " +
                "the eyedropper must apply the sign-inversion when picking foils.");
        }

        [Test]
        public void UsesWorldIntentPitch_ReturnsTrueForAeroFin()
        {
            BlockDefinition aeroFinDef = _lib.Get(BlockIds.AeroFin);
            Assume.That(aeroFinDef, Is.Not.Null, "AeroFin block definition must exist in the library.");

            Assert.IsTrue(BlockOrientation.UsesWorldIntentPitch(aeroFinDef),
                "AeroFin block must be identified as using world-intent pitch.");
        }

        [Test]
        public void UsesWorldIntentPitch_ReturnsFalseForRotor()
        {
            // A rotor's collective pitch is a local-frame value baked into
            // the blade at adoption time — it has no world-intent sign
            // correction.  If this gate fires incorrectly, picking a rotor
            // and re-placing it flips blade incidence without user input.
            BlockDefinition rotorDef = _lib.Get(BlockIds.Rotor);
            Assume.That(rotorDef, Is.Not.Null, "Rotor block definition must exist in the library.");

            Assert.IsFalse(BlockOrientation.UsesWorldIntentPitch(rotorDef),
                "Rotor must NOT use the world-intent pitch scheme — its collective pitch is " +
                "local-frame only and must not be sign-corrected by the eyedropper.");
        }

        [Test]
        public void UsesWorldIntentPitch_ReturnsFalseForNull()
        {
            // Null-safety: the eyedropper may query before the definition
            // is resolved (e.g. on a freshly deserialized entry without a
            // matching library entry).  Must not throw or misfire.
            Assert.IsFalse(BlockOrientation.UsesWorldIntentPitch(null),
                "Null BlockDefinition must return false (safe default — no conversion applied).");
        }

        [Test]
        public void UsesWorldIntentPitch_ReturnsFalseForStructureBlock()
        {
            // Spot-check a plain structure block: cube pitch is not a chord
            // angle, it's undefined.  The gate must be false so cubes are
            // never sign-inverted by the eyedropper.
            BlockDefinition cubeDef = _lib.Get(BlockIds.Cube);
            Assume.That(cubeDef, Is.Not.Null, "Cube block definition must exist in the library.");

            Assert.IsFalse(BlockOrientation.UsesWorldIntentPitch(cubeDef),
                "Structure block (cube) must not use world-intent pitch conversion.");
        }

        // -----------------------------------------------------------------
        // Convenience overload: NormalizePitchForUp(def, worldPitch, up)
        // -----------------------------------------------------------------

        [Test]
        public void NormalizePitchForUp_ConvenienceOverload_BypassesConversionForRotor()
        {
            // The three-arg overload is what placement and eyedropper code
            // call.  For non-foil blocks it must return pitchDeg unchanged
            // regardless of up, so that setting a rotor config then picking
            // and re-placing produces an identical stored value.
            BlockDefinition rotorDef = _lib.Get(BlockIds.Rotor);
            Assume.That(rotorDef, Is.Not.Null);

            foreach (Vector3Int up in new[]
            {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                Vector3Int.up,            Vector3Int.down,
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0, -1),
            })
            {
                float result = BlockOrientation.NormalizePitchForUp(rotorDef, 15f, up);
                Assert.AreEqual(15f, result, 1e-4f,
                    $"Rotor pitch must pass through unchanged for up={up} " +
                    "(not a foil block — no world-intent sign correction should apply).");
            }
        }

        [Test]
        public void NormalizePitchForUp_ConvenienceOverload_MatchesTwoArgOverloadForFoil()
        {
            // For foil blocks, the three-arg overload must exactly match the
            // two-arg overload.  Tests that the convenience wrapper doesn't
            // suppress or double-apply the conversion on lateral axes where
            // sign-flipping differs from identity.
            BlockDefinition aeroDef = _lib.Get(BlockIds.Aero);
            Assume.That(aeroDef, Is.Not.Null);

            foreach (Vector3Int up in new[]
            {
                new Vector3Int( 1, 0, 0),
                new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0,  1),
                new Vector3Int( 0, 0, -1),
            })
            {
                float expected = BlockOrientation.NormalizePitchForUp(18f, up);
                float actual   = BlockOrientation.NormalizePitchForUp(aeroDef, 18f, up);
                Assert.AreEqual(expected, actual, 1e-4f,
                    $"Convenience overload must delegate to two-arg overload for foil up={up}.");
            }
        }

        // -----------------------------------------------------------------
        // Round-trip: pick then re-place must yield original stored pitch
        // -----------------------------------------------------------------

        [Test]
        public void PickAndReplace_FoilOnLateralFace_PitchRoundTrips()
        {
            // Simulate what the eyedropper does when picking a foil entry:
            //   stored_local = NormalizePitchForUp(world_intent, up)   ← stored in blueprint
            //   recovered_world = NormalizePitchForUp(stored_local, up) ← eyedropper inversion
            //   re_stored_local = NormalizePitchForUp(recovered_world, up) ← re-placement
            //
            // The round-trip stored → recovered_world → re_stored_local must
            // equal the original stored value so the blueprint is bit-identical
            // after pick+re-place.  Tested on lateral faces where the sign
            // flip is non-trivial (the failure mode is easiest to hit here).
            const float worldIntent = 14f;

            foreach (Vector3Int up in new[]
            {
                new Vector3Int( 1, 0, 0),
                new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0,  1),
                new Vector3Int( 0, 0, -1),
            })
            {
                float storedLocal     = BlockOrientation.NormalizePitchForUp(worldIntent, up);
                float recoveredWorld  = BlockOrientation.NormalizePitchForUp(storedLocal,    up);
                float reStoredLocal   = BlockOrientation.NormalizePitchForUp(recoveredWorld, up);

                Assert.AreEqual(storedLocal, reStoredLocal, 1e-4f,
                    $"Pick-then-re-place must produce bit-identical stored pitch for up={up}. " +
                    $"If this fails, picking a foil on a lateral face and re-placing it flips " +
                    $"its incidence angle.");
            }
        }
    }
}
