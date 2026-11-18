using System;
using System.Linq;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The tile clipboard, prefabs and decoration tools.</summary>
    public class EditingTilesTests
    {
        [Fact]
        public void Copy_turn_and_paste_tiles()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var (x, y) = EditingTests.EmptyOpen(ed);
            Assert.Throws<McpEditorException>(() => ed.TilesPaste(1, 10, 10));
            Assert.Contains("Copied", ed.TilesCopy(1, x, y, x + 2, y));
            Assert.Contains("3 × 1", ed.TilesClip());
            Assert.Contains("1 × 3", ed.TilesTurn(1));
            Assert.Contains("Mirrored", ed.TilesMirror());
            ed.TilesPaste(1, 5, 5);
            var t = ed.Edits.For(1).Model.Tiles;
            Assert.Equal(m.Tiles[x, y].Type, t[5, 7].Type);
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            Assert.ThrowsAny<ArgumentException>(() => ed.TilesTurn(4));
        }

        [Fact]
        public void Prefab_save_list_and_place()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var (x, y) = EditingTests.EmptyOpen(ed);
            ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null);
            Assert.Contains("No prefab", ed.PrefabList());
            Assert.Contains("Saved prefab", ed.PrefabSave("little room", 1, x, y, x + 1, y + 1));
            Assert.Contains("little room", ed.PrefabList());
            Assert.Contains("Prefab little room", ed.PrefabPlace(1, "little", 20, 20, 1, true));
            Assert.Contains("Loaded", ed.PrefabLoad("little room"));
            Assert.ThrowsAny<ArgumentException>(() => ed.PrefabLoad("no such"));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
        }

        [Fact]
        public void Decorate_scatters_and_undoes()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            Assert.Contains("bones", ed.DecorationKinds());
            int before = ed.Edits.For(1).Model.Objects.Count(o => !o.Free);
            string said = ed.Decorate(1, 0, 0, 63, 63, "bones, debris", 5, 7, true, false);
            Assert.Contains("Decorated", said);
            Assert.True(ed.Edits.For(1).Model.Objects.Count(o => !o.Free) > before);
            ed.Undo(1, 1);
            Assert.Equal(before, ed.Edits.For(1).Model.Objects.Count(o => !o.Free));
            Assert.ThrowsAny<ArgumentException>(() => ed.Decorate(1, 0, 0, 5, 5, "gold", 5, 1, true, false));
        }
    }
}
