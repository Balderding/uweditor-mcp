using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UltimateEditor.Games;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The key item scatter's walkthrough (Walkthrough, read from the solve's events): it ends at
    /// the goal, every step needs only what earlier steps gave, every key item it uses is picked
    /// up first (at the place the shuffle put it), and the unshuffled game and UW2 do not break it.
    /// </summary>
    public class WalkthroughTests
    {
        readonly ITestOutputHelper output;
        public WalkthroughTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession session;
        static readonly Dictionary<string, KeyShuffleResult> runs = new Dictionary<string, KeyShuffleResult>();

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW");
                if (g == null) return null;
                session = GameSession.Open(g);
                return session;
            }
        }

        static KeyShuffleResult Run(string name, int seed)
        {
            lock (gate)
            {
                string key = name + "|" + seed;
                KeyShuffleResult r;
                if (runs.TryGetValue(key, out r)) return r;
                var o = new KeyShuffleOptions { Seed = seed };
                if (name == "picking on, killing NPCs, orb") { o.Logic.LockPicking = true; o.Logic.KillNpcs = true; o.Logic.RequireOrb = true; }
                if (name == "creatures, magic off, single-use") { o.CreatureInventories = true; o.Logic.Magic = false; o.Logic.SingleUseItems = true; }
                r = KeyShuffle.Run(Session(), null, o);
                runs[key] = r;
                return r;
            }
        }

        public static IEnumerable<object[]> Sets()
        {
            yield return new object[] { "defaults", 1 };
            yield return new object[] { "defaults", 77 };
            yield return new object[] { "picking on, killing NPCs, orb", 13 };
            yield return new object[] { "creatures, magic off, single-use", 9 };
        }

        /// <summary>The world the steps have given so far: the tokens, the zones, the counts.</summary>
        sealed class Replay : IRequirementContext, INowContext
        {
            public SolverOptions Options { get; set; }
            public readonly HashSet<string> Tokens = new HashSet<string>();
            public readonly HashSet<int> Zones = new HashSet<int>();
            public readonly Dictionary<string, int> Counted = new Dictionary<string, int>();
            public bool HasToken(string t) { return Tokens.Contains(Requirement.Normalize(t)); }
            public bool ZoneReachable(int z) { return Zones.Contains(z); }
            public bool ZoneNow(int z) { return Zones.Contains(z); }
            public int TalismanCount() { return Tokens.Count(t => t.StartsWith("talisman:")); }
            public int PlotClock() { return Requirement.PlotClock(Tokens); }
        }

        /// <summary>
        /// Plays the steps in order with nothing but what the steps give: each event's zone must
        /// be reached already, each link's zone of departure too, and each requirement must hold.
        /// Returns what fails.
        /// </summary>
        static List<string> Check(TimelineResult t, Walkthrough w)
        {
            var bad = new List<string>();
            var so = t.Solve != null && t.Solve.Options != null ? t.Solve.Options : new SolverOptions();
            var ctx = new Replay { Options = so };
            foreach (var s in so.StartTokens) ctx.Tokens.Add(Requirement.Normalize(s));
            var counts = t.Facts.OfKind("count").GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var st in w.Steps)
                foreach (var e in st.Events)
                {
                    string at = "step " + st.Number + " (" + e + ")";
                    if (e.Kind == "zone")
                    {
                        if (ctx.Zones.Count == 0) { ctx.Zones.Add(e.Zone); continue; }      // the start
                        if (e.From >= 0 && !ctx.Zones.Contains(e.From)) bad.Add(at + ": comes from zone " + e.From + ", not reached yet");
                        if (e.Warp != null && e.Warp.FromLevel > 0 && !ctx.Zones.Any(z => t.Set.LevelsOf(z).Contains(e.Warp.FromLevel))) bad.Add(at + ": a warp from level " + e.Warp.FromLevel + ", not reached yet");
                        foreach (var f in e.Changes.Where(st.Uses.Contains)) if (!ctx.Tokens.Contains(f)) bad.Add(at + ": needs the change " + f + ", not set off yet");
                    }
                    else if (e.Kind != "flag" && e.Zone != t.Set.StartZone && !ctx.Zones.Contains(e.Zone)) bad.Add(at + ": lies in zone " + e.Zone + ", not reached yet");
                    if (e.Requires.StartsWith("count:"))
                    {
                        string id = e.Requires.Substring(6);
                        int n; ctx.Counted.TryGetValue(id, out n);
                        Fact cf;
                        if (counts.TryGetValue(id, out cf) && n < cf.AtLeast) bad.Add(at + ": only " + n + " of " + cf.AtLeast + " counted for " + id);
                    }
                    else
                    {
                        var r = Requirement.TryParse(string.IsNullOrWhiteSpace(e.Requires) ? "free" : e.Requires, out _);
                        if (r == null) bad.Add(at + ": cannot read " + e.Requires);
                        else if (!r.Eval(ctx)) { var miss = new List<string>(); r.Missing(ctx, miss); bad.Add(at + ": needs " + e.Requires + ", missing " + string.Join(", ", miss)); }
                    }
                    if (e.Kind == "zone") ctx.Zones.Add(e.Zone);
                    else if (e.Kind == "token" || e.Kind == "flag") ctx.Tokens.Add(e.Token);
                    else if (e.Kind == "count") { int n; ctx.Counted.TryGetValue(e.Token, out n); ctx.Counted[e.Token] = n + (e.Placement != null ? e.Placement.Quantity : 0); }
                }
            // and the goal at the end
            var goal = w.Steps.LastOrDefault();
            if (goal == null || goal.Kind != "goal") bad.Add("the last step is not the goal");
            if (!ctx.Zones.Contains(t.Solve.GoalAt)) bad.Add("the goal's zone " + t.Solve.GoalAt + " is never reached");
            var greq = Requirement.Parse(t.Solve.GoalBy.StartsWith("the goal zone") ? t.GoalFor(so) : t.EndFor(so));
            if (!greq.Eval(ctx)) bad.Add("the goal's requirement " + greq + " does not hold at the end");
            return bad;
        }

        static void Show(ITestOutputHelper o, Walkthrough w, int head = 25, int tail = 5)
        {
            var lines = w.Lines();
            o.WriteLine(w.Steps.Count + " steps; doubts: " + (w.Doubts.Count == 0 ? "none" : string.Join(" | ", w.Doubts.Distinct())));
            foreach (var l in lines) o.WriteLine(l);
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void A_shuffled_seed_walks_to_the_goal(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, seed);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            var w = r.Walkthrough;
            Assert.NotNull(w);
            Show(output, w);
            Assert.True(w.Reached);
            Assert.Empty(w.Doubts);
            Assert.Equal("start", w.Steps[0].Kind);
            Assert.Equal("goal", w.Steps.Last().Kind);
            Assert.Equal(Enumerable.Range(1, w.Steps.Count), w.Steps.Select(x => x.Number));
            // the steps keep the solve's spheres in order, and each relies only on earlier steps
            for (int i = 1; i < w.Steps.Count - 1; i++) Assert.True(w.Steps[i].Sphere >= w.Steps[i - 1].Sphere, "step " + w.Steps[i].Number + " goes back a sphere");
            Assert.All(w.Steps, x => Assert.True(x.After.All(k => k < x.Number), "step " + x.Number + " relies on a later step"));
            // far fewer steps than the solve found: only what the goal needs
            Assert.True(w.Steps.Count < r.Check.Events.Count / 3, w.Steps.Count + " steps of " + r.Check.Events.Count + " events");
            // the log carries it
            string log = r.Log.ToString();
            Assert.Contains("WALKTHROUGH", log);
            Assert.Contains("  1. Start at L1 (", log);
            Assert.Contains(w.Steps.Last().Number + ". GOAL: ", log);
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void Every_step_needs_only_what_earlier_steps_gave(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, seed);
            Assert.True(r.Success);
            var bad = Check(r.CheckBuild, r.Walkthrough);
            foreach (var b in bad) output.WriteLine(b);
            Assert.Empty(bad);
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void Every_key_item_used_is_picked_up_first_where_the_shuffle_put_it(string name, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(name, seed);
            Assert.True(r.Success);
            var w = r.Walkthrough;
            // the token each step gains, and where
            var gained = new Dictionary<string, WalkStep>();
            foreach (var st in w.Steps)
            {
                foreach (var u in st.Uses)
                {
                    if (u.StartsWith("talismans:")) continue;
                    Assert.True(gained.ContainsKey(u), "step " + st.Number + " uses " + u + " before any step gives it");
                }
                if (st.Event != null && (st.Event.Kind == "token" || st.Event.Kind == "flag") && !gained.ContainsKey(st.Event.Token)) gained[st.Event.Token] = st;
            }
            // every key item the route picks up from the levels lies where the shuffle put it (or where it stayed)
            var placed = new Regex(@"^#(\d+) .*?, level (\d+) \((\d+), (\d+)\)");
            var keyTokens = new HashSet<string>(r.Moves.Where(mv => mv.ByFill).SelectMany(mv => mv.Item.Gives).Select(g => Requirement.Normalize(g.Grants)).Where(g => g.Length > 0));
            int items = 0, moved = 0;
            foreach (var st in w.Steps.Where(x => x.Event != null && x.Event.Kind == "token" && keyTokens.Contains(x.Event.Token)))
            {
                var m = placed.Match(st.Event.Placement?.Notes ?? "");
                if (!m.Success || st.Event.Placement.Source != "levels") continue;
                string u = st.Event.Token;
                int index = int.Parse(m.Groups[1].Value), level = int.Parse(m.Groups[2].Value), x = int.Parse(m.Groups[3].Value), y = int.Parse(m.Groups[4].Value);
                // the object slot the step takes from: if the shuffle filled it, with an item that gives this token, on that square
                var mv = r.Moves.FirstOrDefault(k => k.Place.Level == level && k.Place.Index == index);
                items++;
                if (mv == null) continue;
                Assert.True(mv.Item.Gives.Any(g => Requirement.Normalize(g.Grants) == u), "step " + st.Number + ": " + u + " is taken from #" + index + " on L" + level + ", where the shuffle put " + mv.Item.Name + " [" + mv.Item.Tokens + "]");
                Assert.True(mv.Place.X == x && mv.Place.Y == y, "step " + st.Number + ": " + u + " at (" + x + ", " + y + "), the shuffle's place is " + mv.Place.Where);
                if (mv.Item != mv.Place) moved++;
            }
            // and the keys the route opens doors with are among them, picked up before
            int keys = w.Steps.SelectMany(x => x.Uses).Distinct().Count(u => u.StartsWith("key:"));
            output.WriteLine(items + " key item pick-ups from the levels (" + moved + " at a place the shuffle chose); " + keys + " keys used by the route, each picked up first");
            Assert.True(moved > 5);
            Assert.True(keys > 0);
        }

        [Fact]
        public void Uw2_unshuffled_has_a_walkthrough_to_the_end()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
            Assert.True(t.Solve.GoalReached);
            var w = Walkthrough.Build(t, t.Solve, null, l => s.LoadLevel(l));
            Assert.True(w.Reached);
            Assert.Empty(w.Doubts);
            // a way between every two steps: what the hand reaches from afar (the large blackrock gem's square) is not stood in
            Assert.Empty(w.NoWay);
            Assert.True(w.ReverseRan);
            Assert.Empty(w.ReverseProblems);
            var text = string.Join(" ", w.Lines());
            Assert.Contains("plotstep:djinn", text);
            Assert.Contains("plotstep:nystul_hour", text);
            Assert.Contains("flag:lines_cut", text);
        }

        [Fact]
        public void The_unshuffled_game_has_a_walkthrough()
        {
            var s = Session(); if (s == null) return;
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
            Assert.True(t.Solve.GoalReached);
            var w = Walkthrough.Build(t, t.Solve, null, l => s.LoadLevel(l));
            Show(output, w);
            Assert.True(w.Reached);
            Assert.Empty(w.Doubts);
            var bad = Check(t, w);
            Assert.Empty(bad);
            var text = string.Join("\n", w.Lines());
            // the talismans and the ritual, the stairs, and a reason with the step it refers to
            Assert.Contains("Take the stairs at L1 (", text);
            Assert.Contains("talismans:8", text);
            Assert.Matches(@"met by .* \(step \d+\)", text);
            // the unshuffled Key of Love: Judy and the picture of Tom
            Assert.Contains(w.Steps, x => x.Event != null && x.Event.Token == "item:key_of_love");
        }

        [Fact]
        public void Lock_picking_used_by_the_logic_is_named()
        {
            var s = Session(); if (s == null) return;
            // no bashing in the logic: a door the logic opens with lock picking says so
            var o = new AbilityOptions { LockPicking = true, Magic = false };
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), o, null, null, false);
            var so = Timeline.SolverFor(t, o, null);
            so.AllowBash = false;
            var sv = t.Engine.Run(so, o, "no bashing", null);
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            var w = Walkthrough.Build(t, sv, null, l => s.LoadLevel(l));
            Assert.True(w.Reached);
            Assert.Empty(w.Doubts);
            Assert.Contains(w.Steps, x => x.Options.Contains("lock picking") && x.Why.Contains("pick the lock (difficulty"));
            Assert.DoesNotContain(w.Steps, x => x.Options.Contains("bashing"));
        }

        [Fact]
        public void Uw2_does_not_break_it()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
            var w = Walkthrough.Build(t, t.Solve, null, l => s.LoadLevel(l));
            output.WriteLine("UW2: goal " + (t.Solve.GoalReached ? "reached" : "not reached") + ", " + w.Steps.Count + " steps; " + string.Join(" | ", w.Doubts.Distinct().Take(5)));
            Assert.NotNull(w.Lines());
            Assert.Equal(t.Solve.GoalReached, w.Reached);
        }

        // ---- what is handed over, the way from step to step, the reverse check

        /// <summary>A start zone with so many pieces of food, two things that each take one, and a goal behind both.</summary>
        static TimelineResult Hungry(int food)
        {
            var zs = new ZoneSet(1);
            zs.InitLevel(1, (x, y) => x < 8 && y < 4);
            for (int x = 0; x < 4; x++) zs.SetZone(1, x, 0, 2);
            for (int x = 4; x < 8; x++) zs.SetZone(1, x, 0, 3);
            zs.EnsureZone(2); zs.EnsureZone(3);
            zs.StartZone = 2; zs.GoalZone = 3;
            zs.Links.Add(new ZoneLink { Id = 1, From = 2, To = 3, Kind = "door", Requires = "flag:taper & flag:boots", Level = 1, X = 4, Y = 0 });
            for (int i = 0; i < food; i++) zs.Placements.Add(new Placement { Zone = 2, Grants = "item:food", Notes = "#" + (10 + i) + " loaf of bread, level 1 (" + i + ", 0)", Source = "levels" });
            zs.Placements.Add(new Placement { Zone = 2, Grants = "flag:taper", Requires = "item:food", UsesUp = new List<string> { "item:food" }, Notes = "Zak", Source = "facts" });
            zs.Placements.Add(new Placement { Zone = 2, Grants = "flag:boots", Requires = "item:food | flag:never", UsesUp = new List<string> { "item:food" }, Notes = "Marrowsuck", Source = "facts" });
            return Timeline.OnZones(zs);
        }

        [Fact]
        public void A_thing_handed_over_pays_once()
        {
            // one loaf does not pay two
            var one = Hungry(1);
            Assert.False(one.Solve.GoalReached);
            Assert.Single(one.Solve.TokenSphere.Keys.Where(k => k == "flag:taper" || k == "flag:boots"));
            // two do; each use names its own piece, and the reverse check counts them
            var two = Hungry(2);
            Assert.True(two.Solve.GoalReached, string.Join(", ", two.Solve.GoalMissing));
            Assert.Equal(new[] { "item:food", "item:food" }, two.Solve.OnceSpent.ToArray());
            var w = Walkthrough.Build(two, two.Solve);
            Assert.True(w.Reached);
            Assert.Empty(w.Doubts);
            Assert.Empty(w.ReverseProblems);
            var uses = w.Steps.Where(x => x.Why.Contains("Uses up the food")).ToList();
            Assert.Equal(2, uses.Count);
            Assert.NotEqual(uses[0].Why, uses[1].Why);      // not the same loaf's step
            // a use the solve did not pay for: the reverse check finds it
            two.Solve.Events.First(e => e.Token == "flag:taper").Spent.Add("item:food");
            var bad = Walkthrough.Build(two, two.Solve);
            Assert.Contains(bad.ReverseProblems, p => p.StartsWith("item:food: 1 more use"));
            Assert.Contains(bad.ReverseLines(), l => l.Contains("FAILED"));
        }

        [Fact]
        public void The_reverse_check_finds_a_need_gained_too_late()
        {
            var t = Hungry(2);
            var sv = t.Solve;
            // the door's flags gained only after the door in the record: owed at the start
            var door = sv.Events.First(e => e.Kind == "zone" && e.Zone == 3);
            sv.Events.Remove(door);
            sv.Events.Insert(1, door);
            var w = Walkthrough.Build(t, sv);
            Assert.True(w.Doubts.Count > 0 || w.ReverseProblems.Count > 0);
        }

        [Theory]
        [MemberData(nameof(Sets))]
        public void The_reverse_check_passes_and_the_player_can_walk_it(string name, int seed)
        {
            var r = Run(name, seed); if (r == null) return;
            var w = r.Walkthrough;
            Assert.True(w.ReverseRan);
            Assert.True(w.ReverseProblems.Count == 0, string.Join(" | ", w.ReverseProblems));
            Assert.True(w.ReversePaid > w.Steps.Count);
            Assert.Empty(w.ReverseUnused);
            // every step is done where the step before left the player, or the way there is given
            Assert.True(w.NoWay.Count == 0, string.Join(" | ", w.NoWay));
            Assert.Contains(w.Steps, x => x.Route.Count > 0 && x.Route[0].StartsWith("the way there from "));
            string log = r.Log.ToString();
            Assert.Contains("REVERSE CHECK", log);
            Assert.Contains("Passed: " + w.ReversePaid + " needs", log);
            // food handed over is another piece each time
            var fed = w.Steps.Where(x => x.Event != null && x.Event.Spent.Contains("item:food")).ToList();
            var loaves = w.Steps.Where(x => x.Event != null && x.Event.Token == "item:food").ToList();
            Assert.True(loaves.Count >= fed.Count, fed.Count + " uses of food, " + loaves.Count + " pieces taken");
            // a fight is named as one
            Assert.All(w.Steps.Where(x => x.Text.StartsWith("Kill ")), x => Assert.Contains("A fight", x.Why));
        }

        [Fact]
        public void A_warp_uses_up_its_scroll()
        {
            var r = Run("creatures, magic off, single-use", 9); if (r == null) return;
            var warp = r.Walkthrough.Steps.FirstOrDefault(x => x.Event != null && x.Event.Warp != null && x.Event.Spent.Count > 0);
            if (warp == null) { output.WriteLine("this seed makes no warp with a single-use item"); return; }
            Assert.Contains("Uses up", warp.Why);
            Assert.StartsWith("once:", warp.Event.Spent[0]);
        }

        [Fact]
        public void A_way_that_was_always_there_names_no_change()
        {
            // UW1 level 7: the door at (55, 2) is on Tyball's side; opening the wall at (42, 10) joins Tyball's zone to the level's
            // main one, which gives the door's zone another id but does not make the door's way
            var r = Run("defaults", 1); if (r == null) return;
            var door = r.Walkthrough.Steps.FirstOrDefault(x => x.Event != null && x.Event.Link != null && x.Event.Link.Level == 7 && x.Event.Link.X == 55 && x.Event.Link.Y == 2);
            if (door == null) { output.WriteLine("this seed does not go through the door"); return; }
            Assert.DoesNotContain("flag:terrain_l7_351", door.Uses);
            Assert.DoesNotContain("since the change", door.Why);
        }
    }
}
