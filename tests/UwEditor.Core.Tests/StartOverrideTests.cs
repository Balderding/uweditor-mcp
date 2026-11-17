using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// NavOptions.StartOverride: the solve over time from another square than the game's own.
    /// UW2 with the default options: the game's start and squares the game can put the Avatar on
    /// (the blackrock gem's facets) reach the end; four places locked in from outside do not.
    /// Each case builds the zones once (about ten seconds for UW2).
    /// </summary>
    public class StartOverrideTests
    {
        static TimelineResult From(GameSession s, int level, int x, int y, bool death = true)
        {
            var nav = new NavOptions { StartOverride = new StartPoint { Level = level, X = x + 0.5, Y = y + 0.5, Description = "test start" } };
            return Timeline.Build(s, l => s.LoadLevel(l), nav, new AbilityOptions { DeathReturns = death }, null, null, variants: false);
        }

        [Fact]
        public void The_games_own_start_given_as_the_override_reaches_the_end()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var own = s.NewGameStart ?? s.Start;
            var t = From(s, own.Level, (int)own.X, (int)own.Y);
            Assert.Equal(t.Set.ZoneAt(own.Level, (int)own.X, (int)own.Y), t.Set.StartZone);
            string report = Timeline.Report(t);
            Assert.Contains("Start: test start, level " + own.Level + " (" + (int)own.X + ", " + (int)own.Y + ")", report);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
            Assert.Empty(t.Solve.CutOff);
            Assert.True(t.Solve.ZoneSphere.Count > 330, t.Solve.ZoneSphere.Count + " zones");
            // the report counts the zones reached of the zone set (UW2's ids are level * 256 + number), not those below 1000
            int reached = Timeline.BaseZonesReached(t.Solve);
            Assert.True(reached > 330, reached + " zones");
            Assert.Contains(string.Format(" {0} of {1} zones reached", reached, t.Set.Zones.Count(z => z.Id > ZoneSet.Unassigned)), report);
        }

        /// <summary>An open square of the castle (level 1 (40, 30)), and where the gem's facets of the Prison Tower and the Pits land (level 5 trigger #934, hack 55).</summary>
        [Theory]
        [InlineData(1, 40, 30)]
        [InlineData(9, 33, 32)]
        [InlineData(57, 59, 20)]
        public void Another_start_reaches_the_end(int level, int x, int y)
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = From(s, level, x, y);
            Assert.Equal(t.Set.ZoneAt(level, x, y), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
            Assert.Empty(t.Solve.CutOff);
            Assert.True(t.Solve.ZoneSphere.Count > 330, t.Solve.ZoneSphere.Count + " zones");
        }

        /// <summary>
        /// The castle's two jail cells: Fissif's at level 1 (38, 38), behind a portcullis nothing
        /// opens, and the Avatar's at (42, 38) (event/WORLDEV.C put_player_in_jail), whose
        /// portcullis only Lord British opens, in a talk he holds only with one the castle guards
        /// have jailed (conversation 142: quest 112, which put_player_in_jail alone sets, then
        /// gronk_door(41, 38, 0)); a start there has no quest 112, so no release. The top of the
        /// Prison Tower (level 16 (33, 33): down to level 14, then locked doors; the guard who
        /// opens the one at (29, 32) for the password stands on its other side) and the round room
        /// of level 18 behind the door at (25, 28) (key 9 or a pick of 13): no way out, as in the game.
        /// </summary>
        [Theory]
        [InlineData(1, 38, 38)]
        [InlineData(1, 42, 38)]
        [InlineData(16, 33, 33)]
        [InlineData(18, 29, 28)]
        public void A_start_locked_in_does_not_reach_the_end(int level, int x, int y)
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // without dying as a way back to the gem (0.1.52): with it, the starts of levels 16 and 18 are no longer shut in
            var t = From(s, level, x, y, death: false);
            Assert.Equal(t.Set.ZoneAt(level, x, y), t.Set.StartZone);
            Assert.False(t.Solve.GoalReached);
            Assert.True(t.Solve.ZoneSphere.Count < 10, t.Solve.ZoneSphere.Count + " zones");
        }

        [Fact]
        public void A_UW1_start_on_level_2_reaches_the_end()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = From(s, 2, 9, 35);
            Assert.Equal(t.Set.ZoneAt(2, 9, 35), t.Set.StartZone);
            Assert.NotEqual(t.Set.ZoneAt(1, 32, 2), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
        }
    }
}
