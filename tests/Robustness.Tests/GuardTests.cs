using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>One test per guard added to the readers and the object lists (ROADMAP.md §2.1).</summary>
    public class GuardTests
    {
        static void Word(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        static void Long(List<byte> b, long v) { for (int i = 0; i < 4; i++) b.Add((byte)(v >> (8 * i))); }

        [Fact]
        public void An_archive_whose_offsets_lie_outside_it_is_refused_with_its_block()
        {
            // UW1: two blocks, the second at 0x7FFFFFFF
            var b = new List<byte>(); Word(b, 2); Long(b, 10); Long(b, 0x7FFFFFFF); b.AddRange(new byte[4]);
            var e = Assert.Throws<GameFileException>(() => Archive.Open(b.ToArray(), GameKind.UW1));
            Assert.Contains("block 1", e.Message);
            // UW2: a block claiming 4 GB
            var c = new List<byte>(); Word(c, 1); Long(c, 0); Long(c, 22); Long(c, 0); Long(c, 0xFFFFFFF0); Long(c, 0);
            Assert.Throws<GameFileException>(() => Archive.Open(c.ToArray(), GameKind.UW2));
            // a table that does not fit: 1000 blocks in 10 bytes
            var d = new List<byte>(); Word(d, 1000); d.AddRange(new byte[8]);
            Assert.Throws<GameFileException>(() => Archive.Open(d.ToArray(), GameKind.UW1));
            Assert.Throws<GameFileException>(() => Archive.Open(new byte[0], GameKind.UW1));
        }

        [Fact]
        public void Archive_Load_names_the_damaged_file()
        {
            string p = Path.Combine(Path.GetTempPath(), "uwedit-robust-" + Guid.NewGuid().ToString("N") + "-LEV.ARK");
            File.WriteAllBytes(p, new byte[] { 5, 0, 1 });
            try
            {
                var e = Assert.Throws<GameFileException>(() => Archive.Load(p, GameKind.UW1));
                Assert.Equal(p, e.File);
                Assert.StartsWith(Path.GetFileName(p) + " is damaged", e.Message);
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void A_STRINGS_PAK_with_a_bad_tree_is_refused()
        {
            // a truncated file: one byte
            Assert.Throws<GameFileException>(() => UwEditor.Core.Conversations.StringsPakFile.FromBytes(new byte[] { 3 }));
            // a tree whose node names a child outside it
            var b = new List<byte>(); Word(b, 1); b.AddRange(new byte[] { (byte)'a', 255, 7, 9 }); Word(b, 0);
            var e = Assert.Throws<GameFileException>(() => UwEditor.Core.Conversations.StringsPakFile.FromBytes(b.ToArray()));
            Assert.Contains("outside the tree", e.Message);
        }

        [Fact]
        public void Encoding_with_a_looping_tree_stops()
        {
            // leaf 'a' (0) under node 1, node 1 under node 2, node 2 under node 1 again: no root
            var nodes = new[]
            {
                new HuffmanNode('a', 0, 1, 255, 255),
                new HuffmanNode(0, 1, 2, 0, 2),
                new HuffmanNode(0, 2, 1, 1, 0),
            };
            Damage.Run("a looping tree", () => StringsPak.ConvertString("a", nodes), 5);
        }

        [Fact]
        public void A_deeply_nested_XMI_does_not_overflow_the_stack()
        {
            // 20,000 FORM groups each inside the last, then FORM XMID: too deep to recurse into
            const int depth = 20000;
            var b = new byte[depth * 12 + 12];
            for (int i = 0; i < depth; i++)
            {
                int at = i * 12, len = b.Length - at - 8;
                Encoding.ASCII.GetBytes("FORM").CopyTo(b, at);
                b[at + 4] = (byte)(len >> 24); b[at + 5] = (byte)(len >> 16); b[at + 6] = (byte)(len >> 8); b[at + 7] = (byte)len;
                Encoding.ASCII.GetBytes("XDIR").CopyTo(b, at + 8);
            }
            Damage.Run("nested XMI", () => XmiFile.Parse(b), 10);
        }

        [Fact]
        public void A_BDF_with_a_word_for_a_number_is_refused()
        {
            string bdf = "STARTFONT 2.1\nFONTBOUNDINGBOX eight 8 0 0\nENDFONT\n";
            var e = Assert.Throws<InvalidDataException>(() => FontBdf.Import(bdf, null));
            Assert.Contains("eight", e.Message);
            string missing = "STARTFONT 2.1\nFONTBOUNDINGBOX 8\nENDFONT\n";
            Assert.Throws<InvalidDataException>(() => FontBdf.Import(missing, null));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Damaged_free_lists_stop_adding_and_deleting(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEditor(s, 1);
            Assert.Null(new LevelBlock(e.Data, s.Kind).FreeListProblem());
            int x = -1, y = -1;
            for (int yy = 2; yy < 62 && x < 0; yy++) for (int xx = 2; xx < 62; xx++) if (!e.Model.Tiles[xx, yy].IsSolid) { x = xx; y = yy; break; }
            int coins = e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);

            // the static free list's top entry now names a mobile object
            var b = new LevelBlock(e.Data, s.Kind);
            int top = b.StaticTop;
            b.Put16(LevelBlock.StaticFreeAt + 2 * top, 5);
            Assert.Contains("static free entry", b.FreeListProblem());
            var ex = Assert.Throws<InvalidOperationException>(() => e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y));
            Assert.Contains("Fixes", ex.Message);
            Assert.Throws<InvalidOperationException>(() => e.DeleteObject(coins));
            Assert.False(e.Model.Objects[coins].Free);

            // a top past the end of the list
            b.Put16(LevelBlock.StaticFreeAt + 2 * top, 1000);
            b.Put16(LevelBlock.StaticTopAt, 800);
            Assert.Contains("top", b.FreeListProblem());
            Assert.Throws<InvalidOperationException>(() => e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y));
            b.CrunchFreeRecords();   // skips what is out of range instead of running off the block
        }
    }
}
