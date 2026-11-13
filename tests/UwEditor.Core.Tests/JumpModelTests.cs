using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The jump formula against the game's own arithmetic: Data/jump-sim.csv is written by
    /// Data/jump_table.py from Data/jump_sim.py, which steps the jump tick by tick as
    /// motion/PHYSICS.C and MOTION.C do. The model may say less than the game, never more.
    /// </summary>
    public class JumpModelTests
    {
        sealed class Row
        {
            public JumpKind Kind; public bool Leap; public int Floor, Dt; public double Peak;
            /// <summary>By dh (-4 .. 6): the centre's distance where the feet come down to that floor; NaN: never that high.</summary>
            public Dictionary<int, double> Range = new Dictionary<int, double>();
        }

        static List<Row> Table()
        {
            var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", "jump-sim.csv"));
            var rows = new List<Row>();
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
            {
                var c = line.Split(',');
                var r = new Row { Kind = c[0] == "sprint" ? JumpKind.Sprint : JumpKind.Standing, Leap = c[1] == "1", Floor = int.Parse(c[2]), Dt = int.Parse(c[3]), Peak = double.Parse(c[4], CultureInfo.InvariantCulture) };
                for (int dh = -4; dh <= 6; dh++)
                    r.Range[dh] = c[5 + dh + 4].Length == 0 ? double.NaN : double.Parse(c[5 + dh + 4], CultureInfo.InvariantCulture);
                rows.Add(r);
            }
            return rows;
        }

        [Fact]
        public void The_table_covers_every_jump()
        {
            var t = Table();
            Assert.Equal(2 * 2 * 4 * 5, t.Count);
        }

        [Fact]
        public void The_peak_is_the_games_and_never_above_it()
        {
            foreach (var r in Table())
            {
                double p = JumpModel.Peak(r.Leap, r.Floor, r.Dt);
                Assert.True(p <= r.Peak + 1e-6, Name(r) + ": peak " + p + " above the game's " + r.Peak);
                Assert.True(r.Peak - p <= 2 * JumpModel.Sag(r.Leap, r.Dt) + 0.08, Name(r) + ": peak " + p + " far under the game's " + r.Peak);
            }
        }

        [Fact]
        public void The_range_is_the_games_and_never_beyond_it()
        {
            foreach (var r in Table())
                for (int dh = -4; dh <= 6; dh++)
                {
                    double x = JumpModel.Range(dh, r.Kind, r.Leap, r.Floor, r.Dt), sim = r.Range[dh];
                    string what = Name(r) + " dh " + dh;
                    if (double.IsNaN(sim)) { Assert.True(double.IsNaN(x), what + ": the game never gets that high, the model says " + x); continue; }
                    if (double.IsNaN(x)) { Assert.True(r.Peak - dh < 2 * JumpModel.Sag(r.Leap, r.Dt) + 0.08, what + ": the model never gets there, the game does at " + sim); continue; }
                    Assert.True(x <= sim + 1e-6, what + ": " + x + " beyond the game's " + sim);
                    // near the peak a little height is a lot of distance; elsewhere the two agree closely
                    if (r.Peak - dh > 0.5) Assert.True(sim - x < 0.02 + 0.5 * r.Dt / 64.0, what + ": " + x + " far short of the game's " + sim);
                }
        }

        [Fact]
        public void Every_gap_the_model_crosses_the_game_crosses()
        {
            // the pass or fail grid: a gap of g tiles to a floor dh levels away is crossed when the centre
            // gets g - 2 * radius on (take-off with the back corners on the ledge, landing on the front ones)
            int passes = 0, cautious = 0;
            foreach (var r in Table())
                for (int g = 1; g <= 9; g++)
                    for (int dh = -4; dh <= 0; dh++)
                    {
                        double need = g - 2 * JumpModel.Radius;
                        bool model = JumpModel.Range(dh, r.Kind, r.Leap, r.Floor, r.Dt) >= need, game = r.Range[dh] >= need;
                        Assert.False(model && !game, Name(r) + " gap " + g + " dh " + dh + ": the model crosses, the game does not");
                        if (model) passes++; else if (game) cautious++;
                    }
            Assert.True(passes > 1000);
            Assert.True(cautious < passes / 20, cautious + " crossings of the game's the model refuses, of " + passes);
        }

        [Theory]
        [InlineData(false, 0, 16, 3)]
        [InlineData(false, 10, 16, 3)]
        [InlineData(false, 11, 16, 2)]
        [InlineData(false, 12, 16, 1)]
        [InlineData(false, 15, 16, 1)]
        [InlineData(true, 0, 16, 6)]
        [InlineData(true, 11, 16, 4)]
        [InlineData(true, 12, 16, 2)]
        [InlineData(false, 0, 64, 2)]
        [InlineData(true, 0, 64, 5)]
        public void The_highest_step_a_jump_gets_onto(bool leap, int floor, int dt, int levels)
        {
            Assert.Equal(levels, JumpModel.Climb(leap, floor, dt));
        }

        [Fact]
        public void The_run_up_at_full_load()
        {
            // 18 ticks of 48: the handoff's table (1.00, 1.99, 3.99 and 7.97 tiles at dt 8, 16, 32 and 64)
            Assert.Equal(1.00, JumpModel.RunUp(8), 2);
            Assert.Equal(1.99, JumpModel.RunUp(16), 2);
            Assert.Equal(7.97, JumpModel.RunUp(64), 2);
        }

        static string Name(Row r) { return string.Format("{0}{1} floor {2} dt {3}", r.Kind, r.Leap ? " leap" : "", r.Floor, r.Dt); }
    }
}
