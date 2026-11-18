using System;
using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Run and Test, prepared only: the squares a check uses, the play test's, check's and spot test's copies written to a new folder; nothing is started.</summary>
    public class RandomizerRunTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        /// <summary>A folder beside the game copy (removed with the copies by a later run, as GameCopy's).</summary>
        static string NewFolder(McpEditor ed) { return Path.Combine(Path.GetDirectoryName(ed.Session.GameFolder), "uwmcp-rzrun-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }

        static (int X, int Y) Square(McpEditor ed)
        {
            string s = ed.RunTestStartSquares(1);
            int a = s.IndexOf('('), c = s.IndexOf(','), e = s.IndexOf(')');
            return (int.Parse(s.Substring(a + 1, c - a - 1)), int.Parse(s.Substring(c + 1, e - c - 1).Trim()));
        }

        [Fact]
        public void Paths_and_squares()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Contains("Nothing was started", ed.RunTestPaths());
            Assert.Throws<McpEditorException>(() => ed.RunTestPathsSet("x", null, null, null, null));
            Assert.Contains("mount c", ed.RunTestDosBoxCommand());
            var sq = Square(ed);
            Assert.Contains("is a square the player can be sent to", ed.RunTestStandable(1, sq.X, sq.Y));
            Assert.Contains("not one", ed.RunTestStandable(1, 0, 0));
            Assert.Contains("level 1: (", ed.RunTestStops("1", true, true));
            Assert.Throws<McpEditorException>(() => ed.RunTestStops(null, null, null));
            Assert.Contains("uw1port.cfg", ed.RunTestExhumedSettings(null));
        }

        [Fact]
        public void Play_test_written_to_a_new_folder()
        {
            var ed = Opened(); if (ed == null) return;
            var sq = Square(ed);
            string f = NewFolder(ed);
            string said = ed.RunTestPlayTest(1, sq.X, sq.Y, f, false);
            Assert.Contains("Play test ready in", said);
            Assert.Contains("-conf", said);
            Assert.True(File.Exists(Path.Combine(f, "dosbox-playtest.conf")));
            Assert.True(Directory.Exists(Path.Combine(f, "SAVE1")));
            // a folder with files is never cleared
            Assert.Throws<McpEditorException>(() => ed.RunTestPlayTest(1, sq.X, sq.Y, f, false));
            Assert.Throws<McpEditorException>(() => ed.RunTestPlayTest(1, sq.X, sq.Y, ed.Session.GameFolder, false));
            string g = NewFolder(ed);
            Assert.Contains("--data", ed.RunTestPlayTest(1, sq.X, sq.Y, g, true));
        }

        [Fact]
        public void Exhumed_check_and_spot_test_prepared()
        {
            var ed = Opened(); if (ed == null) return;
            string f = NewFolder(ed);
            string said = ed.RunTestExhumedCheck("1", f, true, true, null, null, null, false);
            Assert.Contains("--input-script", said);
            Assert.True(File.Exists(Path.Combine(f, "script.txt")));
            string s = NewFolder(ed);
            string spot = ed.RunTestSpotStart("1,2", s, 30, 30, 30, 30, true, 2, false, false);
            Assert.Contains("Round written (spots 1-2", spot);
            Assert.True(File.Exists(Path.Combine(s, "SAVE1", "PLAYER.DAT")));
            Assert.Contains("Every spot is done", ed.RunTestSpotRound("fine", null));
            Assert.Contains("Round written (spots 1-", ed.RunTestSpotRound(null, 1));
        }
    }
}
