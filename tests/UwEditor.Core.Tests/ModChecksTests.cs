using System;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The checks run when a mod is made (ModChecks): the list check, the Fixes and the world check, problems first.</summary>
    public class ModChecksTests
    {
        [Theory]
        [InlineData("UW", 9)]
        [InlineData("UW2", 80)]
        public void The_shipped_game_is_checked_level_by_level_and_a_broken_list_is_reported_first(string game, int most)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var shipped = ModChecks.Run(s);
            Assert.InRange(shipped.LevelsChecked, 9, most);
            // the shipped levels all pass the game's own list check
            Assert.DoesNotContain(shipped.Problems, p => p.Contains("fails the game's object list check"));
            string report = shipped.Report();
            Assert.True(report.StartsWith("Checked ") || report.StartsWith("CHECK FIRST"), report);

            // a level whose list is broken: a tile's list made to loop back on itself
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            var o = e.Model.Objects.First(x => !x.Free && x.OnTile && x.Next == 0 && !x.IsMobile);
            e.EditBytes("break", (d, t) =>
            {
                int at = UltimateEditor.Games.LevelBlock.ObjectOffset(o.Index) + 4;
                int w = d[at] | d[at + 1] << 8;
                w = (w & 0x3F) | o.Index << 6;                // its next is itself
                d[at] = (byte)w; d[at + 1] = (byte)(w >> 8);
            });
            var broken = ModChecks.Run(s, l => edits.Model(l));
            Assert.True(broken.Problems.Count > shipped.Problems.Count);
            Assert.Contains("Level 1", broken.Problems[0]);
            Assert.StartsWith("CHECK FIRST", broken.Report());
            Assert.Contains(broken.Problems, p => p.StartsWith("Level 1"));
        }
    }
}
