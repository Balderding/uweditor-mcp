using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2's trap chains and state: the condition traps branch, a state the game does not
    /// start in is a token the set variable traps grant, the castle plot is a count, and the
    /// blackrock gem links level 5 to the worlds by the plot.
    /// </summary>
    public class Uw2StateTests
    {
        static NavGraph graph;
        static bool tried;

        static NavGraph Graph()
        {
            if (tried) return graph;
            tried = true;
            string g = GameData.Game("UW2");
            if (g == null) return null;
            var s = GameSession.Open(g);
            return graph = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), null);
        }

        sealed class Ctx : IRequirementContext
        {
            public readonly HashSet<string> Tokens = new HashSet<string>();
            public bool HasToken(string token) { return Tokens.Contains(Requirement.Normalize(token)); }
            public bool ZoneReachable(int zone) { return true; }
            public int TalismanCount() { return 0; }
            public int PlotClock() { return Requirement.PlotClock(Tokens); }
            public SolverOptions Options { get; } = new SolverOptions();
        }

        [Fact]
        public void The_plot_is_the_highest_set_plus_the_steps()
        {
            var c = new Ctx();
            var four = Requirement.Parse("plot:4");
            Assert.False(four.Eval(c));
            c.Tokens.Add("plotset:1");
            c.Tokens.Add("plotset:3");
            Assert.Equal(3, c.PlotClock());
            Assert.True(Requirement.Parse("plot:3").Eval(c));
            Assert.False(four.Eval(c));
            c.Tokens.Add("plotstep:miranda_tower");
            Assert.True(four.Eval(c));
            c.Tokens.Add("plotstep:l26_797");
            Assert.Equal(5, c.PlotClock());
            var missing = new List<string>();
            Requirement.Parse("plot:8").Missing(c, missing);
            Assert.Equal(new[] { "plot:8" }, missing.ToArray());
        }

        [Fact]
        public void A_test_reads_the_variables_as_the_trap_does()
        {
            // the sum of two variables, and three bits of each joined
            var sum = new StateTest { First = 200, Count = 1, Sum = true, Value = 6 };
            var vars = new Dictionary<int, int> { { 200, 4 }, { 201, 2 } };
            System.Func<int, int> get = i => { int v; return vars.TryGetValue(i, out v) ? v : 0; };
            Assert.True(sum.Equal(get));
            Assert.Equal("flag:state_var200_to_var201_sum_is_6", sum.Token);
            var digits = new StateTest { First = 200, Count = 1, Value = 4 << 3 | 2 };
            Assert.True(digits.Equal(get));
            var not = new StateTest { First = 0x100 + 109, Sum = true, Value = 0, Negated = true };
            Assert.Equal("flag:state_quest109_not_0", not.Token);
            Assert.False(not.Holds(i => 0));
            Assert.Equal("xclock1", StateTest.Name(StateRules.PlotVariable));
            Assert.Equal("qbyte3", StateTest.Name(0x183));
        }

        [Fact]
        public void Set_traps_grant_the_states_they_bring_about()
        {
            // four pick-ups each add one, the door asks for four: all of them
            var sets = new List<StateSet>();
            for (int i = 0; i < 4; i++) { var x = new StateSet { Level = 18, Trap = 867 + i, Variable = 2, Op = 0, Value = 1 }; x.Flags.Add("flag:variable_L18_" + i); sets.Add(x); }
            var four = new StateTest { First = 2, Sum = true, Value = 4 };
            var st = Assert.Single(StateRules.Derive(new[] { four }, sets));
            Assert.Equal("flag:variable_L18_0 & flag:variable_L18_1 & flag:variable_L18_2 & flag:variable_L18_3", st.Requires);
            // one of them is enough for "is 1", and a plain set to the value is enough alone
            var one = new StateTest { First = 2, Sum = true, Value = 1 };
            Assert.Equal(4, StateRules.Derive(new[] { one }, sets).Single().Requires.Split('|').Length);
            // a lever puzzle that counts: 0 then 1 gives 2 (do_math_op 7), on the level it lies on
            var lv = new List<StateSet>();
            foreach (int level in new[] { 51, 73 })
            {
                var a = new StateSet { Level = level, Trap = 742, Variable = 241, Op = 7, Value = 1 }; a.Flags.Add("flag:b" + level);
                var b = new StateSet { Level = level, Trap = 743, Variable = 241, Op = 7, Value = 0 }; b.Flags.Add("flag:a" + level);
                lv.Add(a); lv.Add(b);
            }
            var two = new StateTest { First = 241, Sum = true, Value = 2 };
            Assert.Equal("(flag:a51 & flag:b51) | (flag:a73 & flag:b73)", StateRules.Derive(new[] { two }, lv).Single().Requires);
            // nothing sets it: no source
            Assert.Null(StateRules.Derive(new[] { new StateTest { First = 0x100 + 112, Sum = true, Value = 1 } }, sets).Single().Requires);
            // the plot's own traps
            var plot = new List<StateSet>
            {
                new StateSet { Level = 14, Trap = 946, Variable = StateRules.PlotVariable, Op = 2, Value = 3 },
                new StateSet { Level = 26, Trap = 797, Variable = StateRules.PlotVariable, Op = 0, Value = 1 },
            };
            foreach (var p in plot) p.Flags.Add("flag:x" + p.Trap);
            var toks = StateRules.Derive(new StateTest[0], plot).Select(x => x.Token).ToArray();
            Assert.Equal(new[] { "plotset:3", "plotstep:l26_797" }, toks);
            // a quest flag is one bit: any value above 0 sets it, an xor toggles it
            var q = new Dictionary<int, int>();
            new StateSet { Variable = 0x100 + 104, Op = 0, Value = 1 }.Apply(q);
            Assert.Equal(1, q[0x100 + 104]);
            new StateSet { Variable = 0x100 + 104, Op = 5, Value = 0 }.Apply(q);
            Assert.Equal(0, q[0x100 + 104]);
        }

        [Fact]
        public void The_gem_links_level_5_to_the_worlds_by_the_plot()
        {
            var g = Graph(); if (g == null) return;
            var gem = g.Edges.Where(e => e.Notes.StartsWith("the blackrock gem")).ToList();
            Assert.NotEmpty(gem);
            // the gem closes its own square: no way starts on it, and none is made back to it from the worlds
            Assert.DoesNotContain(g.Edges, e => e.Kind == "reach" && g.Regions[e.To].Level == 5 && g.Regions[e.From].Level != 5);
            Assert.All(gem, e => Assert.Equal(5, g.Regions[e.From].Level));
            var by = gem.GroupBy(e => g.Regions[e.To].Level).ToDictionary(x => x.Key, x => x.Select(e => e.Requires).Distinct().Single());
            Assert.Equal(new[] { 9, 17, 25, 33, 41, 49, 57, 69 }, by.Keys.OrderBy(x => x).ToArray());
            Assert.Equal("free", by[9]);
            Assert.Equal("plot:4", by[17]);
            Assert.Equal("plot:4", by[25]);
            Assert.Equal("plot:8", by[33]);
            Assert.Equal("plot:8", by[41]);
            Assert.Equal("plot:8", by[57]);
            Assert.Equal("plot:13", by[49]);
            Assert.Equal("plot:13", by[69]);
            // the facet lands where black_gem_trip puts the player
            Assert.Contains(gem, e => e.To == g.RegionOf(9, 33, 32));
        }

        [Fact]
        public void The_levels_own_traps_move_the_plot()
        {
            var g = Graph(); if (g == null) return;
            var toks = g.States.Where(x => x.Token.StartsWith("plot")).ToDictionary(x => x.Token, x => x.Requires);
            Assert.Equal(new[] { "plotset:1", "plotset:2", "plotset:3", "plotstep:l26_797", "plotstep:l48_901" }, toks.Keys.OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
            Assert.Equal("flag:variable_L14_945", toks["plotset:3"]);
            // every flag named is a mechanism's
            var flags = new HashSet<string>(g.Mechanisms.Select(m => m.Flag));
            foreach (var st in g.States.Where(x => x.Requires != null))
                foreach (var f in st.Requires.Split(new[] { ' ', '(', ')', '|', '&' }, System.StringSplitOptions.RemoveEmptyEntries)) Assert.Contains(f, flags);
        }

        [Fact]
        public void A_chain_branches_on_its_condition_traps()
        {
            var g = Graph(); if (g == null) return;
            // level 68 (20, 25): variable 255 is 0 at the start, so one teleport is free and the other waits for the trap that sets it
            var from = g.Edges.Where(e => e.Level == 68 && e.X == 20 && e.Y == 25 && e.Trigger == 936).ToList();
            Assert.Contains(from, e => e.Requires == "free");
            Assert.Contains(from, e => e.Requires == "flag:state_var255_not_0");
            Assert.Equal("flag:variable_L68_934", g.States.Single(x => x.Token == "flag:state_var255_not_0").Requires);
            // level 18 (58, 52): the teleport to level 72 is on the chain that runs while variable 2 is not 4, as at the start
            Assert.Contains(g.Edges, e => e.Level == 18 && e.Trigger == 977 && g.Regions[e.To].Level == 72 && e.Requires == "free");
            // level 65 (29, 12): a skill trap that rolls (heading 1) may go either way, so both chains' teleports exist
            var skill = g.Edges.Where(e => e.Level == 65 && e.Trigger == 868 && g.Regions[e.To].Level == 69).Select(e => e.To).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new[] { g.RegionOf(69, 50, 19), g.RegionOf(69, 21, 35) }.OrderBy(x => x).ToList(), skill);
            // level 45's three levers together open the door by (12, 37)
            Assert.Contains(g.Edges, e => e.Level == 45 && e.Requires.Contains("flag:state_var160_is_7"));
            Assert.Equal(3, g.States.Single(x => x.Token == "flag:state_var160_is_7").Requires.Split('&').Length);
        }

        [Fact]
        public void Who_sets_a_trigger_off()
        {
            var g = Graph(); if (g == null) return;
            // level 1's move trigger #711 at (40, 38) has no flag 11: its chain never runs for the player
            Assert.Contains(g.Notes, n => n.StartsWith("L1 trigger #711"));
            Assert.All(g.Mechanisms.Where(m => m.Level == 1 && m.Trigger == 711), m => Assert.Equal("", m.SetOff));
            // a scheduled trigger waits for its schedule row
            var sched = g.Mechanisms.Where(m => (m.TriggerItem & 0xF) == 0xC && m.TriggerItem >= 0x1A0 && m.SetOff.Length > 0).ToList();
            Assert.NotEmpty(sched);
            Assert.All(sched, m => Assert.StartsWith("flag:schedule_l" + m.Level + "_", m.SetOff));
            Assert.Equal("flag:schedule_l50_38_32", NavGraph.ScheduleToken(50, 38, 32));
        }

        [Fact]
        public void An_airlock_is_not_a_change_of_the_world()
        {
            var g = Graph(); if (g == null) return;
            // the Prison Tower, level 10: opening the door at (32, 36) locks the door at (34, 36), closing it takes the lock off again
            var air = g.Mechanisms.Where(m => m.Airlock).ToList();
            Assert.Contains(air, m => m.Level == 10 && m.X == 34 && m.Y == 36);
            Assert.Contains(air, m => m.Level == 10 && m.X == 32 && m.Y == 36);
            Assert.All(air, m => Assert.Equal("door", m.Kind));
        }

        [Fact]
        public void The_shipped_facts_step_the_plot()
        {
            var f = FactSet.Shipped(true);
            Assert.Empty(f.Check());
            Assert.Equal("UW2", f.Game);
            Assert.Contains(f.Facts, x => x.Grants == "plotstep:miranda_tower" && x.Requires == "plot:3" && x.Level == 1);
            Assert.All(f.Facts, x => Assert.False(string.IsNullOrEmpty(x.Source)));
            // UW1's facts stay UW1's
            Assert.DoesNotContain(FactSet.Shipped(false).Facts, x => x.Grants.StartsWith("plot"));
        }

        [Fact]
        public void The_solve_goes_through_the_gem()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            var levels = new HashSet<int>(t.Solve.ZoneSphere.Keys.SelectMany(z => t.Set.LevelsOf(z)));
            // the Prison Tower by the first facet; Killorn Keep and the Ice Caverns once Miranda has heard of the tower
            foreach (int l in new[] { 5, 9, 14, 17, 25, 26 }) Assert.Contains(l, levels);
            // the tower's top is reached through level 10's airlock, and stays reached when a floor pad rises in Talorus
            Assert.Contains(16, levels);
            Assert.All(t.Changes.Where(c => c.Mechanism.Airlock), c => Assert.False(c.Applied));
            Assert.Empty(t.Solve.CutOff);
            Assert.Contains("plotset:3", t.Solve.TokenSphere.Keys);
            Assert.Contains("plotstep:miranda_tower", t.Solve.TokenSphere.Keys);
            Assert.True(t.Solve.TokenSphere["plotstep:miranda_tower"] >= t.Solve.TokenSphere["plotset:3"]);
            // the listener, the schedule and Miranda again bring the plot to 8: Talorus, the Academy and the Pits open
            Assert.Contains("plotstep:listener", t.Solve.TokenSphere.Keys);
            Assert.Contains("plotstep:miranda_7", t.Solve.TokenSphere.Keys);
            foreach (int l in new[] { 33, 41, 57 }) Assert.Contains(l, levels);
            // the Tomb and the Void wait for plot 13, which nothing known reaches yet
            Assert.DoesNotContain(49, levels);
            Assert.DoesNotContain(69, levels);
        }
    }
}
