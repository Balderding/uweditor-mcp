using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The timeline solver on small zone sets made by hand (no game data): what a one-way trap
    /// holds, levers inside a trap, single-use sources, and the reading of bracketed requirements.
    /// Zone 2 is the start.
    /// </summary>
    public class SolverSoundnessTests
    {
        static ZoneSet Zones(int last, int goal)
        {
            var zs = new ZoneSet(1) { StartZone = 2, GoalZone = goal };
            for (int i = 2; i <= last; i++) zs.EnsureZone(i);
            return zs;
        }

        static void Link(ZoneSet zs, int from, int to, string requires = "free", bool twoWay = false)
        {
            zs.Links.Add(new ZoneLink { Id = zs.Links.Count + 1, From = from, To = to, Kind = "walk", Requires = requires, TwoWay = twoWay });
        }

        static void Put(ZoneSet zs, int zone, string grants, string requires = "")
        {
            zs.Placements.Add(new Placement { Zone = zone, Grants = grants, Requires = requires });
        }

        static AbilityOptions NoMagic(bool singleUse) { return new AbilityOptions { Magic = false, SingleUseItems = singleUse }; }

        /// <summary>Start -> trap 3 (one way, out only with key 1); start -> 4 with levitation, where key 1 lies; the goal 5 behind 4.</summary>
        static ZoneSet TrapWith(string source)
        {
            var zs = Zones(5, 5);
            Link(zs, 2, 3);
            Link(zs, 3, 2, "key:1");
            Link(zs, 2, 4, "spell:levitate");
            Link(zs, 4, 2);
            Link(zs, 4, 5, "free", true);
            Put(zs, 3, source);
            Put(zs, 4, "key:1");
            return zs;
        }

        [Theory]
        [InlineData("once:levitate", true)]
        [InlineData("worn:levitate", false)]
        [InlineData("perm:levitate", false)]
        public void The_only_source_of_an_ability_inside_a_one_way_trap_strands_the_player(string source, bool singleUse)
        {
            var r = Timeline.OnZones(TrapWith(source), NoMagic(singleUse));
            Assert.True(r.Solve.GoalReached);
            var trap = Assert.Single(r.Solve.Traps);
            Assert.Equal(3, trap.Zone);
            Assert.Contains(source, trap.Holds);
            Assert.False(r.Solve.SafeGoal);
            Assert.Contains(source, trap.Strands);
        }

        [Theory]
        [InlineData("cast:open", true, false, true)]
        [InlineData("cast:open", true, true, true)]
        [InlineData("cast:open", false, true, false)]
        [InlineData("cast:open", false, false, false)]
        [InlineData("worn:open", true, false, true)]
        [InlineData("worn:open", false, true, false)]
        public void The_Open_spell_counts_with_Magic_on_only_whatever_lock_picking_is(string source, bool magic, bool picking, bool opens)
        {
            var zs = Zones(3, 3);
            Link(zs, 2, 3, "pick:5");
            Put(zs, 2, source);
            var r = Timeline.OnZones(zs, new AbilityOptions { Magic = magic, LockPicking = picking, PickSkill = 0, SingleUseItems = true });
            Assert.Equal(opens, r.Solve.GoalReached);
        }

        [Fact]
        public void A_trap_holding_what_another_place_gives_too_does_not_strand()
        {
            var zs = TrapWith("worn:levitate");
            Put(zs, 2, "worn:levitate");
            var r = Timeline.OnZones(zs, NoMagic(false));
            var trap = Assert.Single(r.Solve.Traps);
            Assert.True(r.Solve.SafeGoal);
            Assert.Empty(trap.Strands);
        }

        [Fact]
        public void A_trap_that_leads_on_to_the_goal_is_a_point_of_no_return_not_a_strand()
        {
            var zs = Zones(4, 4);
            Link(zs, 2, 3);
            Link(zs, 3, 4, "key:1");
            Put(zs, 3, "key:1");
            var r = Timeline.OnZones(zs, NoMagic(false));
            Assert.True(r.Solve.GoalReached);
            Assert.Contains(r.Solve.Traps, t => t.Zone == 3);
            Assert.All(r.Solve.Traps, t => Assert.Empty(t.Strands));
        }

        [Fact]
        public void A_lever_inside_a_trap_that_the_goal_needs_strands_the_player()
        {
            // the door to the goal opens from zone 3, which is entered one way and never left
            var zs = Zones(4, 4);
            Link(zs, 2, 3);
            Link(zs, 2, 4, "zone:3", true);
            var r = Timeline.OnZones(zs, NoMagic(false));
            Assert.True(r.Solve.GoalReached);
            var trap = Assert.Single(r.Solve.Traps);
            Assert.False(r.Solve.SafeGoal);
            Assert.NotEmpty(trap.Strands);
        }

        /// <summary>Two ways out of the start that need levitation, a key behind each, and the goal behind both keys.</summary>
        static ZoneSet TwoGates()
        {
            var zs = Zones(5, 5);
            Link(zs, 2, 3, "spell:levitate", true);
            Link(zs, 2, 4, "spell:levitate", true);
            Link(zs, 2, 5, "key:1 & key:2", true);
            Put(zs, 3, "key:1");
            Put(zs, 4, "key:2");
            return zs;
        }

        [Fact]
        public void One_potion_does_not_cross_two_gates()
        {
            var zs = TwoGates();
            Put(zs, 2, "once:levitate");
            var r = Timeline.OnZones(zs, NoMagic(true));
            Assert.False(r.Solve.GoalReached);
            Assert.Equal(new[] { "once:levitate" }, r.Solve.OnceSpent.ToArray());
            Assert.Single(r.Solve.ZoneSphere.Keys, z => z == 3 || z == 4);
        }

        [Fact]
        public void Two_potions_cross_two_gates_and_a_ring_spends_none()
        {
            var two = TwoGates();
            Put(two, 2, "once:levitate");
            Put(two, 2, "once:levitate");
            var r = Timeline.OnZones(two, NoMagic(true));
            Assert.True(r.Solve.GoalReached);
            Assert.Equal(2, r.Solve.OnceSpent.Count);

            var ring = TwoGates();
            Put(ring, 2, "once:levitate");
            Put(ring, 2, "worn:levitate");
            r = Timeline.OnZones(ring, NoMagic(true));
            Assert.True(r.Solve.GoalReached);
            Assert.Empty(r.Solve.OnceSpent);
        }

        [Fact]
        public void A_potion_found_later_opens_the_second_gate()
        {
            // the second potion lies behind the first gate
            var zs = TwoGates();
            Put(zs, 2, "once:levitate");
            Put(zs, 3, "once:levitate");
            Put(zs, 4, "once:levitate");
            var r = Timeline.OnZones(zs, NoMagic(true));
            Assert.True(r.Solve.GoalReached);
            Assert.Equal(2, r.Solve.OnceSpent.Count);
        }

        [Fact]
        public void Single_use_sources_do_nothing_with_the_option_off()
        {
            var zs = TwoGates();
            Put(zs, 2, "once:levitate");
            var r = Timeline.OnZones(zs, NoMagic(false));
            Assert.False(r.Solve.GoalReached);
            Assert.Single(r.Solve.ZoneSphere);
            Assert.Empty(r.Solve.OnceSpent);
        }

        [Fact]
        public void A_way_back_that_needs_a_potion_already_spent_is_a_trap()
        {
            // up a ledge with the only potion; the way down again needs levitation too
            var zs = Zones(4, 4);
            Link(zs, 2, 3, "spell:levitate", true);
            Link(zs, 3, 4, "free", true);
            Put(zs, 2, "once:levitate");
            var r = Timeline.OnZones(zs, NoMagic(true));
            Assert.True(r.Solve.GoalReached);
            Assert.Contains(r.Solve.Traps, t => t.Zone == 4);
            // with a second potion found up there, he gets back
            Put(zs, 3, "once:levitate");
            r = Timeline.OnZones(zs, NoMagic(true));
            Assert.DoesNotContain(r.Solve.Traps, t => t.Zone == 4);
        }

        [Fact]
        public void A_spell_whose_last_rune_lies_inside_a_zone_gets_the_player_out_of_it()
        {
            // the cast placement lies in the start zone, as the automatic zones put it; the rune is found in zone 3
            var zs = Zones(4, 4);
            Link(zs, 2, 3);
            Link(zs, 3, 2, "spell:levitate");
            Link(zs, 2, 4, "free", true);
            Put(zs, 3, "item:rune_hur");
            Put(zs, 2, "cast:levitate", "item:rune_hur");
            var r = Timeline.OnZones(zs, new AbilityOptions());
            Assert.Empty(r.Solve.Traps);
        }

        [Fact]
        public void Bracketed_requirements_keep_their_brackets()
        {
            Assert.Equal(new[] { "(a | b) & (c | d)" }, AutoZones.Alternatives("(a | b) & (c | d)").ToArray());
            Assert.Equal(new[] { "a & b", "c & d" }, AutoZones.Alternatives("(a & b) | (c & d)").ToArray());
            Assert.Equal(new[] { "a | b" }, AutoZones.Alternatives("(a | b)").ToArray());
            // the term a link from its own zone always meets goes, also inside such a requirement
            Assert.Equal("(a | b) & c", AutoZones.Normalize(AutoZones.Simplify("(a | b) & (c) & zone:7", 7)));
            Assert.Equal("(a | b) & (c | d)", AutoZones.Normalize(AutoZones.Simplify("(a | b) & (c | d) & zone:7", 7)));
        }

        [Fact]
        public void The_solve_is_the_same_twice_over()
        {
            var a = Timeline.OnZones(TrapWith("once:levitate"), NoMagic(true)).Solve;
            var b = Timeline.OnZones(TrapWith("once:levitate"), NoMagic(true)).Solve;
            Assert.Equal(a.ZoneSphere.OrderBy(x => x.Key), b.ZoneSphere.OrderBy(x => x.Key));
            Assert.Equal(a.TokenSphere.OrderBy(x => x.Key), b.TokenSphere.OrderBy(x => x.Key));
        }
    }
}
