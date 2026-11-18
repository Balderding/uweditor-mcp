using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Run and Test, without starting anything: the programs and paths, the squares a check can use, and the copies a test runs in.</summary>
    public sealed partial class EditorTools
    {
        const string RzNoLaunch = " Nothing is started: launching DOSBox or Underworld Exhumed is left to the user.";

        [McpServerTool(Name = "runtest_paths"), Description("Run and Test > Programs and paths: DOSBox and the Underworld Exhumed ports (UW1, UW2) chosen and the ones that would be used, their extra arguments, where the DOSBox configuration comes from, the editor's settings for the port." + RzNoLaunch)]
        public CallToolResult RuntestPaths() { return Answer(() => ed.RunTestPaths()); }

        [McpServerTool(Name = "runtest_paths_set"), Description("Sets Programs and paths (the editor's own settings, so only in the editor's window): a program path (empty: let the editor look for it) or the extra arguments; leave out what stays." + RzNoLaunch)]
        public CallToolResult RuntestPathsSet([Description("DOSBox's program.")] string dosbox = null, [Description("DOSBox's extra arguments.")] string dosboxArgs = null,
            [Description("Underworld Exhumed's uw1port.")] string exhumedUw1 = null, [Description("Underworld Exhumed's uw2port.")] string exhumedUw2 = null,
            [Description("Underworld Exhumed's extra arguments (--scale 4...).")] string exhumedArgs = null)
        {
            return Answer(() => ed.RunTestPathsSet(dosbox, dosboxArgs, exhumedUw1, exhumedUw2, exhumedArgs));
        }

        [McpServerTool(Name = "runtest_exhumed_settings"), Description("The editor's settings for Underworld Exhumed (key=value lines written into the port's uw1port.cfg/uw2port.cfg in the folder the game runs in, before a run).")]
        public CallToolResult RuntestExhumedSettings([Description("UW1 or UW2; leave out for the open game's.")] string game = null) { return Answer(() => ed.RunTestExhumedSettings(game)); }

        [McpServerTool(Name = "runtest_exhumed_settings_set"), Description("Sets the editor's settings for Underworld Exhumed (the whole text; # lines do nothing). In the editor's window only.")]
        public CallToolResult RuntestExhumedSettingsSet([Description("The settings text.")] string text, [Description("UW1 or UW2; leave out for the open game's.")] string game = null)
        {
            return Answer(() => ed.RunTestExhumedSettingsSet(game, text));
        }

        [McpServerTool(Name = "runtest_dosbox_command"), Description("What Run in DOSBox would use on the game folder: the DOSBox configuration (GOG's or the editor's defaults, C: on the game folder) and the command line." + RzNoLaunch)]
        public CallToolResult RuntestDosboxCommand() { return Answer(() => ed.RunTestDosBoxCommand()); }

        [McpServerTool(Name = "runtest_start_squares"), Description("Each level's square the Exhumed check and spot test send the player to (open, flat, its eight neighbours open at the same height, no water or lava, nothing on it), or that a level has none (its check is refused).")]
        public CallToolResult RuntestStartSquares([Description("A level; leave out for every level.")] int? level = null) { return Answer(() => ed.RunTestStartSquares(level)); }

        [McpServerTool(Name = "runtest_standable"), Description("Whether a square is one the player can be sent to (the check's test), whether a play test may start there, and the nearest square that can be used.")]
        public CallToolResult RuntestStandable([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y)
        {
            return Answer(() => ed.RunTestStandable(level, x, y));
        }

        [McpServerTool(Name = "runtest_stops"), Description("The stops a check in Underworld Exhumed or a spot test makes on levels: each level's square, then (with the options) one beside each edit and a sweep of the level.")]
        public CallToolResult RuntestStops([Description("Levels: 1,2,5, all, or edited (default: the levels with unsaved changes).")] string levels = null,
            [Description("Also beside the edits (default: the editor's setting).")] bool? edits = null, [Description("Also a sweep of each level.")] bool? sweep = null)
        {
            return Answer(() => ed.RunTestStops(levels, edits, sweep));
        }

        [McpServerTool(Name = "runtest_play_test"), Description("Play-test from a tile, prepared: a copy of the game in a new or empty folder with the unsaved levels and the player on the square (in a saved game of the copy; a full lantern added when he has none), with the DOSBox configuration or the port's settings written there and the command line to start it." + RzNoLaunch)]
        public CallToolResult RuntestPlayTest([Description("Level number, from 1.")] int level, [Description("1-62.")] int x, [Description("1-62.")] int y,
            [Description("A new or empty folder outside the game folder.")] string folder, [Description("For Underworld Exhumed instead of DOSBox.")] bool exhumed = false)
        {
            return Answer(() => ed.RunTestPlayTest(level, x, y, folder, exhumed));
        }

        [McpServerTool(Name = "runtest_exhumed_check"), Description("Check in Underworld Exhumed, prepared: in a new or empty folder the copied game, the port's home with saved game I holding the edits, the input script sending the player to each stop, and the command line with its environment. The port's result is read only by a run, which is left to the user." + RzNoLaunch)]
        public CallToolResult RuntestExhumedCheck([Description("A new or empty folder outside the game folder.")] string folder,
            [Description("Levels: 1,2,5, all, or edited (default).")] string levels = null, [Description("UW1: save in slot I at the end.")] bool saveAtEnd = true,
            [Description("Keep the player alive (level 16, strength, hit points before each stop).")] bool keepAlive = true,
            [Description("Also beside the edits.")] bool? edits = null, [Description("Also a sweep of each level.")] bool? sweep = null, [Description("Look round at each stop.")] bool? look = null,
            [Description("Show the game's window while it runs.")] bool showWindow = false)
        {
            return Answer(() => ed.RunTestExhumedCheck(levels, folder, saveAtEnd, keepAlive, edits, sweep, look, showWindow));
        }

        [McpServerTool(Name = "runtest_spot_start"), Description("DOSBox spot test (UW1), prepared: a copy in a new or empty folder, the spots (each level's square, beside edits, sweeps), the tester, and the first round of saved games SAVE1-4 (one a spot) with the DOSBox configuration and command line." + RzNoLaunch)]
        public CallToolResult RuntestSpotStart([Description("A new or empty folder outside the game folder.")] string folder,
            [Description("Levels: 1,2,5, all, or edited (default).")] string levels = null,
            [Description("1-99.")] int strength = 30, [Description("1-99.")] int dexterity = 30, [Description("1-99.")] int intelligence = 30, [Description("1-99.")] int defense = 30,
            [Description("Wear the best armour.")] bool bestArmour = true, [Description("Saved games a round, 1-4.")] int perRound = 4,
            [Description("Also beside the edits.")] bool? edits = null, [Description("Also a sweep of each level.")] bool? sweep = null)
        {
            return Answer(() => ed.RunTestSpotStart(levels, folder, strength, dexterity, intelligence, defense, bestArmour, perRound, edits, sweep));
        }

        [McpServerTool(Name = "runtest_spot_round"), Description("The spot test's next round: the round written last is logged as tested with the notes, and the next round's saved games are written; with again, the round from that spot is written again." + RzNoLaunch)]
        public CallToolResult RuntestSpotRound([Description("What was seen in the round (for the log).")] string notes = null,
            [Description("Write the round from this spot again (1-based) instead.")] int? again = null)
        {
            return Answer(() => ed.RunTestSpotRound(notes, again));
        }
    }
}
