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
        public static string Run(string what, Action read, int seconds = 10)
        {
            Exception thrown = null;
            var task = Task.Run(() =>
            {
                try { read(); }
                catch (Exception e) { thrown = e; }
            });
            Assert.True(task.Wait(TimeSpan.FromSeconds(seconds)), what + ": no answer within " + seconds + " s (a loop?)");
            if (thrown != null && !IsRefusal(thrown))
                Assert.Fail(what + ": " + thrown.GetType().Name + " instead of a refusal: " + thrown.Message + "\n" + thrown.StackTrace);
            return thrown?.Message;
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
