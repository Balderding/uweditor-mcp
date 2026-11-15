using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Things the game takes away for good (docs/UW2-RANDOMIZER-NEXT-STEPS.md 2.1): the facts'
    /// mayBeLost (kind a: lost only by an avoidable choice; b: with time; c: with its place), the
    /// solver's AvoidLost (a source of kind b or c counts only inside its window), the key item
    /// fill's place filter, and the walkthrough's warning.
    /// </summary>
    public class LostSourcesTests
    {
        [Fact]
        public void The_mark_is_read_from_the_facts()
        {
            var set = FactSet.FromJson(@"{ ""format"": ""uw1-facts"", ""facts"": [
                { ""id"": ""k"", ""kind"": ""flag"", ""grants"": ""key:1"", ""level"": 1, ""object"": 5, ""requires"": ""free"",
                  ""mayBeLost"": { ""kind"": ""b"", ""when"": ""plot:3"", ""why"": ""only at plot 0 to 2"" } },
                { ""id"": ""p"", ""kind"": ""lost"", ""level"": 2, ""rect"": [1, 1, 4, 4], ""mayBeLost"": { ""kind"": ""c"", ""why"": ""the room caves in"" } },
                { ""id"": ""t"", ""kind"": ""lost"", ""token"": ""item:rune_bag"", ""mayBeLost"": { ""kind"": ""a"", ""why"": ""do not give it away"" } },
                { ""id"": ""bad"", ""kind"": ""lost"", ""level"": 3, ""mayBeLost"": ""x"" } ] }");
            var k = set.Facts.Single(f => f.Id == "k");
            Assert.Equal("b", k.Lost);
            Assert.Equal("plot:3", k.LostWhen);
            Assert.Equal("only at plot 0 to 2", k.LostWhy);
            Assert.Equal("c", set.Facts.Single(f => f.Id == "p").Lost);
            Assert.Equal("a", set.LostToken("item:Rune_Bag").Lost);
            Assert.Null(set.LostToken("item:lockpick"));
            var problems = set.Check();
            Assert.Contains(problems, x => x.StartsWith("bad: mayBeLost's kind"));
            Assert.Contains(problems, x => x.StartsWith("bad: mayBeLost needs 'why'"));
            Assert.DoesNotContain(problems, x => x.StartsWith("k:") || x.StartsWith("p:") || x.StartsWith("t:"));
            // a placement takes the stronger mark only
            var pl = new Placement { Grants = "key:1" };
            pl.MarkLost(set.LostToken("item:rune_bag"));
            Assert.Equal("a", pl.Lost);
            pl.MarkLost(k);
            Assert.Equal("b", pl.Lost);
            Assert.True(pl.LostForGood);
            pl.MarkLost(set.LostToken("item:rune_bag"));
            Assert.Equal("b", pl.Lost);

            // the shipped UW2 facts: the plot windows (b) and the avoidable choices (a)
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            Func<string, Fact> F = id => uw2.Facts.Single(f => f.Id == id);
            Assert.Equal(new[] { "b", "plot:1" }, new[] { F("dupre_sewer_key").Lost, F("dupre_sewer_key").LostWhen });
            Assert.Equal(new[] { "b", "plot:12" }, new[] { F("charles_key").Lost, F("charles_key").LostWhen });
            Assert.Equal(new[] { "b", "plot:14" }, new[] { F("altara_dagger").Lost, F("altara_dagger").LostWhen });
            Assert.Equal(new[] { "b", "plot:14" }, new[] { F("altara_rod").Lost, F("altara_rod").LostWhen });
            foreach (var id in new[] { "merzan_iron_flesh", "prison_password_borne", "prison_password_armorer", "prison_gate_guard", "prison_l14_guard", "lost_killorn_17", "lost_killorn_18", "lost_rune_bag" })
                Assert.Equal("a", F(id).Lost);
            Assert.Equal("plot:14", F("lost_altara").LostWhen);
            Assert.Equal("plot:16", F("lost_mors_gotha_killorn").LostWhen);
            // nothing of kind c in UW2: the one place that closes, Killorn Keep, falls only when the player kills both brain creatures
            Assert.DoesNotContain(uw2.Facts, f => f.Lost == "c");
            Assert.DoesNotContain(FactSet.Shipped(false).Facts, f => f.Lost.Length > 0);
        }

        /// <summary>Two zones: the start grants the plot's step and (listed after it) a key of kind b gone at plot 1; the key opens the goal.</summary>
        static ZoneSet Tiny(string lostWhen)
        {
            var zs = new ZoneSet(1) { StartZone = 2, GoalZone = 3 };
            zs.Zones.Add(new Zone { Id = 2, Name = "start" });
            zs.Zones.Add(new Zone { Id = 3, Name = "goal" });
            zs.Links.Add(new ZoneLink { Id = 1, From = 2, To = 3, Requires = "key:1" });
            zs.Placements.Add(new Placement { Zone = 2, Grants = "plotstep:first" });
            zs.Placements.Add(new Placement { Zone = 2, Grants = "key:1", Lost = "b", LostWhy = "only at plot 0", LostWhen = lostWhen });
            return zs;
        }

        [Fact]
        public void A_source_of_kind_b_counts_only_inside_its_window()
        {
            // the plot moves on before the key is looked at: with the option on the key is gone, off it counts
            Assert.False(new ZoneSolver(Tiny("plot:1"), new SolverOptions { AvoidLost = true }).Solve(2, 3).GoalReached);
            Assert.True(new ZoneSolver(Tiny("plot:1"), new SolverOptions { AvoidLost = false }).Solve(2, 3).GoalReached);
            // its window still open (gone at plot 2): it counts
            Assert.True(new ZoneSolver(Tiny("plot:2"), new SolverOptions { AvoidLost = true }).Solve(2, 3).GoalReached);
            // no window known: never relied on
            Assert.False(new ZoneSolver(Tiny(""), new SolverOptions { AvoidLost = true }).Solve(2, 3).GoalReached);
            Assert.True(new AbilityOptions().AvoidLost);
            Assert.True(new AbilityOptions().ToSolver().AvoidLost);
        }

        [Fact]
        public void The_fill_keeps_key_items_out_of_places_that_may_be_lost()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var facts = FactSet.Shipped(true);
            var m = s.LoadLevel(17);
            Func<LevelModel, int, ShuffleObject> place = (lm, i) => { var o = lm.Objects[i]; var so = new ShuffleObject { Level = lm.Level, Index = i, Item = o.Item }; KeyShuffle.MarkLost(so, facts, lm, o); return so; };
            // Altara's jewelled dagger (#971) and Kintara's boots (#749): carried by creatures the schedule takes off the map at plot 14
            var dagger = place(m, 971);
            Assert.Equal(0x0A, dagger.Item);
            Assert.Equal("b", dagger.Lost);
            Assert.Contains("Altara", dagger.LostWhy);
            Assert.False(KeyShuffle.KeyPlaceAllowed(dagger));
            Assert.False(KeyShuffle.KeyPlaceAllowed(place(m, 749)));
            // the rest of Killorn Keep falls only by the player's choice (kind a): allowed, with the warning
            var l18 = s.LoadLevel(18);
            var gem = place(l18, 907);
            Assert.Equal("a", gem.Lost);
            Assert.True(KeyShuffle.KeyPlaceAllowed(gem));
            // elsewhere nothing is marked
            var other = place(s.LoadLevel(3), 831);
            Assert.Equal("", other.Lost);
            Assert.True(KeyShuffle.KeyPlaceAllowed(other));

            // the same filter for UW1 facts that carry the mark: a place of kind c by its square (a key on the floor of level 1)
            string g1 = GameData.Game("UW"); if (g1 == null) return;
            var s1 = GameSession.Open(g1);
            var m1 = s1.LoadLevel(1);
            var floor = m1.Objects.First(o => !o.Free && o.OnTile && o.Index > 1 && !o.IsCritter && !o.IsTrapOrTrigger && o.Item < 0x140);
            var uw1 = FactSet.FromJson("{ \"facts\": [ { \"id\": \"cave_in\", \"kind\": \"lost\", \"level\": 1, \"rect\": [" + floor.TileX + ", " + floor.TileY + ", " + floor.TileX + ", " + floor.TileY + "], \"mayBeLost\": { \"kind\": \"c\", \"why\": \"the room caves in\" } } ] }");
            var p1 = new ShuffleObject { Level = 1, Index = floor.Index };
            KeyShuffle.MarkLost(p1, uw1, m1, floor);
            Assert.Equal("c", p1.Lost);
            Assert.False(KeyShuffle.KeyPlaceAllowed(p1));
            var away = m1.Objects.First(o => !o.Free && o.OnTile && o.Index > 1 && (o.TileX != floor.TileX || o.TileY != floor.TileY));
            var p2 = new ShuffleObject { Level = 1, Index = away.Index };
            KeyShuffle.MarkLost(p2, uw1, m1, away);
            Assert.True(KeyShuffle.KeyPlaceAllowed(p2));
            // the shuffle itself still refuses UW2
            Assert.NotNull(KeyShuffle.Run(s, null, new KeyShuffleOptions()).Refused);
        }

        [Fact]
        public void Uw2_ends_the_game_without_what_may_be_lost_and_the_walkthrough_warns()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var opt = new AbilityOptions();
            Assert.True(opt.AvoidLost);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), opt, null, null, variants: false);
            var sv = t.Solve;
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Empty(sv.CutOff);
            Assert.Equal(334, Timeline.BaseZonesReached(sv));     // 336 before 0.1.44's Britannia rule (WhereMagicWorksTests)
            // every source of kind b the solve takes comes while its window is open, in the solve's own order
            var held = new List<string>();
            foreach (var e in sv.Events)
            {
                if (e.Placement != null && e.Placement.LostForGood)
                    Assert.True(!Requirement.Parse(e.Placement.LostWhen).Eval(new Held(held, sv.Options)), e.Placement.Grants + " taken after " + e.Placement.LostWhen);
                if ((e.Kind == "token" || e.Kind == "flag") && e.Token.Length > 0) held.Add(e.Token);
            }
            Func<string, Placement> fact = id => t.Set.Placements.Single(p => p.Source == "facts: " + id);
            Func<Placement, bool> used = p => sv.Events.Any(e => e.Placement == p);
            // Dupre's key at plot 0 and Altara's rod before plot 14 are used; Charles's key is not (key 2 on level 3 comes first), nor Borne's password (the armorer's)
            Assert.True(used(fact("dupre_sewer_key")));
            Assert.True(used(fact("altara_rod")));
            Assert.False(used(fact("charles_key")));
            Assert.False(used(fact("prison_password_borne")));
            Assert.Equal("b", fact("charles_key").Lost);
            Assert.Equal("a", fact("line_2").Lost);       // on Killorn's lower level
            // the walkthrough warns at a source lost by a choice (the rune bag, Killorn's lower level) and at one only there for a time (Altara's rod)
            var w = Walkthrough.Build(t, sv, null, l => s.LoadLevel(l));
            Assert.True(w.Reached);
            Assert.Contains(w.Steps, st => st.Event?.Token == "item:rune_bag" && st.Warning.StartsWith("this may be lost for good: ") && st.Warning.Contains("do not offer to join her"));
            Assert.Contains(w.Steps, st => st.Event?.Token == "flag:line_2" && st.Warning.Contains("brain creatures"));
            Assert.Contains(w.Steps, st => st.Event?.Token == "item:pearl_tipped_rod" && st.Warning.Contains("(gone at plot:14)"));
            Assert.Contains(w.Lines(), x => x.Trim().StartsWith("Beware: "));
            Assert.All(w.Steps.Where(st => st.Event?.Placement == null), st => Assert.Equal("", st.Warning));
        }

        /// <summary>The tokens held so far, for a window's requirement (plot:N).</summary>
        sealed class Held : IRequirementContext
        {
            readonly List<string> tokens;
            public Held(List<string> t, SolverOptions o) { tokens = t; Options = o; }
            public SolverOptions Options { get; private set; }
            public bool HasToken(string token) { return tokens.Contains(Requirement.Normalize(token)); }
            public bool ZoneReachable(int zone) { return true; }
            public int TalismanCount() { return 0; }
            public int PlotClock() { return Requirement.PlotClock(tokens); }
        }
    }
}
