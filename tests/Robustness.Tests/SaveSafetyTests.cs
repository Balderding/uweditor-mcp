using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UwEditor.Core.Conversations;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Saving under bad conditions (ROADMAP.md §2.2): a full disk at any point of a write, a
    /// read-only or locked file. The file is either the old version or the new one, byte for
    /// byte, and no temporary file is left; temporary files an editor that died left are swept.
    /// </summary>
    public class SaveSafetyTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "uwedit-save-" + Guid.NewGuid().ToString("N"));
        public SaveSafetyTests() { Directory.CreateDirectory(dir); }
        public void Dispose()
        {
            SafeFile.Writing = null;
            foreach (var f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        static byte[] Bytes(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }
        string[] Temps() { return Directory.GetFiles(dir, "*.tmp"); }

        [Fact]
        public void A_full_disk_at_any_point_leaves_the_old_file_whole()
        {
            string p = Path.Combine(dir, "LEV.ARK");
            var old = Bytes(200000, 1);
            var next = Bytes(300000, 2);
            File.WriteAllBytes(p, old);
            for (long failAt = 0; failAt <= next.Length; failAt += 0x10000)
            {
                long at = Math.Min(failAt, next.Length);
                SafeFile.Writing = (tmp, written) => { if (written >= at) throw new IOException("There is not enough space on the disk."); };
                Assert.Throws<IOException>(() => SafeFile.Write(p, next));
                Assert.Equal(old, File.ReadAllBytes(p));
                Assert.Empty(Temps());
            }
            SafeFile.Writing = null;
            SafeFile.Write(p, next);
            Assert.Equal(next, File.ReadAllBytes(p));
            Assert.Equal(old, File.ReadAllBytes(p + ".BKP"));
            Assert.Equal(old, File.ReadAllBytes(p + ".ORIG"));
            Assert.Empty(Temps());
        }

        [Fact]
        public void A_read_only_or_locked_file_is_refused_and_kept()
        {
            string p = Path.Combine(dir, "STRINGS.PAK");
            var old = Bytes(5000, 3);
            File.WriteAllBytes(p, old);
            File.SetAttributes(p, FileAttributes.ReadOnly);
            Assert.ThrowsAny<Exception>(() => SafeFile.Write(p, Bytes(6000, 4)));
            File.SetAttributes(p, FileAttributes.Normal);
            Assert.Equal(old, File.ReadAllBytes(p));
            Assert.Empty(Temps());

            // DOSBox or the game holding it open
            using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => SafeFile.Write(p, Bytes(6000, 4)));
                Assert.Empty(Temps());
            }
            Assert.Equal(old, File.ReadAllBytes(p));
        }

        [Fact]
        public void The_editors_own_files_are_written_whole_without_copies()
        {
            string p = Path.Combine(dir, "uw1-zones.json");
            SafeFile.WriteAtomic(p, "{ \"a\": 1 }");
            SafeFile.Writing = (tmp, written) => { throw new IOException("disk full"); };
            Assert.Throws<IOException>(() => SafeFile.WriteAtomic(p, "{ \"a\": 2 }"));
            SafeFile.Writing = null;
            Assert.Equal("{ \"a\": 1 }", File.ReadAllText(p));
            Assert.Equal(new[] { "uw1-zones.json" }, Directory.GetFiles(dir).Select(Path.GetFileName));
        }

        [Fact]
        public void Temporary_files_of_dead_editors_are_swept()
        {
            File.WriteAllBytes(Path.Combine(dir, "LEV.ARK"), new byte[4]);
            int other = Process.GetProcesses().Select(x => x.Id).First(id => id != Environment.ProcessId && id > 4);
            string dead = Path.Combine(dir, "LEV.ARK.2147483646.tmp");
            string mine = SafeFile.TempOf(Path.Combine(dir, "LEV.ARK"));
            string live = Path.Combine(dir, "LEV.ARK." + other + ".tmp");
            string legacy = Path.Combine(dir, "LEV.ARK.tmp");
            string stranger = Path.Combine(dir, "notes.tmp");
            foreach (var f in new[] { dead, mine, live, legacy, stranger }) File.WriteAllBytes(f, new byte[1]);
            File.SetLastWriteTimeUtc(legacy, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(2, SafeFile.SweepTemporaryFiles(dir));
            Assert.False(File.Exists(dead));
            Assert.False(File.Exists(legacy));
            Assert.True(File.Exists(mine));
            Assert.True(File.Exists(live));
            Assert.True(File.Exists(stranger));
        }

        [Fact]
        public void Opening_a_game_sweeps_its_folders()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            using (var copy = new Damage.GameCopy(g))
            {
                string left = LevArk(copy) + ".2147483646.tmp";
                File.WriteAllBytes(left, new byte[1]);
                GameSession.Open(copy.Folder);
                Assert.False(File.Exists(left));
            }
        }

        static string LevArk(Damage.GameCopy copy) { return UltimateEditor.Games.GameProfile.Find(Path.Combine(copy.Folder, "DATA"), "LEV.ARK"); }
    }
}
