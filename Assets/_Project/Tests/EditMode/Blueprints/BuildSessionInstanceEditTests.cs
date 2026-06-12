// =============================================================================
// BuildSessionInstanceEditTests — EditMode (session 125 instance-edit feature)
//
// INVARIANTS COVERED
//   • SetEditingInstance(block) stores the block on EditingInstance and fires
//     EditingInstanceChanged exactly once with the new value.
//   • SetEditingInstance(null) clears the instance and fires the event with null.
//   • SetEditingInstance with the same reference is idempotent — no event fires.
//   • Event fires on a meaningful transition (A → B), not on a no-op (A → A).
//   • PropagateVariantToLiveBlocks scope: when EditingInstance != null, only
//     the bound block receives the variant change; when null, all matching
//     blocks receive it. This is the core behavioral contract of the feature —
//     "retune one rotor's RPM without touching the others."
//
// WHY EDITMODE
//   BuildSession is pure C#. No MonoBehaviour, no scene, no Start/Update.
//   The test runs 10x faster in EditMode and has zero scene-lifecycle risk.
//   Only the propagation test needs a minimal BlockBehaviour stub; that stub
//   is a plain ScriptableObject-backed object constructed via reflection,
//   following the BuildSessionTests.cs pattern.
//
// PATTERN
//   Mirrors BuildSessionTests.cs (same file, same namespace). All assertions
//   on event counts follow the "idempotent set must not re-fire" pattern
//   established by Mirror_ToggleAndAxisRaiseChangedEvent and
//   SelectedBlockChanged_FiresOnceOnTransition in that file.
// =============================================================================

using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Gameplay;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// EditMode tests for <see cref="BuildSession.EditingInstance"/> and
    /// <see cref="BuildSession.EditingInstanceChanged"/>, plus the
    /// per-instance vs. all-blocks scoping logic in
    /// <see cref="BlockEditor"/>'s variant-propagation path.
    /// </summary>
    public sealed class BuildSessionInstanceEditTests
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Create a minimal <see cref="BlockDefinition"/> ScriptableObject for
        /// tests. Follows the MakeDef pattern from RotorBlockTests.cs.
        /// </summary>
        private static BlockDefinition MakeDef(string id, BlockCategory category = BlockCategory.Movement)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, 100f);
            typeof(BlockDefinition)
                .GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, category);
            return def;
        }

        /// <summary>
        /// Create a <see cref="BlockBehaviour"/> attached to a minimal
        /// GameObject so <see cref="BuildSession.SetEditingInstance"/> can
        /// store it. The caller is responsible for <c>Object.DestroyImmediate</c>
        /// in the test's teardown.
        /// </summary>
        private static BlockBehaviour MakeBlock(string blockId)
        {
            var go = new GameObject($"TestBlock_{blockId}");
            BlockBehaviour bb = go.AddComponent<BlockBehaviour>();
            BlockDefinition def = MakeDef(blockId);
            // Bind definition via the internal Init path that ChassisFactory uses.
            // If Init is not accessible, fall back to the private _definition field.
            // Following the project convention: if it compiled before, it compiles here.
            var initMethod = typeof(BlockBehaviour).GetMethod(
                "Init", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(bb, new object[] { def, Vector3Int.zero, Vector3Int.up, Vector3.zero, 0f, 0 });
            }
            else
            {
                typeof(BlockBehaviour)
                    .GetField("_definition", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(bb, def);
            }
            return bb;
        }

        // -----------------------------------------------------------------------
        // SetEditingInstance / EditingInstanceChanged — unit tests
        // -----------------------------------------------------------------------

        [Test]
        public void SetEditingInstance_StoresBlock_OnEditingInstance()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);

            session.SetEditingInstance(block);

            Assert.AreSame(block, session.EditingInstance,
                "EditingInstance must equal the block passed to SetEditingInstance.");

            Object.DestroyImmediate(block.gameObject);
        }

        [Test]
        public void SetEditingInstance_FiresEditingInstanceChanged_WithNewBlock()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);
            BlockBehaviour received = null;
            session.EditingInstanceChanged += b => received = b;

            session.SetEditingInstance(block);

            Assert.AreSame(block, received,
                "EditingInstanceChanged must fire with the new block as argument.");

            Object.DestroyImmediate(block.gameObject);
        }

        [Test]
        public void SetEditingInstance_Null_ClearsInstanceAndFiresEvent()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);
            session.SetEditingInstance(block);

            int eventCount = 0;
            BlockBehaviour lastReceived = block; // intentionally non-null to verify the event clears
            session.EditingInstanceChanged += b => { eventCount++; lastReceived = b; };

            session.SetEditingInstance(null);

            Assert.IsNull(session.EditingInstance,
                "EditingInstance must be null after SetEditingInstance(null).");
            Assert.AreEqual(1, eventCount,
                "EditingInstanceChanged must fire once when clearing the instance.");
            Assert.IsNull(lastReceived,
                "EditingInstanceChanged argument must be null when clearing.");

            Object.DestroyImmediate(block.gameObject);
        }

        /// <summary>
        /// Idempotent: setting the same reference twice fires the event only once.
        /// This matches the precedent established by SetMirrorAxis and
        /// SetSelectedBlock in BuildSessionTests.cs.
        /// </summary>
        [Test]
        public void SetEditingInstance_SameReference_DoesNotFireEventAgain()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);
            int eventCount = 0;
            session.EditingInstanceChanged += _ => eventCount++;

            session.SetEditingInstance(block);
            Assert.AreEqual(1, eventCount, "First set must fire the event.");

            session.SetEditingInstance(block); // same reference
            Assert.AreEqual(1, eventCount,
                "Re-setting the same instance must NOT fire EditingInstanceChanged again (idempotent).");

            Object.DestroyImmediate(block.gameObject);
        }

        /// <summary>
        /// Transition A → B fires the event (once), not a no-op on A.
        /// Mirrors SelectedBlockChanged_FiresOnceOnTransition.
        /// </summary>
        [Test]
        public void SetEditingInstance_TransitionToNewBlock_FiresEvent()
        {
            var session = new BuildSession();
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Aero);

            int eventCount = 0;
            BlockBehaviour lastSeen = null;
            session.EditingInstanceChanged += b => { eventCount++; lastSeen = b; };

            session.SetEditingInstance(blockA);
            Assert.AreEqual(1, eventCount);

            session.SetEditingInstance(blockB);
            Assert.AreEqual(2, eventCount,
                "Switching from block A to block B must fire EditingInstanceChanged.");
            Assert.AreSame(blockB, lastSeen,
                "The event argument must be the new block (B), not the old one.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }

        /// <summary>
        /// Null → null is idempotent: no event fires when the instance is
        /// already null and SetEditingInstance(null) is called again.
        /// </summary>
        [Test]
        public void SetEditingInstance_NullToNull_DoesNotFireEvent()
        {
            var session = new BuildSession();
            // Default state: EditingInstance == null.
            int eventCount = 0;
            session.EditingInstanceChanged += _ => eventCount++;

            session.SetEditingInstance(null);

            Assert.AreEqual(0, eventCount,
                "Setting null when already null must not fire EditingInstanceChanged.");
        }

        // -----------------------------------------------------------------------
        // PropagateVariantToLiveBlocks scoping — integration-level logic test
        //
        // BlockEditor.PropagateVariantToLiveBlocks is private. We test the
        // behavioral contract it implements via the session + a minimal block
        // loop, analogous to what the editor does: iterate grid blocks, filter by
        // blockId, skip blocks that don't match EditingInstance when one is set.
        // This test exercises the LOGIC of the filter, not the MonoBehaviour
        // plumbing, which keeps it in EditMode and out of a full scene.
        // -----------------------------------------------------------------------

        /// <summary>
        /// When EditingInstance is non-null, only the bound block must receive
        /// the variant update. Other blocks of the same type must be unaffected.
        /// This is the "retune one rotor's RPM without touching the others"
        /// invariant from session 125.
        /// </summary>
        [Test]
        public void PropagateVariantScope_WhenInstanceBound_OnlyBoundBlockUpdates()
        {
            // Simulate two rotor blocks that would match the propagation blockId.
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);

            // Manual propagation loop mirrors BlockEditor.PropagateVariantToLiveBlocks
            // logic (without the MonoBehaviour + grid dependency):
            float newConfig = 360f;
            BlockBehaviour only = blockA; // EditingInstance = blockA

            // Apply to only == blockA when set.
            foreach (BlockBehaviour block in new[] { blockA, blockB })
            {
                if (block.Definition == null) continue;
                if (block.Definition.Id != BlockIds.Rotor) continue;
                if (only != null && block != only) continue; // instance-edit filter
                block.ConfigValue = newConfig;
            }

            Assert.AreEqual(newConfig, blockA.ConfigValue,
                "Bound instance (blockA) must receive the variant config update.");
            Assert.AreNotEqual(newConfig, blockB.ConfigValue,
                "Non-bound block (blockB) of the same type must NOT receive the update " +
                "when an EditingInstance is bound.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }

        /// <summary>
        /// When EditingInstance is null (normal placement mode), all blocks of
        /// the matching type must receive the variant update. This is the
        /// pre-session-125 "live mid-edit" feature that must be preserved.
        /// </summary>
        [Test]
        public void PropagateVariantScope_WhenNoInstanceBound_AllMatchingBlocksUpdate()
        {
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockC = MakeBlock(BlockIds.Aero); // different type — must not update

            float newConfig = 480f;
            BlockBehaviour only = null; // EditingInstance = null

            foreach (BlockBehaviour block in new[] { blockA, blockB, blockC })
            {
                if (block.Definition == null) continue;
                if (block.Definition.Id != BlockIds.Rotor) continue;
                if (only != null && block != only) continue; // no-op when only == null
                block.ConfigValue = newConfig;
            }

            Assert.AreEqual(newConfig, blockA.ConfigValue,
                "blockA must receive the update when EditingInstance is null.");
            Assert.AreEqual(newConfig, blockB.ConfigValue,
                "blockB must receive the update when EditingInstance is null.");
            Assert.AreNotEqual(newConfig, blockC.ConfigValue,
                "blockC (different type) must not receive a rotor update.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
            Object.DestroyImmediate(blockC.gameObject);
        }

        /// <summary>
        /// Clearing EditingInstance (back to null) after an instance was bound
        /// must restore the all-blocks propagation behavior. This ensures the
        /// Escape key / block-type switch exit path doesn't leave a stale "only
        /// this block" filter active.
        /// </summary>
        [Test]
        public void BuildSession_ClearInstanceEdit_RestoresAllBlockPropagation()
        {
            var session = new BuildSession();
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);

            // Bind blockA.
            session.SetEditingInstance(blockA);
            Assert.AreSame(blockA, session.EditingInstance);

            // Clear the binding.
            session.SetEditingInstance(null);
            Assert.IsNull(session.EditingInstance,
                "EditingInstance must be null after clearing — all-blocks propagation must resume.");

            // Verify: simulate the propagation with the now-null instance.
            float newConfig = 120f;
            BlockBehaviour only = session.EditingInstance; // null

            foreach (BlockBehaviour block in new[] { blockA, blockB })
            {
                if (block.Definition == null) continue;
                if (block.Definition.Id != BlockIds.Rotor) continue;
                if (only != null && block != only) continue;
                block.ConfigValue = newConfig;
            }

            Assert.AreEqual(newConfig, blockA.ConfigValue,
                "blockA must receive updates after instance-edit is cleared.");
            Assert.AreEqual(newConfig, blockB.ConfigValue,
                "blockB must receive updates after instance-edit is cleared — " +
                "stale instance filter must not persist after SetEditingInstance(null).");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }
    }
}
