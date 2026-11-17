using System;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The walkthrough's order around one-way ways (0.1.52): the order by spheres does not know
    /// that a one-way way gives up what lies behind it, so from two UW2 starts it took one while a
    /// step behind it was still to do (NO WAY FOUND). The walkthrough now walks its order and, when
    /// that fails, makes it again by walking (Walkthrough's Reorder): such a way waits until every
    /// step behind it is done. A run takes about 30 s.
    /// </summary>
    public class Uw2WalkOrderTests
    {
        readonly ITestOutputHelper output;
        public Uw2WalkOrderTests(ITestOutputHelper output) { this.output = output; }

        /// <summary>
        /// The Scintillus Academy, level 46 (49, 43) seed 1: the teleport at (60, 39) into the room at
        /// (53, 31) is one way, and the room's stairs to level 47 must be taken before the way on to
        /// level 41 (the order by spheres went to levels 41 and 42 first and could not get back). The
        /// Ethereal Void, level 65 (37, 27) seed 1: the stairs into the pocket at (44, 52) lead to
        /// level 71, from where there is no way back to the Void's way out; the pocket waits until
        /// the world is left and opened again.
        /// </summary>
        [Theory]
        [InlineData(46, 49, 43, 1)]
        [InlineData(65, 37, 27, 1)]
        public void The_walkthrough_from_a_start_with_one_way_ways_can_be_walked(int level, int x, int y, int seed)
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var r = KeyShuffle.RunOn(s, KeyShuffle.Snapshot(s, null), new KeyShuffleOptions { Seed = seed, Retries = 3, Start = StartChoice.Given, StartAt = KeyShuffleOptions.GivenStart(level, x, y) });
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            var w = r.Walkthrough;
            foreach (var l in w.Lines().Take(60)) output.WriteLine(l);
            foreach (var n in w.NoWay) output.WriteLine("NO WAY: " + n);
            Assert.True(w.Reached);
            Assert.Empty(w.NoWay);
            // the walk from each step to the next held all the way (Routes), and the order is the walked one
            Assert.True(w.Walkable);
            Assert.True(w.Reordered);
            Assert.DoesNotContain(w.Steps, st => st.Route.Any(rt => rt.StartsWith("NO WAY FOUND")));
            for (int i = 1; i < w.Steps.Count - 1; i++) Assert.True(w.Steps[i].Sphere >= w.Steps[i - 1].Sphere, "step " + w.Steps[i].Number + " goes back a sphere");
            Assert.All(w.Steps, st => Assert.True(st.After.All(k => k < st.Number), "step " + st.Number + " relies on a later step"));
            string log = r.Log.ToString();
            Assert.DoesNotContain("WALKTHROUGH NOT FOLLOWABLE", log);
            Assert.Contains("Here some steps come before or after their sphere", log);
            Func<int, int, int, int> stepAt = (l, sx, sy) => w.Steps.FindIndex(st => st.Event != null && st.Event.Link != null && st.Event.Link.Level == l && st.Event.Link.X == sx && st.Event.Link.Y == sy);
            if (level == 46)
            {
                // the room's stairs to level 47 come before the stairs from the start's area to level 41
                int to47 = stepAt(46, 55, 33), to41 = stepAt(46, 42, 38);
                Assert.True(to47 > 0 && to41 > 0, "both stairs are steps");
                Assert.True(to47 < to41, "the stairs to level 47 (step " + (to47 + 1) + ") come before those to level 41 (step " + (to41 + 1) + ")");
            }
            else
            {
                // the pocket's stairs from level 69 (35, 41) come after the step that leaves the Void
                int pocket = stepAt(69, 35, 41), leave = w.Steps.FindIndex(st => st.Note.StartsWith("Leave the Ethereal Void now"));
                Assert.True(pocket > 0 && leave > 0, "the pocket's entry and the leaving are steps");
                Assert.True(pocket > leave, "the pocket (step " + (pocket + 1) + ") comes after the Void is left (step " + (leave + 1) + ")");
            }
        }
    }
}
