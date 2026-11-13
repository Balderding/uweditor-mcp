using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The Try Jump tool's flight (JumpTrace) on UW1's levels.</summary>
    public class JumpTraceTests
    {
        static LevelModel Level(int n)
        {
            string g = GameData.Game("UW");
            return g == null ? null : GameSession.Open(g).LoadLevel(n);
        }

        [Fact]
        public void A_jump_over_one_low_tile_onto_a_lower_ledge_lands()
        {
            // level 5: (16, 25) at height 9, one tile at height 0, then (16, 27) at height 8
            var m = Level(5); if (m == null) return;
            var t = JumpTrace.Try(m, 16, 25, 16, 27);
            Assert.True(t.Lands, t.Text);
            Assert.False(t.Leap);
            Assert.Equal(JumpKind.Standing, t.Kind);
            Assert.StartsWith("Lands on the floor at height 8 of (16, 27)", t.Text);
            // the flight leaves the first square, rises, and ends on the far floor
            Assert.True(t.Path.Count > 20);
            Assert.True(t.Path.Max(p => p.Y) > t.Path[0].Y);
            Assert.Equal(m.FloorAt(16, 27, 0.5, 0.5), t.End.Y, 3);
            Assert.Equal(16, (int)t.End.X);
            Assert.Equal(27, (int)t.End.Z);
        }

        [Fact]
        public void A_jump_that_falls_short_ends_where_it_comes_down_or_hits()
        {
            // level 7: from (36, 20) at height 11 past the pillar at (36, 24) to the one at (36, 28), seven tiles on:
            // no jump without the spell carries that far, and the pillar between is in the way
            var m = Level(7); if (m == null) return;
            var t = JumpTrace.Try(m, 36, 20, 36, 28);
            Assert.False(t.Lands && !t.Leap, t.Text);
            if (t.Lands) return;
            Assert.StartsWith("Does not reach (36, 28)", t.Text);
            Assert.True(t.Path.Count > 5);
            Assert.True(t.End.Z < 28, "it ends before the pillar: " + t.Text);
        }

        [Fact]
        public void Rock_and_the_same_square_are_refused_with_a_sentence()
        {
            var m = Level(1); if (m == null) return;
            int rx = -1, ry = -1, ox = -1, oy = -1;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                if (m.Tiles[x, y].IsSolid) { if (rx < 0) { rx = x; ry = y; } } else if (ox < 0) { ox = x; oy = y; }
            Assert.False(JumpTrace.Try(m, rx, ry, ox, oy).Lands);
            Assert.Contains("rock", JumpTrace.Try(m, rx, ry, ox, oy).Text);
            Assert.Contains("rock", JumpTrace.Try(m, ox, oy, rx, ry).Text);
            Assert.Empty(JumpTrace.Try(m, ox, oy, ox, oy).Path);
        }

        [Fact]
        public void A_bridge_and_the_floor_under_it_are_two_places_to_land()
        {
            // UW2 level 65: (29, 30) is a pillar of height 7; (32, 27) has a bridge with its top at height 10 over a floor at height 0
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var m = s.LoadLevel(65);
            var boxes = JumpTrace.Obstacles(m, Hitboxes.ReadComObj(s));
            var there = JumpTrace.Surfaces(m, boxes, 32, 27, i => s.Names.Get(i));
            Assert.Equal(2, there.Count);
            Assert.Equal(10, there[0].Top, 1);
            Assert.True(there[0].Object >= 0);
            Assert.Contains("bridge", there[0].Name);
            Assert.Equal(0, there[1].Top, 1);
            Assert.Equal(-1, there[1].Object);
            // onto the bridge, three steps up and two tiles off on the diagonal: only with the jump spell
            var up = JumpTrace.Try(m, 29, 30, 32, 27, null, boxes, null, there[0]);
            Assert.True(up.Lands, up.Text);
            Assert.True(up.Leap);
            Assert.Equal(there[0].Top, up.On.Top, 1);
            // onto the floor under it: a plain jump, the head clear of the bridge's underside
            var down = JumpTrace.Try(m, 29, 30, 32, 27, null, boxes, null, there[1]);
            Assert.True(down.Lands, down.Text);
            Assert.False(down.Leap);
            Assert.Equal(-1, down.On.Object);
            // with no surface chosen, whatever the first jump that gets there comes down on
            Assert.True(JumpTrace.Try(m, 29, 30, 32, 27, null, boxes).Lands);
            // along the bridge from its top: the next bridge square but one
            var along = JumpTrace.Try(m, 32, 27, 34, 27, null, boxes, there[0], null);
            Assert.True(along.Lands, along.Text);
            Assert.True(along.On.Object >= 0);
            // without the objects the bridge is not there at all
            Assert.Single(JumpTrace.Surfaces(m, null, 32, 27));
        }

        [Fact]
        public void What_the_zones_take_for_a_free_jump_the_trace_lands()
        {
            // every free jump link of level 1 to 8, from its take-off square to its landing square (named in the link's notes)
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var graph = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions());
            int tried = 0, landed = 0;
            var misses = new System.Collections.Generic.List<string>();
            foreach (var e in graph.Edges.Where(e => e.Kind == "jump" && e.Requires == "free"))
            {
                int i = e.Notes.IndexOf("to (") + 4, j = e.Notes.IndexOf(')', i);
                var xy = e.Notes.Substring(i, j - i).Split(',');
                var t = JumpTrace.Try(s.LoadLevel(e.Level), e.X, e.Y, int.Parse(xy[0]), int.Parse(xy[1]));
                tried++;
                if (t.Lands && !t.Leap) landed++; else misses.Add(string.Format("L{0} ({1}, {2}) {3}: {4}", e.Level, e.X, e.Y, e.Notes, t.Text));
            }
            Assert.True(tried > 100);
            Assert.True(landed >= tried * 9 / 10, landed + " of " + tried + " land; first misses: " + string.Join(" | ", misses.Take(6)));
        }
    }
}
