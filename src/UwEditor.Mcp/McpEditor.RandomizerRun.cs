using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// What the app holds for Run and Test: the program paths and arguments of Programs and paths
    /// (the editor's settings), the editor's settings for Underworld Exhumed, the check's
    /// thoroughness. Set by the app's window.
    /// </summary>
    public sealed class RandomizerRunHost
    {
        public Func<string> DosBoxPath; public Action<string> SetDosBoxPath;
        public Func<string> DosBoxArgs; public Action<string> SetDosBoxArgs;
        public Func<string> ExhumedArgs; public Action<string> SetExhumedArgs;
        public Func<GameKind, string> ExhumedChosen; public Action<GameKind, string> SetExhumedPort;
        public Func<GameKind, string> ExhumedSettingsText; public Action<GameKind, string> SetExhumedSettingsText;
        public Func<ExhumedOptions> Thoroughness; public Action<ExhumedOptions> SetThoroughness;
    }

    /// <summary>
    /// Run and Test without launching anything: the configured programs (read and set), the
    /// squares a check can send the player to, and the copies a play test, spot test or check in
    /// Underworld Exhumed runs in, written to a folder the caller names, with the command line to
    /// start it. Starting DOSBox or Underworld Exhumed is always left to the user.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>Attached: the app's Run and Test settings; null for this server alone.</summary>
        public RandomizerRunHost HostRun { get; set; }

        SpotTestRun rzSpot;

        const string RzLaunchNote = "Nothing was started: launching DOSBox or Underworld Exhumed is left to you.";

        static string RzQuote(string a) { return a.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0 ? "\"" + a.Replace("\"", "\\\"") + "\"" : a; }

        static string RzCommand(string program, IEnumerable<string> args) { return RzQuote(program ?? "(program not found)") + " " + string.Join(" ", args.Select(RzQuote)); }

        string RzDosBoxFound()
        {
            string chosen = HostRun?.DosBoxPath?.Invoke();
            if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return chosen;
            return PlayTest.FindDosBox(Session?.GameFolder);
        }

        string RzPortFound(GameKind kind)
        {
            string chosen = HostRun?.ExhumedChosen?.Invoke(kind);
            if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return chosen;
            return ExhumedCheck.FindPort(kind, new[] { Session?.GameFolder, Path.GetDirectoryName(Session?.GameFolder ?? "") });
        }

        string RzDosBoxBase(out string from)
        {
            string gog = DosBoxConfig.FindGogConf(Session?.GameFolder);
            if (gog != null) { try { from = gog; return File.ReadAllText(gog); } catch (Exception) { } }
            from = null;
            return DosBoxConfig.Defaults;
        }

        string RzExhumedText(GameKind kind)
        {
            string t = HostRun?.ExhumedSettingsText?.Invoke(kind);
            return t ?? ExhumedConfig.Defaults(kind);
        }

        /// <summary>Programs and paths: the programs Run and Test starts (chosen, and the one that would be used), their extra arguments, the DOSBox configuration's source, the editor's settings for the port.</summary>
        public string RunTestPaths()
        {
            var h = HostRun;
            var b = new StringBuilder();
            Func<string, string> chosen = v => h == null ? "(the editor's setting: only in the editor's window)" : string.IsNullOrEmpty(v) ? "(none: the editor looks for it)" : v + (File.Exists(v) ? "" : " (NOT FOUND)");
            b.Append("DOSBox chosen: ").Append(chosen(h?.DosBoxPath?.Invoke())).Append("\nDOSBox used: ").Append(RzDosBoxFound() ?? "not found (next to the game, as GOG installs it, or where it is usually installed)").Append('\n');
            b.Append("DOSBox extra arguments: ").Append(h == null ? "(the editor's setting)" : h.DosBoxArgs?.Invoke() ?? "").Append('\n');
            foreach (var kind in new[] { GameKind.UW1, GameKind.UW2 })
                b.Append("Underworld Exhumed ").Append(kind).Append(" chosen: ").Append(chosen(h?.ExhumedChosen?.Invoke(kind))).Append("; used: ").Append(RzPortFound(kind) ?? "not found (" + ExhumedCheck.ProgramName(kind) + ")").Append('\n');
            b.Append("Underworld Exhumed extra arguments: ").Append(h == null ? "(the editor's setting)" : h.ExhumedArgs?.Invoke() ?? "").Append('\n');
            if (Session != null)
            {
                string from;
                RzDosBoxBase(out from);
                b.Append("DOSBox configuration from: ").Append(from ?? "the editor's defaults (no GOG configuration next to the game)").Append('\n');
                var keys = ExhumedConfig.Keys(RzExhumedText(Session.Kind));
                b.Append("The editor's settings for the port (").Append(ExhumedConfig.FileName(Session.Kind)).Append("): ").Append(keys.Count == 0 ? "no key set" : string.Join(", ", keys.Select(k => k.Key + "=" + k.Value))).Append('\n');
            }
            b.Append(RzLaunchNote);
            return b.ToString();
        }

        /// <summary>Sets the programs and paths (the editor's settings; in the editor's window only). Empty text clears a path (the editor then looks for the program); null leaves it.</summary>
        public string RunTestPathsSet(string dosbox, string dosboxArgs, string exhumedUw1, string exhumedUw2, string exhumedArgs)
        {
            var h = HostRun;
            if (h == null) throw new McpEditorException("Programs and paths are the editor's own settings: set them in the editor's window (Let an AI assistant work here), or in Run and Test > Programs and paths.");
            Action<string, string> program = (what, p) =>
            {
                if (!string.IsNullOrWhiteSpace(p) && !File.Exists(p.Trim().Trim('"'))) throw new McpEditorException(what + ": there is no file " + p.Trim() + ".");
            };
            program("DOSBox", dosbox); program("UW1 port", exhumedUw1); program("UW2 port", exhumedUw2);
            var done = new List<string>();
            if (dosbox != null) { h.SetDosBoxPath(dosbox.Trim().Trim('"')); done.Add("DOSBox"); }
            if (dosboxArgs != null) { h.SetDosBoxArgs(dosboxArgs.Trim()); done.Add("DOSBox's arguments"); }
            if (exhumedUw1 != null) { h.SetExhumedPort(GameKind.UW1, exhumedUw1.Trim().Trim('"')); done.Add("the UW1 port"); }
            if (exhumedUw2 != null) { h.SetExhumedPort(GameKind.UW2, exhumedUw2.Trim().Trim('"')); done.Add("the UW2 port"); }
            if (exhumedArgs != null) { h.SetExhumedArgs(exhumedArgs.Trim()); done.Add("the port's arguments"); }
            if (done.Count == 0) return "Nothing given to set.";
            return "Set " + string.Join(", ", done) + ".";
        }

        /// <summary>The editor's settings for Underworld Exhumed (key=value lines written into the port's settings file before a run), of the open game's port or the one named.</summary>
        public string RunTestExhumedSettings(string game)
        {
            var kind = RzKind(game);
            return "The editor's settings for " + ExhumedConfig.FileName(kind) + (HostRun == null ? " (the defaults: the editor's own file is reached in its window)" : "") + ":\n" + RzExhumedText(kind);
        }

        public string RunTestExhumedSettingsSet(string game, string text)
        {
            var kind = RzKind(game);
            if (HostRun?.SetExhumedSettingsText == null) throw new McpEditorException("The settings for the port are the editor's own: set them in the editor's window.");
            HostRun.SetExhumedSettingsText(kind, text ?? "");
            var keys = ExhumedConfig.Keys(text ?? "");
            return "The editor's settings for " + ExhumedConfig.FileName(kind) + " set: " + (keys.Count == 0 ? "no key set." : string.Join(", ", keys.Select(k => k.Key + "=" + k.Value)) + ".");
        }

        GameKind RzKind(string game)
        {
            string g = (game ?? "").Trim().ToUpperInvariant();
            if (g == "UW1") return GameKind.UW1;
            if (g == "UW2") return GameKind.UW2;
            if (g.Length == 0 && Session != null) return Session.Kind;
            throw new McpEditorException("Name the game: UW1 or UW2" + (Session == null ? " (no game is open)." : "."));
        }

        /// <summary>The DOSBox configuration and command line Run in DOSBox would use on the game folder (nothing written, nothing started).</summary>
        public string RunTestDosBoxCommand()
        {
            Require();
            var s = Session;
            string from;
            string exe = Path.GetFileName(s.ExePath ?? StartPosition.ExePath(s.DataFolder, s.Kind) ?? (s.IsUw2 ? "UW2.EXE" : "UW.EXE"));
            string conf = DosBoxConfig.Build(RzDosBoxBase(out from), s.GameFolder, exe);
            var args = DosBoxConfig.Arguments("<the configuration below, saved as a file>", HostRun?.DosBoxArgs?.Invoke());
            return "Run in DOSBox mounts " + s.GameFolder + " as C: and runs " + exe + ". Configuration (from " + (from ?? "the editor's defaults") + "):\n" + conf + "\nCommand: " + RzCommand(RzDosBoxFound(), args) + "\n" + RzLaunchNote;
        }

        // ---- the squares a player can be sent to (the check's)

        /// <summary>Each level's square the check sends the player to (open, flat, its eight neighbours open at the same height, dry, nothing on it), or that it has none.</summary>
        public string RunTestStartSquares(int? level)
        {
            Require();
            var levels = level != null ? new List<int> { level.Value } : Session.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            if (level != null) CheckLevelNumber(Session, level.Value);
            TerrainTable terrain = null;
            try { terrain = TerrainTable.Load(Session); } catch (Exception) { }
            var b = new StringBuilder();
            foreach (int l in levels)
            {
                var m = ReadModel(l);
                if (!m.Present) { b.Append("level ").Append(l).Append(": empty slot\n"); continue; }
                var sq = ExhumedCheck.GotoSquare(Session, m, terrain);
                b.Append("level ").Append(l).Append(": ").Append(sq == null ? "NO square to send the player to (a check or spot test of this level is refused)" : "(" + sq.Value.X + ", " + sq.Value.Y + ")").Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Whether one square is a square the player can be sent to, and if not the nearest that is.</summary>
        public string RunTestStandable(int level, int x, int y)
        {
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            if (x < 0 || x > 63 || y < 0 || y > 63) throw new ArgumentOutOfRangeException(null, "x and y are 0-63.");
            TerrainTable terrain = null;
            try { terrain = TerrainTable.Load(Session); } catch (Exception) { }
            if (ExhumedCheck.Standable(Session, m, terrain, x, y)) return "Level " + level + " (" + x + ", " + y + ") is a square the player can be sent to.";
            bool loose = ExhumedCheck.Standable(Session, m, terrain, x, y, true);
            var near = ExhumedCheck.NearestStandable(Session, m, terrain, x, y, 6);
            var t = m.Tiles[x, y];
            bool open = !t.IsSolid && x >= 1 && y >= 1 && x <= 62 && y <= 62;
            return "Level " + level + " (" + x + ", " + y + ") is not one the check sends the player to (it needs open flat floor, its eight neighbours open at the same height, no water or lava, nothing on it)"
                + (loose ? "; it is open, dry and empty with room around (a spot beside an edit may use it)" : "")
                + (open ? "; a play test may still start there (it only needs an open square, 1-62)" : "; a play test cannot start there either")
                + (near != null ? ". The nearest such square: (" + near.Value.X + ", " + near.Value.Y + ")." : ". None within 6 squares.");
        }

        static ExhumedOptions RzStops(bool? edits, bool? sweep, bool? look, ExhumedOptions now)
        {
            var o = (now ?? new ExhumedOptions()).Clone();
            if (edits != null) o.VisitEdits = edits.Value;
            if (sweep != null) o.Sweep = sweep.Value;
            if (look != null) o.LookAround = look.Value;
            return o;
        }

        List<int> RzLevelList(string levels)
        {
            Require();
            if (string.IsNullOrWhiteSpace(levels) || levels.Trim().ToLowerInvariant() == "edited")
            {
                var l = ExhumedCheck.EditedLevels(Edits);
                if (l.Count == 0) throw new McpEditorException("No level has unsaved changes: name the levels (1,2,5), or all.");
                return l;
            }
            if (levels.Trim().ToLowerInvariant() == "all") return Session.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var list = new List<int>();
            foreach (var part in levels.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (!int.TryParse(part, out n)) throw new McpEditorException("\"" + part + "\" is not a level number.");
                CheckLevelNumber(Session, n);
                if (!list.Contains(n)) list.Add(n);
            }
            return list;
        }

        /// <summary>The stops a check (or spot test) would make: each level's square, then with the options beside each edit and a sweep of the level.</summary>
        public string RunTestStops(string levels, bool? edits, bool? sweep)
        {
            var list = RzLevelList(levels);
            var o = RzStops(edits, sweep, null, HostRun?.Thoroughness?.Invoke());
            var nowhere = ExhumedCheck.LevelsWithNowhereToStand(Session, Edits, list);
            var b = new StringBuilder();
            if (nowhere.Count > 0) b.Append("No square to send the player to on level(s) ").Append(string.Join(", ", nowhere)).Append(": a check of them is refused.\n");
            foreach (int l in list.Except(nowhere))
            {
                var stops = ExhumedCheck.Stops(Session, Edits, l, o);
                b.Append("level ").Append(l).Append(": ").Append(string.Join(", ", stops.Select(v => "(" + v.X + ", " + v.Y + ")" + (v.Why == "edit" ? " by an edit" : v.Why == "sweep" ? " sweep" : "")))).Append('\n');
            }
            return b.ToString();
        }

        // ---- copies to run in (written, never started)

        static string RzNewFolder(string folder, GameSession s)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new McpEditorException("Name the folder to write it in (a new or empty one).");
            string f = Path.GetFullPath(folder.Trim().Trim('"')).TrimEnd('\\', '/');
            string game = Path.GetFullPath(s.GameFolder).TrimEnd('\\', '/');
            if (f.Equals(game, StringComparison.OrdinalIgnoreCase) || game.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || f.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new McpEditorException("Name a folder outside the game folder (" + game + "): the copy holds the whole game.");
            if (Directory.Exists(f) && Directory.EnumerateFileSystemEntries(f).Any()) throw new McpEditorException(f + " is not empty: name a new or empty folder (nothing is deleted here).");
            return f;
        }

        /// <summary>The open save's player as edited, when it has unsaved changes (the play test then uses it); else null.</summary>
        byte[] RzPlayerBytes() { var p = SvSheetOrNull(); return p != null && p.Dirty ? p.ToBytes() : null; }

        /// <summary>
        /// Play-test from a tile: a copy of the game in folder with the unsaved levels and the player
        /// on the square (in a saved game of the copy; a lantern added when he has none), with the
        /// command line to start it in DOSBox or Underworld Exhumed. Nothing is started.
        /// </summary>
        public string RunTestPlayTest(int level, int x, int y, string folder, bool exhumed)
        {
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            var s = Session;
            string f = RzNewFolder(folder, s);
            var plan = PlayTest.Prepare(s, Edits, RzPlayerBytes(), level, x, y, f, exhumed);
            var b = new StringBuilder("Play test ready in " + plan.Folder + ". " + plan.Note + "\n");
            if (exhumed)
            {
                string port = RzPortFound(s.Kind);
                string cfg = ExhumedConfig.Apply(plan.Folder, s.Kind, RzExhumedText(s.Kind));
                if (cfg != null) b.Append("The editor's settings for the port were written to ").Append(cfg).Append(".\n");
                string problem = ExhumedConfig.Problem(port, plan.Folder, plan.Folder);
                if (problem != null) b.Append("Note: ").Append(problem).Append('\n');
                b.Append("Command: ").Append(RzCommand(port, PlayTest.ExhumedArguments(plan, HostRun?.ExhumedArgs?.Invoke()))).Append('\n');
            }
            else
            {
                string from;
                string conf = Path.Combine(plan.Folder, "dosbox-playtest.conf");
                File.WriteAllText(conf, DosBoxConfig.Build(RzDosBoxBase(out from), plan.Folder, plan.Exe));
                b.Append("DOSBox configuration written to ").Append(conf).Append(" (from ").Append(from ?? "the editor's defaults").Append(").\n");
                b.Append("Command: ").Append(RzCommand(RzDosBoxFound(), DosBoxConfig.Arguments(conf, HostRun?.DosBoxArgs?.Invoke()))).Append('\n');
            }
            b.Append(RzLaunchNote);
            return b.ToString();
        }

        /// <summary>
        /// Check in Underworld Exhumed, prepared: the copy (game and home with saved game I holding
        /// the edits), the input script that sends the player to each stop, and the command line
        /// (with its environment) to run it. The port is not run, so nothing here reads its result.
        /// </summary>
        public string RunTestExhumedCheck(string levels, string folder, bool saveAtEnd, bool keepAlive, bool? edits, bool? sweep, bool? look, bool showWindow)
        {
            var list = RzLevelList(levels);
            var s = Session;
            string f = RzNewFolder(folder, s);
            var o = RzStops(edits, sweep, look, HostRun?.Thoroughness?.Invoke());
            var plan = ExhumedCheck.Prepare(s, Edits, RzPlayerBytes(), list, f, saveAtEnd, keepAlive, o, showWindow);
            string port = RzPortFound(s.Kind);
            var b = new StringBuilder("Check ready in " + plan.Folder + ": " + plan.Note + "\n");
            b.Append("Script: ").Append(plan.Script).Append("; the last screen goes to ").Append(plan.Screenshot).Append(".\n");
            b.Append("Environment: ").Append(string.Join(" ", plan.Environment.Select(kv => kv.Key + "=" + kv.Value))).Append('\n');
            b.Append("Command: ").Append(RzCommand(port, plan.Arguments)).Append('\n');
            b.Append(RzLaunchNote).Append(" Reading the port's result (Check in Underworld Exhumed's report) needs the run itself, so it is not here.");
            return b.ToString();
        }

        /// <summary>
        /// DOSBox spot test (UW1), started: a copy in folder, the spots (each level's square, beside
        /// edits, sweeps), a tester (strength, dexterity...; best armour), and the first round of
        /// saved games (SAVE1-4, one a spot) written. runtest_spot_round writes the next.
        /// </summary>
        public string RunTestSpotStart(string levels, string folder, int strength, int dexterity, int intelligence, int defense, bool bestArmour, int perRound, bool? edits, bool? sweep)
        {
            var list = RzLevelList(levels);
            var s = Session;
            string f = RzNewFolder(folder, s);
            foreach (int v in new[] { strength, dexterity, intelligence, defense }) if (v < 1 || v > 99) throw new ArgumentOutOfRangeException(null, "Attributes are 1-99.");
            var o = new SpotTestOptions { Strength = strength, Dexterity = dexterity, Intelligence = intelligence, Defense = defense, BestArmour = bestArmour, PerRound = perRound };
            o.Stops = RzStops(edits, sweep, false, HostRun?.Thoroughness?.Invoke());
            var run = SpotTest.Start(s, Edits, list, o, f);
            var round = SpotTest.WriteRound(s, run);
            rzSpotFor = s;
            rzSpot = run;
            return "Spot test ready in " + run.Folder + ": " + run.Spots.Count + " spot(s); the tester " + SpotTest.Tester(run.Options, run.Wearing) + ".\n" + RzRound(run, round);
        }

        GameSession rzSpotFor;

        string RzRound(SpotTestRun run, List<ExhumedVisit> round)
        {
            var b = new StringBuilder();
            if (round.Count == 0) return "Every spot is done: " + run.Tested + " of " + run.Spots.Count + " tested; the log is " + Path.Combine(run.Folder, SpotTest.LogName) + ".";
            b.Append("Round written (spots ").Append(run.RoundStart + 1).Append('-').Append(run.RoundStart + run.RoundCount).Append(" of ").Append(run.Spots.Count).Append("):\n");
            for (int i = 0; i < run.RoundCount; i++) b.Append("  ").Append(SpotTest.Describe(run, run.RoundStart + i, i + 1)).Append('\n');
            string from;
            string conf = Path.Combine(run.Folder, "dosbox-spottest.conf");
            File.WriteAllText(conf, DosBoxConfig.Build(RzDosBoxBase(out from), run.Folder, run.Exe));
            b.Append("In DOSBox restore each saved game in turn. Command: ").Append(RzCommand(RzDosBoxFound(), DosBoxConfig.Arguments(conf, HostRun?.DosBoxArgs?.Invoke()))).Append('\n');
            b.Append(RzLaunchNote).Append(" Afterwards runtest_spot_round with your notes logs this round and writes the next.");
            return b.ToString();
        }

        /// <summary>The spot test's next round: the round written last is logged as tested (with the notes), the next written; or, with again, the round from a spot (1-based) written again.</summary>
        public string RunTestSpotRound(string notes, int? again)
        {
            Require();
            if (rzSpot == null || rzSpotFor != Session) throw new McpEditorException("No spot test was started here for this game: runtest_spot_start first.");
            var run = rzSpot;
            if (again != null)
            {
                if (again < 1 || again > run.Spots.Count) throw new ArgumentOutOfRangeException(null, "Spots are 1-" + run.Spots.Count + ".");
                return RzRound(run, SpotTest.WriteRound(Session, run, again.Value - 1));
            }
            SpotTest.FinishRound(run, notes);
            return RzRound(run, SpotTest.WriteRound(Session, run));
        }
    }
}
