using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The rule for every damaged input (ROADMAP.md §2.1): the editor may refuse it with a message,
    /// but must never crash with an exception of its own making (index out of range, null, overflow,
    /// out of memory), hang, or eat all memory.
    /// </summary>
    public static class Damage
    {
        /// <summary>Exceptions that are a refusal with a message: the readers' own.</summary>
        public static bool IsRefusal(Exception e)
        {
            return e is UltimateEditor.Games.GameFileException || e is InvalidDataException || e is FormatException;
        }

        /// <summary>Runs a reader on damaged input: it must finish within the time limit and either succeed or
        /// refuse. Returns the refusal's message (null when it read the input), for the test's report.</summary>
        public static string Run(string what, Action read, int seconds = 10, Func<Exception, bool> alsoRefusal = null)
        {
            Exception thrown = null;
            long allocated = 0;
            long baseline = GC.GetTotalMemory(false), peak = baseline;
            var task = Task.Run(() =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                try { read(); }
                catch (Exception e) { thrown = e; }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            });
            // the heap while it reads (this project runs its tests one at a time: AssemblyInfo.cs)
            var clock = Stopwatch.StartNew();
            while (!task.Wait(5) && clock.Elapsed.TotalSeconds < seconds) peak = Math.Max(peak, GC.GetTotalMemory(false));
            peak = Math.Max(peak, GC.GetTotalMemory(false));
            Assert.True(task.IsCompleted, what + ": no answer within " + seconds + " s (a loop?)");
            if (thrown != null && !IsRefusal(thrown) && (alsoRefusal == null || !alsoRefusal(thrown)))
                Assert.Fail(what + ": " + thrown.GetType().Name + " instead of a refusal: " + thrown.Message + Environment.NewLine + thrown.StackTrace);
            Assert.True(peak - baseline < MaxLiveBytes, what + ": the heap grew by " + (peak - baseline) / (1 << 20) + " MB (more than " + MaxLiveBytes / (1 << 20) + ")");
            Assert.True(allocated < MaxAllocatedBytes, what + ": " + allocated / (1 << 20) + " MB allocated in all (more than " + MaxAllocatedBytes / (1 << 20) + ")");
            return thrown?.Message;
        }

        /// <summary>How much the heap may grow while one damaged file is read (ROADMAP.md §2.1: memory under 500 MB).</summary>
        public const long MaxLiveBytes = 500L << 20;
        /// <summary>All a read may allocate, short-lived objects included: a runaway that churns.</summary>
        public const long MaxAllocatedBytes = 4L << 30;

        /// <summary>Seeds per file kind: 200 by default, 2,000 with UW_FUZZ_LONG=1 (before each release).</summary>
        public static int Seeds { get { return Environment.GetEnvironmentVariable("UW_FUZZ_LONG") == "1" ? 2000 : 200; } }

        /// <summary>
        /// A damaged copy of a file, reproducible from the seed: random bytes flipped anywhere, or a
        /// count, offset or length in the header (its first headerLength bytes) set to a value that
        /// lies (0, -1, 0x7FFFFFFF, the file's length + 1...). what says which.
        /// </summary>
        public static byte[] Mutate(byte[] data, int seed, int headerLength, out string what)
        {
            var r = new Random(seed);
            var b = (byte[])data.Clone();
            if (b.Length == 0) { what = "empty"; return b; }
            int header = Math.Max(2, Math.Min(b.Length, headerLength));
            var parts = new List<string>();
            int edits = 1 + r.Next(3);
            for (int e = 0; e < edits; e++)
            {
                switch (r.Next(3))
                {
                    case 0:
                        {
                            int n = 1 + r.Next(8);
                            for (int k = 0; k < n; k++) { int at = r.Next(b.Length); b[at] ^= (byte)(1 + r.Next(255)); }
                            parts.Add(n + " bytes flipped");
                            break;
                        }
                    default:
                        {
                            long[] lies = { 0, -1, 0x7FFFFFFF, b.Length + 1, b.Length - 1, b.Length, 0x8000, 0xFFFF, 1, r.Next() };
                            long v = lies[r.Next(lies.Length)];
                            bool wide = r.Next(2) == 0;
                            int at = r.Next(header) & ~1;
                            int width = wide ? 4 : 2;
                            if (at + width > b.Length) at = Math.Max(0, b.Length - width);
                            for (int i = 0; i < width && at + i < b.Length; i++) b[at + i] = (byte)(v >> (8 * i));
                            parts.Add((wide ? "long" : "word") + " at 0x" + at.ToString("X") + " = " + v);
                            break;
                        }
                }
            }
            what = "seed " + seed + ": " + string.Join(", ", parts);
            return b;
        }

        /// <summary>The lengths a file is cut at: the first bytes one by one, every header word, the
        /// quarters, and one byte short.</summary>
        public static IEnumerable<int> Cuts(int length)
        {
            var set = new SortedSet<int>();
            for (int i = 0; i <= 16 && i < length; i++) set.Add(i);
            for (int i = 18; i < 64 && i < length; i += 2) set.Add(i);
            foreach (int pct in new[] { 1, 5, 10, 25, 50, 75, 90, 99 }) set.Add((int)((long)length * pct / 100));
            set.Add(Math.Max(0, length - 1));
            set.Remove(length);
            return set;
        }

        public static byte[] Cut(byte[] data, int length)
        {
            var b = new byte[length];
            Array.Copy(data, b, length);
            return b;
        }

        /// <summary>A copy of a game folder (DATA and the executable), deleted when disposed.</summary>
        public sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-robust-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string game)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(game, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(game, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public string Data(string name) { return Path.Combine(Folder, "DATA", name); }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }
    }
}
