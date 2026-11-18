using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The guards: unsaved edits kept on open, the --only start option, empty or damaged slots; fill's speed and wording.</summary>
    public class GuardTests
    {
        static McpEditor Opened(out string g)
        {
            g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        [Fact]
        public void Open_again_refuses_with_unsaved_edits()
        {
            var ed = Opened(out string g); if (ed == null) return;
            ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null);
            var e = Assert.Throws<McpEditorException>(() => ed.Open(g));
            Assert.Contains("level 1", e.Message);
            Assert.True(ed.Edits.HasChanges);                           // the edits are still there
            Assert.Contains("level 1: in use", ed.Open(g, discard: true));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Open_again_without_edits_is_fine()
        {
            var ed = Opened(out string g); if (ed == null) return;
            Assert.Contains("level 1: in use", ed.Open(g));
        }

        [Theory]
        [InlineData("--only")]
        [InlineData("--only|")]
        [InlineData("--only|   ")]
        [InlineData("--only=O:\\x")]
        [InlineData("--onyl|O:\\x")]
        public void Bad_start_options_stop_the_server(string args)
        {
            Assert.Throws<ArgumentException>(() => UwEditor.Mcp.Server.Program.ParseOnly(args.Split('|')));
        }

        [Fact]
        public void Good_start_options()
        {
            Assert.Null(UwEditor.Mcp.Server.Program.ParseOnly(new string[0]));
            Assert.Equal("O:\\x", UwEditor.Mcp.Server.Program.ParseOnly(new[] { "--only", "O:\\x" }));
        }

        /// <summary>Level 9 of the copy with its end mark spoiled: a damaged slot.</summary>
        static void Spoil9(string g)
        {
            string lev = Path.Combine(g, "DATA", "LEV.ARK");
            byte[] d = File.ReadAllBytes(lev);
            int at = BitConverter.ToInt32(d, 2 + 4 * 8) + UltimateEditor.Games.LevelBlock.MagicAt;
            d[at] ^= 0xFF; d[at + 1] ^= 0xFF;
            File.WriteAllBytes(lev, d);
        }

        [Fact]
        public void Damaged_slot_refuses_edits()
        {
            string g = GameCopy.Make(); if (g == null) return;
            Spoil9(g);
            var ed = new McpEditor(g);
            Assert.Contains("level 9: damaged", ed.Open(g));
            Assert.Throws<McpEditorException>(() => ed.TileSet(9, 5, 5, "Type", 1));
            Assert.Throws<McpEditorException>(() => ed.Fill(9, 5, 5, 6, 6, "open", null, null, null));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Tools_before_open_answer_in_words()
        {
            Assert.Equal("Refused: Open a game folder first.", EditorTools.Text(new EditorTools(new McpEditor(null)).LevelMap(1)));
        }

        [Fact]
        public void Fill_whole_level_is_quick()
        {
            var ed = Opened(out _); if (ed == null) return;
            var w = Stopwatch.StartNew();
            ed.Fill(1, 0, 0, 63, 63, "open", 2, 1, 1);
            Assert.True(w.ElapsedMilliseconds < 3000, "a 64x64 fill took " + w.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void Fill_says_tiles_set()
        {
            var ed = Opened(out _); if (ed == null) return;
            Assert.StartsWith("Set 20 tiles", ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null));
        }
    }
}
