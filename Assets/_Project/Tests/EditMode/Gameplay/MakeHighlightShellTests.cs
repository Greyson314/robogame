// =============================================================================
// MakeHighlightShellTests — EditMode (CHG-027, F-050; moved to
// BlockEditHighlights by CHG-032, F-049)
//
// WHAT THIS COVERS
//   BlockEditHighlights.MakeHighlightShell(Material) is the de-duplicated
//   shell builder shared by HighlightInstance (the bound-instance edit
//   highlight) and DriveHover (the tune-mode hover highlight). Both old call
//   sites built a Cube primitive, assigned the given material as
//   sharedMaterial, and disabled shadow casting / receiving — this test
//   locks that shape in, using the exact material construction the old
//   HighlightInstance call site used (session 125).
//
// SCOPE NOTE — why the Collider isn't asserted here
//   The old code (and the extracted helper) also destroys the primitive's
//   auto-added Collider via Destroy(col). Object.Destroy is documented as
//   a Play-Mode operation; called from an EditMode test (Application.
//   isPlaying == false) it may log an Editor error without actually
//   detaching the Collider — edit-mode Destroy semantics are not
//   guaranteed to match play-mode. BlockEditor only ever runs in Play
//   Mode, so this test does not assert the Collider is gone; that half of
//   the contract is proven by the red team's live-Editor Play Mode
//   hierarchy dump instead (CHG-027 spec: Garage build mode with a
//   hovered instance). LogAssert is silenced around the call so an
//   edit-mode Destroy log can't fail this test either way.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.EditMode.Gameplay
{
    public sealed class MakeHighlightShellTests
    {
        private GameObject _shell;
        private Material _material;

        [TearDown]
        public void TearDown()
        {
            if (_shell != null) Object.DestroyImmediate(_shell);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void MakeHighlightShell_BuildsCubeWithMaterialAndShadowsOff()
        {
            MethodInfo method = typeof(BlockEditHighlights).GetMethod(
                "MakeHighlightShell", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "BlockEditHighlights.MakeHighlightShell not found — has BlockEditHighlights been extracted from BlockEditor yet (CHG-032, F-049)?");

            // Same construction the old HighlightInstance call site used
            // (BlockEditor.cs, session 125's instance-edit highlight).
            _material = Robogame.Core.RuntimeMaterials.UnlitTransparent(new Color(1f, 0.62f, 0.10f, 0.22f));

            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _shell = (GameObject)method.Invoke(null, new object[] { _material });
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }

            Assert.IsNotNull(_shell, "MakeHighlightShell returned null.");
            Assert.IsNotNull(_shell.GetComponent<MeshFilter>(),
                "shell has no MeshFilter — not a GameObject.CreatePrimitive(Cube) result.");

            var mr = _shell.GetComponent<MeshRenderer>();
            Assert.IsNotNull(mr, "shell has no MeshRenderer.");
            Assert.AreEqual(_material, mr.sharedMaterial,
                "sharedMaterial was not set to the material passed in (old code: mr.sharedMaterial = s_highlightMat / _hoverMat).");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, mr.shadowCastingMode,
                "shadowCastingMode was not disabled (old code: mr.shadowCastingMode = ShadowCastingMode.Off).");
            Assert.IsFalse(mr.receiveShadows,
                "receiveShadows was not disabled (old code: mr.receiveShadows = false).");
        }
    }
}
