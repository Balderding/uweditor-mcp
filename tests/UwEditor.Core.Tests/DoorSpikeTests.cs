using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// A spiked door (Editing.DoorSpikes): owner bit 0 and a strength in bits 1-5. With strength
    /// left a hit wears it down and never breaks it (combat/DAMAGE.C damage_object, both games), so
    /// the logic offers no bash through it; with strength 0 it breaks as any door; the player opens
    /// one that is not locked. The walkthrough's bash says to charge each blow on a tough door.
    /// </summary>
    public class DoorSpikeTests
    {
        static ObjectInfo DoorAt(LevelModel m, int x, int y) { return m.TileObjects(x, y).First(o => DoorSpikes.IsClosedDoor(o.Item)); }

        [Fact]
        public void A_spiked_uw1_door_with_strength_is_not_bashed_and_with_strength_0_it_is()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            // L1 (9, 19): key 2, pick 2 or bash
            int door = DoorAt(e.Model, 9, 19).Index;
            string notes;
            Assert.Equal("key:2 | pick:2 | bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            Assert.Equal(0, e.Model.Objects[door].Owner);
            // spiked as the spike does: owner 0x3F, strength 31
            DoorSpikes.Set(e, door, DoorSpikes.Spiked, 1);
            Assert.Equal(DoorSpikes.SpikedOwner, e.Model.Objects[door].Owner);
            Assert.True(DoorSpikes.Holds(e.Model.Objects[door]));
            Assert.Equal("key:2 | pick:2", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            Assert.Contains("spiked (strength 31): hits wear it down, it never breaks", notes);
            // worn down to strength 0 (owner 1): the damage code breaks it as an unspiked door
            DoorSpikes.Set(e, door, DoorSpikes.Strength, 0);
            Assert.Equal(1, e.Model.Objects[door].Owner);
            Assert.False(DoorSpikes.Holds(e.Model.Objects[door]));
            Assert.Equal("key:2 | pick:2 | bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            DoorSpikes.Set(e, door, DoorSpikes.Strength, 5);
            Assert.Equal(11, e.Model.Objects[door].Owner);
            Assert.Equal("key:2 | pick:2", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            Assert.Contains("spiked (strength 5)", notes);
            // the spike pulled out as opening the door does: bit 0 only
            DoorSpikes.Set(e, door, DoorSpikes.Spiked, 0);
            Assert.Equal(10, e.Model.Objects[door].Owner);
            Assert.Equal("key:2 | pick:2 | bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            // every step undoes
            for (int i = 0; i < 4; i++) e.Undo();
            Assert.Equal(0, e.Model.Objects[door].Owner);
            // only a closed door is spiked
            int notDoor = e.Model.Objects.First(o => !o.Free && o.OnTile && o.Item < 0x140).Index;
            Assert.Throws<System.InvalidOperationException>(() => DoorSpikes.Set(e, notDoor, DoorSpikes.Spiked, 1));
        }

        [Fact]
        public void A_spiked_door_that_is_not_locked_stays_free_and_id_bit_13_still_holds_in_uw1()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            // a closed door with no lock: the player opens it, spike or not (UseRect)
            var open = e.Model.Objects.First(o => !o.Free && o.OnTile && DoorSpikes.IsClosedDoor(o.Item) && !o.Children.Any(c => e.Model.Objects[c].Item == 0x10F));
            DoorSpikes.Set(e, open.Index, DoorSpikes.Spiked, 1);
            string notes;
            Assert.Equal("free", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[open.Index], out notes));
            Assert.Contains("spiked (strength 31)", notes);
            // L1 (9, 19) with id bit 13: indestructible in UW1, spike or not
            int door = DoorAt(e.Model, 9, 19).Index;
            e.EditBytes("test: tenacious", (data, tex) => { data[LevelBlock.ObjectOffset(door) + 1] |= 0x20; });
            Assert.Equal("key:2 | pick:2", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            Assert.Contains("indestructible (tenacious)", notes);
        }

        [Fact]
        public void Uw2_keeps_its_rule_bit_13_takes_nothing_a_spike_with_strength_does()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            // L1 (45, 45): a secret door only bashing opens
            int door = DoorAt(e.Model, 45, 45).Index;
            string notes;
            Assert.Equal("bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            e.EditBytes("test: door direction", (data, tex) => { data[LevelBlock.ObjectOffset(door) + 1] ^= 0x20; });
            Assert.Equal("bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            DoorSpikes.Set(e, door, DoorSpikes.Spiked, 1);
            Assert.StartsWith("flag:door_l1_45_45", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
            Assert.Contains("spiked (strength 31): hits wear it down, it never breaks", notes);
            DoorSpikes.Set(e, door, DoorSpikes.Strength, 0);
            Assert.Equal("bash", ZoneDoors.Requirement(s, e.Model, e.Model.Objects[door], out notes));
        }

        [Fact]
        public void A_bash_says_to_charge_each_blow_on_a_tough_door_only()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            // class 2 (0x144, L7 (30, 20)), class 1 (0x141, L1 (4, 49)), class 0 (0x140, L1 (33, 8)); the link's square may be beside the door
            Assert.Equal(0x144, DoorAt(s.LoadLevel(7), 30, 20).Item);
            Assert.Equal("; charge each blow, the door takes only a quarter of it, so it may take a while", Walkthrough.BashNote(s.LoadLevel(7), new ZoneLink { Level = 7, X = 30, Y = 21 }));
            Assert.Equal(0x141, DoorAt(s.LoadLevel(1), 4, 49).Item);
            Assert.Equal("; charge each blow, the door takes only half of it, so it may take a while", Walkthrough.BashNote(s.LoadLevel(1), new ZoneLink { Level = 1, X = 4, Y = 49 }));
            Assert.Equal(0x140, DoorAt(s.LoadLevel(1), 33, 8).Item);
            Assert.Equal("", Walkthrough.BashNote(s.LoadLevel(1), new ZoneLink { Level = 1, X = 33, Y = 8 }));
            Assert.Equal("", Walkthrough.BashNote(s.LoadLevel(1), new ZoneLink { Level = 1, X = 0, Y = 0 }));
        }
    }
}
