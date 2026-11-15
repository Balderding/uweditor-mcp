using System;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The jump with the head counted: cut under the ceiling or an object's underside (JumpFlight), in the model, the zones and the Try Jump trace.</summary>
    public class JumpFlightTests
    {
        [Fact]
        public void With_nothing_overhead_the_flight_is_the_models()
        {
            foreach (var kind in new[] { JumpKind.Sprint, JumpKind.Standing })
                foreach (bool leap in new[] { false, true })
                {
                    var f = new JumpFlight(kind, leap, 0, 16);
                    Assert.False(f.Bumps);
                    Assert.Equal(JumpModel.Climb(leap, 0, 16), f.Climb);
                    for (double x = 0; x < 9; x += 0.25) Assert.Equal(JumpModel.Height(x, kind, leap, 0, 16), f.Height(x), 9);
                }
        }

        [Theory]
        [InlineData(JumpKind.Sprint, false, 1.0)]
        [InlineData(JumpKind.Standing, false, 0.5)]
        [InlineData(JumpKind.Sprint, true, 2.0)]
        [InlineData(JumpKind.Standing, true, 0.1)]
        public void Under_a_cap_the_feet_stay_below_it_and_the_jump_is_shorter(JumpKind kind, bool leap, double cap)
        {
            var free = new JumpFlight(kind, leap, 0, 16);
            var cut = new JumpFlight(kind, leap, 0, 16, cap);
            Assert.True(cut.Bumps);
            Assert.True(cut.BumpAt < free.PeakAt);
            Assert.True(cut.Peak <= cap + 1e-9);
            double last = double.MaxValue;
            for (double x = 0; x < 9; x += 0.05)
            {
                Assert.True(cut.Height(x) <= cap + 1e-9, "at " + x);
                Assert.True(cut.Height(x) <= free.Height(x) + 1e-9, "at " + x + ": the cut flight is above the free one");
                // after the bump it only falls
                if (x > cut.BumpAt) { Assert.True(cut.Height(x) <= last + 1e-9); last = cut.Height(x); }
            }
            // back at the take-off height sooner
            Func<JumpFlight, double> range = f => { for (double x = 0.05; x < 12; x += 0.01) if (f.Height(x) < 0) return x; return 12; };
            Assert.True(range(cut) < range(free));
        }

        [Theory]
        [InlineData(false, 13, 1)]      // 128 - 23 = 105 units: 0.1 level over a floor of height 13
        [InlineData(false, 12, 1)]
        [InlineData(false, 10, 3)]      // the peak, 2.5 levels, is under the cap of 3.1
        [InlineData(true, 10, 4)]       // the jump spell's 5.4 is cut to 3.1
        [InlineData(true, 8, 6)]        // cut to 5.1: one step less than the 6 it climbs in the open... but floor(5.1 + 1) is still 6
        [InlineData(true, 9, 5)]
        public void The_ceiling_cuts_the_step_a_jump_climbs(bool leap, int floor, int climb)
        {
            double cap = (128 - 23) / 8.0 - floor - 0.05;
            Assert.Equal(climb, new JumpFlight(JumpKind.Standing, leap, floor, 16, cap).Climb);
        }

        [Fact]
        public void The_trace_cuts_a_jump_under_the_ceiling()
        {
            // UW1 level 1: from (26, 25) at height 11 with the jump spell the head would pass the ceiling by 13 units
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var m = s.LoadLevel(1);
            for (int ty = 22; ty >= 20; ty--)
            {
                var t = JumpTrace.Try(m, 26, 25, 26, ty);
                // the feet never pass the ceiling less the player's height, whatever the jump
                Assert.All(t.Path, p => Assert.True(p.Y / LevelGeometry.Step <= 16 - JumpTrace.PlayerHeight + 1e-6, "feet at " + p.Y / LevelGeometry.Step));
                if (t.Lands && t.Leap) { Assert.Equal("the ceiling", t.Bumped); Assert.Contains("the head meeting the ceiling", t.Text); }
            }
        }

        [Fact]
        public void A_floor_of_height_14_is_no_place_to_stand_in_the_zones()
        {
            // UW2 level 25: (53, 16) and (54, 16) are floors of height 14 (112 + 23 is over 127), which a lever steps down to 10
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var graph = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), new[] { 25 });
            int r = graph.RegionOf(25, 53, 16);
            Assert.True(r >= 0);
            // nothing leads onto it or off it as it stands: only once the lever has stepped it down (NavGraph.FindOscillators)
            Assert.DoesNotContain(graph.Edges, e => e.To == r && e.Kind != "teleport" && e.Kind != "stairs" && !e.Requires.Contains("flag:oscillator_L25_742"));
            Assert.DoesNotContain(graph.Edges, e => e.From == r && e.Kind != "teleport" && e.Kind != "stairs" && !e.Requires.Contains("flag:oscillator_L25_742"));
            Assert.Contains(graph.Edges, e => e.To == r && e.Requires.Contains("flag:oscillator_L25_742"));
        }
    }
}
