using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// What UW2's creatures carry (0.1.51): the facts' kind "carried" names each creature that
    /// holds something the logic counts, from its conversation (what it hands over needs what the
    /// talk asks, what it never gives needs option:kill_npcs, what a guardian that fights the Avatar
    /// carries stays free); "only" splits one creature's things. UW1's other corrections now run on
    /// UW2 too: a locked container needs its key or a pick, a create trap's template its flag.
    /// </summary>
    public class Uw2CarriedTests
    {
        readonly ITestOutputHelper output;
        public Uw2CarriedTests(ITestOutputHelper output) { this.output = output; }

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

        /// <summary>The placement of one object (by level and index), as the level scan made it.</summary>
        static Placement Of(TimelineResult t, int level, int index)
        {
            return t.Set.Placements.Single(p => !string.IsNullOrEmpty(p.Grants) && (p.Notes ?? "").StartsWith("#" + index + " ") && (p.Notes ?? "").Contains("level " + level + " ("));
        }

        [Fact]
        public void The_carried_facts_read_and_check()
        {
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            Func<string, Fact> F = id => uw2.Facts.Single(f => f.Id == id);
            Assert.Equal("npc:praecor_loth | option:kill_npcs", F("carried_praecor_loth").Requires);
            Assert.Equal("(plotstep:listener & item:rune_bag) | option:kill_npcs", F("carried_altara_runes").Requires);
            Assert.Equal(new[] { 673, 674, 761 }, F("carried_altara_runes").Only);
            Assert.Empty(F("carried_altara").Only);
            Assert.Equal("item:two_eyeballs | option:kill_npcs", F("carried_prinx").Requires);
            Assert.Equal(2, F("two_eyeballs").AtLeast);
            var bad = FactSet.FromJson(@"{ ""format"": ""uw2-facts"", ""facts"": [
                { ""id"": ""c"", ""kind"": ""carried"", ""level"": 1, ""object"": 5, ""only"": [5], ""requires"": ""option:kill_npcs"" } ] }").Check();
            Assert.Contains(bad, x => x.StartsWith("c: 'only' names what the creature carries"));
        }

        /// <summary>
        /// The game's own start: the horn needs Praecor Loth's talk, Altara's runes the listener dead
        /// and a rune bag (her dagger, which her talk gives by the flag fact, needs killing her as an
        /// object), the armorer's gauntlets his talk and his runes killing him, Zaria's runes killing
        /// her, Prinx's scroll two eyeballs; the Tomb's guardians and Sentinel 868 fight, so what they
        /// carry stays free. The chest of level 3 (34, 16) needs key 3 or a pick, Fissif's lock picks
        /// the create trap that makes him. The solve still reaches Nystul's ceremony.
        /// </summary>
        [Fact]
        public void The_own_start_reads_what_each_creature_carries()
        {
            var s = Session(); if (s == null) return;
            var t = Own(s);
            var horn = Of(t, 52, 947);
            Assert.Equal("item:horn", horn.Grants);
            Assert.Contains("npc:praecor_loth | option:kill_npcs", horn.Requires);
            foreach (int i in new[] { 673, 674, 761 }) Assert.Contains("(plotstep:listener & item:rune_bag) | option:kill_npcs", Of(t, 17, i).Requires);
            Assert.Equal("option:kill_npcs", Of(t, 17, 971).Requires);
            Assert.Contains(t.Set.Placements, p => p.Grants == "item:jewelled_dagger" && p.Source == "facts: altara_dagger" && p.Requires == "npc:bishop | npc:mystell");
            Assert.Contains("npc:goblin_armorer | option:kill_npcs", Of(t, 13, 996).Requires);
            Assert.Equal("option:kill_npcs", Of(t, 13, 931).Requires);
            Assert.Contains("npc:borne | option:kill_npcs", Of(t, 14, 988).Requires);
            Assert.Equal("option:kill_npcs", Of(t, 14, 955).Requires);
            Assert.Equal("option:kill_npcs", Of(t, 57, 589).Requires);
            Assert.Contains("item:two_eyeballs | option:kill_npcs", Of(t, 65, 807).Requires);
            // the guardians who fight: Morphius, Lord Umbria, Lethe, Sentinel 868
            foreach (var at in new[] { Tuple.Create(52, 934), Tuple.Create(52, 804), Tuple.Create(52, 790), Tuple.Create(25, 898) })
                Assert.True(string.IsNullOrEmpty(Of(t, at.Item1, at.Item2).Requires) || Of(t, at.Item1, at.Item2).Requires == "free", at.ToString());
            // a locked chest and a create trap's template
            Assert.Contains("key:3 | pick:3", Of(t, 3, 831).Requires);
            Assert.Contains("pick:5", Of(t, 3, 722).Requires);
            foreach (int i in new[] { 555, 721, 722 }) Assert.Contains("flag:create_L1_844", Of(t, 1, i).Requires);

            var sv = t.Solve;
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Equal(334, Timeline.BaseZonesReached(sv));
            Assert.True(sv.TokenSphere["item:horn"] >= sv.TokenSphere["npc:praecor_loth"]);
            Assert.True(sv.TokenSphere["key:2"] >= sv.TokenSphere["key:3"]);
            output.WriteLine("horn {0}, rune:quas {1}, listener {2}, key 2 {3}, key 3 {4}, cast:open {5}", sv.TokenSphere["item:horn"], sv.TokenSphere["rune:quas"], sv.TokenSphere["plotstep:listener"], sv.TokenSphere["key:2"], sv.TokenSphere["key:3"], sv.TokenSphere["cast:open"]);
        }

        /// <summary>
        /// Seed 2 from the game's own start: the check's solve walks into the Void's pocket at level 65
        /// (44, 52) from the Pits (one way until lava protection) for the sceptre, which only gives a
        /// second bottle; the walkthrough used to follow it there and found no way back to the Academy
        /// (NO WAY FOUND twice). It now follows the solve that takes nothing from inside a trap
        /// (TimelineSolve.Safe), and puts off the pocket's entry until the step that needs it.
        /// </summary>
        [Fact]
        public void The_walkthrough_keeps_out_of_a_one_way_pocket()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.RunOn(s, KeyShuffle.Snapshot(s, null), new KeyShuffleOptions { Seed = 2, Retries = 0 });
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.NotNull(r.Check.Safe);
            Assert.True(r.Check.Safe.GoalReached);
            Assert.Contains(r.Check.Traps, x => r.CheckBuild.Set.Label(x.Zone) == "L65:5");
            Assert.Empty(r.Walkthrough.NoWay);
            Assert.Contains("It keeps out of the one-way traps", r.Log.ToString());
            Assert.Contains(r.Walkthrough.Steps, x => x.Warning.Contains("one-way pocket"));
        }

        /// <summary>The pure fighter (magic off, of the single-use things only Iron Flesh) still ends the game with the new marks.</summary>
        [Fact]
        public void The_pure_fighter_still_ends_the_game()
        {
            var s = Session(); if (s == null) return;
            var t = Own(s);
            var fighter = new AbilityOptions { Magic = false, SingleUseItems = true, SingleUseOnly = new HashSet<string> { "iron_flesh" } };
            var fs = Timeline.Solve(t, fighter, "fighter");
            Assert.True(fs.GoalReached, string.Join(", ", fs.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(fs.TokenSphere.Keys));
        }
    }
}
