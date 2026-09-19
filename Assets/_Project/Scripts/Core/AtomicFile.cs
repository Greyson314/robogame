using System.IO;
using System.Text;

namespace Robogame.Core
{
    /// <summary>
    /// Crash-safe text writes for player save data (best-practices.md § 11.3):
    /// blueprints, concoctions and tweakables all go through this instead of
    /// a bare <see cref="File.WriteAllText(string, string)"/>.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> by first
        /// writing a <c>.tmp</c> sibling, then swapping it in — via
        /// <see cref="File.Replace(string, string, string)"/> (leaving a
        /// <c>.bak</c> of whatever <paramref name="path"/> held) when
        /// <paramref name="path"/> already exists, or a plain
        /// <see cref="File.Move(string, string)"/> for a first save. A reader
        /// never observes a partially written file. On any failure the
        /// <c>.tmp</c> is removed best-effort, <paramref name="path"/> is left
        /// exactly as it was, and the exception is rethrown so the caller's
        /// existing error handling is unchanged.
        /// </summary>
        public static void WriteAllText(string path, string text, Encoding encoding)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, text, encoding);
                if (File.Exists(path))
                    File.Replace(tmp, path, path + ".bak");
                else
                    File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { /* best-effort cleanup; the original exception is what matters */ }
                throw;
            }
        }
    }
}
