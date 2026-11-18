using System;
using System.Linq;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>item_find and the object tools on a copy of level 1.</summary>
    public class ObjectTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static (int x, int y) EmptyOpenTile(McpEditor ed)
        {
            var m = ed.Edits.For(1).Model;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (m.Tiles[x, y].Type == UwEditor.Core.TileType.Open && m.TileObjects(x, y).Count == 0) return (x, y);
            throw new Exception("no empty open tile");
        }

        [Fact]
        public void Find_lists_items_by_name_and_number()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Contains("0x140", ed.ItemFind("door"));
            Assert.Contains(ed.Session.Names.Get(0x0B0), ed.ItemFind("0xB0"));
            Assert.Contains(ed.Session.Names.Get(0x0B0), ed.ItemFind("176"));
        }

        [Fact]
        public void Add_by_name_shows_on_the_tile()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpenTile(ed);
            string name = ed.Session.Names.Get(0x0B0);
            string said = ed.ObjectAdd(1, x, y, name, null, null, null, null, null, null);
            Assert.Contains("#", said);
            var objs = ed.Edits.For(1).Model.TileObjects(x, y);
            Assert.Single(objs);
            Assert.Equal(0x0B0, objs[0].Item);
            Assert.Contains(name, ed.ObjectList(1, x, y));
        }

        [Fact]
        public void Ambiguous_name_adds_nothing()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpenTile(ed);
            var e = Assert.ThrowsAny<ArgumentException>(() => ed.ObjectAdd(1, x, y, "door", null, null, null, null, null, null));
            Assert.Contains("0x140", e.Message);
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Move_and_delete()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpenTile(ed);
            ed.ObjectAdd(1, x, y, "0xB0", null, null, null, null, null, null);
            int i = ed.Edits.For(1).Model.TileObjects(x, y)[0].Index;
            var m = ed.Edits.For(1).Model;
            var (x2, y2) = Enumerable.Range(0, 64 * 64).Select(k => (k % 64, k / 64))
                .First(t => m.Tiles[t.Item1, t.Item2].Type == UwEditor.Core.TileType.Open && (t.Item1 != x || t.Item2 != y));
            ed.ObjectMove(1, i, x2, y2, null, null);
            Assert.Equal(x2, ed.Edits.For(1).Model.Objects[i].TileX);
            Assert.Empty(ed.Edits.For(1).Model.TileObjects(x, y));
            ed.ObjectDelete(1, i);
            Assert.DoesNotContain(ed.Edits.For(1).Model.TileObjects(x2, y2), o => o.Index == i);
        }

        [Fact]
        public void Solid_tile_refused()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var s = Enumerable.Range(0, 64 * 64).Select(k => (k % 64, k / 64)).First(t => m.Tiles[t.Item1, t.Item2].IsSolid);
            Assert.ThrowsAny<InvalidOperationException>(() => ed.ObjectAdd(1, s.Item1, s.Item2, "0xB0", null, null, null, null, null, null));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Bad_index_refused()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectDelete(1, 5000));
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectMove(1, -1, 5, 5, null, null));
        }
    }
}
