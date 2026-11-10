using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core.Conversations;
using UwEditor.Core.Graphics;
using UwEditor.Core.Music;
using UwEditor.Core.Player;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Bit flips and lying header fields (ROADMAP.md §2.1, test 2): Damage.Seeds damaged copies of
    /// each kind of game file, each read the way the editor reads it. Every copy must be refused
    /// with a message or read, never crash, hang or allocate more than 500 MB. A failure names the
    /// seed, so it can be replayed with Damage.Mutate.
    /// </summary>
    public class FuzzTests
    {
        readonly ITestOutputHelper output;
        public FuzzTests(ITestOutputHelper output) { this.output = output; }

        static GameKind KindOf(string game) { return game == "UW2" ? GameKind.UW2 : GameKind.UW1; }

        static readonly ConcurrentDictionary<string, GameSession> sessions = new ConcurrentDictionary<string, GameSession>();
        static GameSession Session(string game) { return sessions.GetOrAdd(game, g => GameSession.Open(GameData.Game(g))); }

        static string PathOf(string game, string file)
        {
            string g = GameData.Game(game);
            if (g == null) return null;
            if (file.Contains("/")) { string p = Path.Combine(g, file); return File.Exists(p) ? p : null; }
            return GameProfile.Find(Path.Combine(g, "DATA"), file);
        }

        /// <summary>
        /// Seeds that once broke a reader (ROADMAP.md §2.7: every crash found is kept, as the recipe
        /// that makes it from the game's file): run every time, not only in the long run.
        /// </summary>
        static readonly Dictionary<string, int[]> KnownCases = new Dictionary<string, int[]>
        {
            // a block shorter than its own string table (StringsPakFile.Original)
            { "UW STRINGS.PAK", new[] { 27150015 } },
            { "UW2 STRINGS.PAK", new[] { 198230007 } },
            // a string holding code 1, taken for a menu's marker (Decompiler's emit)
            { "UW2 CNV.ARK", new[] { 148720629 } },
        };

        /// <summary>Runs Damage.Seeds damaged copies of a file through read (seeds differ per file), and its known cases.</summary>
        void Fuzz(string game, string file, int headerLength, Action<byte[]> read, Func<Exception, bool> alsoRefusal = null)
        {
            string path = PathOf(game, file);
            if (path == null) return;
            var data = File.ReadAllBytes(path);
            int baseSeed = (game + file).Aggregate(17, (h, c) => h * 31 + c) & 0x7FFF;
            int refused = 0, n = Damage.Seeds;
            int[] known;
            KnownCases.TryGetValue(game + " " + file, out known);
            foreach (int seed in Enumerable.Range(0, n).Select(k => baseSeed * 10000 + k).Concat(known ?? new int[0]))
            {
                string what;
                var bad = Damage.Mutate(data, seed, headerLength, out what);
                if (Damage.Run(game + " " + file + " " + what, () => read(bad), 10, alsoRefusal) != null) refused++;
            }
            output.WriteLine("{0} {1}: {2} damaged copies, {3} refused, the rest read", game, file, n, refused);
        }

        static int ArchiveHeader(byte[] d, GameKind kind)
        {
            int count = d.Length >= 2 ? d[0] | d[1] << 8 : 0;
            return kind == GameKind.UW2 ? 6 + 16 * count : 2 + 4 * count;
        }

        static int HeaderOf(string game, string file, Func<byte[], int> header)
        {
            string p = PathOf(game, file);
            return p == null ? 0 : header(File.ReadAllBytes(p));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Damaged_LEV_ARK(string game)
        {
            var kind = KindOf(game);
            Fuzz(game, "LEV.ARK", HeaderOf(game, "LEV.ARK", d => ArchiveHeader(d, kind)), d =>
            {
                var ark = Archive.Open(d, kind);
                for (int b = 0; b < ark.Count; b++) if (ark.Has(b)) ark.Read(b);
            });
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Damaged_CNV_ARK(string game)
        {
            var kind = KindOf(game);
            var s = Session(game);
            if (s == null) return;
            Fuzz(game, "CNV.ARK", HeaderOf(game, "CNV.ARK", d => ArchiveHeader(d, kind)), d =>
            {
                var a = ConversationArchive.FromBytes(d, kind);
                // as the conversation editor shows one: parsed, then decompiled with its strings
                foreach (int slot in a.Slots().Take(12).ToList())
                {
                    var c = a.Read(slot);
                    if (c != null) Decompiler.Decompile(c, s.Strings.Block(c.StringBlock) ?? new string[0], kind);
                }
            });
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Damaged_STRINGS_PAK(string game)
        {
            // the Huffman tree (4 bytes a node) is the header the lies go to
            Fuzz(game, "STRINGS.PAK", HeaderOf(game, "STRINGS.PAK", d => 2 + 4 * (d[0] | d[1] << 8) + 2 + 6 * 64), d =>
            {
                var f = StringsPakFile.FromBytes(d);
                foreach (int b in f.Blocks.ToList()) f.Get(b);
            });
        }

        [Theory]
        [InlineData("UW", "OBJECTS.GR")]
        [InlineData("UW", "CHARHEAD.GR")]
        [InlineData("UW", "W64.TR")]
        [InlineData("UW", "MAIN.BYT")]
        [InlineData("UW2", "OBJECTS.GR")]
        [InlineData("UW2", "T64.TR")]
        [InlineData("UW2", "BYT.ARK")]
        public void Damaged_picture_files(string game, string file)
        {
            var kind = KindOf(game);
            string data = PathOf(game, file) == null ? null : Path.GetDirectoryName(PathOf(game, file));
            if (data == null) return;
            var pals = Palettes.Load(data);
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-fuzz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Fuzz(game, file, HeaderOf(game, file, d => file.EndsWith(".ARK") ? ArchiveHeader(d, kind) : 3 + 4 * (d.Length > 2 ? d[1] | d[2] << 8 : 0)), d =>
                {
                    // PictureFile reads from a path, as the graphics window does
                    string p = Path.Combine(dir, file);
                    lock (dir) File.WriteAllBytes(p, d);
                    var f = PictureFile.Load(p, kind);
                    for (int i = 0; i < f.Count; i++) f.Get(i, pals, 0);
                });
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Theory]
        [InlineData("UW", "OBJECTS.DAT")]
        [InlineData("UW", "COMOBJ.DAT")]
        [InlineData("UW2", "OBJECTS.DAT")]
        [InlineData("UW2", "COMOBJ.DAT")]
        public void Damaged_object_tables(string game, string file)
        {
            // fixed-size tables: what matters is that nothing reads past a shortened or odd file
            Fuzz(game, file, 64, d =>
            {
                UwEditor.Core.ObjectModels.ReadRenderTypes(file == "COMOBJ.DAT" ? d : null);
            });
        }

        [Theory]
        [InlineData("UW2", "SAVE1/PLAYER.DAT")]
        public void Damaged_PLAYER_DAT(string game, string file)
        {
            var kind = KindOf(game);
            string data = Path.Combine(GameData.Game(game) ?? "", "DATA");
            Fuzz(game, file, 0x80, d =>
            {
                PlayerDat.Parse(d, kind);
                PlayerSheet.FromBytes(d, kind, data);
            });
        }

        [Theory]
        [InlineData("UW2", "SAVE1/BGLOBALS.DAT", true)]
        [InlineData("UW", "BABGLOBS.DAT", false)]
        [InlineData("UW2", "BABGLOBS.DAT", false)]
        public void Damaged_global_variables(string game, string file, bool withWords)
        {
            Fuzz(game, file, 64, d => BGlobals.Parse(d, withWords).ToBytes(withWords));
        }

        [Theory]
        [InlineData("UW2", "SCD.ARK")]
        public void Damaged_SCD_ARK(string game, string file)
        {
            Fuzz(game, file, HeaderOf(game, file, d => ArchiveHeader(d, GameKind.UW2)), d =>
            {
                var a = (Uw2Archive)Archive.Open(d, GameKind.UW2);
                ScdArk.Read(a);
            });
        }

        [Theory]
        [InlineData("UW", "SOUND/AW01.XMI")]
        [InlineData("UW2", "SOUND/UWA01.XMI")]
        public void Damaged_music(string game, string file)
        {
            if (PathOf(game, file) == null)
            {
                // the UW2 names differ between releases: take its first XMI
                string sound = Path.Combine(GameData.Game(game) ?? "", "SOUND");
                if (!Directory.Exists(sound)) return;
                var first = Directory.GetFiles(sound, "*.XMI").OrderBy(f => f).FirstOrDefault();
                if (first == null) return;
                file = "SOUND/" + Path.GetFileName(first);
            }
            Fuzz(game, file, 64, d =>
            {
                var x = XmiFile.Parse(d);
                x.Events();
                XmiToMidi.Convert(x);
            });
        }

        [Theory]
        [InlineData("UW", "FONTBIG.SYS")]
        [InlineData("UW2", "FONTBIG.SYS")]
        public void Damaged_fonts(string game, string file)
        {
            Fuzz(game, file, 12, d => UwFont.Read(d));
        }
    }
}
