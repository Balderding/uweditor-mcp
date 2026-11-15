using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The timeline's cut-off check (Timeline.Engine.StillReached): after a change that takes
    /// ways away, only the zones the player could still get to from where he stood just before it
    /// and cannot after it are reported. A zone already out of his reach before (a start left by a
    /// one-way way) is not one; a change that shuts him in is still found.
    /// </summary>
    public class CutOffTests
    {
        static TimelineResult From(GameSession s, int level, int x, int y)
        {
            var nav = new NavOptions { StartOverride = new StartPoint { Level = level, X = x + 0.5, Y = y + 0.5, Description = "test start" } };
            return Timeline.Build(s, l => s.LoadLevel(l), nav, new AbilityOptions(), null, null, variants: false);
        }

        /// <summary>The start on level 48 near (30, 30) (docs/UW2-RANDOMIZER-NEXT-STEPS.md 3): the start's zone is left one way by the stairs at (27, 31), and level 51's changes later found it "cut off".</summary>
        [Fact]
        public void A_UW2_start_on_level_48_reports_no_cut_off()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = From(s, 48, 30, 30);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Empty(t.Solve.CutOff);
        }

        /// <summary>
        /// Starts in zones the old check reported cut off (21 zones of UW2 did, with the default
        /// options, before 0.1.40; none does now): a ledge of level 42, rooms of levels 51 and 52.
        /// Each was out of reach from where the player stood when the change went off.
        /// </summary>
        [Fact]
        public void UW2_starts_the_old_check_found_cut_off_report_none()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = Timeline.Build(s, l => s.LoadLevel(l), null, new AbilityOptions(), null, null, variants: false);
            var p = new StartPicker(s, l => s.LoadLevel(l), t, new AbilityOptions(), null, true);
            foreach (var sq in new[] { (42, 20, 32), (51, 25, 19), (51, 24, 10), (52, 43, 15) })
            {
                int z = t.Set.ZoneAt(sq.Item1, sq.Item2, sq.Item3);
                Assert.True(z > ZoneSet.Unassigned, sq.ToString());
                var v = p.CheckZone(z);
                Assert.True(v.Viable, sq + ": " + v.Reason);
                Assert.DoesNotContain("cut off", v.Reason);
            }
        }

        /// <summary>
        /// A real cut-off is still found: UW1 level 1 (60, 49) is a dead end whose only open
        /// neighbour is (60, 50). A move trigger on the dead end that turns (60, 50) to rock (a
        /// change terrain trap: type 0, floor and height unchanged) shuts the player in when he
        /// walks in. Started in that area (level 1 (60, 51)), with magic off so that Gate Travel
        /// does not take him out, the area he stood in is cut off. (From the game's own start the
        /// trigger goes off together with a door of level 2, and the solver takes the player as
        /// standing at both; later, once the silver seed is held, death takes him back to the
        /// sapling: neither is a cut-off.)
        /// </summary>
        [Fact]
        public void A_change_that_shuts_the_player_in_is_a_cut_off()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var s = GameSession.Open(game);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            Assert.Equal(TileType.Open, e.Model.Tiles[60, 49].Type);
            Assert.False(e.Model.Tiles[60, 50].IsSolid);
            // change_terrain: type (heading * 2 + quality & 1) 0 solid, floor (quality >> 1) 11 unchanged, height (z >> 3) 14 unchanged, owner 63 wall unchanged, one square
            var trap = TrapRecipes.TrapSpec(0x185, 112);
            trap.Quality = 22; trap.Owner = 63;
            int ti = e.AddObject(trap, 60, 50);
            int gi = e.AddObject(TrapRecipes.TriggerSpec(TrapRecipes.MoveTrigger, 60, 50, new TriggerOptions(), null), 60, 49);
            TrapRecipes.SetLink(e, gi, ti);
            TrapRecipes.CountTriggers(e, ti);
            var nav = new NavOptions { StartOverride = new StartPoint { Level = 1, X = 60.5, Y = 51.5, Description = "test start" } };
            var t = Timeline.Build(s, edits.Model, nav, new AbilityOptions { Magic = false }, null, null, variants: false);
            Assert.Equal(t.Set.ZoneAt(1, 60, 49), t.Set.StartZone);
            Assert.Contains("flag:terrain_l1_" + gi, t.Solve.Fired);
            Assert.NotEmpty(t.Solve.CutOff);
            Assert.Contains(t.Set.StartZone, t.Solve.CutOff.SelectMany(c => c.Value));
            Assert.False(t.Solve.GoalReached);
        }
    }
}
