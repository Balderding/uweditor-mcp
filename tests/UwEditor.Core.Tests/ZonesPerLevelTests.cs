using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Zone ids numbered a level (ZoneSet.PerLevel, UW2): id = level * 256 + number, the level's
    /// map still one byte a tile, so it is painted by hand or as a picture as before.
    /// </summary>
    public class ZonesPerLevelTests
    {
        static ZoneSet Two()
        {
            var zs = new ZoneSet(3) { PerLevel = true };
            for (int l = 1; l <= 3; l++) zs.InitLevel(l, (x, y) => x < 10 && y < 10);
            return zs;
        }

        [Fact]
        public void An_id_is_a_level_and_a_number()
        {
            var zs = Two();
            int a = zs.Global(1, 5), b = zs.Global(2, 5);
            Assert.Equal(261, a);
            Assert.Equal(517, b);
            Assert.Equal("L1:5", zs.Label(a));
            Assert.Equal(5, zs.Number(b));
            // rock and "not given a zone" are the same on every level
            Assert.Equal(0, zs.Global(2, 0));
            Assert.Equal(1, zs.Global(2, 1));
            zs.SetZone(1, 3, 3, a);
            zs.SetZone(2, 3, 3, b);
            Assert.Equal(a, zs.ZoneAt(1, 3, 3));
            Assert.Equal(b, zs.ZoneAt(2, 3, 3));
            // the map holds the number only
            Assert.Equal(5, zs.Tiles[0][3 * 64 + 3]);
            Assert.Equal(5, zs.Tiles[1][3 * 64 + 3]);
            // a zone of another level is refused
            zs.SetZone(2, 4, 4, a);
            Assert.Equal(ZoneSet.Unassigned, zs.ZoneAt(2, 4, 4));
            Assert.Equal(0, zs.Fill(2, 4, 4, a));
            Assert.Equal(1, zs.TileCount(a));
            Assert.Equal(new[] { 1 }, zs.LevelsOf(a).ToArray());
            Assert.Equal(new[] { 2 }, zs.LevelsOf(b).ToArray());
            Assert.Equal(1, zs.CountsOnLevel(2)[b]);
            Assert.False(zs.CountsOnLevel(2).ContainsKey(a));
        }

        [Fact]
        public void Each_level_has_its_own_254_zones()
        {
            var zs = Two();
            Assert.Equal(-1, zs.NextZoneId());               // which level?
            for (int n = 2; n <= 255; n++)
            {
                int id = zs.NextZoneId(2);
                Assert.Equal(2 * 256 + n, id);
                zs.EnsureZone(id);
            }
            Assert.Equal(-1, zs.NextZoneId(2));
            Assert.Equal(3 * 256 + 2, zs.NextZoneId(3));
            Assert.Equal("Zone L2:7", zs.GetZone(2 * 256 + 7).Name);
        }

        [Fact]
        public void The_file_and_the_picture_keep_the_numbering()
        {
            var zs = Two();
            int a = zs.Global(2, 9);
            zs.EnsureZone(a).Name = "Hall";
            zs.Fill(2, 0, 0, a);
            zs.StartZone = a;
            zs.Links.Add(new ZoneLink { Id = 1, From = a, To = zs.Global(3, 2), Kind = "stairs" });
            var back = ZoneSet.FromJson(zs.ToJson(), 3);
            Assert.True(back.PerLevel);
            Assert.Equal(a, back.StartZone);
            Assert.Equal(a, back.ZoneAt(2, 5, 5));
            Assert.Equal("Hall", back.GetZone(a).Name);
            Assert.Equal(100, back.TileCount(a));
            Assert.Equal(zs.Global(3, 2), back.Links[0].To);
            // a level's picture: the palette index is the number, and it comes back as that level's zone
            var png = zs.ExportPicture(2);
            var other = Two();
            Assert.Null(other.ImportPicture(2, png));
            Assert.Equal(a, other.ZoneAt(2, 5, 5));
            Assert.NotNull(other.GetZone(a));
            // a file without the mark is numbered as before
            var plain = new ZoneSet(3);
            plain.InitLevel(1, (x, y) => true);
            plain.SetZone(1, 1, 1, 9);
            Assert.False(ZoneSet.FromJson(plain.ToJson(), 3).PerLevel);
            Assert.Equal(9, ZoneSet.FromJson(plain.ToJson(), 3).ZoneAt(1, 1, 1));
            // split by level: nothing to split
            string note;
            Assert.Equal(zs.Zones.Count, zs.SplitByLevel(out note).Zones.Count);
        }

        [Fact]
        public void UW2s_automatic_zones_all_get_an_id()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var r = AutoZones.Build(s, l => s.LoadLevel(l));
            Assert.True(r.Set.PerLevel);
            Assert.Equal(0, r.Unzoned);
            Assert.DoesNotContain(r.Warnings, w => w.Contains("More zones than ids"));
            var zones = r.Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).ToList();
            Assert.True(zones.Count > 254, zones.Count + " zones");
            // each zone lies on the level its id names, and on no other
            foreach (var z in zones)
            {
                var lv = r.Set.LevelsOf(z.Id);
                Assert.True(lv.Count <= 1, r.Set.Label(z.Id));
                if (lv.Count == 1) Assert.Equal(z.Id >> 8, lv[0]);
            }
            Assert.All(r.Graph.Regions.Where(x => x.Zone > ZoneSet.Unassigned), x => Assert.Equal(x.Level, x.Zone >> 8));
            // the ways between levels are links between zones of two levels
            Assert.Contains(r.Set.Links, k => k.From >> 8 != k.To >> 8);
            Assert.Equal(1, r.Set.StartZone >> 8);
        }

        [Fact]
        public void UW1s_zones_keep_one_numbering()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var r = AutoZones.Build(s, l => s.LoadLevel(l));
            Assert.False(r.Set.PerLevel);
            Assert.All(r.Set.Zones, z => Assert.True(z.Id <= ZoneSet.MaxZone));
        }
    }
}
