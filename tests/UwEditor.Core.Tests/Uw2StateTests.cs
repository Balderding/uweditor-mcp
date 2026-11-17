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
            // the plot in the game's order (0.1.49): the set to 3 counts once the lower sets have gone off, a trap's step once the set to 3 has
            Assert.Equal("(flag:variable_L14_945) & plotset:1 & plotset:2", toks["plotset:3"]);
            Assert.Equal("(flag:variable_L26_796) & plotset:3", toks["plotstep:l26_797"]);
            // every trap flag named is a mechanism's, and every other flag is a state or a schedule row that is listed too
            var flags = new HashSet<string>(g.Mechanisms.Select(m => m.Flag));
            var listed = new HashSet<string>(g.States.Select(x => x.Token));
            foreach (var st in g.States.Where(x => x.Requires != null))
                foreach (var f in st.Requires.Split(new[] { ' ', '(', ')', '|', '&' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (f.StartsWith("flag:variable_")) Assert.Contains(f, flags);
                    else if (f.StartsWith("flag:state_") || f.StartsWith("flag:schedule_row_")) Assert.Contains(f, listed);
                }
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

        static UltimateEditor.Games.ScdRow Row(int time, int level, int ev, params int[] bytes)
        {
            var r = new UltimateEditor.Games.ScdRow { Time = time, Level = level, Event = ev };
            for (int i = 0; i < bytes.Length; i++) r.Bytes[5 + i] = (byte)bytes[i];
            return r;
        }

        [Fact]
        public void A_schedule_row_fires_its_triggers_when_its_test_passes()
        {
            var blocks = Enumerable.Range(0, 16).Select(i => new UltimateEditor.Games.ScdBlock()).ToList();
            // block 0, the time of day: a plain row is free
            blocks[0].Rows.Add(Row(3, 1, 5, 30, 44));
            // block 1, the castle plot: a row at time 8 needs the plot at 8
            blocks[1].Rows.Add(Row(8, 1, 5, 25, 40));
            // block 15: if quest flag 51 (variable 0x100 + 51) is 1, the disabled row after it runs; a test among the disabled rows adds its own
            blocks[15].Rows.Add(Row(0, 5, 10, 0x33, 0x01, 1, 0, 0, 1, 0));
            blocks[15].Rows.Add(Row(0, 5, -5, 18, 34));
            blocks[15].Rows.Add(Row(0, 5, -10, 0x3B, 0x01, 1, 0, 0, 1, 0));
            blocks[15].Rows.Add(Row(0, 5, -5, 17, 40));
            // a test the start passes (quest 60 is 0) costs nothing; a disabled row no test leads to never runs; a world's row is for each of its levels
            blocks[15].Rows.Add(Row(0, 0xF6 + 1, 10, 0x3C, 0x01, 1, 0, 0, 0, 0));
            blocks[15].Rows.Add(Row(0, 0xF6 + 1, -5, 5, 25));
            blocks[14].Rows.Add(Row(0, 57, -5, 30, 30));
            // a row that sets a quest flag is a set, granted with the row
            blocks[15].Rows.Add(Row(0, 14, 4, 3, 1));
            var tests = new Dictionary<string, StateTest>();
            var sets = new List<StateSet>();
            var states = new List<NavState>();
            StateRules.ReadSchedules(blocks, l => l == 1 || l == 5 || l == 9 || l == 10 || l == 57, tests, sets, states);
            var by = states.ToDictionary(x => x.Token, x => x.Requires);
            Assert.Equal("free", by["flag:schedule_l1_30_44"]);
            Assert.Equal("plot:8", by["flag:schedule_l1_25_40"]);
            Assert.Equal("flag:state_quest51_is_1", by["flag:schedule_l5_18_34"]);
            Assert.Equal("flag:state_quest51_is_1 & flag:state_quest59_is_1", by["flag:schedule_l5_17_40"]);
            Assert.Equal("free", by["flag:schedule_l9_5_25"]);
            Assert.Equal("free", by["flag:schedule_l10_5_25"]);
            Assert.DoesNotContain("flag:schedule_l57_30_30", by.Keys);
            Assert.Contains("flag:state_quest51_is_1", tests.Keys);
            var set = Assert.Single(sets);
            Assert.Equal(0x100 + 3, set.Variable);
            Assert.Equal("free", by[set.Flags[0]]);
        }

        [Fact]
        public void The_Voids_pyramid_leads_into_the_ring_after_the_four_trials()
        {
            var g = Graph(); if (g == null) return;
            // each coloured trial ends at the pyramid on level 69 (49, 51): levels 65, 66, 68 and 71 (event/WORLDEV.C do_qbert, a colour under 10)
            var ends = g.Edges.Where(e => e.Notes.StartsWith("the end of the Void's trial")).ToList();
            Assert.Equal(new[] { 65, 66, 68, 71 }, ends.Select(e => e.Level).Distinct().OrderBy(x => x).ToArray());
            int pyramid = g.RegionOf(69, 49, 51), ring = g.RegionOf(69, 32, 25);
            Assert.All(ends, e => Assert.Equal(pyramid, e.To));
            // with all four done the pyramid's moongate leads to (32, 25), inside the walled ring under the gem's platform
            var gate = Assert.Single(g.Edges.Where(e => e.Notes.StartsWith("the Void's pyramid turned the fifth")));
            Assert.Equal(pyramid, gate.From);
            Assert.Equal(ring, gate.To);
            foreach (var f in new[] { "flag:hack_L65_882", "flag:hack_L66_1019", "flag:hack_L68_956", "flag:hack_L71_991" }) Assert.Contains(f, gate.Requires);
            Assert.Equal(4, gate.Requires.Split('&').Length);
            Assert.NotEqual(ring, g.RegionOf(69, 32, 22));
            Assert.NotEqual(ring, g.RegionOf(69, 32, 28));     // the platform is its own
        }

        [Fact]
        public void The_Voids_gates_and_the_pyramids_rooms()
        {
            var g = Graph(); if (g == null) return;
            // a gate draws its destination from a table in the map's squares (hack 32, owner 10 to 19): each is a way
            var gates = g.Edges.Where(e => e.Notes.StartsWith("one of the Void's shifting gates")).ToList();
            Assert.NotEmpty(gates);
            Assert.All(gates, e => Assert.Equal("stairs", e.Kind));
            Assert.All(gates, e => Assert.Equal(69, e.Level));
            // table 14 holds the way to level 68 (2, 20), table 13 one to level 65 (26, 13)
            Assert.Contains(gates, e => e.To == g.RegionOf(68, 2, 20) && e.Notes.Contains("table 14"));
            Assert.Contains(gates, e => e.To == g.RegionOf(65, 26, 13) && e.Notes.Contains("table 13"));
            // the pyramid turned a colour leads to that colour's room at (4, 4 + 6 * colour): colour 6 from the start, the others after their trials
            int pyramid = g.RegionOf(69, 49, 51);
            var rooms = g.Edges.Where(e => e.Notes.StartsWith("the Void's pyramid turned colour")).ToList();
            Assert.Equal(5, rooms.Count);
            Assert.All(rooms, e => Assert.Equal(pyramid, e.From));
            Assert.Equal("free", rooms.Single(e => e.To == g.RegionOf(69, 4, 40)).Requires);
            Assert.Equal("flag:hack_L66_1019", rooms.Single(e => e.To == g.RegionOf(69, 4, 22)).Requires);
            Assert.Equal("flag:hack_L71_991", rooms.Single(e => e.To == g.RegionOf(69, 4, 4)).Requires);
            // they are kept when level 69 is built again on its own after a change
            Assert.All(rooms, e => Assert.Equal("stairs", e.Kind));
        }

        [Fact]
        public void A_pit_with_a_lift_and_a_fill_switch_is_left()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // level 46 (41, 52): as the level is, the square is a ledge and nothing is added
            var g = Graph();
            Assert.DoesNotContain(g.Edges, e => e.Notes.StartsWith("out of the pit"));
            // once the pit is open (its floor at 0), the low switch #907 throws the player up and the high one #968 fills it under him
            var m = s.LoadLevel(46);
            m.Tiles[41, 52].Height = 0;
            var open = NavGraph.Build(s, l => l == 46 ? m : s.LoadLevel(l), new NavOptions(), new[] { 46 });
            var way = Assert.Single(open.Edges.Where(e => e.Notes.StartsWith("out of the pit at (41, 52)")));
            Assert.Equal(open.RegionOf(46, 41, 52), way.From);
            Assert.Equal(open.RegionOf(46, 40, 52), way.To);
            Assert.Equal("free", way.Requires);
            Assert.Contains("#907", way.Notes);
            Assert.Contains("#968", way.Notes);
        }

        [Fact]
        public void The_games_schedules_are_read()
        {
            var g = Graph(); if (g == null) return;
            var by = g.States.Where(x => x.Token.StartsWith("flag:schedule_l")).ToDictionary(x => x.Token, x => x.Requires);
            // the castle's doors close by the time of day; Lady Tory's murder comes at plot 8; level 5's wall opens once quest flag 51 is set on level 25
            Assert.Equal("free", by["flag:schedule_l1_30_44"]);
            Assert.Equal("plot:8", by["flag:schedule_l1_25_40"]);
            Assert.Equal("flag:state_quest51_is_1", by["flag:schedule_l5_18_34"]);
            Assert.Equal("flag:variable_L25_844", g.States.Single(x => x.Token == "flag:state_quest51_is_1").Requires);
            // the plot's own row is a step of the facts, not a set
            Assert.DoesNotContain(g.States, x => x.Token.StartsWith("plotset:7"));
            Assert.DoesNotContain(g.Notes, n => n.Contains("SCD.ARK"));
        }

        [Fact]
        public void The_shipped_facts_step_the_plot()
        {
            var f = FactSet.Shipped(true);
            Assert.Empty(f.Check());
            Assert.Equal("UW2", f.Game);
            Assert.Contains(f.Facts, x => x.Grants == "plotstep:miranda_tower" && x.Requires == "plot:3 & flag:lord_british_speech" && x.Level == 1);
            // the castle's people talk only after Lord British's opening speech (quest 109, conversation 142), granted at his object
            Assert.Contains(f.Facts, x => x.Id == "lord_british_speech" && x.Grants == "flag:lord_british_speech" && x.Level == 1 && x.Object == 235);
            Assert.Contains(f.Facts, x => x.Kind == "trade" && x.Token == "npc:nystul" && x.Requires == "flag:lord_british_speech");
            Assert.All(f.Facts, x => Assert.False(string.IsNullOrEmpty(x.Source)));
            // UW1's facts stay UW1's
            Assert.DoesNotContain(FactSet.Shipped(false).Facts, x => x.Grants.StartsWith("plot"));
        }

        [Fact]
        public void The_solve_goes_through_the_gem()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions());
            var levels = new HashSet<int>(t.Solve.ZoneSphere.Keys.SelectMany(z => t.Set.LevelsOf(z)));
            // the Prison Tower by the first facet; Killorn Keep and the Ice Caverns once Miranda has heard of the tower
            foreach (int l in new[] { 5, 9, 14, 17, 25, 26 }) Assert.Contains(l, levels);
            // the tower's top is reached through level 10's airlock, and stays reached when a floor pad rises in Talorus
            Assert.Contains(16, levels);
            Assert.All(t.Changes.Where(c => c.Mechanism.Airlock), c => Assert.False(c.Applied));            Assert.Contains("plotset:3", t.Solve.TokenSphere.Keys);
            Assert.Contains("plotstep:miranda_tower", t.Solve.TokenSphere.Keys);
            Assert.True(t.Solve.TokenSphere["plotstep:miranda_tower"] >= t.Solve.TokenSphere["plotset:3"]);
            // the listener, the schedule and Miranda again bring the plot to 8: Talorus, the Academy and the Pits open
            Assert.Contains("plotstep:listener", t.Solve.TokenSphere.Keys);
            Assert.Contains("plotstep:miranda_7", t.Solve.TokenSphere.Keys);
            foreach (int l in new[] { 33, 41, 57 }) Assert.Contains(l, levels);
            // Zoranthus, the gems of the Academy and of Talorus (treated by Nystul and fused), Miranda at 11 and Patterson's death bring it to 13:
            // the Tomb and the Void open
            Assert.Contains("plotstep:gem_4", t.Solve.TokenSphere.Keys);
            Assert.Contains("plotstep:patterson", t.Solve.TokenSphere.Keys);
            Assert.True(t.Solve.TokenSphere["plotstep:patterson"] <= t.Solve.TokenSphere["plotstep:djinn"]);
            Assert.Contains(49, levels);
            Assert.Contains(69, levels);
            // the Void's key gem: thrown down from its platform in a dream (a dream plant), or taken with Levitate and Telekinesis
            var gem8 = t.Set.Placements.Where(p => p.Grants == "item:key_gem_8").ToList();
            Assert.Equal(2, gem8.Count);
            // awake from outside the ring: up to the platform, then Telekinesis or Portal
            var awake = gem8.Single(p => p.Requires.Contains("spell:levitate"));
            Assert.Equal("spell:levitate & (spell:telekinesis | spell:portal)", awake.Requires);
            Assert.Equal(t.Set.ZoneAt(69, 32, 22), awake.Zone);
            // thrown down in a dream: it lies in the walled ring under the platform, to be picked up there awake
            var dream = gem8.Single(p => p.Requires == "item:dream_plant");
            Assert.Equal(t.Set.ZoneAt(69, 30, 26), dream.Zone);
            Assert.NotEqual(awake.Zone, dream.Zone);
            Assert.Contains(t.Set.Placements, p => p.Grants == "item:dream_plant" && p.Notes.Contains("level 4 "));
            Assert.Contains("item:key_gem_8", t.Solve.TokenSphere.Keys);
            // the schedule's rows fire their triggers as the plot moves
            Assert.Contains("flag:schedule_l1_25_40", t.Solve.TokenSphere.Keys);
            // nothing the solve sets off cuts the player off: the Academy's pit on level 46 is left by its switches
            Assert.Empty(t.Solve.CutOff);
            // the Void's other levels are reached through its shifting gates
            foreach (int l in new[] { 65, 66, 67, 68, 71, 72 }) Assert.Contains(l, levels);
            Assert.True(t.Solve.ZoneSphere.Count > 330, t.Solve.ZoneSphere.Count + " zones");
            // the Ice Caverns' north: level 25's maze set for the door, the pull chain at (22, 7), the schedule's wall on level 26
            foreach (var tok in new[] { "flag:variable_l25_899", "flag:state_quest44_is_1", "flag:schedule_l26_33_33", "flag:terrain_l26_1021" }) Assert.Contains(tok, t.Solve.TokenSphere.Keys);
            Assert.Contains(t.Set.ZoneAt(26, 19, 49), t.Solve.ZoneSphere.Keys);
            // and none of it by the Portal spell, which is off unless asked for
            Assert.DoesNotContain(t.Set.Links, k => k.Requires.Contains("spell:portal"));
        }

        [Fact]
        public void The_Academys_falling_platforms_are_left_to_the_player()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            // no spells from the runes: scrolls, wands and potions only
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions { Magic = false, SingleUseItems = true });
            // level 47: the two platforms whose triggers drop (34, 38), the square before the ledge of the way on
            foreach (var flag in new[] { "flag:terrain_l47_987", "flag:terrain_l47_1002" })
            {
                var c = t.Changes.Single(x => x.Flag == flag);
                Assert.False(c.Applied, flag);
                Assert.Equal("closes", c.Effect);         // the ledge's own ways are no new ways
            }
            // the others change no way alone; those that would strand the ledge after the rest are left too
            var left = t.Changes.Where(x => x.Level == 47 && x.Effect == "closes with others").ToList();
            Assert.NotEmpty(left);
            Assert.All(left, x => Assert.False(x.Applied));
            Assert.Contains(t.Changes, x => x.Level == 47 && x.Mechanism.Kind == "terrain" && x.Applied && x.Effect == "no way changed");
            // squares opened that were no zone's take nothing away (level 51's wall at (38, 22))
            Assert.Equal("opens", t.Changes.Single(x => x.Flag == "flag:terrain_l51_979").Effect);
            // so the Academy's last level and the worlds after it are reached with no Levitate, and nothing cuts the player off
            Assert.Contains(t.Solve.ZoneSphere.Keys, z => z < 1000000 && t.Set.LevelsOf(z).Contains(48));
            Assert.Contains(t.Solve.ZoneSphere.Keys, z => z < 1000000 && t.Set.LevelsOf(z).Contains(51));
            Assert.Empty(t.Solve.CutOff);
            // the whole game, with no spell from the runes
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
        }

        [Fact]
        public void Mors_Gothas_book_waits_for_her_and_her_door_is_bashed()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            var m = s.LoadLevel(17);
            // the book lies invisible on the square where a scheduled trigger creates her; another on the same square shows it (hack 39)
            Assert.True(m.Objects[818].Invisible);
            Assert.Equal(0x138, m.Objects[818].Item);
            Assert.Equal(818, m.Objects[834].Link);
            Assert.Equal(39, m.Objects[834].Quality);
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions { Magic = false, SingleUseItems = true });
            var fact = t.Abilities.Facts.Facts.Single(f => f.Id == "mors_gotha_book");
            Assert.Contains("flag:hack_L17_819", fact.Requires);
            Assert.Contains("flag:create_L17_861", fact.Requires);
            Assert.True(t.Solve.TokenSphere["item:mors_gotha_book"] >= t.Solve.TokenSphere["flag:hack_l17_819"]);
            // her room's door, (21, 41): no key fits; picked, or bashed, since in UW2 id bit 13 is a door's direction and not "indestructible"
            string notes;
            var door = m.Objects.Single(o => !o.Free && o.OnTile && o.TileX == 21 && o.TileY == 41 && o.Item >= 0x140 && o.Item <= 0x147);
            Assert.NotEqual(0, door.Record[1] & 0x20);
            Assert.Equal("pick:3 | bash", ZoneDoors.Requirement(s, m, door, out notes));
            // no scheduled trigger of the level acts on a door or a lock
            Assert.DoesNotContain(t.Abilities.Auto.Graph.Mechanisms, x => x.Level == 17 && x.Kind == "door");
            var locks = new HashSet<int>(m.Objects.Where(o => !o.Free && o.Item == 0x10F).Select(o => o.Index));
            Assert.DoesNotContain(m.Objects, o => !o.Free && o.IsTrapOrTrigger && !o.IsQuantity && locks.Contains(o.Link));
        }

        [Fact]
        public void A_weapon_of_Entry_is_a_lasting_source_of_Open_and_the_search_finds_it_by_that_name()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            // the Tomb's cudgel, level 57 #484: class 12 effect 13, special weapon 6, which opens the door it hits (combat/COMBAT.C player_attack)
            var o = s.LoadLevel(57).Objects[484];
            Assert.Equal(0x007, o.Item);
            Assert.Equal(0xCD, o.Link & 0x1FF);
            var hits = LevelSearch.Run(s, new ObjectQuery { Text = "entry" });
            Assert.Contains(hits, h => h.Level == 57 && h.Index == 484 && h.Matched == "Entry");
            // only iron flesh among the scrolls and potions: the cudgel still counts, as a thing kept
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions { Magic = false, SingleUseItems = true, SingleUseOnly = new HashSet<string> { "iron_flesh" } });
            var src = t.Abilities.Sources.Single(x => x.Level == 57 && x.Index == 484);
            Assert.Equal("open", src.Ability);
            Assert.True(src.Permanent);
            Assert.Equal("Entry", src.Spell);
            Assert.Contains("perm:open", t.Solve.TokenSphere.Keys);
            Assert.DoesNotContain(t.Solve.TokenSphere.Keys, k => k.StartsWith("once:") && k != "once:iron_flesh");
            Assert.All(t.Solve.OnceSpent, x => Assert.Equal("once:iron_flesh", x));
            // a pure fighter ends the game: no rune magic, and of the scrolls and potions only Iron Flesh for the djinn
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
            Assert.Empty(t.Solve.CutOff);
            Assert.Equal(new[] { "once:iron_flesh" }, t.Solve.OnceSpent.ToArray());
            // Talorus: the Historian sends the Avatar to the island of the key gem once the new Ductosnore is made, and the brass plate's bridge leads off it
            var facts = t.Abilities.Facts.Facts;
            var island = facts.Single(f => f.Id == "talorus_historian_island");
            Assert.Equal("link", island.Kind);
            Assert.Equal("flag:talorus_new_ductosnore", island.Requires);
            Assert.Single(facts, f => f.Id == "talorus_island_bridge");
            int isle = t.Set.ZoneAt(34, 31, 35);
            Assert.Contains(isle, t.Solve.ZoneSphere.Keys);
            Assert.Contains("item:key_gem_4", t.Solve.TokenSphere.Keys);
            // the Void's key gem by the dream plant, and the Ice Caverns' line past the stream the schedule opens
            Assert.Contains("item:key_gem_8", t.Solve.TokenSphere.Keys);
            Assert.Contains("flag:line_3", t.Solve.TokenSphere.Keys);
        }

        [Fact]
        public void A_stream_one_square_wide_is_jumped_over()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // level 26 once the schedule has opened (33, 33) to (33, 36): water flowing east, with ice to its east and the floor of the north to its west
            var m = s.LoadLevel(26);
            for (int y = 33; y <= 36; y++) { var tile = m.Tiles[33, y]; tile.Type = TileType.Open; tile.Height = 4; tile.FloorIndex = 11; }
            var g = NavGraph.Build(s, l => l == 26 ? m : s.LoadLevel(l), new NavOptions(), new[] { 26 });
            int ice = g.RegionOf(26, 34, 34), north = g.RegionOf(26, 32, 34), stream = g.RegionOf(26, 33, 34);
            Assert.True(ice >= 0 && north >= 0 && stream >= 0);
            Assert.NotEqual(ice, north);
            // walking in against the current asks for more
            Assert.Contains(g.Edges, e => e.From == ice && e.To == stream && e.Requires.Contains("spell:waterwalk"));
            // the jump over it asks for nothing
            var jump = g.Edges.Single(e => e.From == ice && e.To == north && e.Kind == "jump");
            Assert.Equal("free", jump.Requires);
            Assert.Contains("over 1 tile", jump.Notes);
        }

        [Fact]
        public void A_move_trigger_in_a_wall_is_set_off_from_the_square_beyond_its_edge()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var g = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), new[] { 1, 2, 5 });
            // the sewers' wall at level 5 (17, 22) is walked through both ways: a trigger at each edge, each sending to the far side
            int west = g.RegionOf(5, 16, 22), east = g.RegionOf(5, 18, 22);
            Assert.True(west >= 0 && east >= 0 && west != east);
            Assert.Contains(g.Edges, e => e.Level == 5 && e.From == east && e.To == west && e.Kind == "teleport" && e.Trigger == 863);
            Assert.Contains(g.Edges, e => e.Level == 5 && e.From == west && e.To == east && e.Kind == "teleport" && e.Trigger == 865);
            Assert.DoesNotContain(g.Edges, e => e.Level == 5 && e.Trigger == 863 && e.From != east);
            // the wall at the end of the passage at level 1 (37, 40) leads down to Nystul's hidden room, not the cell beside it
            int passage = g.RegionOf(1, 37, 40), cell = g.RegionOf(1, 38, 39), room = g.RegionOf(2, 11, 42);
            Assert.True(passage >= 0 && cell >= 0 && room >= 0 && passage != cell);
            Assert.Contains(g.Edges, e => e.Level == 1 && e.Trigger == 667 && e.From == passage && e.To == room);
            Assert.DoesNotContain(g.Edges, e => e.Level == 1 && e.Trigger == 667 && e.From == cell);
            // and the way back lands in that passage's doorway, not in the cell
            Assert.Contains(g.Edges, e => e.Level == 2 && e.Trigger == 1011 && e.From == room && e.To == passage);
            Assert.DoesNotContain(g.Edges, e => e.Level == 2 && e.Trigger == 1011 && e.To == cell);
        }

        [Fact]
        public void No_spell_is_cast_from_runes_on_the_Academys_middle_levels()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions());
            // Bishop's cell is entered with fraznium on, and Altara's dagger waits for Bishop or Mystell
            var field = t.Facts.Facts.Single(f => f.Id == "bishop_force_field");
            Assert.Contains("item:fraznium_gauntlets", field.Requires);
            Assert.Contains(t.Set.ZoneAt(16, 27, 33), t.Solve.ZoneSphere.Keys);
            Assert.Contains("npc:bishop", t.Solve.TokenSphere.Keys);
            Assert.Equal("npc:bishop | npc:mystell", t.Facts.Facts.Single(f => f.Id == "altara_dagger").Requires);
            Assert.Empty(t.Abilities.Warnings.Where(w => w.StartsWith("Facts:")));
            var no = t.Facts.OfKind("nomana").Select(f => f.Level).OrderBy(l => l).ToArray();
            Assert.Equal(new[] { 42, 43, 44, 45, 46, 47, 48 }, no);
            // level 48's only west of x 25 (combat/SPELLS.C restore_mana)
            Assert.Equal(new[] { 0, 0, 24, 63 }, t.Facts.Facts.Single(f => f.Id == "academy_no_casting_48").Rect);
            Assert.True(t.Abilities.NoManaEdges > 0);
            // a way of those levels that asks for a spell takes it from a thing used, never from the runes; the first and last levels are as elsewhere
            var links = t.Set.Links.Where(k => k.Requires.Contains("cast:") || k.Requires.Contains("use:") || k.Requires.Contains("spell:")).ToList();
            Assert.Contains(links, k => k.Level >= 42 && k.Level <= 47);
            Assert.All(links.Where(k => k.Level >= 42 && k.Level <= 47 && k.Requires.Contains("cast:")), k => Assert.Contains("flag:__never__", k.Requires));
            Assert.DoesNotContain(links, k => (k.Level == 41 || (k.Level == 48 && k.X >= 25)) && k.Requires.Contains("flag:__never__"));
            // and the game is still ended
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
        }

        [Fact]
        public void A_thing_that_holds_Open_opens_a_lock_with_the_runes_off()
        {
            var held = new HashSet<string>();
            System.Func<bool, bool, bool> opens = (magic, once) =>
                Requirement.Parse("pick:3").Eval(new AbilityZones.FixedContext(new SolverOptions { AllowBash = true, LockpickSkill = -1, AbilitySources = true, Magic = magic, SingleUseItems = once }, held));
            Assert.False(opens(true, true));
            held.Add("cast:open");                  // the runes
            Assert.True(opens(true, false));
            Assert.False(opens(false, true));
            held.Clear(); held.Add("perm:open");    // a weapon or wand that holds it
            Assert.True(opens(false, false));
            held.Clear(); held.Add("once:open");    // a scroll
            Assert.True(opens(false, true));
            Assert.False(opens(false, false));
        }

        [Fact]
        public void The_solve_reaches_the_end_of_the_game()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = TestWorld.Session("UW2");
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions());
            var got = t.Solve.TokenSphere;
            Assert.Empty(t.Abilities.Warnings.Where(w => w.StartsWith("Facts:")));
            // the djinn: basilisk oil into the mud of level 26, the pool's trigger, lava, Iron Flesh, the bottle broken in the Void
            Assert.Contains(t.Set.Placements, p => p.Grants == "flag:state_xclock3_low3_is_2" && p.Requires == "item:basilisk_oil" && t.Set.LevelsOf(p.Zone).Contains(26));
            foreach (var tok in new[] { "item:basilisk_oil", "flag:variable_l26_969", "flag:djinn_baked", "flag:djinn_glazed", "item:bottle", "plotstep:djinn" }) Assert.Contains(tok, got.Keys);
            Assert.True(got["flag:djinn_baked"] >= got["flag:variable_l26_969"]);
            Assert.Equal("iron_flesh", Abilities.OfSpell(true, 59));
            Assert.Contains(t.Set.Placements, p => p.Grants == "cast:iron_flesh");
            // the bottle lies in the pyramid's room of colour 3, opened by the trial of level 66
            var djinn = t.Set.Placements.Single(p => p.Grants == "plotstep:djinn");
            Assert.Equal(t.Set.NearestZone(69, 21, 52), djinn.Zone);
            Assert.Contains("plot:13", djinn.Requires);
            // the lines of power: Altara's rod from its parts once the Listener is dead, used by the marker of each world
            Assert.Contains("item:pearl_tipped_rod", got.Keys);
            Assert.True(got["item:pearl_tipped_rod"] >= got["plotstep:listener"]);
            for (int w = 1; w <= 8; w++) Assert.Contains("flag:line_" + w, got.Keys);
            Assert.Equal(new[] { 16, 18, 26, 33, 48, 49, 57, 69 }, Enumerable.Range(1, 8).Select(w => t.Set.LevelsOf(t.Set.Placements.Single(p => p.Grants == "flag:line_" + w).Zone).First()).ToArray());
            // the eight gems, the book, the horn: Nystul at 14, the hour at 15, the ceremony at 16
            for (int n = 1; n <= 8; n++) Assert.Contains("item:key_gem_" + n, got.Keys);
            foreach (var tok in new[] { "flag:gems_fused", "flag:lines_cut", "item:mors_gotha_book", "item:horn", "plotstep:nystul_book", "plotstep:nystul_hour" }) Assert.Contains(tok, got.Keys);
            Assert.True(got["plotstep:nystul_book"] >= got["plotstep:djinn"]);
            // the bottle: Zoranthus's, or the one in the Void pyramid's room
            Assert.Contains(t.Set.Placements, p => p.Grants == "item:bottle" && t.Set.LevelsOf(p.Zone).Contains(58));
            Assert.Contains(t.Set.Placements, p => p.Grants == "item:bottle" && t.Set.LevelsOf(p.Zone).Contains(69));
            // the Academy's lava room is crossed over its pillars with the level's own wand, not through the lava
            var pillars = t.Abilities.Facts.Facts.Single(f => f.Id == "academy_pillars");
            Assert.Equal("link", pillars.Kind);
            Assert.Contains("flag:telekinesis_on_l43", pillars.Requires);
            Assert.DoesNotContain("lava", pillars.Requires);
            Assert.Equal(16, Requirement.PlotClock(got.Keys));
            Assert.True(t.Solve.GoalReached);
            Assert.Empty(t.Solve.GoalMissing);
        }
    }
}
