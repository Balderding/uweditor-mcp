using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The randomizer tools on a copy of UW1: settings, preview (nothing changes), apply (levels change) and undo (they come back byte for byte).</summary>
    public class RandomizerTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static string Levels(McpEditor ed) { return RandomizerSettings.LevelsHash(ed.Session, ed.Edits); }

        [Fact]
        public void Lists_every_randomizer_and_explains_one()
        {
            var ed = Opened(); if (ed == null) return;
            string l = ed.RandomizerList();
            foreach (var t in RandomizerSettings.Tools) Assert.Contains(t.Id + " (", l);
            Assert.Contains("move (the categories", ed.RandomizerHelp("key-scatter"));
            Assert.Contains("Monk", ed.RandomizerHelp("challenges"));
            Assert.Contains("seed = ", ed.RandomizerHelp("shuffle"));
            Assert.StartsWith("Refused: ", EditorTools.Text(new EditorTools(ed).RandomizerHelp("nonsense")));
        }

        [Fact]
        public void Settings_set_by_json_by_name_and_seed()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Equal("Item property shuffle seed 5.", ed.RandomizerSeed("item-shuffle", 5));
            Assert.Contains("\"seed\": 5", ed.RandomizerSettingsGet("item-shuffle"));
            Assert.Contains("seed 7", ed.RandomizerSettingsSet("key-scatter", "{\"seed\": 7, \"anywhere\": false}"));
            string s = ed.RandomizerSettingsGet("key-scatter");
            Assert.Contains("\"seed\": 7", s);
            Assert.Contains("\"anywhere\": false", s);
            Assert.Contains("logic.lockPicking = true", ed.RandomizerSettingSet("key-scatter", "logic.lockPicking", "true"));
            Assert.Contains("\"lockPicking\": true", ed.RandomizerSettingsGet("key-scatter"));
            Assert.Throws<McpEditorException>(() => ed.RandomizerSettingSet("key-scatter", "noSuchSetting", "1"));
            // a whole text read back gives the same settings
            string whole = ed.RandomizerSettingsGet("key-scatter");
            ed.RandomizerSettingsReset("key-scatter");
            Assert.Contains("\"anywhere\": true", ed.RandomizerSettingsGet("key-scatter"));
            ed.RandomizerSettingsSet("key-scatter", whole);
            Assert.Contains("\"anywhere\": false", ed.RandomizerSettingsGet("key-scatter"));
            // another tool's text is refused
            Assert.Throws<McpEditorException>(() => ed.RandomizerSettingsSet("item-swap", whole));
            Assert.Throws<McpEditorException>(() => ed.RandomizerSettingsFromWindow("key-scatter"));
        }

        [Fact]
        public void Item_shuffle_previews_applies_and_undoes()
        {
            var ed = Opened(); if (ed == null) return;
            string before = Levels(ed);
            ed.RandomizerSeed("item-shuffle", 11);
            Assert.StartsWith("Preview, nothing changed", ed.RandomizerPreview("item-shuffle"));
            Assert.Equal(before, Levels(ed));
            Assert.False(ed.Edits.HasChanges);
            Assert.StartsWith("Shuffled (seed 11)", ed.RandomizerApply("item-shuffle"));
            Assert.True(ed.Edits.HasChanges);
            Assert.NotEqual(before, Levels(ed));
            Assert.Contains("characters 0-", ed.RandomizerReport("item-shuffle", 0, 100));
            Assert.StartsWith("Undone: 1 run(s)", ed.RandomizerUndo("item-shuffle"));
            Assert.Equal(before, Levels(ed));
            Assert.Throws<McpEditorException>(() => ed.RandomizerUndo("item-shuffle"));
        }

        [Fact]
        public void Item_swap_and_monster_swap_apply_and_undo()
        {
            var ed = Opened(); if (ed == null) return;
            string before = Levels(ed);
            ed.RandomizerSeed("item-swap", 3);
            Assert.StartsWith("Swapped (seed 3)", ed.RandomizerApply("item-swap"));
            ed.RandomizerSeed("monster-swap", 4);
            Assert.StartsWith("Swapped (seed 4)", ed.RandomizerApply("monster-swap"));
            Assert.NotEqual(before, Levels(ed));
            ed.RandomizerUndo("monster-swap");
            ed.RandomizerUndo("item-swap");
            Assert.Equal(before, Levels(ed));
            Assert.Contains("combined", ed.RandomizerStrengths());
            Assert.False(string.IsNullOrEmpty(ed.RandomizerFormula("monster-swap")));
            Assert.False(string.IsNullOrEmpty(ed.RandomizerWorld("item-swap")));
        }

        [Fact]
        public void Object_lists_add_remove_and_defaults()
        {
            var ed = Opened(); if (ed == null) return;
            var cand = ItemShuffle.Candidates(ed.Session, l => ed.Edits.Model(l), true);
            Assert.NotEmpty(cand);
            var c = cand.First(x => !ed.RandomizerListShow("item-swap", "ignored", false, null).Contains("#" + x.Index + " " + x.Label));
            Assert.StartsWith("Added", ed.RandomizerListEdit("item-swap", "ignored", "add", c.Level, c.Index));
            Assert.Contains("level " + c.Level + " #" + c.Index, ed.RandomizerListShow("item-swap", "ignored", false, null));
            Assert.StartsWith("Removed", ed.RandomizerListEdit("item-swap", "ignored", "remove", c.Level, c.Index));
            Assert.Contains("is empty", ed.RandomizerListEdit("item-swap", "start", "clear", null, null));
            Assert.Contains("defaults", ed.RandomizerListEdit("item-swap", "start", "defaults", null, null));
            Assert.Throws<McpEditorException>(() => ed.RandomizerListEdit("item-swap", "nope", "clear", null, null));
            Assert.Throws<McpEditorException>(() => ed.RandomizerListEdit("item-swap", "ignored", "add", 1, 9999));
        }

        [Fact]
        public void Challenges_apply_undo_and_refuse_a_conflict()
        {
            var ed = Opened(); if (ed == null) return;
            string before = Levels(ed);
            ed.RandomizerSettingSet("challenges", "selected", "[\"Monk\", \"Ranger\"]");
            Assert.Throws<McpEditorException>(() => ed.RandomizerPreview("challenges"));
            ed.RandomizerSettingSet("challenges", "selected", "[\"Monk\"]");
            Assert.StartsWith("Preview, nothing changed", ed.RandomizerPreview("challenges"));
            Assert.Equal(before, Levels(ed));
            Assert.StartsWith("Applied (seed", ed.RandomizerApply("challenges"));
            Assert.NotEqual(before, Levels(ed));
            ed.RandomizerUndo("challenges");
            Assert.Equal(before, Levels(ed));
        }

        [Fact]
        public void Mantras_and_vowel_play_change_and_put_back_the_strings()
        {
            var ed = Opened(); if (ed == null) return;
            ed.RandomizerSeed("mantras", 9);
            string was = ed.StringGet("2", MantraShuffle.First);
            Assert.StartsWith("Preview, nothing changed", ed.RandomizerPreview("mantras"));
            Assert.Equal(was, ed.StringGet("2", MantraShuffle.First));
            Assert.StartsWith("Mantras swapped (seed 9)", ed.RandomizerApply("mantras"));
            Assert.StartsWith("Randomize mantras put back", ed.RandomizerUndo("mantras"));
            Assert.Equal(was, ed.StringGet("2", MantraShuffle.First));
            ed.RandomizerSeed("vowel-play", 2);
            Assert.StartsWith("Preview, nothing changed", ed.RandomizerPreview("vowel-play"));
            Assert.Equal("Nothing to save.", ed.Save());
        }

        [Fact]
        public void Tools_answer_through_the_staged_call()
        {
            var ed = Opened(); if (ed == null) return;
            var tools = new EditorTools(ed);
            Assert.StartsWith("Preview, nothing changed", EditorTools.Text(tools.RandomizerPreview("monster-swap")));
            Assert.StartsWith("Refused: ", EditorTools.Text(tools.RandomizerPreview("nope")));
            Assert.StartsWith("Refused: ", EditorTools.Text(tools.RandomizerReach("spheres")));
        }

        [Fact]
        public void Solve_says_whether_the_game_is_beatable_and_what_it_reaches()
        {
            var ed = Opened(); if (ed == null) return;
            string v = ed.RandomizerSolve();
            Assert.Contains("olvable", v);
            Assert.Contains("Sphere 0", ed.RandomizerReach("spheres", null, null, null, null));
            var st = ed.Session.Start;
            Assert.Contains("is in zone", ed.RandomizerReach("square", st.Level, (int)st.X, (int)st.Y, null));
            Assert.Contains("ability source", ed.RandomizerReach("sources", null, null, null, null));
            Assert.Contains("never reached", ed.RandomizerReach("unreached", null, null, null, null));
            Assert.Contains("one-way trap(s)", ed.RandomizerReach("blocked", null, null, null, null));
            Assert.Contains("characters 0-", ed.RandomizerSolveReport(0, 500));
        }

        [Fact]
        public void Key_scatter_applies_and_undoes()
        {
            var ed = Opened(); if (ed == null) return;
            string before = Levels(ed);
            ed.RandomizerSeed("key-scatter", 1);
            string p = ed.RandomizerPreview("key-scatter");
            Assert.StartsWith("Preview, nothing changed. Seed ", p);
            Assert.Equal(before, Levels(ed));
            string a = ed.RandomizerApply("key-scatter");
            Assert.StartsWith("Applied (the preview's result)", a);
            Assert.NotEqual(before, Levels(ed));
            string log = Path.Combine(Path.GetDirectoryName(ed.Session.GameFolder), "uwmcp-rzlog-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            try
            {
                Assert.StartsWith("Wrote", ed.RandomizerReportSave("key-scatter", log, false));
                Assert.Contains("uw-randomizer-settings", File.ReadAllText(log));
                Assert.Throws<McpEditorException>(() => ed.RandomizerReportSave("key-scatter", log, false));
            }
            finally { File.Delete(log); }
            Assert.StartsWith("The scatter of seed", ed.RandomizerUndo("key-scatter"));
            Assert.Equal(before, Levels(ed));
        }
    }
}
