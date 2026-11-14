using System;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The automatic zones' second floors and closed squares: a bridge is a deck over its tile's
    /// floor, a force field or a grating is a barrier, and what lies behind a barrier in a dead
    /// end is reached with Telekinesis only.
    /// </summary>
    public class DecksAndBarriersTests
    {
        static NavGraph Graph(string game, params int[] levels)
        {
            string g = GameData.Game(game);
            if (g == null) return null;
            var s = GameSession.Open(g);
            return NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), levels.Length == 0 ? null : levels);
        }

        [Fact]
        public void A_bridge_is_a_region_apart_from_the_floor_under_it()
        {
            // UW2 level 65: (32, 27) has a bridge with its top at 80 height units over a floor at 0
            var g = Graph("UW2", 65); if (g == null) return;
            var decks = g.DecksAt(65, 32, 27);
            var deck = Assert.Single(decks);
            Assert.Equal(80, deck.Top);
            Assert.Equal(10, deck.Step);
            int on = g.RegionOf(65, 32, 27, 80), under = g.RegionOf(65, 32, 27, 0);
            Assert.Equal(deck.Region, on);
            Assert.Equal(g.RegionOf(65, 32, 27), under);
            Assert.NotEqual(on, under);
            // a thing lying on the bridge is in the bridge's region, one on the floor in the floor's
            Assert.Equal(on, g.RegionOf(65, 32, 27, 82));
            Assert.Equal(under, g.RegionOf(65, 32, 27, 40));
            // the bridge goes on over the next squares in one region, and nothing leads up to it from under it for free
            Assert.Equal(on, g.RegionOf(65, 33, 27, 80));
            Assert.Contains(g.Regions[on].Decks, d => d.X == 32 && d.Y == 27);
            Assert.DoesNotContain(g.Edges, e => e.From == under && e.To == on && e.Requires == "free");
            // off the bridge down to the floor beside it is a free drop
            Assert.Contains(g.Edges, e => e.From == on && e.To == under && e.Kind == "drop" && e.Requires == "free");
        }

        [Fact]
        public void Bridges_join_the_ledges_they_meet_in_UW1()
        {
            var g = Graph("UW"); if (g == null) return;
            Assert.True(g.Decks.Count > 50, g.Decks.Count + " decks");
            // every UW1 bridge lies level with a ledge: none is a region of its own
            Assert.All(g.Decks, d => Assert.True(g.Regions[d.Region].Tiles.Count > 0, d.ToString()));
            // and some run over a floor of another region (a chasm, water, lava)
            Assert.Contains(g.Decks, d => g.RegionOf(d.Level, d.X, d.Y) != d.Region);
            // a bridge laid against the ceiling is no deck
            Assert.All(g.Decks, d => Assert.True(d.Top + NavGraph.PlayerHeight <= 128));
        }

        [Fact]
        public void A_grating_closes_its_square_and_what_is_behind_needs_telekinesis()
        {
            // UW2 level 34 (Karl): the special tmap object #1023 on (30, 50) closes the passage; (30, 51) and (30, 52) lie behind it
            var g = Graph("UW2", 34); if (g == null) return;
            int front = g.RegionOf(34, 30, 49), bar = g.RegionOf(34, 30, 50), behind = g.RegionOf(34, 30, 51);
            Assert.True(g.Regions[bar].IsBarrier);
            Assert.True(g.Regions[bar].IsDoor);
            Assert.Equal("barrier", g.Regions[bar].DoorKind);
            Assert.Equal(1023, g.Regions[bar].DoorIndex);
            Assert.NotEqual(front, behind);
            Assert.Equal(behind, g.RegionOf(34, 30, 52));
            // through the barrier: only ways that ask for what nothing gives
            foreach (var e in g.Edges.Where(e => (e.From == bar || e.To == bar) && e.Kind != "reach"))
                Assert.Contains(NavGraph.BarrierToken, e.Requires);
            // from the front, the dead end and the barrier's own square are reached from afar
            foreach (int to in new[] { behind, bar })
            {
                var reach = Assert.Single(g.Edges, e => e.From == front && e.To == to && e.Kind == "reach");
                Assert.Equal("spell:telekinesis", reach.Requires);
            }
            // and nothing comes back out
            Assert.DoesNotContain(g.Edges, e => e.From == behind && e.To == front && !e.Requires.Contains("spell:portal"));
            // the token is one nothing grants
            Assert.False(Requirement.TryParse(NavGraph.BarrierToken, out _).Eval(new Holding()));
        }

        sealed class Holding : IRequirementContext
        {
            public SolverOptions Options { get { return new SolverOptions { AbilitySources = true, Magic = true }; } }
            public bool HasToken(string token) { return token.StartsWith("spell:") || token.StartsWith("cast:") || token.StartsWith("key:") || token.StartsWith("item:"); }
            public bool ZoneReachable(int zone) { return true; }
            public int TalismanCount() { return 8; }
        }

        [Fact]
        public void The_zones_keep_each_decks_zone_beside_the_tile_map()
        {
            var zs = new ZoneSet(1);
            zs.InitLevel(1, (x, y) => true);
            zs.SetZone(1, 5, 5, 3);
            zs.EnsureZone(3); zs.EnsureZone(7);
            zs.Decks.Add(new ZoneDeck { Level = 1, X = 5, Y = 5, Top = 80, Zone = 7 });
            foreach (var z in new[] { zs, ZoneSet.FromJson(zs.ToJson(), 1) })
            {
                Assert.Equal(3, z.ZoneOf(1, 5, 5, 0));
                Assert.Equal(3, z.ZoneOf(1, 5, 5, 70));
                Assert.Equal(7, z.ZoneOf(1, 5, 5, 80));
                Assert.Equal(7, z.ZoneOf(1, 5, 5, 100));
                Assert.Equal(7, z.DeckZone(1, 5, 5, 79));
                Assert.Equal(0, z.DeckZone(1, 5, 6, 80));
                Assert.Single(z.Decks);
            }
        }

        [Fact]
        public void The_automatic_zones_of_UW1_list_their_decks()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var s = GameSession.Open(game);
            var r = AutoZones.Build(s, l => s.LoadLevel(l));
            Assert.Equal(r.Graph.Decks.Count, r.Set.Decks.Count);
            foreach (var d in r.Graph.Decks)
            {
                Assert.Equal(r.Graph.Regions[d.Region].Zone, r.Set.DeckZone(d.Level, d.X, d.Y, d.Top));
                // what lies on the deck is in the deck's zone, whatever the floor under it is
                Assert.Equal(r.Graph.Regions[d.Region].Zone, r.Set.ZoneOf(d.Level, d.X, d.Y, d.Top));
            }
        }
    }
}
