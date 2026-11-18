using System;
using System.Linq;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>object_get, object_set, object_put_inside, object_add_inside and lock_door on a copy of level 1.</summary>
    public class ObjectFieldTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static ObjectInfo Critter(McpEditor ed)
        {
            return ed.Edits.For(1).Model.Objects.First(o => o != null && !o.Free && o.IsCritter && o.OnTile && o.Index >= 2);
        }

        static (int x, int y) EmptyOpenTile(McpEditor ed)
        {
            var m = ed.Edits.For(1).Model;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0) return (x, y);
            throw new Exception("no empty open tile");
        }

        static int Added(string said) { int h = said.IndexOf('#'); return int.Parse(new string(said.Skip(h + 1).TakeWhile(char.IsDigit).ToArray())); }

        [Fact]
        public void Get_shows_record_and_mobile_fields()
        {
            var ed = Opened(); if (ed == null) return;
            var c = Critter(ed);
            string s = ed.ObjectGet(1, c.Index);
            foreach (var n in new[] { "Item id = ", "Quality = ", "Owner = ", "Z = ", "Heading = ", "HP = ", "Goal = ", "Goal target = ", "Attitude = ", "Home X = ", "Whoami = ", "Talked to = " })
                Assert.Contains(n, s);
            Assert.Contains("Next: ", s);   // the list field is shown, not settable
        }

        [Fact]
        public void Set_one_and_several_fields_as_one_undo_step()
        {
            var ed = Opened(); if (ed == null) return;
            var c = Critter(ed);
            var M = Uw1Schemas.MobileObject;
            int attitude = M["Attitude"].Get(c.Record);
            ed.ObjectSet(1, c.Index, "hp", "40", null);
            Assert.Equal(40, M["HP"].Get(ed.Edits.For(1).Model.Objects[c.Index].Record));
            string said = ed.ObjectSet(1, c.Index, null, null, "HP=12, attitude=friendly, gtarg=1, talked to=true");
            Assert.Contains("HP 40 -> 12", said);
            var r = ed.Edits.For(1).Model.Objects[c.Index].Record;
            Assert.Equal(12, M["HP"].Get(r));
            Assert.Equal(3, M["Attitude"].Get(r));
            Assert.Equal(1, M["Goal target"].Get(r));
            Assert.Equal(1, M["Talked to"].Get(r));
            ed.Undo(1, 1);
            r = ed.Edits.For(1).Model.Objects[c.Index].Record;
            Assert.Equal(40, M["HP"].Get(r));
            Assert.Equal(attitude, M["Attitude"].Get(r));
        }

        [Fact]
        public void Wrong_name_or_value_refused_and_nothing_changes()
        {
            var ed = Opened(); if (ed == null) return;
            var c = Critter(ed);
            int hp = Uw1Schemas.MobileObject["HP"].Get(c.Record);
            var e1 = Assert.ThrowsAny<ArgumentException>(() => ed.ObjectSet(1, c.Index, "bogus", "1", null));
            Assert.Contains("Attitude", e1.Message);   // the refusal lists its fields
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectSet(1, c.Index, "hp", "300", null));
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectSet(1, c.Index, "attitude", "grumpy", null));
            // the second pair is out of range: the first is not kept either
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectSet(1, c.Index, null, null, "hp=5, heading=9"));
            Assert.Equal(hp, Uw1Schemas.MobileObject["HP"].Get(ed.Edits.For(1).Model.Objects[c.Index].Record));
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectSet(1, c.Index, "Next", "0", null));
            Assert.False(ed.Edits.Existing(1) != null && ed.Edits.Existing(1).Dirty);
        }

        [Fact]
        public void Add_inside_and_put_inside_a_container()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpenTile(ed);
            int bagItem = Enumerable.Range(0x80, 16).First(i => ObjectSpec.FormOf(i, ed.Session.Kind) == ItemForm.Container);
            int bag = Added(ed.ObjectAdd(1, x, y, "0x" + bagItem.ToString("X3"), null, null, null, null, null, null));
            int coin = Added(ed.ObjectAddInside(1, bag, "0x0A0", null, null, 7));
            var m = ed.Edits.For(1).Model;
            Assert.Equal(bag, m.Objects[coin].Parent);
            Assert.Equal(7, m.Objects[coin].Link);
            int loose = Added(ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null));
            ed.ObjectPutInside(1, loose, bag);
            m = ed.Edits.For(1).Model;
            Assert.Equal(bag, m.Objects[loose].Parent);
            Assert.Contains("Holds:", ed.ObjectGet(1, bag));
            Assert.ThrowsAny<InvalidOperationException>(() => ed.ObjectPutInside(1, bag, loose));
        }

        [Fact]
        public void Lock_door_adds_a_lock_and_names_its_key()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && DoorSpikes.IsClosedDoor(o.Item) && !o.Children.Any(c => m.Objects[c].Item == 0x10F));
            string said = ed.LockDoor(1, door.Index, 9, true, 3);
            Assert.Contains("owner is 9", said);
            var lk = ed.Edits.For(1).LockOf(door.Index);
            Assert.NotNull(lk);
            Assert.Equal(9, lk.Link & 0x1FF);
            Assert.Equal(1, (lk.Record[1] >> 1) & 1);
            Assert.Equal(3, lk.Z);
            // again: the same lock changed, unlocked
            ed.LockDoor(1, door.Index, 12, false, null);
            var lk2 = ed.Edits.For(1).LockOf(door.Index);
            Assert.Equal(lk.Index, lk2.Index);
            Assert.Equal(12, lk2.Link & 0x1FF);
            Assert.Equal(0, (lk2.Record[1] >> 1) & 1);
            // its typed fields set by name
            ed.ObjectSet(1, lk2.Index, "Locked", "yes", null);
            Assert.Equal(1, (ed.Edits.For(1).Model.Objects[lk2.Index].Record[1] >> 1) & 1);
            Assert.Contains("Lock number = 12", ed.ObjectGet(1, lk2.Index));
        }
    }
}
