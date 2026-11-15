using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The key item scatter on UW2 from the game's own start (docs/UW2-RANDOMIZER-NEXT-STEPS.md
    /// 4.1): UW2's categories; the plot things held fixed on purpose (the key gems the facts give
    /// where they lie, what creatures and traps hold, the Academy's telekinesis wand); the facts
    /// that follow their object by its kind ("match": key gems 2, 4 and 6, the basilisk oil, the
    /// Skup Chamber's crystals, the delivery voucher); the fresh solve of the shuffled levels
    /// reaching the castle's plot 16 with no cut-off, no key item in a place that may be lost, in
    /// a spared creature or on a square a trap turns to rock; Apply and Undo. About 20 s a run.
    /// </summary>
    public class Uw2KeyShuffleTests
    {
        readonly ITestOutputHelper output;
        public Uw2KeyShuffleTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession session;
        static LevelSnapshot snapshot;
        static readonly Dictionary<string, KeyShuffleResult> runs = new Dictionary<string, KeyShuffleResult>();

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW2");
                if (g == null) return null;
                session = GameSession.Open(g);
                snapshot = KeyShuffle.Snapshot(session, null);
                return session;
            }
        }

        static KeyShuffleOptions Options(string set, int seed)
        {
            var o = new KeyShuffleOptions { Seed = seed };
            if (set == "single-use") o.Logic.SingleUseItems = true;
            // a pure fighter: no rune magic, and of the single-use things only Iron Flesh (the djinn needs it; magic off alone cannot finish UW2 even unshuffled)
            if (set == "fighter") { o.Logic.Magic = false; o.Logic.SingleUseItems = true; o.Logic.SingleUseOnly = new HashSet<string> { "iron_flesh" }; }
            return o;
        }

        /// <summary>One run a set and seed, shared by the tests (the default seed 1 serves the survey too).</summary>
        static KeyShuffleResult Run(string set, int seed)
        {
            var s = Session();
            lock (gate)
            {
                KeyShuffleResult r;
                string key = set + "|" + seed;
                if (!runs.TryGetValue(key, out r)) runs[key] = r = KeyShuffle.RunOn(s, snapshot, Options(set, seed));
                return r;
            }
        }

        static ShuffleObject At(KeyShuffleResult r, int level, int index)
        {
            return r.Objects.Single(o => o.Made == null && o.Level == level && o.Index == index && o.Item >= 0);
        }

        [Fact]
        public void The_survey_takes_uw2s_categories_and_holds_the_plot_things_it_must()
        {
            var s = Session(); if (s == null) return;
            var r = Run("default", 1);
            Assert.Null(r.Refused);
            Assert.Equal(UltimateEditor.Games.GameKind.UW2, r.Game);
            // UW2's categories only: items 0xE1-0xE7 are potions there, not the Key of Infinity
            Assert.All(r.Objects, o => Assert.Contains(o.Category, KeyShuffle.Uw2Categories.Concat(new[] { "filler" })));
            Assert.Equal(new[] { "keys", "runes", "abilities", "plot" }, KeyShuffle.CategoriesOf(s));
            Assert.Contains("key gems", KeyShuffle.CategoryName("plot", true));
            Assert.Equal(KeyShuffle.CategoryName("plot"), KeyShuffle.CategoryName("plot", false));
            // the facts that follow their object move with it, their token along: key gems 2, 4 and 6, the oil, the crystals, the voucher
            foreach (var (level, index, token) in new[] { (18, 907, "item:key_gem_2"), (34, 1022, "item:key_gem_4"), (49, 1004, "item:key_gem_6"), (3, 583, "item:basilisk_oil"), (57, 790, "item:basilisk_oil"),
                (59, 823, "item:basilisk_oil"), (67, 902, "item:basilisk_oil"), (34, 947, "item:crystal_e0y2"), (33, 819, "item:controller_crystal"), (9, 986, "item:delivery_voucher") })
            {
                var o = At(r, level, index);
                Assert.True(o.Stays == null, "L" + level + " #" + index + " stays: " + o.Stays);
                Assert.True(o.Key);
                Assert.Equal("plot", o.Category);
                Assert.Contains(o.Gives, p => p.Grants == token && p.Source.StartsWith("facts: "));
                Assert.NotEmpty(o.FollowFacts);
            }
            // held fixed, each for its reason
            Func<int, int, string> stays = (l, i) => At(r, l, i).Stays ?? "";
            Assert.Contains("carried by a creature", stays(14, 1017));      // key gem 1: Borne
            Assert.Contains("trap or trigger", stays(26, 931));             // key gem 3: a pick-up trigger
            Assert.Contains("trap or trigger", stays(48, 898));             // key gem 5: a pick-up trigger
            Assert.Contains("carried by a creature", stays(57, 784));       // key gem 7: Dorstag
            Assert.Contains("create trap's template", stays(57, 782));      // Blog's copy of gem 7
            Assert.Contains("blackrock key gem", stays(69, 906));           // key gem 8 on the Void's platform
            Assert.Contains("blackrock key gem", stays(65, 831));           // the other gem 8
            // the Sceptre of the Deadly Seeker (its spell is an object in it) and the Delgnizator (a pick-up trigger in it): no category of UW2's, so not looked at, and their facts give them where they lie
            Assert.DoesNotContain(r.Moving, o => o.Level == 65 && o.Index == 844);
            Assert.DoesNotContain(r.Moving, o => o.Level == 34 && o.Index == 1005);
            Assert.Contains("carried by a creature", stays(52, 947));       // Praecor Loth's horn
            Assert.Contains("own level only", stays(43, 1011));             // the Academy's telekinesis wand
            Assert.Contains("must spare", stays(18, 929));                  // what a brain creature carries
            Assert.Contains("does not ask for", stays(33, 655));            // a strange artifact of Talorus
            // what moves gives what the logic asks for, or is an ability, a key or a rune
            Assert.All(r.Moving.Where(o => o.Category == "plot"), o => Assert.True(o.Key, o + " moves as a plot item the logic does not ask for"));
            Assert.Contains("Plot items (key gems", r.Log.ToString());
        }

        /// <summary>In the shipped levels each fact that follows its object stands on the object it names; with the object moved it finds it where it went.</summary>
        [Fact]
        public void A_fact_that_follows_its_object_finds_it_where_it_lies()
        {
            var s = Session(); if (s == null) return;
            var facts = FactSet.Shipped(true);
            Assert.Empty(facts.Check());
            var follow = facts.OfKind("flag").Where(f => f.Match != null).ToList();
            Assert.Equal(10, follow.Count);
            var at = AbilityZones.FactObjects(s, l => s.LoadLevel(l), facts);
            foreach (var f in follow) { Assert.True(at.ContainsKey(f.Id), f.Id); Assert.Equal((f.Level, f.Object), (at[f.Id].Level, at[f.Id].Object)); }
            // key gem 2 taken off level 18 and made anew on level 3: the fact goes with it
            var edits = new LevelEdits(s);
            var gem = s.LoadLevel(18).Objects[907];
            edits.For(18).DeleteObject(907);
            int made = edits.For(3).AddObject(new ObjectSpec { Item = gem.Item, Quality = gem.Quality, IsQuantity = gem.IsQuantity, Link = gem.Link }, 50, 28);
            var moved = AbilityZones.FactObjects(s, edits.Model, facts);
            Assert.Equal((3, made), (moved["key_gem_2"].Level, moved["key_gem_2"].Object));
            // with no gem of its kind left, the fact has no object, and the build says so instead of giving it where the gem was
            edits.For(3).DeleteObject(made);
            Assert.False(AbilityZones.FactObjects(s, edits.Model, facts).ContainsKey("key_gem_2"));
        }

        [Theory]
        [InlineData("default", 1)]
        [InlineData("default", 2)]
        [InlineData("default", 3)]
        [InlineData("single-use", 1)]
        [InlineData("fighter", 1)]
        public void Shuffled_uw2_reaches_the_ceremony_and_keeps_key_items_in_safe_places(string set, int seed)
        {
            var s = Session(); if (s == null) return;
            var r = Run(set, seed);
            output.WriteLine("{0} seed {1}: used {2} after {3} attempts, spheres {4} -> {5}, {6} moves", set, seed, r.UsedSeed, r.Attempts, r.SpheresBefore, r.SpheresAfter, r.Moves.Count(m => m.Item != m.Place));
            foreach (var f in r.Failures) output.WriteLine("  " + f);
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            var sv = r.Check;
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Empty(sv.CutOff);
            Assert.DoesNotContain(sv.Traps, x => x.Strands.Count > 0);
            Assert.True(r.Moves.Count(m => m.ByFill && m.Item != m.Place) > 40);
            // no key item in a place that may be lost whatever the player does, in a creature the logic must spare, or on a square a trap turns to rock
            var rock = KeyShuffle.ChangingSquares(s, r.CheckBuild);
            Assert.NotEmpty(rock);
            foreach (var m in r.Moves.Where(m => m.ByFill))
            {
                Assert.True(KeyShuffle.KeyPlaceAllowed(m.Place), m.Item + " -> " + m.Place + " (" + m.Place.LostWhy + ")");
                Assert.Null(m.Place.Stays);
                Assert.DoesNotContain(m.Place.Level + ":" + m.Place.X + ":" + m.Place.Y, rock);
                var lm = LevelModel.FromBytes(s, m.Place.Level, r.After.ContainsKey(m.Place.Level) ? r.After[m.Place.Level] : r.Before[m.Place.Level], r.Textures[m.Place.Level]);
                Assert.Null(r.CheckBuild.Facts.Spared(m.Place.Level, lm, lm.Objects[m.Place.Index]));
            }
            // Killorn's brain creatures live: nothing the check uses stands for their deaths
            Assert.False(sv.TokenSphere.ContainsKey("flag:killorn_fallen"));
            // every source of kind b the check takes comes while its window is open, in the solve's own order (AvoidLost)
            var held = new List<string>();
            foreach (var e in sv.Events)
            {
                if (e.Placement != null && e.Placement.LostForGood)
                    Assert.True(!Requirement.Parse(e.Placement.LostWhen).Eval(new Held(held, sv.Options)), e.Placement.Grants + " taken after " + e.Placement.LostWhen);
                if ((e.Kind == "token" || e.Kind == "flag") && e.Token.Length > 0) held.Add(e.Token);
            }
            // a key gem, crystal or the voucher that moved: the check's fact stands on it where it went (the four flasks of oil are alike, so any flask may stand for any of their facts)
            foreach (var m in r.Moves.Where(m => m.Item.FollowFacts.Count > 0 && m.Item != m.Place))
                foreach (var id in m.Item.FollowFacts.Where(id => !id.StartsWith("basilisk_oil")))
                    Assert.Equal((m.Place.Level, m.Place.Index), (r.CheckBuild.Abilities.FactObjects[id].Level, r.CheckBuild.Abilities.FactObjects[id].Object));
            foreach (var f in r.CheckBuild.Facts.OfKind("flag").Where(f => f.Match != null))
            {
                var at = r.CheckBuild.Abilities.FactObjects[f.Id];
                var lv = LevelModel.FromBytes(s, at.Level, r.After.ContainsKey(at.Level) ? r.After[at.Level] : r.Before[at.Level], r.Textures[at.Level]);
                Assert.True(f.Match.Matches(lv.Objects[at.Object]), f.Id + " stands on L" + at.Level + " #" + at.Object + ", not of its kind");
            }
            Assert.NotNull(r.Walkthrough);
            Assert.True(r.Walkthrough.Reached);
            Assert.Contains("The castle plot reaches 16", r.Log.ToString());
            if (set != "default" || seed != 1) return;
            // Apply writes the shuffled levels, Undo puts every byte back
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            foreach (var kv in r.After) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            foreach (int l in r.After.Keys) { bool ok; edits.For(l).Check(out ok); Assert.True(ok, "level " + l + " fails the list check"); }
            Assert.Empty(KeyShuffle.Undo(r, edits));
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
        }

        /// <summary>Rune magic off with single-use things not counted: the djinn needs Iron Flesh, so not even the unshuffled game ends, and the scatter says so at once instead of trying every seed.</summary>
        [Fact]
        public void Magic_off_without_single_use_things_is_refused_at_once()
        {
            var s = Session(); if (s == null) return;
            var o = new KeyShuffleOptions { Seed = 1 };
            o.Logic.Magic = false;
            var r = KeyShuffle.RunOn(s, snapshot, o);
            Assert.False(r.Success);
            Assert.Contains("even unshuffled", r.Refused);
            Assert.Equal(0, r.Attempts);
        }

        /// <summary>The tokens held so far, for a window's requirement (plot:N), as LostSourcesTests reads them.</summary>
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
