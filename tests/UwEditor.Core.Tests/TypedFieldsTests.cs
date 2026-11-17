using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The panel's typed fields by kind of object, locks on doors, and snapping to a wall (TypedFields, LevelEditor.AddLock, SnapToWall).</summary>
    public class TypedFieldsTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        /// <summary>The first object of a kind on any level, with its editor.</summary>
        static (LevelEditor E, ObjectInfo O) Find(GameSession s, LevelEdits edits, Func<ObjectInfo, bool> which)
        {
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var o = edits.Model(slot.Level).Objects.FirstOrDefault(x => !x.Free && x.TileX >= 0 && which(x));
                if (o != null) return (edits.For(slot.Level), o);
            }
            return (null, null);
        }

        [Fact]
        public void Each_typed_field_is_the_same_bits_as_the_record_and_edits_through_the_panels_rows()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var S = Uw1Schemas.StaticObject;

            // a teleport trap: destination and level
            var tp = Find(s, edits, o => o.Item == 0x181);
            var rows = PropertyRows.ForObject(tp.E.Model, tp.O);
            Assert.Contains(rows, r => r.IsHeader && r.Name == "For this kind");
            Assert.True(rows.FindIndex(r => r.Name == "Destination X") < rows.FindIndex(r => r.IsHeader && r.Name == "Record"));
            Assert.Equal(tp.O.Quality.ToString(), rows.First(r => r.Name == "Destination X").Value);
            Assert.Equal(tp.O.Owner.ToString(), rows.First(r => r.Name == "Destination Y").Value);
            Assert.Equal(tp.O.Z.ToString(), rows.First(r => r.Name == "Destination level").Value);
            // typed through its own row: the record's Quality follows
            tp.E.SetObjectField(tp.O.Index, rows.First(r => r.Name == "Destination X").Field, 9);
            Assert.Equal(9, tp.E.Model.Objects[tp.O.Index].Quality);
            Assert.Equal("9", PropertyRows.ForObject(tp.E.Model, tp.E.Model.Objects[tp.O.Index]).First(r => r.Name == "Quality").Value);
            tp.E.Undo();

            // a door trap: what it does, from a list
            var door = Find(s, edits, o => o.Item == 0x188);
            var what = PropertyRows.ForObject(door.E.Model, door.O).First(r => r.Name == "What it does");
            Assert.Equal(4, what.Choices.Count);
            Assert.Equal(door.O.Quality.ToString(), what.Value);
            if (door.O.Quality >= 1 && door.O.Quality <= 3) Assert.Contains("door", what.ValueNote);

            // a set variable trap: the operation's names
            var set = Find(s, edits, o => o.Item == 0x18D);
            var op = PropertyRows.ForObject(set.E.Model, set.O).First(r => r.Name == "Operation");
            Assert.Equal(8, op.Choices.Count);
            Assert.Equal(set.O.Heading.ToString(), op.Value);
            Assert.Equal(set.O.Z.ToString(), PropertyRows.ForObject(set.E.Model, set.O).First(r => r.Name == "Variable").Value);

            // a change terrain trap: fields that are parts of the record's fields
            var ct = Find(s, edits, o => o.Item == 0x185);
            var ctRows = PropertyRows.ForObject(ct.E.Model, ct.O);
            Assert.Equal((ct.O.Quality >> 1).ToString(), ctRows.First(r => r.Name == "New floor texture").Value);
            Assert.Equal((ct.O.Z >> 3).ToString(), ctRows.First(r => r.Name == "New floor height").Value);
            ct.E.SetObjectField(ct.O.Index, ctRows.First(r => r.Name == "New floor texture").Field, 3);
            var now = ct.E.Model.Objects[ct.O.Index];
            Assert.Equal(3, now.Quality >> 1);
            Assert.Equal(ct.O.Quality & 1, now.Quality & 1);             // the other bit of the quality is not touched
            ct.E.Undo();

            // a hack trap: its kind by name; a trigger: its target and who sets it off
            var hack = Find(s, edits, o => o.Item == 0x183 && Uw1Traps.HackTraps.Any(h => h.Key == o.Quality));
            Assert.Contains(Uw1Traps.HackTrapName(hack.O.Quality).Split(' ')[0], PropertyRows.ForObject(hack.E.Model, hack.O).First(r => r.Name == "Special trap").ValueNote);
            var trig = Find(s, edits, o => o.Item == 0x1A0);
            var tr = PropertyRows.ForObject(trig.E.Model, trig.O);
            Assert.Equal(trig.O.Quality.ToString(), tr.First(r => r.Name == "Target X").Value);
            Assert.True(tr.First(r => r.Name == "Reusable").IsFlag);

            // a lock: number, locked, difficulty; a stack: how many, and the +512 that makes it a property
            var lk = Find(s, edits, o => o.Item == 0x10F && o.IsQuantity);
            var lr = PropertyRows.ForObject(lk.E.Model, lk.O);
            Assert.Equal((lk.O.Link & 0x1FF).ToString(), lr.First(r => r.Name == "Lock number").Value);
            Assert.True(lr.First(r => r.Name == "Locked").IsFlag);
            var stack = Find(s, edits, o => o.IsQuantity && o.Link > 1 && o.Link < 0x200 && o.Item < 0x180 && !o.IsCritter && o.Item != 0x10F && !KeyIndex.IsKey(o.Item, s.Kind));
            var sr = PropertyRows.ForObject(stack.E.Model, stack.O);
            Assert.Equal(stack.O.Link.ToString(), sr.First(r => r.Name == "How many").Value);
            Assert.Equal("0", sr.First(r => r.Name == "Special (+512)").Value);
            // every typed row has a field and names bits inside the record
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
                foreach (var o in edits.Model(slot.Level).Objects.Where(x => !x.Free && x.TileX >= 0))
                    foreach (var t in TypedFields.For(s.Kind, o))
                        Assert.True(t.Field.Offset + t.Field.ByteSpan <= o.Record.Length, o + " " + t.Field.Name);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_door_gets_a_lock_a_copy_of_another_doors_lock_and_loses_it(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            Func<ObjectInfo, bool> isDoor = o => o.OnTile && (ItemCategories.Of(o.Item, s.Kind) & ItemCategory.Door) != 0;
            // a level with a door that has no lock and one that has
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var e = edits.For(slot.Level);
                var m = e.Model;
                var bare = m.Objects.FirstOrDefault(o => !o.Free && isDoor(o) && e.LockOf(o.Index) == null && o.Children.Count == 0);
                var locked = m.Objects.FirstOrDefault(o => !o.Free && isDoor(o) && e.LockOf(o.Index) != null);
                if (bare == null || locked == null) continue;
                var before = (byte[])e.Data.Clone();

                int lk = e.AddLock(bare.Index, 37, true, 6);
                Assert.True(Ok(e));
                var l = e.Model.Objects[lk];
                Assert.Equal(0x10F, l.Item);
                Assert.Equal(bare.Index, l.Parent);
                Assert.Equal(0x200 + 37, l.Link);
                Assert.True(l.IsQuantity);
                Assert.Equal(6, l.Z);
                Assert.Equal(1, (l.Record[1] >> 1) & 1);                  // flag 9: locked
                Assert.Equal(lk, e.LockOf(bare.Index).Index);
                Assert.Contains("has a lock already", Assert.Throws<InvalidOperationException>(() => e.AddLock(bare.Index, 5)).Message);
                e.Undo();
                Assert.Equal(before, e.Data);

                // a copy of the other door's lock, in place of its own
                e.AddLock(bare.Index, 37);
                var src = e.LockOf(locked.Index);
                int copy = e.CopyLock(locked.Index, bare.Index);
                Assert.True(Ok(e));
                var c = e.Model.Objects[copy];
                Assert.Equal(src.Link, c.Link);
                Assert.Equal(src.Record[1] & 0x0E, c.Record[1] & 0x0E);
                Assert.Equal(src.Z, c.Z);
                Assert.Single(e.Model.Objects[bare.Index].Children.Where(k => e.Model.Objects[k].Item == 0x10F));
                Assert.NotNull(e.LockOf(locked.Index));                   // the one copied from keeps its lock

                Assert.True(e.RemoveLock(bare.Index));
                Assert.Null(e.LockOf(bare.Index));
                Assert.False(e.RemoveLock(bare.Index));
                Assert.True(Ok(e));
                // only doors and containers take a lock
                var plain = e.Model.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && o.Item < 0x40);
                Assert.Contains("takes no lock", Assert.Throws<InvalidOperationException>(() => e.AddLock(plain.Index, 1)).Message);
                return;
            }
            Assert.Fail("no level with a bare door and a locked door");
        }

        [Fact]
        public void An_object_snaps_to_the_nearest_wall_of_its_tile_facing_into_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var m = e.Model;
            // an open tile with rock to the east only, and one with no rock about it
            Func<int, int, bool> solid = (x, y) => m.Tiles[x, y].IsSolid;
            var east = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(t => t.X > 0 && t.Y > 0 && t.X < 63 && t.Y < 63 && !solid(t.X, t.Y)
                && solid(t.X + 1, t.Y) && !solid(t.X - 1, t.Y) && !solid(t.X, t.Y + 1) && !solid(t.X, t.Y - 1));
            var open = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(t => t.X > 0 && t.Y > 0 && t.X < 63 && t.Y < 63 && !solid(t.X, t.Y)
                && !solid(t.X + 1, t.Y) && !solid(t.X - 1, t.Y) && !solid(t.X, t.Y + 1) && !solid(t.X, t.Y - 1));
            int lever = e.AddObject(new ObjectSpec { Item = 0x170, FineX = 2, FineY = 5, Heading = 1 }, east.X, east.Y);
            int steps = e.UndoLabels.Count;
            Assert.True(e.SnapToWall(lever));
            var o = e.Model.Objects[lever];
            Assert.Equal((7, 5, 2), (o.FineX, o.FineY, o.Heading));         // against the east edge, where it was along it, facing east
            Assert.Equal(steps + 1, e.UndoLabels.Count);
            Assert.True(e.SnapToWall(lever));                              // already there: nothing more
            Assert.Equal(steps + 1, e.UndoLabels.Count);
            int free = e.AddObject(new ObjectSpec { Item = 0x170 }, open.X, open.Y);
            Assert.False(e.SnapToWall(free));
            Assert.True(Ok(e));
        }
    }
}
