using NUnit.Framework;
using Robogame.Tools.Editor;

namespace Robogame.Tests.EditMode.Tools
{
    /// <summary>
    /// ContinualTraces.TryParseMarker is the single per-line rule that decides
    /// whether a `// TRACE[id]: note` marker is scanned into docs/TRACES.md
    /// (CLAUDE.md § Continual Traces). F-041: on main the marker regex only
    /// matches `TRACE[` immediately after `//`, so a marker written mid-comment
    /// or on a `///` doc line is invisible to the index — the ONLY INV-2 anchor
    /// (BuildModeController.cs:92) is exactly that shape, so "building happens
    /// only in the garage" has a trace in the code and no row in the generated
    /// map, and a future rename there would rot silently. These cases must scan.
    /// </summary>
    public sealed class ContinualTracesTests
    {
        [Test]
        public void TryParseMarker_LeadingMarker_Scans()
        {
            bool ok = ContinualTraces.TryParseMarker(
                "// TRACE[INV-2]: single Rigidbody", out string id, out string note);

            Assert.That(ok, Is.True);
            Assert.That(id, Is.EqualTo("INV-2"));
            Assert.That(note, Is.EqualTo("single Rigidbody"));
        }

        [Test]
        public void TryParseMarker_MidComment_Scans()
        {
            // F-041: the anchor sits after prose on the same comment line, the
            // shape of BuildModeController.cs:92's INV-2 marker. Must not be
            // invisible to Validate/Rebuild Index.
            bool ok = ContinualTraces.TryParseMarker(
                "// keeps INV-2 honest: TRACE[INV-2]: mirror", out string id, out string note);

            Assert.That(ok, Is.True);
            Assert.That(id, Is.EqualTo("INV-2"));
            Assert.That(note, Is.EqualTo("mirror"));
        }

        [Test]
        public void TryParseMarker_TripleSlashDocLine_Scans()
        {
            bool ok = ContinualTraces.TryParseMarker(
                "/// TRACE[ADR-0003]: shared cooldown", out string id, out string note);

            Assert.That(ok, Is.True);
            Assert.That(id, Is.EqualTo("ADR-0003"));
            Assert.That(note, Is.EqualTo("shared cooldown"));
        }

        [Test]
        public void TryParseMarker_TrailingAfterCode_Scans()
        {
            bool ok = ContinualTraces.TryParseMarker(
                "int x = 1; // TRACE[LOG-5]: trailing", out string id, out string note);

            Assert.That(ok, Is.True);
            Assert.That(id, Is.EqualTo("LOG-5"));
            Assert.That(note, Is.EqualTo("trailing"));
        }

        [Test]
        public void TryParseMarker_NoMarker_DoesNotScan()
        {
            bool ok = ContinualTraces.TryParseMarker(
                "// no marker here", out string id, out string note);

            Assert.That(ok, Is.False);
        }
    }
}
