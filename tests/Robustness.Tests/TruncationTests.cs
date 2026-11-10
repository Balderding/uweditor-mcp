using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Every game file the editor reads, cut short at many lengths (ROADMAP.md §2.1, test 1): each
    /// reader, and opening the whole game, must refuse with a message or read what is there; never
    /// crash, hang or run out of memory.
    /// </summary>
    public class TruncationTests
    {
        readonly ITestOutputHelper output;
        public TruncationTests(ITestOutputHelper output) { this.output = output; }

        static GameKind KindOf(string game) { return game == "UW2" ? GameKind.UW2 : GameKind.UW1; }

        static byte[] Original(string game, string file)
        {
            string g = GameData.Game(game);
            if (g == null) return null;
            string p = GameProfile.Find(Path.Combine(g, "DATA"), file);
            return p != null ? File.ReadAllBytes(p) : null;
        }

        [Theory]
        [InlineData("UW", "LEV.ARK")]
        [InlineData("UW2", "LEV.ARK")]
        [InlineData("UW", "CNV.ARK")]
        [InlineData("UW2", "CNV.ARK")]
        [InlineData("UW2", "BYT.ARK")]
        [InlineData("UW2", "SCD.ARK")]
        public void A_cut_archive_is_refused_or_read(string game, string file)
        {
            var data = Original(game, file); if (data == null) return;
            var kind = KindOf(game);
            int refused = 0, cuts = 0;
            foreach (int len in Damage.Cuts(data.Length))
            {
                cuts++;
                var cut = Damage.Cut(data, len);
                string why = Damage.Run(file + " cut at " + len, () =>
                {
                    var ark = Archive.Open(cut, kind);
                    for (int b = 0; b < ark.Count; b++) ark.Read(b);
                });
                if (why != null) refused++;
            }
            output.WriteLine("{0} {1}: {2} cuts, {3} refused", game, file, cuts, refused);
            Assert.True(refused > 0, "no cut was refused: the checks never ran");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_cut_STRINGS_PAK_is_refused_or_read(string game)
        {
            var data = Original(game, "STRINGS.PAK"); if (data == null) return;
            int refused = 0;
            foreach (int len in Damage.Cuts(data.Length))
            {
                var cut = Damage.Cut(data, len);
                if (Damage.Run("STRINGS.PAK cut at " + len, () =>
                {
                    var f = StringsPakFile.FromBytes(cut);
                    foreach (int b in f.Blocks.ToList()) f.Get(b);
                }) != null) refused++;
            }
            Assert.True(refused > 0);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Cut_conversations_are_refused_or_read(string game)
        {
            var data = Original(game, "CNV.ARK"); if (data == null) return;
            var kind = KindOf(game);
            foreach (int len in Damage.Cuts(data.Length))
            {
                var cut = Damage.Cut(data, len);
                Damage.Run("CNV.ARK cut at " + len, () =>
                {
                    var a = ConversationArchive.FromBytes(cut, kind);
                    foreach (int s in a.Slots().ToList()) a.Read(s);
                });
            }
        }

        [Theory]
        [InlineData("UW", "OBJECTS.GR")]
        [InlineData("UW", "CHARHEAD.GR")]
        [InlineData("UW2", "OBJECTS.GR")]
        [InlineData("UW2", "CHARHEAD.GR")]
        public void A_cut_picture_file_is_refused_or_read(string game, string file)
        {
            var data = Original(game, file); if (data == null) return;
            foreach (int len in Damage.Cuts(data.Length))
            {
                var cut = Damage.Cut(data, len);
                Damage.Run(file + " cut at " + len, () =>
                {
                    var o = GrFile.Offsets(cut);
                    for (int i = 0; i + 1 < o.Length; i++) if (!GrFile.IsEmpty(o, i)) GrFile.Decode(cut, o, i);
                });
            }
        }

        /// <summary>The whole path a player takes: open the game with one file cut short, then every level.</summary>
        [Theory]
        [InlineData("UW", "LEV.ARK")]
        [InlineData("UW2", "LEV.ARK")]
        [InlineData("UW", "STRINGS.PAK")]
        [InlineData("UW2", "STRINGS.PAK")]
        [InlineData("UW", "COMOBJ.DAT")]
        [InlineData("UW2", "COMOBJ.DAT")]
        public void A_game_with_a_cut_file_opens_or_is_refused(string game, string file)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var copy = new Damage.GameCopy(g))
            {
                string path = GameProfile.Find(Path.Combine(copy.Folder, "DATA"), file);
                var data = File.ReadAllBytes(path);
                // fewer cuts than the reader tests: each one opens the game
                foreach (int len in Damage.Cuts(data.Length).Where((c, i) => i % 4 == 0 || c > 64))
                {
                    File.WriteAllBytes(path, Damage.Cut(data, len));
                    string why = Damage.Run("open " + game + " with " + file + " cut at " + len, () =>
                    {
                        var s = GameSession.Open(copy.Folder);
                        for (int level = 1; level <= s.Profile.LevelSlots; level++) s.LoadLevel(level);
                    }, 30);
                    if (why != null) output.WriteLine("{0} at {1}: {2}", file, len, why);
                }
                File.WriteAllBytes(path, data);
            }
        }
    }
}
