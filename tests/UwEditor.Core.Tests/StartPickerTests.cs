using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// StartPicker: the squares a random start may use (the square rules) and the zone filter
    /// (a solve from the zone reaches the goal with no cut-off: UW1 with every moving key item
    /// held, UW2 the unshuffled game's solve standing in). Each picker builds the zones once
    /// (about 3 s for UW1, 10 s for UW2).
    /// </summary>
    public class StartPickerTests
    {
        static readonly object gate = new object();
        static StartPicker uw1, uw2;

        static StartPicker Uw1()
        {
            lock (gate)
            {
                if (uw1 != null) return uw1;
                string g = GameData.Game("UW"); if (g == null) return null;
                var s = GameSession.Open(g);
                return uw1 = StartPicker.For(s, l => s.LoadLevel(l));
            }
        }

        static StartPicker Uw2()
        {
            lock (gate)
            {
                if (uw2 != null) return uw2;
                string g = GameData.Game("UW2"); if (g == null) return null;
                var s = GameSession.Open(g);
                return uw2 = StartPicker.For(s, l => s.LoadLevel(l));
            }
        }

        /// <summary>The first square of the scope whose floor is of a terrain class, or null.</summary>
        static (int L, int X, int Y)? FirstOfClass(StartPicker p, int cls)
        {
            var terrain = CreatureTerrain.TryLoad(p.Session);
            foreach (int l in StartPicker.LevelsIn(p.Session, StartScope.AnyLevel))
            {
                var m = p.Session.LoadLevel(l);
                for (int y = 1; y < 63; y++)
                    for (int x = 1; x < 63; x++)
                        if (!m.Tiles[x, y].IsSolid && CreatureTerrain.FloorClass(p.Session, terrain, m, x, y) == cls) return (l, x, y);
            }
            return null;
        }

        [Fact]
        public void UW1_squares_are_rejected_for_lava_water_rock_triggers_the_goal_level_and_the_teleport_ring()
        {
            var p = Uw1(); if (p == null) return;
            var lava = FirstOfClass(p, CreatureTerrain.Lava);
            Assert.NotNull(lava);
            Assert.Equal("lava", p.Check(lava.Value.L, lava.Value.X, lava.Value.Y).Reason);
            var water = p.Check(1, 40, 30);
            Assert.False(water.Viable);
            Assert.Equal("water", water.Reason);
            Assert.Equal("solid rock", p.Check(5, 30, 30).Reason);
            // a square with a move trigger (the first on level 2)
            var m2 = p.Session.LoadLevel(2);
            var trig = m2.Objects.First(o => !o.Free && o.OnTile && o.Item == 0x1A0 && !m2.Tiles[o.TileX, o.TileY].IsSolid && o.TileX > 0 && o.TileX < 63 && o.TileY > 0 && o.TileY < 63);
            var c = p.Check(2, trig.TileX, trig.TileY);
            Assert.False(c.Viable);
            Assert.StartsWith("a move trigger", c.Reason);
            Assert.Equal("the goal's level", p.Check(9, 30, 30).Reason);
            // the game's own start and the squares round it hold the start teleport's triggers
            Assert.StartsWith("the game's own start", p.Check(1, 32, 2).Reason);
            Assert.StartsWith("the game's own start", p.Check(1, 33, 3).Reason);
            // an open square of level 2 the solve reaches the goal from, with every moving item held
            var ok = p.Check(2, 9, 35);
            Assert.True(ok.Viable, ok.Reason);
            Assert.False(ok.StandIn);
            Assert.True(p.Assumed.Count > 20, p.Assumed.Count + " tokens");
        }

        [Fact]
        public void UW1_draws_are_the_same_for_a_seed_and_keep_to_the_scope()
        {
            var p = Uw1(); if (p == null) return;
            var a = p.Pick(7, StartScope.AnyLevel);
            var b = p.Pick(7, StartScope.AnyLevel);
            Assert.True(a.Viable, a.Reason);
            Assert.Equal((a.Level, a.X, a.Y), (b.Level, b.X, b.Y));
            // a picker of its own (built afresh) draws the same
            var s = p.Session;
            var q = StartPicker.For(s, l => s.LoadLevel(l));
            var c = q.Pick(7, StartScope.AnyLevel);
            Assert.Equal((a.Level, a.X, a.Y), (c.Level, c.X, c.Y));
            var levels = Enumerable.Range(1, 12).Select(seed => p.Pick(seed, StartScope.FirstLevels)).ToList();
            Assert.All(levels, x => { Assert.True(x.Viable, x.Reason); Assert.Equal(1, x.Level); Assert.Null(p.SquareProblem(x.Level, x.X, x.Y)); });
            var any = Enumerable.Range(1, 12).Select(seed => p.Pick(seed, StartScope.AnyLevel)).ToList();
            Assert.Contains(any, x => x.Level != 1);
            Assert.All(any, x => Assert.InRange(x.Level, 1, 8));
            Assert.True(levels.Select(x => (x.X, x.Y)).Distinct().Count() > 3);
        }

        /// <summary>Every square of an automatic zone outside its folded dead ends and doors reaches every other freely: one solve a zone is enough.</summary>
        [Fact]
        public void Every_zones_candidate_squares_reach_one_another()
        {
            var p = Uw1(); if (p == null) return;
            Assert.Empty(p.ZonesNotStronglyConnected(StartScope.AnyLevel));
            var p2 = Uw2(); if (p2 == null) return;
            Assert.Empty(p2.ZonesNotStronglyConnected(StartScope.AnyLevel));
        }

        /// <summary>
        /// The three level 7 ledges the filter used to reject (35, 48), (36, 24), (34, 45): from them
        /// the solve reaches the area at (43, 18) and the western area at (9, 33) in the same sphere,
        /// both by levitating up from the lava; the walk-on trigger there closes the door at (7, 35),
        /// and the search after the change looks only among the zones reached so far, so the way back
        /// to (43, 18) through level 8 is not seen until the next sphere, which does get back there.
        /// A zone cut off and got back is not a cut-off for the filter (TimelineSolve.CutOffRegained).
        /// The solve below starts at level 7 (60, 62) with the default options, where the same cut-off
        /// of (43, 18) comes up in sphere 5 and is got back (0.1.44; before, from (35, 48) with every
        /// circle counted, which relied on Gate Travel cast from runes on level 7 with no mana).
        /// </summary>
        [Fact]
        public void UW1_a_zone_cut_off_and_got_back_later_does_not_reject_a_start()
        {
            var p = Uw1(); if (p == null) return;
            foreach (var (x, y) in new[] { (35, 48), (36, 24), (34, 45) })
            {
                var c = p.Check(7, x, y);
                Assert.True(c.Viable, c.ToString());
            }
            var s = p.Session;
            // since 0.1.44 Gate Travel is not cast from runes on level 7 while the orb stands (no mana), so the solve from (35, 48)
            // with every circle no longer leaves early by the moonstone and is cut off nowhere; from (60, 62) with the default options it is
            var any = new AbilityOptions();
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = new StartPoint { Level = 7, X = 60.5, Y = 62.5 } }, any, null, null, variants: false);
            var sv = Timeline.Solve(t, any, "held", p.Assumed);
            Assert.True(sv.GoalReached);
            int area = t.Set.ZoneAt(7, 43, 18);
            Assert.Contains(sv.CutOff, c => c.Value.Contains(area));
            Assert.Contains(area, sv.CutOffRegained);
            Assert.All(sv.CutOff.SelectMany(c => c.Value), z => Assert.Contains(z, sv.CutOffRegained));
        }

        /// <summary>
        /// UW2: the cells and rooms locked from outside are rejected, the gem's facets accepted. The
        /// Avatar's cell at level 1 (42, 38) holds an exit trigger and the round room's middle at
        /// level 18 (29, 28) is lava: the square rules reject them (the cell's other squares lie in
        /// an empty dead end the zones fold into the corridor, not candidates either), and the zone
        /// filter rejects the rest of the round room (27, 28). The facet of the Prison Tower lands on level 9 (33, 32), an
        /// enter trigger's square: its zone is accepted (the zone holds no other candidate), as the Pits' landing (57 (59, 20)).
        /// </summary>
        [Fact]
        public void UW2_locked_in_starts_are_rejected_and_the_facets_accepted()
        {
            var p = Uw2(); if (p == null) return;
            Assert.True(p.StandIn);
            foreach (var sq in new[] { (1, 38, 38), (16, 33, 33), (18, 27, 28) })
            {
                var c = p.Check(sq.Item1, sq.Item2, sq.Item3);
                Assert.False(c.Viable, sq + " " + c.Reason);
                Assert.StartsWith("the solve from here misses the goal", c.Reason);
            }
            var cell = p.Check(1, 42, 38);
            Assert.False(cell.Viable);
            Assert.StartsWith("a trigger", cell.Reason);
            Assert.Equal("lava", p.Check(18, 29, 28).Reason);
            var facet = p.Check(9, 33, 32);
            Assert.StartsWith("a trigger", facet.Reason);
            var ok = p.CheckZone(facet.Zone);
            Assert.True(ok.Viable, ok.Reason);
            Assert.True(ok.StandIn);
            var pits = p.Check(57, 59, 20);
            Assert.True(pits.Viable, pits.Reason);
            Assert.Contains("unshuffled", pits.Reason);
            // the castle scope keeps to slots 1 to 5
            for (int seed = 1; seed <= 6; seed++)
            {
                var d = p.Pick(seed, StartScope.FirstLevels);
                Assert.True(d.Viable, d.Reason);
                Assert.InRange(d.Level, 1, 5);
            }
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, StartPicker.LevelsIn(p.Session, StartScope.FirstLevels).ToArray());
            Assert.DoesNotContain(73, StartPicker.LevelsIn(p.Session, StartScope.AnyLevel));
        }
    }
}
