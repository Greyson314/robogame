using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// Pin the world-intent → local-pitch conversion. The user-facing
    /// rule the variant panel encodes: positive pitch tilts the foil's
    /// tip toward world +Y. The same rule must apply on every face the
    /// foil can be placed on; without per-up sign correction, the
    /// chord-axis rotation lands on the same world axis but the tip
    /// goes opposite directions on opposite faces.
    /// </summary>
    public sealed class BlockOrientationTests
    {
        // -----------------------------------------------------------------
        // Per-axis sign rule
        // -----------------------------------------------------------------

        [Test]
        public void NormalizePitchForUp_LateralXMounts_NegateOpposite()
        {
            float pX  = BlockOrientation.NormalizePitchForUp(18f, new Vector3Int( 1, 0, 0));
            float pNX = BlockOrientation.NormalizePitchForUp(18f, new Vector3Int(-1, 0, 0));
            Assert.AreEqual(-pX, pNX, 1e-4f,
                "Same world-intent pitch must produce opposite local-frame pitches on +X vs -X mounts (for tip-up consistency).");
        }

        [Test]
        public void NormalizePitchForUp_LateralZMounts_NegateOpposite()
        {
            float pZ  = BlockOrientation.NormalizePitchForUp(18f, new Vector3Int(0, 0,  1));
            float pNZ = BlockOrientation.NormalizePitchForUp(18f, new Vector3Int(0, 0, -1));
            Assert.AreEqual(-pZ, pNZ, 1e-4f);
        }

        [Test]
        public void NormalizePitchForUp_TopAndBottomMounts_PreservePitch()
        {
            // For ±Y mounts the chord axis aligns with the world +Z, and
            // local-X has zero world-Y component, so pitch is preserved
            // (the visual tilt is sideways, the world-up rule doesn't
            // really apply — but the invariant we care about is
            // "preserve, don't sign-flip arbitrarily").
            Assert.AreEqual(18f, BlockOrientation.NormalizePitchForUp(18f, new Vector3Int(0,  1, 0)), 1e-4f);
            Assert.AreEqual(18f, BlockOrientation.NormalizePitchForUp(18f, new Vector3Int(0, -1, 0)), 1e-4f);
        }

        [Test]
        public void NormalizePitchForUp_IsInvolutive()
        {
            // Apply twice with the same up → return to input.
            foreach (Vector3Int up in new[]
            {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            })
            {
                float once  = BlockOrientation.NormalizePitchForUp(7f, up);
                float twice = BlockOrientation.NormalizePitchForUp(once, up);
                Assert.AreEqual(7f, twice, 1e-4f, $"Involutive failed for up={up}");
            }
        }

        // -----------------------------------------------------------------
        // World-up tip invariant (the user's "+18 = sky" rule)
        // -----------------------------------------------------------------

        [Test]
        public void TipDirection_PositiveWorldPitch_AlwaysTiltsTowardSky_OnLateralFaces()
        {
            // For each lateral face (±X, ±Z), apply the same world-intent
            // pitch and verify the tip ends up with a positive world Y
            // component after the chord-axis rotation. This is the exact
            // invariant the user's bug report calls out — same hand-set
            // pitch should give same world tilt across faces.
            foreach (Vector3Int up in new[]
            {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            })
            {
                float localPitch = BlockOrientation.NormalizePitchForUp(18f, up);
                Vector3 tipWorldAfter = SimulateTipWorldAfterPitch(up, localPitch);
                Assert.Greater(tipWorldAfter.y, 0f,
                    $"Tip should rotate toward world +Y on every lateral face. Failed for up={up}, local pitch={localPitch}.");
            }
        }

        // Simulate "the tip's new world position after chord-axis pitch
        // by `localPitch`". Mirrors what
        // AeroSurfaceBlock.ApplyOrientationToVisual does to the wing
        // mesh — Quaternion.AngleAxis(pitchDeg, Vector3.forward) applied
        // in foil-local space, then transformed back into world.
        private static Vector3 SimulateTipWorldAfterPitch(Vector3Int up, float localPitch)
        {
            Quaternion q = BlockGrid.OrientationFromUp(up);
            // Tip is foil-local +Y at distance 1 (the "outward" direction
            // along span). After pitch around foil-local +Z, the tip moves.
            Quaternion pitch = Quaternion.AngleAxis(localPitch, Vector3.forward);
            Vector3 tipFoilLocalAfter = pitch * Vector3.up;
            Vector3 tipWorldAfter = q * tipFoilLocalAfter;
            return tipWorldAfter;
        }
    }
}
