// =============================================================================
// MakeHighlightShellColliderTests — PlayMode (CHG-031, follow-up to CHG-027;
// moved to BlockEditHighlights by CHG-032, F-049)
//
// WHY THIS MATTERS
//   BlockEditHighlights's shells (bound-instance highlight, tune-mode hover
//   highlight) are parented to a block on the chassis. A shell that kept the
//   cube primitive's auto-added BoxCollider would sit between the build-mode
//   raycast and the block under the cursor, and would add a collider to the
//   chassis Rigidbody. The EditMode test cannot assert the Collider is gone
//   (Object.Destroy is a Play Mode operation, see MakeHighlightShellTests
//   § SCOPE NOTE); this test closes that half of the contract.
// =============================================================================

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Gameplay
{
    public sealed class MakeHighlightShellColliderTests
    {
        private GameObject _shell;
        private Material _material;

        [TearDown]
        public void TearDown()
        {
            if (_shell != null) Object.Destroy(_shell);
            if (_material != null) Object.Destroy(_material);
        }

        [UnityTest]
        public IEnumerator MakeHighlightShell_HasNoColliderAfterOneFrame()
        {
            MethodInfo method = typeof(BlockEditHighlights).GetMethod(
                "MakeHighlightShell", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "BlockEditHighlights.MakeHighlightShell not found (CHG-032, F-049).");

            _material = Robogame.Core.RuntimeMaterials.UnlitTransparent(new Color(1f, 0.62f, 0.10f, 0.22f));
            _shell = (GameObject)method.Invoke(null, new object[] { _material });
            Assert.IsNotNull(_shell, "MakeHighlightShell returned null.");

            // Object.Destroy is deferred to the end of the frame.
            yield return null;

            Assert.IsNull(_shell.GetComponent<Collider>(),
                "the highlight shell still carries the cube primitive's Collider: it would block build-mode raycasts and add a collider under the chassis Rigidbody.");
        }
    }
}
