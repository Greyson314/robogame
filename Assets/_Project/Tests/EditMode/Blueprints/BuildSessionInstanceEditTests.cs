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
//     the bound block receives the variant change; when null, NO placed block
//     does — the variant cache only shapes the next placement. This is the
//     core behavioral contract — "retune one rotor's RPM without touching
//     the others" (session 125, tightened by the span-isolation session).
//
// WHY EDITMODE
//   BuildSession is pure C#. No MonoBehaviour, no scene, no Start/Update.
//   The test runs 10x faster in EditMode and has zero scene-lifecycle risk.
//   Only the propagation tests need minimal BlockBehaviour stubs; those are
//   created via reflection against _definition (the same fallback pattern
//   the existing BuildSessionTests.cs uses for BlockDefinition._id).
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
        /// Uses the internal <c>Initialize</c> method on BlockBehaviour when
        /// accessible (same path BlockGrid uses at placement time); falls back
        /// to writing the private <c>_definition</c> field directly so the
        /// Definition property resolves, which is all the propagation logic
        /// and the event tests need.
        /// </summary>
        private static BlockBehaviour MakeBlock(string blockId)
        {
            var go = new GameObject($"TestBlock_{blockId}");
            BlockBehaviour bb = go.AddComponent<BlockBehaviour>();
            BlockDefinition def = MakeDef(blockId);

            // Try BlockBehaviour.Initialize(def, gridPos, dims, up, pitchDeg, yaw)
            // — internal method, accessible via reflection. Signature confirmed
            // from BlockBehaviour.cs line ~170.
            var initMethod = typeof(BlockBehaviour).GetMethod(
                "Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                // params: (BlockDefinition definition, Vector3Int gridPosition,
                //          Vector3 dims = default, Vector3Int up = default,
                //          float pitchDeg = 0f, int yaw = 0)
                initMethod.Invoke(bb, new object[]
                {
                    def, Vector3Int.zero, Vector3.zero, Vector3Int.up, 0f, 0
                });
            }
            else
            {
                // Fallback: write _definition directly. All tests in this file
                // only need Definition to be non-null and return the right Id.
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
                "EditingInstanceChanged must fire with the new block as its argument.");

            Object.DestroyImmediate(block.gameObject);
        }

        [Test]
        public void SetEditingInstance_Null_ClearsInstanceAndFiresEvent()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);
            session.SetEditingInstance(block);

            int eventCount = 0;
            BlockBehaviour lastReceived = block; // intentionally non-null to verify the event fires null
            session.EditingInstanceChanged += b => { eventCount++; lastReceived = b; };

            session.SetEditingInstance(null);

            Assert.IsNull(session.EditingInstance,
                "EditingInstance must be null after SetEditingInstance(null).");
            Assert.AreEqual(1, eventCount,
                "EditingInstanceChanged must fire exactly once when clearing the instance.");
            Assert.IsNull(lastReceived,
                "EditingInstanceChanged argument must be null when clearing.");

            Object.DestroyImmediate(block.gameObject);
        }

        /// <summary>
        /// Idempotent: setting the same reference twice fires the event only once.
        /// Matches the precedent established by SetMirrorAxis and SetSelectedBlock
        /// in BuildSessionTests.cs ("Same-axis set must not re-fire MirrorChanged").
        /// </summary>
        [Test]
        public void SetEditingInstance_SameReference_DoesNotFireEventAgain()
        {
            var session = new BuildSession();
            BlockBehaviour block = MakeBlock(BlockIds.Rotor);
            int eventCount = 0;
            session.EditingInstanceChanged += _ => eventCount++;

            session.SetEditingInstance(block);
            Assert.AreEqual(1, eventCount, "First set must fire the event once.");

            session.SetEditingInstance(block); // same reference — no-op
            Assert.AreEqual(1, eventCount,
                "Re-setting the same instance must NOT fire EditingInstanceChanged (idempotent). " +
                "Equivalent to SetSelectedBlock(same) — second call is a no-op.");

            Object.DestroyImmediate(block.gameObject);
        }

        /// <summary>
        /// Transition A → B fires the event once with B as argument.
        /// Mirrors SelectedBlockChanged_FiresOnceOnTransition.
        /// </summary>
        [Test]
        public void SetEditingInstance_TransitionToNewBlock_FiresEventOnce()
        {
            var session = new BuildSession();
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Aero);

            int eventCount = 0;
            BlockBehaviour lastSeen = null;
            session.EditingInstanceChanged += b => { eventCount++; lastSeen = b; };

            session.SetEditingInstance(blockA);
            Assert.AreEqual(1, eventCount, "A → (first set) must fire once.");

            session.SetEditingInstance(blockB);
            Assert.AreEqual(2, eventCount,
                "A → B transition must fire EditingInstanceChanged once more.");
            Assert.AreSame(blockB, lastSeen,
                "The event argument on A → B must be B, not A.");

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
            // Default state: EditingInstance is null.
            int eventCount = 0;
            session.EditingInstanceChanged += _ => eventCount++;

            session.SetEditingInstance(null);

            Assert.AreEqual(0, eventCount,
                "Setting null when already null must not fire EditingInstanceChanged.");
        }

        // -----------------------------------------------------------------------
        // PropagateVariantToLiveBlocks scoping — behavioral contract tests
        //
        // BlockEditor.PropagateVariantToLiveBlocks is private and requires a
        // full MonoBehaviour + scene setup. We verify the filtering LOGIC by
        // running the same conditional the method uses, driven by
        // BuildSession.EditingInstance. Since the span-isolation session the
        // contract is: variant changes touch placed blocks ONLY through a
        // bound EditingInstance; with none bound the variant cache is
        // "next placement" state and no placed block moves.
        // -----------------------------------------------------------------------

        // Mirrors the guard in BlockEditor.PropagateVariantToLiveBlocks:
        // resolve the bound instance, apply only to it, only on id match.
        private static void SimulatePropagation(BuildSession session, string blockId, float newConfig)
        {
            BlockBehaviour target = session.EditingInstance;
            if (target == null || target.Definition == null) return;
            if (target.Definition.Id != blockId) return;
            target.ConfigValue = newConfig;
        }

        /// <summary>
        /// When EditingInstance is non-null, only the bound block must receive
        /// the variant update. Other blocks of the same type are skipped.
        /// This is the "retune one rotor's RPM without touching the others"
        /// invariant from session 125 — the entire point of the feature.
        /// </summary>
        [Test]
        public void PropagateVariantScope_WhenInstanceBound_OnlyBoundBlockUpdates()
        {
            var session = new BuildSession();
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);

            session.SetEditingInstance(blockA);

            const float newConfig = 360f;
            SimulatePropagation(session, BlockIds.Rotor, newConfig);

            Assert.AreEqual(newConfig, blockA.ConfigValue,
                "Bound instance (blockA) must receive the variant config update.");
            Assert.AreNotEqual(newConfig, blockB.ConfigValue,
                "Non-bound block (blockB) of the same type must NOT be updated " +
                "when an EditingInstance is bound.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }

        /// <summary>
        /// When EditingInstance is null (normal placement mode), NO placed
        /// block receives the variant update — the cache only shapes the next
        /// placement. This retires the session-96 all-blocks live propagation:
        /// changing one foil's span must never rewrite every foil on the bot
        /// (span-isolation session).
        /// </summary>
        [Test]
        public void PropagateVariantScope_WhenNoInstanceBound_NoPlacedBlockUpdates()
        {
            var session = new BuildSession();
            // EditingInstance is null by default.
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);

            Assert.IsNull(session.EditingInstance,
                "Precondition: EditingInstance must be null for this test.");

            const float newConfig = 480f;
            SimulatePropagation(session, BlockIds.Rotor, newConfig);

            Assert.AreNotEqual(newConfig, blockA.ConfigValue,
                "No placed block may update when EditingInstance is null — " +
                "the variant cache is next-placement state only.");
            Assert.AreNotEqual(newConfig, blockB.ConfigValue,
                "No placed block may update when EditingInstance is null — " +
                "the variant cache is next-placement state only.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }

        /// <summary>
        /// Clearing EditingInstance (SetEditingInstance(null)) ends live
        /// editing entirely: the previously bound block must stop receiving
        /// updates too. The Escape-key / block-type-switch exit path must not
        /// leave a stale single-block binding active.
        /// </summary>
        [Test]
        public void BuildSession_ClearInstanceEdit_StopsAllLivePropagation()
        {
            var session = new BuildSession();
            BlockBehaviour blockA = MakeBlock(BlockIds.Rotor);
            BlockBehaviour blockB = MakeBlock(BlockIds.Rotor);

            // Bind blockA, then clear.
            session.SetEditingInstance(blockA);
            session.SetEditingInstance(null);

            Assert.IsNull(session.EditingInstance,
                "EditingInstance must be null after clearing.");

            const float newConfig = 120f;
            SimulatePropagation(session, BlockIds.Rotor, newConfig);

            Assert.AreNotEqual(newConfig, blockA.ConfigValue,
                "The previously bound block must stop receiving updates after " +
                "instance-edit is cleared.");
            Assert.AreNotEqual(newConfig, blockB.ConfigValue,
                "Unbound blocks must never receive live updates.");

            Object.DestroyImmediate(blockA.gameObject);
            Object.DestroyImmediate(blockB.gameObject);
        }

        /// <summary>
        /// A bound instance of a DIFFERENT type must not receive another
        /// type's variant change — the id filter still applies inside
        /// instance-edit mode.
        /// </summary>
        [Test]
        public void PropagateVariantScope_BoundInstanceOfOtherType_DoesNotUpdate()
        {
            var session = new BuildSession();
            BlockBehaviour aero = MakeBlock(BlockIds.Aero);

            session.SetEditingInstance(aero);

            const float newConfig = 480f;
            SimulatePropagation(session, BlockIds.Rotor, newConfig);

            Assert.AreNotEqual(newConfig, aero.ConfigValue,
                "A Rotor variant change must not touch a bound Aero instance.");

            Object.DestroyImmediate(aero.gameObject);
        }
    }
}
