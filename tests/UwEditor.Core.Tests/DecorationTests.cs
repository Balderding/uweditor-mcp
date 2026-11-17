using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Scattering debris, plants, moss and bones over a region (Decoration).</summary>
    public class DecorationTests
    {
        static List<(int X, int Y)> Region(LevelModel m)
        {
            // the 12 × 12 block with the most open tiles
            int best = -1, bx = 1, by = 1;
            for (int y = 1; y < 51; y += 3)
                for (int x = 1; x < 51; x += 3)
                {
                    int n = 0;
                    for (int j = 0; j < 12; j++) for (int i = 0; i < 12; i++) if (!m.Tiles[x + i, y + j].IsSolid) n++;
                    if (n > best) { best = n; bx = x; by = y; }
                }
            var l = new List<(int, int)>();
            for (int j = 0; j < 12; j++) for (int i = 0; i < 12; i++) l.Add((bx + i, by + j));
            return l;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Each_game_has_items_for_every_kind_and_none_with_a_use(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var items = Decoration.Items(s);
            string all = string.Join(" | ", items.Select(k => k.Key + ": " + string.Join(", ", k.Value.Select(i => s.Names.Get(i)))));
            Assert.Equal(Decoration.Kinds, Decoration.KindsOf(s).ToArray());
            Assert.Contains(items["debris"], i => s.Names.Get(i).Contains("debris"));
            Assert.Contains(items["bones"], i => s.Names.Get(i).Contains("skull"));
            Assert.Contains(items["moss"], i => s.Names.Get(i).Contains("grass"));
            Assert.Contains(items["plants"], i => s.Names.Get(i).Contains("plant"));
            Assert.All(items.Values.SelectMany(v => v), i => Assert.True((ItemCategories.Of(i, s.Kind) & ItemCategory.Scenery) != 0, all));
            Assert.Contains(items["stalactites"], i => s.Names.Get(i).Contains("stalactite"));
            // what the shipped levels hang from the ceiling hangs: stalactites, and one of the plants; bones, debris and grass lie
            var hang = Decoration.Hanging(s);
            Assert.All(items["stalactites"], i => Assert.True(hang.ContainsKey(i) && hang[i] >= 64, all));
            Assert.Contains(items["plants"], i => hang.ContainsKey(i));
            Assert.Contains(items["plants"], i => !hang.ContainsKey(i));
            Assert.All(items["bones"].Concat(items["debris"]), i => Assert.False(hang.ContainsKey(i), s.Names.Get(i)));
            Assert.All(items["moss"].Where(i => s.Names.Get(i).Contains("grass")), i => Assert.False(hang.ContainsKey(i)));
            Assert.All(items["moss"].Where(i => s.Names.Get(i).Contains("vine")), i => Assert.True(hang.ContainsKey(i)));
            // an item is in one kind only
            Assert.Equal(items.Values.Sum(v => v.Count), items.Values.SelectMany(v => v).Distinct().Count());
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_roll_scatters_the_chosen_kinds_at_the_density_and_another_roll_differs(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var region = Region(e.Model);
            var items = Decoration.Items(s);
            var hanging = Decoration.Hanging(s);
            var before = (byte[])e.Data.Clone();
            int steps = e.UndoLabels.Count;

            var o = new DecorationOptions { Kinds = new HashSet<string> { "bones", "moss" }, PerHundred = 20, Seed = 7 };
            var r = Decoration.Apply(e, region, o);
            bool ok; e.Check(out ok); Assert.True(ok);
            Assert.Equal(steps + 1, e.UndoLabels.Count);
            Assert.True(r.Made.Count > 5, r.Describe());
            Assert.Equal(r.Wanted, r.Made.Count);
            var allowed = new HashSet<int>(items["bones"].Concat(items["moss"]));
            var set = new HashSet<int>(region.Select(t => t.X + 64 * t.Y));
            foreach (int i in r.Made)
            {
                var obj = e.Model.Objects[i];
                Assert.Contains(obj.Item, allowed);
                Assert.True(obj.OnTile && set.Contains(obj.TileX + 64 * obj.TileY));
                Assert.False(e.Model.Tiles[obj.TileX, obj.TileY].IsSolid);
                int floor = e.FloorZ(obj.TileX, obj.TileY, obj.FineX, obj.FineY);
                if (hanging.ContainsKey(obj.Item)) Assert.True(obj.Z >= floor + Decoration.HangsAbove && obj.Z >= hanging[obj.Item], obj + " hangs at " + obj.Z);
                else Assert.Equal(floor, obj.Z);
            }
            Assert.Equal(r.Made.Count(i => hanging.ContainsKey(e.Model.Objects[i].Item)), r.Hanging);
            Assert.Equal(r.Made.Count, r.ByKind.Values.Sum());
            string first = string.Join(",", r.Made.Select(i => e.Model.Objects[i].Item + "@" + e.Model.Objects[i].TileX + "." + e.Model.Objects[i].TileY));

            // the same seed again gives the same scatter; another seed another; one undo takes a roll back whole
            e.Undo();
            Assert.Equal(before, e.Data);
            var again = Decoration.Apply(e, region, o);
            Assert.Equal(first, string.Join(",", again.Made.Select(i => e.Model.Objects[i].Item + "@" + e.Model.Objects[i].TileX + "." + e.Model.Objects[i].TileY)));
            e.Undo();
            var o2 = o.Clone(); o2.Seed = 8;
            var other = Decoration.Apply(e, region, o2);
            Assert.NotEqual(first, string.Join(",", other.Made.Select(i => e.Model.Objects[i].Item + "@" + e.Model.Objects[i].TileX + "." + e.Model.Objects[i].TileY)));
            e.Undo();
            // thicker places more
            var o3 = o.Clone(); o3.PerHundred = 60;
            var thick = Decoration.Apply(e, region, o3);
            Assert.True(thick.Made.Count > 2 * r.Made.Count);
            e.Undo();
            Assert.Equal(before, e.Data);

            // no kind chosen, and a region of rock, do nothing
            Assert.Throws<InvalidOperationException>(() => Decoration.Apply(e, region, new DecorationOptions { Kinds = new HashSet<string>() }));
            var rock = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).Where(t => e.Model.Tiles[t.X, t.Y].IsSolid).Take(20);
            Assert.Empty(Decoration.Apply(e, rock, o).Made);
            Assert.Equal(before, e.Data);
        }
    }
}
