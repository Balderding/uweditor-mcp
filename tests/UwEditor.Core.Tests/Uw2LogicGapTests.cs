using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2 logic gaps closed at 0.1.50: a start in a world the blackrock gem keeps shut (the solve
    /// takes the world first, leaves it, and comes back only once the gem opens it: Timeline's
    /// TimelineSolve.Leaves), ways the game allows once (the Historian's teleport: ZoneLink.Once)
    /// and a mark of kind a on a link (Bishop and the gauntlets), what the Prison Tower's goblins
    /// carry (the facts' kind "carried": Janar's key ring for Borne's codeword), Merzan's price in
    /// gold, and the walkthrough's wording of a teleport walked into through a wall.
    /// </summary>
    public class Uw2LogicGapTests
    {
        readonly ITestOutputHelper output;
        public Uw2LogicGapTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession session;
        static TimelineResult own;

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW2");
                if (g == null) return null;
                session = GameSession.Open(g);
                return session;
            }
        }

        static TimelineResult Own(GameSession s)
        {
            lock (gate)
            {
                return own ?? (own = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false));
            }
        }

        static int LevelOf(TimelineResult t, TimelineSolve sv, int z)
        {
            if (z < Timeline.FreshZoneIds) { var l = t.Set.LevelsOf(z); return l.Count > 0 ? l[0] : 0; }
            string n;
            if (sv.NewZones.TryGetValue(z, out n)) { var m = System.Text.RegularExpressions.Regex.Match(n, @"\bL(\d+)"); if (m.Success) return int.Parse(m.Groups[1].Value); }
            return 0;
        }

        /// <summary>
        /// A start in the Pits of Carnage (level 58 (52, 5), worker B's seed 8 square), with every
        /// moving item held as the start filter has it: the solve takes the Pits first and leaves
        /// once for the castle (level 5); nothing on the Pits' levels is taken or entered again
        /// before the castle plot reaches 8 or key gem 7 is fused (treated by Nystul), so Zoranthus's
        /// plot step, which needs Miranda's news at plot 7, comes after the return. The old model
        /// took it while the player stood in the castle. The walkthrough finds a way to every step
        /// and says when to leave.
        /// </summary>
        [Fact]
        public void A_start_in_a_shut_world_takes_it_first_and_comes_back_only_when_the_gem_opens_it()
        {
            var s = Session(); if (s == null) return;
            var opt = new KeyShuffleOptions();
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = KeyShuffleOptions.GivenStart(58, 52, 5) }, opt.Logic, null, null, false);
            var held = KeyShuffle.MovingTokens(s, l => s.LoadLevel(l), Own(s), opt);
            var sv = Timeline.Solve(t, opt.Logic, "Pits start", held);
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Contains(58, sv.SealedLevels);
            var leave = sv.Leaves.First();
            Assert.Equal(5, LevelOf(t, sv, leave.To));
            Assert.Contains("castle plot at 8", leave.Opens);
            Assert.Contains("key gem (7)", leave.Opens);
            // the sphere from which the Pits are open again: plot 8, or key gem 7 with Nystul
            int open = int.MaxValue;
            for (int sp = 0; sp < sv.Spheres.Count && open == int.MaxValue; sp++)
            {
                var have = sv.TokenSphere.Where(kv => kv.Value <= sp).Select(kv => kv.Key).ToList();
                if (Requirement.PlotClock(have) >= 8 || (have.Contains("item:key_gem_7") && have.Contains("npc:nystul"))) open = sp;
            }
            Assert.NotEqual(int.MaxValue, open);
            Assert.True(open > leave.Sphere);
            foreach (var e in sv.Events)
            {
                if (e.Sphere <= leave.Sphere || (e.Kind != "zone" && e.Kind != "token")) continue;
                if (e.Kind == "token" && e.Placement != null && Timeline.Everywhere(e.Placement, t.Facts)) continue;
                int l = LevelOf(t, sv, e.Zone);
                if (l >= 57 && l <= 64) Assert.True(e.Sphere > open, e.Kind + " " + e.Token + " zone " + e.Zone + " in sphere " + e.Sphere + ", the Pits open in " + open);
            }
            Assert.True(sv.TokenSphere["plotstep:zoranthus"] > open);
            var w = Walkthrough.Build(t, sv, null, l => s.LoadLevel(l));
            foreach (var n in w.NoWay) output.WriteLine(n);
            Assert.Empty(w.NoWay);
            Assert.Empty(w.ReverseProblems);
            var step = Assert.Single(w.Steps, x => x.Note.Length > 0);
            Assert.StartsWith("Leave the Pits of Carnage now", step.Note);
            Assert.Contains(w.Lines(), x => x.StartsWith("  >>> Leave the Pits of Carnage now"));
            // the steps before it are all in the Pits' world
            foreach (var st in w.Steps.TakeWhile(x => x != step)) if (st.Level > 0) Assert.InRange(st.Level, 57, 64);
        }

        /// <summary>The game's own start and a castle start have no shut world; a Prison Tower start neither (its facet shows below plot 4).</summary>
        [Fact]
        public void Only_a_start_in_a_world_the_gem_keeps_shut_is_taken_in_parts()
        {
            var s = Session(); if (s == null) return;
            var t = Own(s);
            Assert.Null(t.Solve.SealedLevels);
            Assert.Empty(t.Solve.Leaves);
            Assert.Equal(-1, t.Solve.SealedLeft);
            var p = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = KeyShuffleOptions.GivenStart(10, 29, 29) }, new AbilityOptions(), null, null, false);
            Assert.Null(p.Solve.SealedLevels);
        }

        /// <summary>
        /// The shipped facts: the Historian's teleport is a way taken once (conversation 156 sets
        /// quest 39 as he sends the Avatar) with a warning, Bishop's force field carries a mark of kind
        /// a (conversation 6: he keeps a pair of gauntlets), Janar's key ring needs Borne's codeword,
        /// Borne's and the goblin guards' keys need killing them, and Merzan's potion a hundred gold.
        /// A link may carry only kind a; "once" belongs to a one-way link.
        /// </summary>
        [Fact]
        public void The_facts_mark_the_historian_bishop_the_goblins_keys_and_merzans_price()
        {
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            Func<string, Fact> F = id => uw2.Facts.Single(f => f.Id == id);
            Assert.True(F("talorus_historian_island").Once);
            Assert.Equal("a", F("talorus_historian_island").Lost);
            Assert.Equal("a", F("bishop_force_field").Lost);
            Assert.False(F("bishop_force_field").Once);
            Assert.Equal("npc:borne | option:kill_npcs", F("carried_janar").Requires);
            Assert.Equal("item:gold_100", F("merzan_iron_flesh").Requires);
            Assert.Equal(100, F("gold_100").AtLeast);
            var bad = FactSet.FromJson(@"{ ""format"": ""uw2-facts"", ""facts"": [
                { ""id"": ""l"", ""kind"": ""link"", ""from"": { ""level"": 1, ""x"": 1, ""y"": 1 }, ""to"": { ""level"": 1, ""x"": 2, ""y"": 2 }, ""twoWay"": true, ""once"": true,
                  ""mayBeLost"": { ""kind"": ""b"", ""why"": ""x"" } },
                { ""id"": ""f"", ""kind"": ""flag"", ""grants"": ""key:1"", ""level"": 1, ""object"": 5, ""once"": true },
                { ""id"": ""c"", ""kind"": ""carried"", ""level"": 1 } ] }").Check();
            Assert.Contains(bad, x => x.StartsWith("l: a link's mayBeLost is of kind a"));
            Assert.Contains(bad, x => x.StartsWith("l: a link taken once is one way"));
            Assert.Contains(bad, x => x.StartsWith("f: 'once' belongs to a link"));
            Assert.Contains(bad, x => x.StartsWith("c: a carried fact names the creature"));
            Assert.Contains(bad, x => x.StartsWith("c: a carried fact needs 'requires'"));
        }

        /// <summary>
        /// The game's own start: what Janar, Borne and the gate guards carry needs the facts'
        /// requirement (key 4 and 6 come by Borne's codeword, key 27 not at all with killing NPCs off),
        /// Merzan's potion waits for a hundred gold coins, the solve still reaches Nystul's ceremony
        /// (334 zones, plot 16). The walkthrough walks into the wall at level 1 (37, 39) to Nystul's
        /// room, and warns at the Historian's teleport that it is taken once.
        /// </summary>
        [Fact]
        public void The_own_start_keeps_to_the_new_marks_and_still_ends_the_game()
        {
            var s = Session(); if (s == null) return;
            var t = Own(s);
            var sv = t.Solve;
            Assert.True(sv.GoalReached);
            Assert.Equal(334, Timeline.BaseZonesReached(sv));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            var janar = t.Set.Placements.Where(p => (p.Notes ?? "").Contains("carried_janar")).ToList();
            Assert.Equal(new[] { "key:4", "key:6" }, janar.Select(p => p.Grants).OrderBy(x => x));
            Assert.All(janar, p => Assert.Contains("npc:borne | option:kill_npcs", p.Requires));
            Assert.Contains(t.Set.Placements, p => p.Grants == "key:27" && p.Requires.Contains("option:kill_npcs"));
            Assert.False(sv.TokenSphere.ContainsKey("key:27"));
            Assert.True(sv.TokenSphere["key:4"] >= sv.TokenSphere["npc:borne"]);
            Assert.Contains(t.Set.Placements, p => p.Grants == "once:iron_flesh" && p.Requires.Contains("item:gold_100"));
            Assert.Contains(t.Set.Placements, p => p.Counts == "gold_100" && p.Quantity > 0);
            // the pure fighter (magic off, of the single-use things only Iron Flesh) buys it and still ends the game
            var fighter = new AbilityOptions { Magic = false, SingleUseItems = true, SingleUseOnly = new HashSet<string> { "iron_flesh" } };
            var fs = Timeline.Solve(t, fighter, "fighter");
            Assert.True(fs.GoalReached, string.Join(", ", fs.GoalMissing));
            Assert.True(fs.TokenSphere["once:iron_flesh"] >= fs.TokenSphere["item:gold_100"]);

            var w = Walkthrough.Build(t, sv, null, l => s.LoadLevel(l));
            Assert.Contains(w.Steps, x => x.Text.StartsWith("Walk into the wall at L1 (37, 39)") && x.Text.Contains("teleports you to level 2"));
            Assert.DoesNotContain(w.Steps, x => x.Text.StartsWith("Take the stairs at L1 (37, 39)"));
            var hist = Assert.Single(w.Steps, x => x.Event != null && x.Event.Link != null && x.Event.Link.Notes == "facts: talorus_historian_island");
            Assert.True(hist.Event.Link.Once);
            Assert.Contains("only once", hist.Warning);
            Assert.Empty(w.NoWay);
        }

        /// <summary>
        /// A way taken once (ZoneLink.Once), on a zone set made by hand: start 2, the once way to the
        /// island 3, a one-way bridge from 3 to the shore 4 (which holds key 1), the goal 5 behind 4
        /// with the gem that lies on the island and needs key 1. The old model came back to the island
        /// for the gem; now the island is gone once walked out of, and the goal is missed, until a
        /// lasting way back (lava protection from the shore) is there.
        /// </summary>
        [Fact]
        public void A_way_taken_once_does_not_bring_the_player_back()
        {
            Func<bool, ZoneSet> make = lavaWay =>
            {
                var zs = new ZoneSet(1) { StartZone = 2, GoalZone = 5 };
                for (int i = 2; i <= 5; i++) zs.EnsureZone(i);
                zs.Links.Add(new ZoneLink { Id = 1, From = 2, To = 3, Kind = "other", Requires = "free", Once = true });
                zs.Links.Add(new ZoneLink { Id = 2, From = 3, To = 4, Kind = "walk", Requires = "free" });
                zs.Links.Add(new ZoneLink { Id = 3, From = 4, To = 2, Kind = "walk", Requires = "free" });
                zs.Links.Add(new ZoneLink { Id = 4, From = 4, To = 5, Kind = "walk", Requires = "item:gem" });
                if (lavaWay) zs.Links.Add(new ZoneLink { Id = 5, From = 4, To = 3, Kind = "walk", Requires = "spell:lava" });
                zs.Placements.Add(new Placement { Zone = 3, Grants = "item:gem", Requires = "key:1" });
                zs.Placements.Add(new Placement { Zone = 4, Grants = "key:1" });
                zs.Placements.Add(new Placement { Zone = 4, Grants = "worn:lava" });
                return zs;
            };
            var opt = new AbilityOptions { Magic = false };
            var r = Timeline.OnZones(make(false), opt);
            Assert.False(r.Solve.GoalReached);
            Assert.True(r.Solve.ZoneSphere.ContainsKey(3) && r.Solve.ZoneSphere.ContainsKey(4));
            Assert.False(r.Solve.TokenSphere.ContainsKey("item:gem"));
            r = Timeline.OnZones(make(true), opt);
            Assert.True(r.Solve.GoalReached);
            Assert.True(r.Solve.TokenSphere["item:gem"] > r.Solve.TokenSphere["key:1"]);
            // the gem on arrival when key 1 is held then: taken, and the goal reached by the once way alone
            var z = make(false);
            z.Placements.RemoveAll(p => p.Grants == "key:1");
            z.Placements.Add(new Placement { Zone = 2, Grants = "key:1" });
            r = Timeline.OnZones(z, opt);
            Assert.True(r.Solve.GoalReached);
            Assert.DoesNotContain(r.Solve.Traps, x => x.Zone == 3 || x.Zone == 4);
        }
    }
}
