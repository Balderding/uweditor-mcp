using System;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>fill: a rectangle of tiles in one undo step, or nothing at all.</summary>
    public class FillTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        [Fact]
        public void Fill_makes_a_room()
        {
            var ed = Opened(); if (ed == null) return;
            ed.Fill(1, 2, 2, 6, 5, "open", 3, 1, 4);
            var e = ed.Edits.For(1);
            for (int y = 2; y <= 5; y++)
                for (int x = 2; x <= 6; x++)
                {
                    Assert.Equal(1, e.TileField(x, y, "Type"));
                    Assert.Equal(3, e.TileField(x, y, "Floor height"));
                    Assert.Equal(1, e.TileField(x, y, "Floor texture"));
                    Assert.Equal(4, e.TileField(x, y, "Wall texture"));
                }
        }

        [Fact]
        public void Fill_corners_any_order()
        {
            var a = Opened(); if (a == null) return;
            var b = Opened();
            a.Fill(1, 10, 10, 14, 13, "#", null, null, null);
            b.Fill(1, 14, 13, 10, 10, "solid", null, null, null);
            Assert.Equal(a.LevelMap(1), b.LevelMap(1));
            Assert.Equal(0, a.Edits.For(1).TileField(12, 11, "Type"));
        }

        [Fact]
        public void Fill_bad_value_changes_nothing()
        {
            var ed = Opened(); if (ed == null) return;
            string before = ed.LevelMap(1);
            Assert.ThrowsAny<ArgumentException>(() => ed.Fill(1, 2, 2, 6, 5, "open", 3, null, 99));
            Assert.ThrowsAny<ArgumentException>(() => ed.Fill(1, 2, 2, 6, 5, "open", 16, null, null));
            Assert.ThrowsAny<ArgumentException>(() => ed.Fill(1, 2, 2, 6, 5, "lava", null, null, null));
            Assert.ThrowsAny<ArgumentException>(() => ed.Fill(1, 2, 2, 64, 5, "open", null, null, null));
            Assert.Equal(before, ed.LevelMap(1));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Fill_is_one_undo()
        {
            var ed = Opened(); if (ed == null) return;
            int n = ed.Edits.For(1).UndoLabels.Count;
            string text = ed.Fill(1, 2, 2, 6, 5, "open", 3, 1, 4);
            Assert.Equal(n + 1, ed.Edits.For(1).UndoLabels.Count);
            Assert.Contains("20 tiles", text);
        }
    }
}
