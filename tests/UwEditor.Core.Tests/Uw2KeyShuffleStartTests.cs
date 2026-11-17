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
    /// The key item scatter on UW2 with a random or given start (0.1.49): the start filter solves
    /// with every moving item held (StartPicker, no stand-in), each attempt draws its start, the fill
    /// and the check solve from it, Apply writes the teleport at the game's square (level 1 (19, 48),
    /// the Avatar's bedroom) and Undo takes it back. The castle plot keeps the game's order from a
    /// start elsewhere (StateRules, the facts' plot steps, Timeline.PlotOrderProblem), and a start in
    /// Britannia withholds what no one casts from runes there (StartAbilities). A run takes 25 to 40 s.
    /// </summary>
    public class Uw2KeyShuffleStartTests
    {
        readonly ITestOutputHelper output;
        public Uw2KeyShuffleStartTests(ITestOutputHelper output) { this.output = output; }

        static readonly object gate = new object();
        static GameSession session;
        static LevelSnapshot snapshot;

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

        /// <summary>
        /// A random start in the castle (seed 1: level 1 (37, 47)) and in any world (seed 8: level 58
        /// (52, 5), beside Zoranthus, whose first talk is a step of the plot): each fills on its first
        /// attempt; the levels as applied, solved afresh with the game's own options, start at the
        /// teleport's square and reach plot 16 with no cut-off and the plot in order; Undo restores
        /// every byte and the teleport is gone.
        /// </summary>
        [Theory]
        [InlineData(1, StartScope.FirstLevels)]
        [InlineData(8, StartScope.AnyLevel)]
        public void A_random_uw2_start_fills_and_the_applied_levels_reach_plot_16_from_it(int seed, StartScope scope)
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.RunOn(s, snapshot, new KeyShuffleOptions { Seed = seed, Retries = 2, Start = StartChoice.Random, StartScope = scope, StartKit = true });
            foreach (var f in r.Failures) output.WriteLine(f);
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.Equal((1, 19, 48), (r.StartFrom.Level, (int)r.StartFrom.X, (int)r.StartFrom.Y));
            Assert.False(r.StartCheck.StandIn);
            Assert.Contains("with every moving item held", r.StartCheck.Reason);
            if (scope == StartScope.FirstLevels) Assert.InRange(r.Start.Level, 1, 5);
            else Assert.Equal(58, r.Start.Level);
            Assert.Equal(16, Requirement.PlotClock(r.Check.TokenSphere.Keys));
            Assert.Null(Timeline.PlotOrderProblem(r.CheckBuild, r.Check));
            string log = r.Log.ToString();
            Assert.Contains(scope == StartScope.FirstLevels ? "random (the castle)" : "random (any world)", log);
            Assert.Contains("The game puts you on level 1 (19, 48) as always", log);
            Assert.Contains("The castle plot: from this start", log);
            // a start in a world the gem opens only later (the Pits: plot 8) carries the note in the start lines and the walkthrough; the castle none
            if (scope == StartScope.FirstLevels) Assert.DoesNotContain("You start in", log);
            else
            {
                Assert.Contains("Note: You start in the Pits of Carnage, which the blackrock gem does not open until the castle plot reaches 8", log);
                Assert.Contains("  You start in the Pits of Carnage", log.Substring(log.IndexOf("WALKTHROUGH", StringComparison.Ordinal)));
                // 0.1.50: the check solve takes the Pits first and leaves them once; the walkthrough finds a way to every step
                Assert.Contains(58, r.Check.SealedLevels);
                Assert.NotEmpty(r.Check.Leaves);
                Assert.Contains("  >>> Leave the Pits of Carnage now", log);
            }
            Assert.DoesNotContain("NO WAY FOUND from", log);
            // a gift whose token a moving item gives (Charles's key 2: the key of level 3 moves) is not made
            Assert.DoesNotContain(r.Objects, o => o.Made != null && o.Made.Token == "key:2");
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var tele = StartTeleport.Find(s, edits.Model);
            Assert.NotNull(tele);
            Assert.Equal((r.Start.Level, (int)r.Start.X, (int)r.Start.Y), (tele.ToLevel, tele.ToX, tele.ToY));
            // the kit: UW2's map (0x13A) and a full torch beside the new start
            var m = edits.Model(r.Start.Level);
            Func<ObjectInfo, bool> beside = o => !o.Free && o.TileX >= 0 && Math.Abs(o.TileX - (int)r.Start.X) <= 1 && Math.Abs(o.TileY - (int)r.Start.Y) <= 1;
            Assert.Contains(m.Objects, o => beside(o) && o.Item == StartAids.Uw2Map);
            Assert.Contains(m.Objects, o => beside(o) && o.Item == StartAids.Torch && o.Quality == StartAids.FullTorch);
            var t = Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.Equal(t.Set.ZoneAt(r.Start.Level, (int)r.Start.X, (int)r.Start.Y), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
            Assert.Empty(t.Solve.CutOff);
            Assert.Null(Timeline.PlotOrderProblem(t, t.Solve));
            Assert.Empty(KeyShuffle.Undo(r, edits));
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            Assert.Null(StartTeleport.Find(s, edits.Model));
        }

        /// <summary>
        /// A sealed start: the room of level 51 at (28, 37) is left only by Levitate (circle 5), which a
        /// new character cannot cast from runes: the scatter makes a wand of Levitate (UW2's item 0x9B,
        /// link 0x200 + 32, as the game's wand of Light at level 67 #899), places it within reach, and the
        /// levels as applied read it as perm:levitate.
        /// </summary>
        [Fact]
        public void A_sealed_uw2_start_gets_a_wand_made_for_its_way_out()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.RunOn(s, snapshot, new KeyShuffleOptions { Seed = 1, Retries = 2, Start = StartChoice.Given, StartAt = KeyShuffleOptions.GivenStart(51, 28, 37), Logic = new AbilityOptions { DeathReturns = false } }); // with dying as a way back (the default) this start needs nothing made
            foreach (var f in r.Failures) output.WriteLine(f);
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.Equal(new[] { "levitate" }, r.StartCheck.Made);
            var wand = r.Moves.Single(mv => mv.Item.Made != null && mv.Item.Made.Ability == "levitate").Item;
            Assert.Equal(0x9B, wand.Item);
            Assert.Equal(0x200 + 32, wand.Record.Link);
            Assert.Contains("Made anew", r.Log.ToString());
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var made = edits.Model(wand.Level).Objects[wand.Index];
            Assert.Equal(0x9B, made.Item);
            Assert.True(made.IsQuantity && made.Enchanted);
            var src = Abilities.Find(s, edits.Model, null).Single(x => x.Level == wand.Level && x.Index == wand.Index);
            Assert.Equal("perm:levitate", src.Token);
            Assert.Empty(KeyShuffle.Undo(r, edits));
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
        }

        /// <summary>Bad squares are refused with the reason: water, a trigger (the Avatar's cell, level 1 (42, 38)), a cell nothing opens (Fissif's, (38, 38)), and the Prison Tower's floors from which level 14's plot trigger comes before the sewers'.</summary>
        [Fact]
        public void Bad_uw2_starts_are_refused_with_the_reason()
        {
            var s = Session(); if (s == null) return;
            var p = StartPicker.For(s, l => s.LoadLevel(l));
            var terrain = CreatureTerrain.TryLoad(s);
            var m = s.LoadLevel(1);
            (int X, int Y)? water = null;
            for (int y = 1; y < 63 && water == null; y++) for (int x = 1; x < 63; x++) if (!m.Tiles[x, y].IsSolid && CreatureTerrain.FloorClass(s, terrain, m, x, y) == CreatureTerrain.Water) { water = (x, y); break; }
            Assert.NotNull(water);
            Assert.Equal("water", p.SquareProblem(1, water.Value.X, water.Value.Y));
            Assert.StartsWith("a trigger", p.Check(1, 42, 38).Reason);
            Assert.StartsWith("the solve from here misses the goal", p.Check(1, 38, 38).Reason);
            Assert.Contains("castle plot's trap that sets it to 3", p.Check(14, 30, 29).Reason);
            // through the scatter: refused at once, with the reason
            var r = KeyShuffle.RunOn(s, snapshot, new KeyShuffleOptions { Start = StartChoice.Given, StartAt = KeyShuffleOptions.GivenStart(1, water.Value.X, water.Value.Y) });
            Assert.False(r.Success);
            Assert.Contains("water", r.Refused);
            r = KeyShuffle.RunOn(s, snapshot, new KeyShuffleOptions { Start = StartChoice.Given, StartAt = KeyShuffleOptions.GivenStart(1, 38, 38) });
            Assert.False(r.Success);
            Assert.Contains("misses the goal", r.Refused);
        }

        /// <summary>The note for a start in a world the gem does not open at plot 0: every world but Britannia and the Prison Tower, with the plot that opens it (event/WORLDEV.C black_gem_rotate: 1, 3, 6 or 8 facets below plot 4, 8, 13).</summary>
        [Fact]
        public void A_start_in_a_world_the_gem_opens_later_carries_a_warning()
        {
            var s = Session(); if (s == null) return;
            foreach (int l in new[] { 1, 5, 8, 9, 16 }) Assert.Null(KeyShuffle.LockedWorldNote(s, l));
            foreach (var (l, plot) in new[] { (17, 4), (26, 4), (34, 8), (44, 8), (50, 13), (58, 8), (69, 13) })
            {
                var n = KeyShuffle.LockedWorldNote(s, l);
                Assert.NotNull(n);
                Assert.Contains("until the castle plot reaches " + plot + " ", n);
                Assert.Contains("you cannot come back until then", n);
            }
            Assert.StartsWith("You start in the Scintillus Academy", KeyShuffle.LockedWorldNote(s, 44));
        }

        /// <summary>Britannia (levels 1 to 8) withholds every ability whose spells are all above circle 3 from a start there, whatever the circle option; elsewhere the circle option alone decides.</summary>
        [Fact]
        public void A_start_in_britannia_withholds_what_no_one_casts_there()
        {
            var s = Session(); if (s == null) return;
            var all = new AbilityOptions { NewCharacterCircle = AbilityOptions.MaxCircle };
            Assert.Empty(StartAbilities.Withheld(s, all));
            Assert.Empty(StartAbilities.Withheld(s, all, 9));
            var brit = StartAbilities.Withheld(s, all, 1);
            foreach (var ab in new[] { "levitate", "gate_travel", "open", "telekinesis", "lava" }) Assert.Contains(ab, brit);
            foreach (var ab in new[] { "jump", "waterwalk", "light" }) Assert.DoesNotContain(ab, brit);
            Assert.Contains("Britannia", StartAbilities.Why(s, all, "gate_travel", 1));
        }
    }
}
