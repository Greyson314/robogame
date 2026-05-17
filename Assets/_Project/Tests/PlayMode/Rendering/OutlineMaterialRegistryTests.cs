using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Player;
using UnityEngine;

namespace Robogame.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Pins the outline→plain lookup that the relevance-gated chassis
    /// outline relies on. The registry is the decoupling point: a
    /// renderer in outline state maps to its plain counterpart; anything
    /// not registered (already non-outline) maps to itself so the swap
    /// is a safe no-op.
    /// </summary>
    public sealed class OutlineMaterialRegistryTests
    {
        private readonly List<Object> _trash = new();

        private Material Mat(string name)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(s) { name = name };
            _trash.Add(m);
            return m;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _trash) if (o != null) Object.DestroyImmediate(o);
            _trash.Clear();
        }

        [Test]
        public void GetPlain_MapsOutlineToPlain_PassesThroughUnknownAndNull()
        {
            var reg = ScriptableObject.CreateInstance<OutlineMaterialRegistry>();
            _trash.Add(reg);

            Material outline = Mat("BlockMat_Structure");
            Material plain = Mat("BlockMat_Structure_Plain");
            Material unregistered = Mat("BlockMat_Wheel"); // never outlined

            reg.EditorSetPairs(new List<OutlineMaterialRegistry.Pair>
            {
                new() { Outline = outline, Plain = plain },
            });

            Assert.AreSame(plain, reg.GetPlain(outline),
                "Outlined material must map to its plain counterpart.");
            Assert.AreSame(unregistered, reg.GetPlain(unregistered),
                "An unregistered (already non-outline) material maps to itself — swap is a no-op.");
            Assert.IsNull(reg.GetPlain(null), "Null in, null out.");
        }

        [Test]
        public void GetPlain_RebuildsLookupAfterPairsReplaced()
        {
            var reg = ScriptableObject.CreateInstance<OutlineMaterialRegistry>();
            _trash.Add(reg);

            Material o1 = Mat("o1"), p1 = Mat("p1");
            reg.EditorSetPairs(new List<OutlineMaterialRegistry.Pair> { new() { Outline = o1, Plain = p1 } });
            Assert.AreSame(p1, reg.GetPlain(o1));

            Material o2 = Mat("o2"), p2 = Mat("p2");
            reg.EditorSetPairs(new List<OutlineMaterialRegistry.Pair> { new() { Outline = o2, Plain = p2 } });
            Assert.AreSame(p2, reg.GetPlain(o2), "Lookup must rebuild after pairs are replaced.");
            Assert.AreSame(o1, reg.GetPlain(o1), "Old mapping must be gone (maps to itself).");
        }
    }
}
