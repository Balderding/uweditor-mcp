using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The key item shuffle with a random or given start (UW1): each attempt draws a start
    /// (StartPicker), the fill and the check solve from it, and Apply writes the start teleport.
    /// A run takes 5 to 10 seconds. The sweep over every viable zone runs only with
    /// UW_START_SWEEP=1 (UW_START_SWEEP_SEEDS seeds a zone, 3 by default; the report goes to
    /// UW_START_SWEEP_REPORT, else uw-start-sweep.txt in the temporary folder).
    /// </summary>
    public class KeyShuffleStartTests
    {
        readonly ITestOutputHelper output;
        public KeyShuffleStartTests(ITestOutputHelper output) { this.output = output; }

        static GameSession Session() { string g = GameData.Game("UW"); return g == null ? null : GameSession.Open(g); }

        /// <summary>Applies a successful shuffle to fresh edits and solves the levels afresh, with the game's own options: the start comes from the teleport.</summary>
        static TimelineResult Applied(GameSession s, KeyShuffleResult r, out LevelEdits edits)
        {
            edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            return Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, variants: false);
        }

        [Theory]
        [InlineData(1, StartScope.AnyLevel)]
        [InlineData(2, StartScope.AnyLevel)]
        [InlineData(3, StartScope.FirstLevels)]
        public void A_random_start_shuffles_and_the_fresh_solve_from_it_reaches_the_goal(int seed, StartScope scope)
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = seed, Start = StartChoice.Random, StartScope = scope });
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.NotNull(r.Start);
            if (scope == StartScope.FirstLevels) Assert.Equal(1, r.Start.Level);
            Assert.Equal((1, 32, 2), (r.StartFrom.Level, (int)r.StartFrom.X, (int)r.StartFrom.Y));
            Assert.True(r.StartCheck.Viable, r.StartCheck.Reason);
            // the check solved from the start
            Assert.Equal(r.CheckBuild.Set.ZoneAt(r.Start.Level, (int)r.Start.X, (int)r.Start.Y), r.CheckBuild.Set.StartZone);
            string log = r.Log.ToString();
            Assert.Contains(string.Format("level {0} ({1}, {2})", r.Start.Level, (int)r.Start.X, (int)r.Start.Y), log);
            Assert.Contains("teleports", log);
            Assert.Contains("The game puts you on level 1 (32, 2) as always", log);
            LevelEdits edits;
            var t = Applied(s, r, out edits);
            var tele = StartTeleport.Find(s, edits.Model);
            Assert.NotNull(tele);
            Assert.Equal((r.Start.Level, (int)r.Start.X, (int)r.Start.Y), (tele.ToLevel, tele.ToX, tele.ToY));
            Assert.Contains(r.AppliedAids, a => a.Contains("teleports the player there"));
            Assert.Equal(t.Set.ZoneAt(r.Start.Level, (int)r.Start.X, (int)r.Start.Y), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Empty(t.Solve.CutOff);
            Assert.DoesNotContain(t.Solve.Traps, x => x.Strands.Count > 0);
            // Undo takes the teleport back with the items
            Assert.Empty(KeyShuffle.Undo(r, edits));
            Assert.Null(StartTeleport.Find(s, edits.Model));
        }

        /// <summary>
        /// A given start the unshuffled game cannot be finished from: level 3 (9, 55), in the water
        /// area at (2, 51), whose way out needs what lies elsewhere (from there the unshuffled game
        /// reaches 7 zones). The fill puts what is needed within reach (seed 4; seeds 1 to 3 fail
        /// the fill and the retries would go on to it).
        /// </summary>
        [Fact]
        public void A_start_the_unshuffled_game_cannot_finish_from_gets_its_way_out_placed()
        {
            var s = Session(); if (s == null) return;
            var at = new StartPoint { Level = 3, X = 9.5, Y = 55.5, Description = "test start" };
            var bare = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = at }, new AbilityOptions(), null, null, variants: false);
            Assert.False(bare.Solve.GoalReached);
            Assert.True(Timeline.BaseZonesReached(bare.Solve) < 10);
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 4, Retries = 0, Start = StartChoice.Given, StartAt = at });
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.Equal(4, r.UsedSeed);
            // some key item went to a place the bare solve reaches
            var reached = new HashSet<int>(bare.Solve.ZoneSphere.Keys);
            Assert.Contains(r.Moves, m => m.ByFill && reached.Contains(bare.Set.ZoneAt(m.Place.Level, m.Place.X, m.Place.Y)));
            LevelEdits edits;
            var t = Applied(s, r, out edits);
            Assert.Equal(t.Set.ZoneAt(3, 9, 55), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Empty(t.Solve.CutOff);
        }

        [Fact]
        public void A_given_start_on_water_is_refused_with_the_reason()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Start = StartChoice.Given, StartAt = new StartPoint { Level = 1, X = 40.5, Y = 30.5 } });
            Assert.False(r.Success);
            Assert.Contains("water", r.Refused);
        }

        /// <summary>
        /// The sweep: a square of every viable UW1 zone (any level), each shuffled with a few seeds
        /// and no retries; reports the share that fill and why the others fail. Off unless
        /// UW_START_SWEEP=1 (about 40 minutes with 3 seeds).
        /// </summary>
        [Fact]
        public void Sweep_every_viable_zone()
        {
            if (Environment.GetEnvironmentVariable("UW_START_SWEEP") != "1") return;
            var s = Session(); if (s == null) return;
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("UW_START_SWEEP_SEEDS"), out int n) && n > 0 ? n : 3;
            string report = Environment.GetEnvironmentVariable("UW_START_SWEEP_REPORT");
            if (string.IsNullOrEmpty(report)) report = Path.Combine(Path.GetTempPath(), "uw-start-sweep.txt");
            var p = StartPicker.For(s, l => s.LoadLevel(l));
            var verdicts = p.Sweep(StartScope.AnyLevel);
            var lines = new List<string>();
            int runs = 0, filled = 0, zonesAll = 0, zonesNone = 0;
            var reasons = new Dictionary<string, int>();
            var started = DateTime.Now;
            foreach (var kv in verdicts.Where(v => v.Value.Viable))
            {
                var squares = p.Candidates(StartScope.AnyLevel).Where(c => c.Zone == kv.Key).ToList();
                var sq = squares[squares.Count / 2];
                int ok = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = seed, Retries = 0, Start = StartChoice.Given, StartAt = new StartPoint { Level = sq.Level, X = sq.X + 0.5, Y = sq.Y + 0.5 } });
                    runs++;
                    if (r.Success) { ok++; filled++; continue; }
                    string why = r.Refused ?? (r.Failures.Count > 0 ? r.Failures[0] : "?");
                    why = System.Text.RegularExpressions.Regex.Replace(why, @"^seed -?\d+: ", "");
                    string key = why.StartsWith("no place left") ? "no place left that the logic reaches for an item" : why.Split('(')[0].Trim();
                    int c; reasons.TryGetValue(key, out c); reasons[key] = c + 1;
                    lines.Add(string.Format("  L{0} ({1}, {2}) seed {3}: {4}", sq.Level, sq.X, sq.Y, seed, why));
                }
                if (ok == seeds) zonesAll++;
                if (ok == 0) zonesNone++;
                lines.Add(string.Format("{0} at L{1} ({2}, {3}): {4} of {5} seeds fill", SolveResult.ZoneName(p.T.Set, kv.Key), sq.Level, sq.X, sq.Y, ok, seeds));
            }
            var head = new List<string>
            {
                string.Format("UW1 random start sweep: {0} viable zones of {1} (any level), {2} seeds each, {3} runs, {4} filled ({5:0.0} %); {6} zones filled with every seed, {7} with none; {8:0} minutes.",
                    verdicts.Count(v => v.Value.Viable), verdicts.Count, seeds, runs, filled, runs == 0 ? 0 : filled * 100.0 / runs, zonesAll, zonesNone, (DateTime.Now - started).TotalMinutes),
                "Failures: " + string.Join("; ", reasons.OrderByDescending(x => x.Value).Select(x => x.Value + " x " + x.Key)),
            };
            File.WriteAllLines(report, head.Concat(lines));
            foreach (var l in head) output.WriteLine(l);
            Assert.True(runs > 0);
        }
    }
}
