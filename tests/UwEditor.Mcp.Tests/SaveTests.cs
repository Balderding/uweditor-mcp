using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>check and save: the game's own checks, and LEV.ARK written (the old one kept as .BKP).</summary>
    public class SaveTests
    {
        [Fact]
        public void Save_writes_and_reopens()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            ed.Fill(1, 2, 2, 6, 5, "open", 3, 1, 4);
            Assert.Equal("Saved level 1.", ed.Save());
            Assert.True(File.Exists(Path.Combine(g, "DATA", "LEV.ARK.BKP")));

            var again = new McpEditor(g); again.Open(g);
            var e = again.Edits.For(1);
            Assert.Equal(1, e.TileField(4, 4, "Type"));
            Assert.Equal(3, e.TileField(4, 4, "Floor height"));
            Assert.Equal(4, e.TileField(6, 5, "Wall texture"));
        }

        [Fact]
        public void Save_nothing()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            byte[] before = File.ReadAllBytes(Path.Combine(g, "DATA", "LEV.ARK"));
            Assert.Equal("Nothing to save.", ed.Save());
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(g, "DATA", "LEV.ARK")));
        }

        [Fact]
        public void Save_refuses_a_level_failing_the_checks()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            var e = ed.Edits.For(1);
            var m = e.Model;
            // a second tile's list starting at another tile's first object: one object in two lists
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
            Assert.StartsWith("Level 1 fails", ed.Check(1));
            string lev = Path.Combine(g, "DATA", "LEV.ARK");
            byte[] before = File.ReadAllBytes(lev);
            Assert.Throws<System.InvalidOperationException>(() => ed.Save());
            Assert.Equal(before, File.ReadAllBytes(lev));
            Assert.StartsWith("Refused: Level 1 was not saved", new EditorTools(ed).Save());
        }

        [Fact]
        public void Check_shipped_level_ok()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            Assert.Equal("Level 1 passes the game's checks.", ed.Check(1));
        }
    }
}
