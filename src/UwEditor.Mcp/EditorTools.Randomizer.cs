using System;
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The Randomizer menu: each randomizer's settings, preview, apply and undo; the solve over time and what it reaches.</summary>
    public sealed partial class EditorTools
    {
        sealed class RzCall { public bool Error; }

        string RzGuard(RzCall st, Func<string> f)
        {
            try { return f(); }
            catch (Exception e) when (e is McpEditorException || e is ArgumentException || e is InvalidOperationException || e is System.IO.IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                st.Error = true;
                return "Refused: " + e.Message;
            }
            catch (Exception e)
            {
                st.Error = true;
                return "Failed: " + e.GetType().Name + ": " + e.Message;
            }
        }

        string RzOnHost(RzCall st, Func<string> f)
        {
            try { return ed.Run(() => RzGuard(st, f)); }
            catch (Exception e) { st.Error = true; return "Failed: " + e.GetType().Name + ": " + e.Message; }
        }

        /// <summary>
        /// A tool call in three stages: job() on the host's thread (the app's UI thread: the levels
        /// copied, the options), its work here (so the editor keeps answering while a solver runs),
        /// and the answer it gives back on the host's thread again (where an apply changes the levels).
        /// </summary>
        CallToolResult AnswerStaged(Func<Func<Func<string>>> job, [System.Runtime.CompilerServices.CallerMemberName] string method = null)
        {
            var st = new RzCall();
            string answer;
            lock (ed)
            {
                Func<Func<string>> work = null;
                Func<string> finish = null;
                answer = RzOnHost(st, () =>
                {
                    string refused = Gate?.Invoke();
                    if (refused != null) { st.Error = true; return refused; }
                    work = job();
                    return null;
                });
                if (!st.Error && work != null) answer = RzGuard(st, () => { finish = work(); return null; });
                if (!st.Error && finish != null) answer = RzOnHost(st, finish);
            }
            Called?.Invoke(ToolName(method), answer);
            return Result(answer, st.Error);
        }

        const string RzIds = "The randomizer: key-scatter, item-shuffle, item-swap, monster-swap, challenges, mantras (UW1), vowel-play or auto-zones.";

        [McpServerTool(Name = "randomizer_list"), Description("The Randomizer menu's tools (Key item scatter > Apply, Automatic zones, Item property shuffle, Swap items, Swap monsters, Named challenges, Randomize mantras, Vowel play): each one's id, what it does, its seed and the runs applied here. Each keeps its settings here, starting from the game's defaults with a new seed.")]
        public CallToolResult RandomizerList() { return Answer(() => ed.RandomizerList()); }

        [McpServerTool(Name = "randomizer_help"), Description("A randomizer's settings one a line (name = value, dots for nested ones) with what they may hold: the key scatter's categories and extra items, the challenges and which cannot go together, the monster measures, the default names of vowel play.")]
        public CallToolResult RandomizerHelp([Description(RzIds)] string randomizer) { return Answer(() => ed.RandomizerHelp(randomizer)); }

        [McpServerTool(Name = "randomizer_settings"), Description("A randomizer's whole settings as the settings file has them (Save/Copy settings in its window: format, game, tool, seed, the levels' hash, every option): what the next preview or apply uses.")]
        public CallToolResult RandomizerSettings([Description(RzIds)] string randomizer) { return Answer(() => ed.RandomizerSettingsGet(randomizer)); }

        [McpServerTool(Name = "randomizer_settings_set"), Description("Sets a randomizer's settings from JSON: a whole settings text ({\"format\": \"uw-randomizer-settings\", ...}, as Copy settings or a spoiler log's first line gives; what it leaves out takes the defaults, as Load settings), or an object of options laid over the ones now ({\"seed\": 5, \"anywhere\": false, \"logic\": {\"lockPicking\": true}}). Unknown names are warned about.")]
        public CallToolResult RandomizerSettingsSet([Description(RzIds)] string randomizer, [Description("The JSON.")] string settings)
        {
            return Answer(() => ed.RandomizerSettingsSet(randomizer, settings));
        }

        [McpServerTool(Name = "randomizer_setting"), Description("Sets one setting of a randomizer by its name as randomizer_help lists it (dots for nested ones: logic.lockPicking, nav.gapJumps, start); a name it does not have is refused.")]
        public CallToolResult RandomizerSetting([Description(RzIds)] string randomizer, [Description("The setting's name.")] string name,
            [Description("The value as JSON (true, 5, \"level\", [\"Monk\", \"Naked\"], {\"level\": 1, \"x\": 30, \"y\": 32}) or plain text.")] string value)
        {
            return Answer(() => ed.RandomizerSettingSet(randomizer, name, value));
        }

        [McpServerTool(Name = "randomizer_seed"), Description("Sets a randomizer's seed (the same seed on the same levels gives the same result); leave it out for a new random one.")]
        public CallToolResult RandomizerSeed([Description(RzIds)] string randomizer, [Description("0 or more; leave out for a new random seed.")] int? seed = null)
        {
            return Answer(() => ed.RandomizerSeed(randomizer, seed));
        }

        [McpServerTool(Name = "randomizer_settings_reset"), Description("Puts a randomizer's settings back to the game's defaults, keeping the seed (for the item shuffle every tab follows the levels as they are now: All from the game).")]
        public CallToolResult RandomizerSettingsReset([Description(RzIds)] string randomizer) { return Answer(() => ed.RandomizerSettingsReset(randomizer)); }

        [McpServerTool(Name = "randomizer_settings_from_window"), Description("In the editor's window: takes the settings of that randomizer's window open in the editor (its controls as they are now), so preview and apply here run what the user set up there.")]
        public CallToolResult RandomizerSettingsFromWindow([Description(RzIds)] string randomizer) { return Answer(() => ed.RandomizerSettingsFromWindow(randomizer)); }

        [McpServerTool(Name = "randomizer_objects"), Description("A randomizer's object list: item-shuffle ignored; item-swap ignored or start (the starting equipment that stays); monster-swap whitelist (creatures with a conversation allowed to move) or ignored; challenges special. With candidates: the objects that may go on it.")]
        public CallToolResult RandomizerObjects([Description(RzIds)] string randomizer, [Description("ignored, start, whitelist or special.")] string list,
            [Description("True: the objects that may go on the list, not the list.")] bool candidates = false,
            [Description("Only those whose label holds this text.")] string filter = null)
        {
            return Answer(() => ed.RandomizerListShow(randomizer, list, candidates, filter));
        }

        [McpServerTool(Name = "randomizer_objects_edit"), Description("Changes a randomizer's object list (see randomizer_objects): add or remove an object by level and index, clear it, or put back its defaults.")]
        public CallToolResult RandomizerObjectsEdit([Description(RzIds)] string randomizer, [Description("ignored, start, whitelist or special.")] string list,
            [Description("add, remove, clear or defaults.")] string action, [Description("The object's level (add, remove).")] int? level = null,
            [Description("The object's index (add, remove).")] int? index = null)
        {
            return Answer(() => ed.RandomizerListEdit(randomizer, list, action, level, index));
        }

        [McpServerTool(Name = "randomizer_formula"), Description("The math a run makes with the settings now (the window's formula box): item-shuffle, item-swap or monster-swap.")]
        public CallToolResult RandomizerFormula([Description("item-shuffle, item-swap or monster-swap.")] string randomizer) { return Answer(() => ed.RandomizerFormula(randomizer)); }

        [McpServerTool(Name = "randomizer_world"), Description("What the levels hold now, as the windows' world report: the items (item-shuffle, item-swap) or the monsters (monster-swap), level by level.")]
        public CallToolResult RandomizerWorld([Description("item-shuffle, item-swap or monster-swap.")] string randomizer) { return Answer(() => ed.RandomizerWorld(randomizer)); }

        [McpServerTool(Name = "randomizer_monster_strengths"), Description("The monster swap's strength table: every kind of creature with xp, hit points, armour, damage, the combined strength, how it moves, fire resistance, height and spells.")]
        public CallToolResult RandomizerMonsterStrengths() { return Answer(() => ed.RandomizerStrengths()); }

        [McpServerTool(Name = "randomizer_preview"), Description("What a randomizer would change with its settings now; nothing changes. The key scatter runs its solver once a key item and checks the result (seconds to minutes) and answers with its summary (the spoiler log: randomizer_report); auto-zones builds the zones and solves them without putting them in the zoning.")]
        public CallToolResult RandomizerPreview([Description(RzIds)] string randomizer) { return AnswerStaged(() => ed.RandomizerPreviewJob(randomizer)); }

        [McpServerTool(Name = "randomizer_apply"), Description("Applies a randomizer with its settings now: the levels change as one undoable step a level (undo, or randomizer_undo); the mantras and vowel play change STRINGS.PAK, the challenges may change OBJECTS.DAT's creature table too; auto-zones puts the automatic zones in the zoning (as Build and Use). The key scatter applies its last preview when the settings and levels are unchanged, else runs anew. Nothing is written to the files until save.")]
        public CallToolResult RandomizerApply([Description(RzIds)] string randomizer,
            [Description("auto-zones only: replace a zoning that has unsaved changes.")] bool replace = false)
        {
            return AnswerStaged(() => ed.RandomizerApplyJob(randomizer, replace));
        }

        [McpServerTool(Name = "randomizer_undo"), Description("Puts back what a randomizer applied here (as its window's Undo): the item shuffle, swaps and challenges every run, the key scatter, mantras and vowel play the last run. A level edited since is left as it is (undo steps back there).")]
        public CallToolResult RandomizerUndo([Description(RzIds)] string randomizer) { return Answer(() => ed.RandomizerUndo(randomizer)); }

        [McpServerTool(Name = "randomizer_report"), Description("A part of a randomizer's last report (preview or apply; for the key scatter its spoiler log), from a character on.")]
        public CallToolResult RandomizerReport([Description(RzIds)] string randomizer, [Description("The first character, from 0.")] int from = 0,
            [Description("How many characters (default 8000, at most 50000).")] int length = 8000)
        {
            return Answer(() => ed.RandomizerReport(randomizer, from, length));
        }

        [McpServerTool(Name = "randomizer_report_save"), Description("Writes a randomizer's last report (the key scatter's spoiler log: Export spoiler log) to a text file.")]
        public CallToolResult RandomizerReportSave([Description(RzIds)] string randomizer, [Description("The file to write.")] string path,
            [Description("Write over an existing file.")] bool overwrite = false)
        {
            return Answer(() => ed.RandomizerReportSave(randomizer, path, overwrite));
        }

        [McpServerTool(Name = "randomizer_start_check"), Description("Whether a square can be the key scatter's given start (start = given): viable or why not, its zone, hostile creatures near, and the scrolls or wands the scatter would make for it. Builds the zones once (seconds).")]
        public CallToolResult RandomizerStartCheck([Description("Level number, from 1.")] int level, [Description("1-62.")] int x, [Description("1-62.")] int y)
        {
            return AnswerStaged(() => ed.RandomizerStartCheckJob(level, x, y));
        }

        [McpServerTool(Name = "randomizer_solve"), Description("Is the game beatable: the solve over time (the Zones window's Check if solvable), on the zoning (its zones, else automatic zones) and the levels with their unsaved edits, with the movement and logic options of the key scatter (or of auto-zones, or the defaults), and the main option sets. Answers the verdict; randomizer_reach and randomizer_solve_report read the result.")]
        public CallToolResult RandomizerSolve([Description("Whose options: key-scatter (default), auto-zones or default.")] string options = null,
            [Description("zones (the zoning; automatic zones when it has none; default) or automatic.")] string zoning = null)
        {
            return AnswerStaged(() => ed.RandomizerSolveJob(options, zoning));
        }

        [McpServerTool(Name = "randomizer_solve_report"), Description("A part of the last solve's full report (the timeline: spheres, links, mechanisms, traps, warnings), from a character on.")]
        public CallToolResult RandomizerSolveReport([Description("The first character, from 0.")] int from = 0, [Description("How many characters (default 8000, at most 50000).")] int length = 8000)
        {
            return Answer(() => ed.RandomizerSolveReport(from, length));
        }

        [McpServerTool(Name = "randomizer_reach"), Description("What the last solve reaches: spheres (each sphere's zones and the keys, items and abilities gained), unreached (zones never reached, what the goal misses), square (a square's zone and the sphere it is reached in), object (an object's), sources (each ability's source and when it is in reach), blocked (ways found shut, one-way traps, warnings).")]
        public CallToolResult RandomizerReach([Description("spheres, unreached, square, object, sources or blocked.")] string what = "spheres",
            [Description("square, object: the level.")] int? level = null, [Description("square: 0-63.")] int? x = null, [Description("square: 0-63.")] int? y = null,
            [Description("object: its index (#).")] int? index = null)
        {
            return Answer(() => ed.RandomizerReach(what, level, x, y, index));
        }
    }
}
