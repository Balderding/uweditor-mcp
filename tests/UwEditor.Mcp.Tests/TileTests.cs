using System;
using System.Linq;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>level_map, tile_get, tile_set on a copy of level 1.</summary>
    public class TileTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static string[] Grid(string map, int which)
        {
            // header line, 64 type rows, a blank line, a header line, 64 height rows
            var lines = map.Replace("\r", "").Split('\n');
            return which == 0 ? lines.Skip(1).Take(64).ToArray() : lines.Skip(67).Take(64).ToArray();
        }

        [Fact]
        public void Map_has_64_rows_of_64()
        {
            var ed = Opened(); if (ed == null) return;
            string map = ed.LevelMap(1);
            foreach (int w in new[] { 0, 1 })
            {
                var rows = Grid(map, w);
                Assert.Equal(64, rows.Length);
                Assert.All(rows, r => Assert.Equal(64, r.Length));
            }
        }

        [Fact]
        public void Map_matches_model()
        {
            var ed = Opened(); if (ed == null) return;
            var rows = Grid(ed.LevelMap(1), 0);
            var hs = Grid(ed.LevelMap(1), 1);
            var m = ed.Edits.For(1).Model;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = m.Tiles[x, y];
                    char c = rows[63 - y][x];
                    Assert.Equal("#.1234nsew"[(int)t.Type], c);
                    Assert.Equal(t.IsSolid ? '#' : "0123456789abcdef"[t.Height], hs[63 - y][x]);
                }
        }

        static (int x, int y) OpenTile(McpEditor ed, bool withObject)
        {
            var m = ed.Edits.For(1).Model;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (m.Tiles[x, y].Type == UwEditor.Core.TileType.Open && m.Tiles[x, y].Height < 10
                        && (m.TileObjects(x, y).Any(o => !o.IsTrapOrTrigger) == withObject)) return (x, y);
            throw new Exception("no such tile");
        }

        [Fact]
        public void Tile_set_changes_one_field()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = OpenTile(ed, false);
            string before = ed.TileGet(1, x, y);
            int old = ed.Edits.For(1).TileField(x, y, "Floor texture");
            int nv = old == 2 ? 3 : 2;
            string after = ed.TileSet(1, x, y, "Floor texture", nv);
            Assert.Contains("Floor texture: " + nv, after);
            Assert.Equal(before.Split('\n').Where(l => !l.StartsWith("Floor texture")),
                         after.Split('\n').Where(l => !l.StartsWith("Floor texture")));
            Assert.True(ed.Edits.HasChanges);
        }

        [Fact]
        public void Tile_set_refuses_out_of_range()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = OpenTile(ed, false);
            Assert.ThrowsAny<ArgumentException>(() => ed.TileSet(1, x, y, "Floor height", 16));
            Assert.ThrowsAny<ArgumentException>(() => ed.TileSet(1, x, y, "Type", 10));
            Assert.ThrowsAny<ArgumentException>(() => ed.TileSet(1, 64, y, "Type", 1));
            Assert.ThrowsAny<ArgumentException>(() => ed.TileSet(1, x, y, "Wall texture", 63));
            Assert.ThrowsAny<ArgumentException>(() => ed.TileSet(1, x, y, "No such field", 1));
            Assert.ThrowsAny<ArgumentException>(() => ed.TileGet(0, x, y));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Raise_carries_objects()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = OpenTile(ed, true);
            var le = ed.Edits.For(1);
            var o = le.Model.TileObjects(x, y).First(q => !q.IsTrapOrTrigger);
            int h = le.TileField(x, y, "Floor height"), z = o.Z;
            ed.TileSet(1, x, y, "Floor height", h + 2);
            Assert.Equal(h + 2, le.TileField(x, y, "Floor height"));
            Assert.Equal(Math.Min(127, z + 16), le.Model.Objects[o.Index].Z);
        }
    }
}
