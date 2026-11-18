using System;
using System.Linq;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Every trap recipe through the editing group's tools, and the trigger and link tools.</summary>
    public class EditingRecipesTests
    {
        static bool Has(McpEditor ed, int item) { return ed.Edits.For(1).Model.Objects.Any(o => o != null && !o.Free && o.Item == item); }

        [Fact]
        public void Plate_runs_every_kind_of_trap()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            Assert.Contains("Pressure plate", ed.RecipeList());
            var m = ed.Edits.For(1).Model;
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146);
            int k = 0;
            Func<(int, int)> next = () => EditingTests.EmptyOpen(ed, 3 * k++);
            var p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "poison", false, true, false, 4, null, null, null, false, null, null, null, null, null, null, null, false, null);
            p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "message", false, false, false, null, "A voice whispers.", null, null, false, null, null, null, null, null, null, null, false, null);
            Assert.True(Has(ed, 0x190));
            p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "variable", false, true, false, null, null, null, 5, false, 2, 3, null, null, null, null, null, false, null);
            Assert.True(Has(ed, 0x18D));
            p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "create", false, true, true, null, null, null, null, false, null, null, "0x0B0", null, null, null, null, false, null);
            Assert.True(Has(ed, 0x187));
            p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "door", false, true, false, null, null, null, null, false, null, null, null, null, null, door.Index, "open", true, null);
            var (dx, dy) = next();
            string said = ed.ObjectAdd(1, dx, dy, "0x0B0", null, null, null, null, null, null);
            int victim = int.Parse(said.Substring(said.IndexOf('#') + 1).Split(' ')[0]);
            p = next(); ed.RecipePlateTrap(1, p.Item1, p.Item2, "delete", false, true, false, null, null, null, null, false, null, null, null, null, null, null, null, false, victim);
            Assert.True(Has(ed, 0x18B));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            Assert.ThrowsAny<ArgumentException>(() => ed.RecipePlateTrap(1, p.Item1, p.Item2, "fireworks", false, true, false, null, null, null, null, false, null, null, null, null, null, null, null, false, null));
            Assert.ThrowsAny<ArgumentException>(() => ed.RecipePlateTrap(1, p.Item1, p.Item2, "message", false, true, false, null, null, null, null, false, null, null, null, null, null, null, null, false, null));
            Assert.ThrowsAny<InvalidOperationException>(() => ed.RecipePlateTrap(1, p.Item1, p.Item2, "damage", true, true, false, 3, null, null, null, false, null, null, null, null, null, null, null, false, null));
        }

        [Fact]
        public void Object_trigger_lever_and_end_of_game()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var (x, y) = EditingTests.EmptyOpen(ed);
            string said = ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null);
            int obj = int.Parse(said.Substring(said.IndexOf('#') + 1).Split(' ')[0]);
            Assert.Contains("Picking up", ed.RecipeObjectTrigger(1, obj, "pickup", "damage", true, false, 9, null, null, null, false, null, null, null, null, null, null, null, false, null));
            Assert.True(Has(ed, 0x1A1));
            var m = ed.Edits.For(1).Model;
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146);
            Assert.Contains("0x170", ed.LeverItems());
            ed.RecipeLever(1, door.Index, null, door.TileX, door.TileY, "auto", "0x171", "close", false, false, false);
            Assert.True(Has(ed, 0x171));
            var (ex, ey) = EditingTests.EmptyOpen(ed, 20);
            Assert.Contains("ends the game", ed.RecipeEndOfGame(1, ex, ey));
            Assert.True(Has(ed, 0x183));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            Assert.ThrowsAny<ArgumentException>(() => ed.RecipeObjectTrigger(1, obj, "sniff", "damage", true, false, 9, null, null, null, false, null, null, null, null, null, null, null, false, null));
        }

        [Fact]
        public void Chains_links_and_targets()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var (ax, ay) = EditingTests.EmptyOpen(ed);
            var (bx, by) = EditingTests.EmptyOpen(ed, 40);
            ed.RecipeTeleport(1, ax, ay, 1, bx, by, false);
            var m = ed.Edits.For(1).Model;
            var trap = m.Objects.First(o => o != null && !o.Free && o.Item == 0x181);
            var trig = m.Objects.First(o => o != null && !o.Free && o.Item == 0x1A0 && o.TileX == ax && o.TileY == ay);
            Assert.Contains("#" + trig.Index, ed.TriggerChains(1));
            Assert.Contains("(" + bx + ", " + by + ")", ed.TeleportTarget(1, trap.Index));
            Assert.Contains("of level 2", ed.TrapTargetSet(1, trap.Index, bx, by, 2));
            Assert.Contains("level 2", ed.TeleportTarget(1, trap.Index).ToLowerInvariant());
            Assert.NotEmpty(ed.ObjectLinks(1, trig.Index));
            Assert.Contains("no longer links", ed.ObjectUnlink(1, trig.Index));
            Assert.Contains("now links to", ed.ObjectLink(1, trig.Index, trap.Index));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            var item = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item < 0x40);
            Assert.ThrowsAny<ArgumentException>(() => ed.TrapTargetSet(1, item.Index, 3, 3, null));
        }

        [Fact]
        public void Keys_and_terrain()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            string keys = ed.KeysList(null);
            Assert.Contains("Lock ", keys);
            Assert.Contains("Keys of lock 3", ed.KeyTextSet(3, "a key to the test"));
            Assert.Contains("a key to the test", ed.KeysList(3) + ed.StringGet("5", 0x64 + 3));
            string changes = ed.TerrainChanges(1);
            Assert.False(string.IsNullOrEmpty(changes));
            // a level with a change-terrain trap draws its preview
            for (int l = 1; l <= 9; l++)
            {
                if (ed.TerrainChanges(l).Contains("no change-terrain")) continue;
                string said;
                var png = ed.TerrainPreviewPng(l, "all", 4, out said);
                Assert.Equal(0x89, png[0]);
                Assert.Contains("applied", said);
                break;
            }
        }
    }
}
