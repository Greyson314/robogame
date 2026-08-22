using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Blueprints
{
    /// <summary>
    /// <see cref="ChassisBlueprint.Revision"/> is the dirty signal the garage
    /// autosave (session 166) keys off: <c>GameStateController.IsDirty</c> is
    /// <c>Revision != revisionAtLastSave</c>. If a persisted mutation ever
    /// stops bumping it, edits silently stop autosaving; if a non-mutation
    /// bumps it, every build-mode exit rewrites an unchanged file. These
    /// tests pin both edges.
    /// </summary>
    public sealed class ChassisBlueprintRevisionTests
    {
        private static ChassisBlueprint Fresh()
        {
            var bp = ScriptableObject.CreateInstance<ChassisBlueprint>();
            bp.DisplayName = "Rev Test";
            return bp;
        }

        [Test]
        public void SetEntries_BumpsRevision_EveryCall()
        {
            ChassisBlueprint bp = Fresh();
            int r0 = bp.Revision;
            bp.SetEntries(new[] { new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero) });
            Assert.AreEqual(r0 + 1, bp.Revision, "first SetEntries must bump");
            // A second sync with identical content still bumps: the counter
            // tracks "a mutation path ran", not content equality. Cheap and
            // conservative — autosave re-writing an identical file is harmless,
            // missing a real edit is not.
            bp.SetEntries(new[] { new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero) });
            Assert.AreEqual(r0 + 2, bp.Revision, "second SetEntries must bump too");
        }

        [Test]
        public void DisplayName_BumpsRevision_OnlyWhenChanged()
        {
            ChassisBlueprint bp = Fresh();
            int r0 = bp.Revision;
            bp.DisplayName = "Rev Test";       // same value → no edit to persist
            Assert.AreEqual(r0, bp.Revision, "unchanged name must not dirty the blueprint");
            bp.DisplayName = "Renamed";        // the HUD name field commits here
            Assert.AreEqual(r0 + 1, bp.Revision, "a real rename must dirty the blueprint");
        }

        [Test]
        public void Revision_IsNotSerialized_IntoJson()
        {
            // The counter is runtime state. A round-trip through the on-disk
            // format must come back at the serializer's own revision (fresh
            // instance + its SetEntries/DisplayName writes), not carry the
            // editing session's count — otherwise a loaded file could look
            // "dirty" or "clean" depending on how many edits preceded the save.
            ChassisBlueprint bp = Fresh();
            for (int i = 0; i < 5; i++)
                bp.SetEntries(new[] { new ChassisBlueprint.Entry(BlockIds.Cube, Vector3Int.zero) });
            string json = BlueprintSerializer.ToJson(bp, prettyPrint: false);
            StringAssert.DoesNotContain("Revision", json);
            StringAssert.DoesNotContain("revision", json);
        }
    }
}
