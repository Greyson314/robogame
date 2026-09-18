// =============================================================================
// BlockEditHighlightsTests — PlayMode (CHG-032, F-049)
//
// WHAT THIS COVERS
//   BlockEditHighlights is the extraction of BlockEditor's instance-edit and
//   tune-mode hover highlight subsystem (session 125 / F-050). This test
//   drives the helper's public surface directly (no reflection into
//   BlockEditor) and pins today's behaviour, captured from BlockEditor.cs
//   before the extraction (HighlightInstance, FitShellToBlock,
//   DriveHoverHighlight / DriveHover, ClearInstanceEdit's highlight half,
//   HideHoverHighlight / HideHover):
//     - HighlightInstance(block, cellSize) parents exactly one shell to the
//       block, with no Collider, scaled to the block's rendered bounds
//       (8% swell + 0.08 pad — FitShellToBlock).
//     - DriveHover(target, cellSize) creates a SECOND, independent shell for
//       a hovered block — the instance-edit highlight is untouched.
//     - ClearInstanceHighlight() destroys the instance shell; the hover
//       shell survives.
//     - HideHover() deactivates (does not destroy) the reusable hover shell,
//       per invariant #6 (no per-frame allocations) — it is reused across
//       hovers, only reparented/reactivated on the next DriveHover.
//
// RED-FIRST
//   BlockEditHighlights does not exist on main; this test is written against
//   its future public surface and is RED at commit 1 for that reason (type
//   not found) — LESSONS-compliant "tests first" for a pure extraction,
//   same shape as MakeHighlightShellTests (CHG-027) / …ColliderTests
//   (CHG-031), which this same change also repoints to the new type.
// =============================================================================

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Gameplay
{
    public sealed class BlockEditHighlightsTests
    {
        private GameObject _blockGo;
        private GameObject _otherBlockGo;
        private BlockDefinition _def;
        // Shells HideHover unparents from a test block would otherwise leak
        // into the PlayMode session past this test.
        private GameObject _unparentedShell;

        [TearDown]
        public void TearDown()
        {
            if (_blockGo != null) Object.Destroy(_blockGo);
            if (_otherBlockGo != null) Object.Destroy(_otherBlockGo);
            if (_def != null) Object.Destroy(_def);
            if (_unparentedShell != null) Object.Destroy(_unparentedShell);
        }

        // Same MakeBlock pattern BuildSessionInstanceEditTests.cs uses: a
        // BlockBehaviour on a plain GameObject, initialized via the internal
        // Initialize method when reachable. A child cube primitive gives the
        // block real Renderer bounds for FitShellToBlock to measure — a
        // block with no renderer falls back to the cell-size box, which
        // would make the "fitted to rendered bounds" assertion vacuous.
        private static BlockBehaviour MakeBlock(string name, BlockDefinition def, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            BlockBehaviour bb = go.AddComponent<BlockBehaviour>();

            var initMethod = typeof(BlockBehaviour).GetMethod(
                "Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(bb, new object[]
                {
                    def, Vector3Int.zero, Vector3.zero, Vector3Int.up, 0f, 0
                });
            }
            else
            {
                typeof(BlockBehaviour)
                    .GetField("_definition", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(bb, def);
            }

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual"; // never a shell name (InstanceEditHighlight / TuneHoverHighlight)
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            Object.Destroy(visual.GetComponent<Collider>());

            return bb;
        }

        private static Bounds ExpectedShellBounds(BlockBehaviour block, GameObject excludeShell)
        {
            Bounds b = default;
            bool has = false;
            foreach (Renderer r in block.GetComponentsInChildren<Renderer>())
            {
                if (r == null || r.gameObject == excludeShell) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            Assert.IsTrue(has, "test block has no Renderer — fixture is broken.");
            return b;
        }

        // Shells are named by production code (InstanceEditHighlight /
        // TuneHoverHighlight) — look up by that name rather than "any
        // MeshFilter child" so the fixture's own visual cube can't be
        // mistaken for a shell.
        private static Transform FindShellByName(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.gameObject.name == name) return t;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator HighlightInstance_ParentsOneShellFittedToBounds_WithNoCollider()
        {
            _def = ScriptableObject.CreateInstance<BlockDefinition>();
            _blockGo = null; // set via MakeBlock below
            BlockBehaviour block = MakeBlock("HighlightTarget", _def, new Vector3(3f, 0f, 0f));
            _blockGo = block.gameObject;

            var highlights = new BlockEditHighlights();
            highlights.HighlightInstance(block, cellSize: 1f);

            Transform shell = FindShellByName(block.transform, "InstanceEditHighlight");
            Assert.IsNotNull(shell, "expected a shell named \"InstanceEditHighlight\" parented to the block.");
            Assert.AreEqual(block.transform, shell.parent, "shell is not parented to the block.");

            Bounds expected = ExpectedShellBounds(block, shell.gameObject);
            Vector3 expectedScale = expected.size * 1.08f + Vector3.one * 0.08f;
            Assert.That(shell.localScale.x, Is.EqualTo(expectedScale.x).Within(0.001f));
            Assert.That(shell.localScale.y, Is.EqualTo(expectedScale.y).Within(0.001f));
            Assert.That(shell.localScale.z, Is.EqualTo(expectedScale.z).Within(0.001f));
            Assert.That(shell.position.x, Is.EqualTo(expected.center.x).Within(0.001f));
            Assert.That(shell.position.y, Is.EqualTo(expected.center.y).Within(0.001f));
            Assert.That(shell.position.z, Is.EqualTo(expected.center.z).Within(0.001f));

            // Object.Destroy(collider) is deferred to end of frame.
            yield return null;
            Assert.IsNull(shell.GetComponent<Collider>(),
                "highlight shell still carries a Collider — would block build-mode raycasts.");
        }

        [UnityTest]
        public IEnumerator DriveHover_CreatesSecondShell_IndependentOfInstanceHighlight()
        {
            _def = ScriptableObject.CreateInstance<BlockDefinition>();
            BlockBehaviour instanceBlock = MakeBlock("BoundInstance", _def, new Vector3(0f, 0f, 0f));
            _blockGo = instanceBlock.gameObject;
            BlockBehaviour hoverBlock = MakeBlock("HoverTarget", _def, new Vector3(5f, 0f, 0f));
            _otherBlockGo = hoverBlock.gameObject;

            var highlights = new BlockEditHighlights();
            highlights.HighlightInstance(instanceBlock, cellSize: 1f);
            highlights.DriveHover(hoverBlock, cellSize: 1f);
            yield return null;

            Assert.IsNotNull(FindShellByName(instanceBlock.transform, "InstanceEditHighlight"),
                "the instance-edit highlight was disturbed by DriveHover.");
            Assert.IsNotNull(FindShellByName(hoverBlock.transform, "TuneHoverHighlight"),
                "DriveHover did not attach a shell to the hovered block.");
        }

        [UnityTest]
        public IEnumerator ClearInstanceHighlight_DestroysInstanceShell_LeavesHoverShellAlone()
        {
            _def = ScriptableObject.CreateInstance<BlockDefinition>();
            BlockBehaviour instanceBlock = MakeBlock("BoundInstance", _def, new Vector3(0f, 0f, 0f));
            _blockGo = instanceBlock.gameObject;
            BlockBehaviour hoverBlock = MakeBlock("HoverTarget", _def, new Vector3(5f, 0f, 0f));
            _otherBlockGo = hoverBlock.gameObject;

            var highlights = new BlockEditHighlights();
            highlights.HighlightInstance(instanceBlock, cellSize: 1f);
            highlights.DriveHover(hoverBlock, cellSize: 1f);
            yield return null;

            highlights.ClearInstanceHighlight();
            yield return null; // Object.Destroy is deferred

            Assert.IsNull(FindShellByName(instanceBlock.transform, "InstanceEditHighlight"),
                "ClearInstanceHighlight did not destroy the instance shell.");
            Assert.IsNotNull(FindShellByName(hoverBlock.transform, "TuneHoverHighlight"),
                "ClearInstanceHighlight destroyed the unrelated hover shell.");
        }

        [UnityTest]
        public IEnumerator HideHover_DeactivatesReusableShell_WithoutDestroyingIt()
        {
            _def = ScriptableObject.CreateInstance<BlockDefinition>();
            BlockBehaviour hoverBlock = MakeBlock("HoverTarget", _def, new Vector3(5f, 0f, 0f));
            _blockGo = hoverBlock.gameObject;

            var highlights = new BlockEditHighlights();
            highlights.DriveHover(hoverBlock, cellSize: 1f);
            yield return null;
            Transform shell = FindShellByName(hoverBlock.transform, "TuneHoverHighlight");
            Assert.IsNotNull(shell, "DriveHover did not attach a hover shell.");

            highlights.HideHover();
            yield return null;
            _unparentedShell = shell != null ? shell.gameObject : null;

            // Reused (invariant #6), not destroyed: it is now unparented and
            // inactive rather than gone.
            Assert.IsNull(FindShellByName(hoverBlock.transform, "TuneHoverHighlight"),
                "HideHover left the shell parented to the block.");
            Assert.IsNull(shell.parent, "HideHover did not unparent the reusable shell.");
            Assert.IsFalse(shell.gameObject.activeSelf, "HideHover did not deactivate the reusable shell.");
        }
    }
}
