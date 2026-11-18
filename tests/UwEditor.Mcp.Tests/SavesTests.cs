using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Saved games: listing, making one, opening, copying, renaming, exporting, the check and refusals (temp copies only).</summary>
    public class SavesTests
    {
        /// <summary>A game copy with SAVE1 made by save_create and opened; null without game data.</summary>
        internal static McpEditor WithSave(out string g)
        {
            g = GameCopy.Make();
            if (g == null) return null;
            var ed = new McpEditor(g) { CheckpointBase = Path.Combine(g, "checkpoints-test") };
            ed.Open(g);
            Assert.StartsWith("Made SAVE1", ed.SaveCreate(1, "Test save", false));
            Assert.StartsWith("Opened SAVE1", ed.SaveOpen(1, false));
            Assert.True(ed.Session.IsSave);
            return ed;
        }

        static string T(Func<ModelContextProtocol.Protocol.CallToolResult> f) { return EditorTools.Text(f()); }

        [Fact]
        public void Saves_create_list_open()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            string list = ed.SaveList();
            Assert.Contains("SAVE1: empty", list);
            Assert.StartsWith("Made SAVE1", ed.SaveCreate(1, "Test save", false));
            Assert.True(File.Exists(Path.Combine(g, "SAVE1", "PLAYER.DAT")));
            Assert.True(File.Exists(Path.Combine(g, "SAVE1", "LEV.ARK")));
            Assert.Contains("SAVE1: \"Test save\"", ed.SaveList());
            // a second time without confirm: nothing made
            Assert.StartsWith("Not made", ed.SaveCreate(1, "Again", false));
            Assert.StartsWith("Opened SAVE1", ed.SaveOpen(1, false));
            Assert.Contains("[open]", ed.SaveList());
            var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(g, "SAVE1", "PLAYER.DAT")), ed.Session.Kind);
            Assert.Equal(1, pd.SavedLevel);
            Assert.Equal((int)ed.Session.NewGameStart.X, pd.TileX);
            Assert.Equal((int)ed.Session.NewGameStart.Y, pd.TileY);
            Assert.Contains("position: level 1", ed.PlayerGet());
            Assert.StartsWith("Opened DATA", ed.SaveOpen(0, false));
            Assert.False(ed.Session.IsSave);
        }

        [Fact]
        public void Saves_refusals()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            var tools = new EditorTools(ed);
            Assert.StartsWith("Refused: SAVE2 is empty", T(() => tools.SaveOpen(2)));
            Assert.StartsWith("Refused: Saved game slots are 1-4", T(() => tools.SaveRename(7, "x")));
            Assert.StartsWith("Refused: The player is kept in a saved game", T(() => tools.PlayerGet()));
            Assert.StartsWith("Refused:", T(() => tools.GogInfo(Path.Combine(g, "nothing.gog"))));
            Assert.StartsWith("Refused:", T(() => tools.SaveExport("2")));   // no saved player to start from
            Assert.StartsWith("Refused:", T(() => tools.SaveExport(@"C:\elsewhere")));
        }

        [Fact]
        public void Saves_open_refuses_unsaved_player()
        {
            var ed = WithSave(out string g); if (ed == null) return;
            ed.PlayerSet("strength", "21");
            Assert.Throws<McpEditorException>(() => ed.SaveOpen(0, false));
            Assert.StartsWith("Opened DATA", ed.SaveOpen(0, true));
        }

        [Fact]
        public void Saves_copy_rename_export()
        {
            var ed = WithSave(out string g); if (ed == null) return;
            Assert.StartsWith("SAVE1 copied to SAVE2.", ed.SaveCopy(1, 2, false));
            Assert.StartsWith("SAVE2 is now \"Second\"", ed.SaveRename(2, "Second"));
            Assert.Contains("SAVE2: \"Second\"", ed.SaveList());
            Assert.StartsWith("Not copied", ed.SaveCopy(1, 2, false));
            Assert.Contains("kept as SAVE2.BKP", ed.SaveCopy(1, 2, true));
            Assert.True(Directory.Exists(Path.Combine(g, "SAVE2.BKP")));
            Assert.Throws<McpEditorException>(() => ed.SaveCopy(2, 1, true));   // SAVE1 is open

            ed.Fill(1, 2, 2, 3, 3, "open", 5, null, null);
            ed.PlayerSet("strength", "22");
            Assert.StartsWith("Saved game written", ed.SaveExport("3", "Exported", false));
            Assert.StartsWith("Not exported", ed.SaveExport("3", "Exported", false));
            Assert.StartsWith("Saved game written", ed.SaveExport("3", "Exported", true));
            var other = new McpEditor(g); other.Open(g);
            other.SaveOpen(3, false);
            Assert.Contains("strength: 22", other.PlayerGet());
            Assert.Equal(5, other.Edits.For(1).TileField(2, 2, "Floor height"));
            Assert.Throws<McpEditorException>(() => ed.SaveExport(Path.Combine(g, "SAVE1"), null, true));   // the open one
        }

        [Fact]
        public void Saves_check_and_repair()
        {
            var ed = WithSave(out string g); if (ed == null) return;
            string found = ed.SaveCheckRun();
            Assert.False(string.IsNullOrEmpty(found));
            string bad = T(() => new EditorTools(ed).SaveRepair(new[] { 999 }));
            if (found == "Nothing wrong found.") Assert.StartsWith("Nothing wrong found", bad);
            else Assert.StartsWith("Refused: Findings are", bad);
            string r = ed.SaveRepair(null);
            Assert.False(string.IsNullOrEmpty(r));
            // a level whose object lists fail: one object in two tiles' lists
            var e = ed.Edits.For(1);
            var m = e.Model;
            (int x, int y) a = (-1, -1), b = (-1, -1);
            for (int y = 0; y < 64 && b.x < 0; y++)
                for (int x = 0; x < 64; x++)
                {
                    if (a.x < 0 && m.Tiles[x, y].FirstObject != 0) a = (x, y);
                    else if (b.x < 0 && !m.Tiles[x, y].IsSolid && m.Tiles[x, y].FirstObject == 0) b = (x, y);
                }
            int first = m.Tiles[a.x, a.y].FirstObject;
            e.EditBytes("broken on purpose", (d, t) =>
                UltimateEditor.Uw1.Uw1Schemas.Tile["First object"].Set(d, first, UltimateEditor.Games.LevelBlock.TilesAt + (b.y * 64 + b.x) * 4));
            Assert.Contains("level 1 objects", ed.SaveCheckRun());
        }
    }
}
