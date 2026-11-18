using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The editor's trap recipes through the tools, on a copy of level 1.</summary>
    public class RecipeTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static List<(int x, int y)> OpenTiles(LevelModel m)
        {
            return Enumerable.Range(0, 64 * 64).Select(k => (k % 64, k / 64)).Where(t => m.Tiles[t.Item1, t.Item2].Type == TileType.Open).ToList();
        }

        [Fact]
        public void Teleport_makes_trigger_and_trap()
        {
            var ed = Opened(); if (ed == null) return;
            var open = OpenTiles(ed.Edits.For(1).Model);
            var a = open[10]; var b = open[200];
            string said = ed.RecipeTeleport(1, a.x, a.y, 1, b.x, b.y, false);
            var items = ed.Edits.For(1).Model.TileObjects(a.x, a.y).Select(o => o.Item).ToList();
            Assert.Contains(0x1A0, items);                                   // move trigger
            Assert.Contains("#", said);
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            var linked = ed.Edits.For(1).Model.Objects.Where(o => o != null && !o.Free && o.Item == 0x181).ToList();
            Assert.NotEmpty(linked);                                         // a teleport trap somewhere
        }

        [Fact]
        public void Lever_opens_a_door()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146);
            int levers = m.Objects.Count(o => o != null && !o.Free && o.Item >= 0x170 && o.Item < 0x178);
            string said = ed.RecipeLeverDoor(1, door.Index, door.TileX, door.TileY, "auto");
            var m2 = ed.Edits.For(1).Model;
            Assert.Equal(levers + 1, m2.Objects.Count(o => o != null && !o.Free && o.Item >= 0x170 && o.Item < 0x178));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
        }

        [Fact]
        public void Plate_damage_and_door()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var open = OpenTiles(m);
            ed.RecipePlate(1, open[20].x, open[20].y, "damage", 7, null);
            Assert.Contains(ed.Edits.For(1).Model.Objects, o => o != null && !o.Free && o.Item == 0x180);
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146);
            ed.RecipePlate(1, open[30].x, open[30].y, "door", null, door.Index);
            Assert.Contains(ed.Edits.For(1).Model.Objects, o => o != null && !o.Free && o.Item == 0x188);
            Assert.ThrowsAny<ArgumentException>(() => ed.RecipePlate(1, open[40].x, open[40].y, "fireworks", null, null));
        }

        [Fact]
        public void Spawner_adds_a_create_trap()
        {
            var ed = Opened(); if (ed == null) return;
            var open = OpenTiles(ed.Edits.For(1).Model);
            ed.RecipeSpawner(1, open[50].x, open[50].y, "0x40", 30);
            Assert.Contains(ed.Edits.For(1).Model.TileObjects(open[50].x, open[50].y), o => o.Item == 0x187);
        }

        [Fact]
        public void Recipe_on_solid_rock_is_refused()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var s = Enumerable.Range(0, 64 * 64).Select(k => (k % 64, k / 64)).First(t => m.Tiles[t.Item1, t.Item2].IsSolid);
            Assert.ThrowsAny<InvalidOperationException>(() => ed.RecipeSpawner(1, s.Item1, s.Item2, "0x40", 30));
            Assert.False(ed.Edits.HasChanges);
        }
    }
}
