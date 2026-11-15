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
        /// The sweep: a square of every viable zone (any level), each shuffled with a few seeds and no
        /// retries; reports the share that fill and why the others fail. Off unless UW_START_SWEEP=1
        /// (UW1: about 40 minutes with 3 seeds). UW_START_SWEEP_GAME=UW2 takes UW2, where a run takes
        /// some 20 s: a sample instead, every castle zone and UW_START_SWEEP_SAMPLE (30) zones of the
        /// other worlds drawn at random, 2 seeds by default. Each run's line is written to the report
        /// as it ends, so a sweep cut short keeps what it did.
        /// </summary>
        [Fact]
        public void Sweep_every_viable_zone()
        {
            if (Environment.GetEnvironmentVariable("UW_START_SWEEP") != "1") return;
            bool uw2 = Environment.GetEnvironmentVariable("UW_START_SWEEP_GAME") == "UW2";
            string g = GameData.Game(uw2 ? "UW2" : "UW");
            if (g == null) return;
            var s = GameSession.Open(g);
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("UW_START_SWEEP_SEEDS"), out int n) && n > 0 ? n : uw2 ? 2 : 3;
            int sample = int.TryParse(Environment.GetEnvironmentVariable("UW_START_SWEEP_SAMPLE"), out int k) && k >= 0 ? k : 30;
            string report = Environment.GetEnvironmentVariable("UW_START_SWEEP_REPORT");
            if (string.IsNullOrEmpty(report)) report = Path.Combine(Path.GetTempPath(), uw2 ? "uw2-start-sweep.txt" : "uw-start-sweep.txt");
            var started = DateTime.Now;
            File.WriteAllText(report, string.Format("{0} random start sweep, started {1:yyyy-MM-dd HH:mm}\n", uw2 ? "UW2" : "UW1", started));
            Action<string> write = line => { File.AppendAllText(report, line + "\n"); };
            var p = StartPicker.For(s, l => s.LoadLevel(l));
            var snapshot = KeyShuffle.Snapshot(s, null);
            // UW1 every viable zone; UW2 every castle zone and a sample of the other worlds' (drawn the same way each time)
            var zones = new List<int>();
            if (!uw2) zones.AddRange(p.Sweep(StartScope.AnyLevel).Keys);
            else
            {
                var castle = p.Candidates(StartScope.FirstLevels).Select(c => c.Zone).Distinct().OrderBy(z => z).ToList();
                var others = p.Candidates(StartScope.AnyLevel).Select(c => c.Zone).Distinct().Where(z => !castle.Contains(z)).OrderBy(z => z).ToList();
                var rng = new Random(2026);
                for (int i = others.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = others[i]; others[i] = others[j]; others[j] = t; }
                zones.AddRange(castle);
                zones.AddRange(others.Take(sample));
            }
            var verdicts = zones.ToDictionary(z => z, z => p.CheckZone(z));
            int runs = 0, filled = 0, zonesAll = 0, zonesNone = 0;
            var reasons = new Dictionary<string, int>();
            foreach (var kv in verdicts)
            {
                var squares = p.Candidates(StartScope.AnyLevel).Where(c => c.Zone == kv.Key).ToList();
                var sq = squares[squares.Count / 2];
                if (!kv.Value.Viable) { write(string.Format("{0} at L{1} ({2}, {3}): not viable ({4})", SolveResult.ZoneName(p.T.Set, kv.Key), sq.Level, sq.X, sq.Y, kv.Value.Reason)); continue; }
                int ok = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var t0 = DateTime.Now;
                    var r = KeyShuffle.RunOn(s, snapshot, new KeyShuffleOptions { Seed = seed, Retries = 0, Start = StartChoice.Given, StartAt = new StartPoint { Level = sq.Level, X = sq.X + 0.5, Y = sq.Y + 0.5 } });
                    runs++;
                    string extra = string.Join("", r.Objects.Where(o => o.Made != null && r.Moves.Any(m => m.Item == o)).Select(o => "; made " + o.Name + " (" + o.Made.Id + ")"))
                        + (r.MovedIntoReach.Count > 0 ? "; " + r.MovedIntoReach.Count + " place(s) moved into reach" : "") + string.Format("; {0:0} s", (DateTime.Now - t0).TotalSeconds);
                    if (r.Success) { ok++; filled++; write(string.Format("  L{0} ({1}, {2}) seed {3}: filled{4}", sq.Level, sq.X, sq.Y, seed, extra)); continue; }
                    string why = r.Refused ?? (r.Failures.Count > 0 ? r.Failures[0] : "?");
                    why = System.Text.RegularExpressions.Regex.Replace(why, @"^seed -?\d+: ", "");
                    string key = why.StartsWith("no place left") ? "no place left that the logic reaches for an item" : why.Split('(')[0].Trim();
                    int c; reasons.TryGetValue(key, out c); reasons[key] = c + 1;
                    write(string.Format("  L{0} ({1}, {2}) seed {3}: {4}{5}", sq.Level, sq.X, sq.Y, seed, why, extra));
                }
                if (ok == seeds) zonesAll++;
                if (ok == 0) zonesNone++;
                write(string.Format("{0} at L{1} ({2}, {3}): {4} of {5} seeds fill", SolveResult.ZoneName(p.T.Set, kv.Key), sq.Level, sq.X, sq.Y, ok, seeds));
            }
            var head = new List<string>
            {
                string.Format("{0} random start sweep: {1} viable zones of {2} looked at (any level), {3} seeds each, {4} runs, {5} filled ({6:0.0} %); {7} zones filled with every seed, {8} with none; {9:0} minutes.",
                    uw2 ? "UW2" : "UW1", verdicts.Count(v => v.Value.Viable), verdicts.Count, seeds, runs, filled, runs == 0 ? 0 : filled * 100.0 / runs, zonesAll, zonesNone, (DateTime.Now - started).TotalMinutes),
                "Failures: " + string.Join("; ", reasons.OrderByDescending(x => x.Value).Select(x => x.Value + " x " + x.Key)),
            };
            foreach (var l in head) { write(l); output.WriteLine(l); }
            Assert.True(runs > 0);
        }
    }
}
