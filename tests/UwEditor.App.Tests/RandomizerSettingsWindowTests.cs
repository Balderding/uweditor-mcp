using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>
    /// The settings row of every randomizer window (Save, Load, Copy and Paste settings): the
    /// settings on screen saved and loaded back set every control again (the text after a load is
    /// the text before), a hand-written text sets the controls it names, and a text for another
    /// game or tool is refused with its message, the controls left alone.
    /// </summary>
    public class RandomizerSettingsWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        const string Head = "{\"format\": \"uw-randomizer-settings\", \"version\": 1, \"game\": \"UW1\", ";

        static MainWindow OpenQuiet()
        {
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            return w;
        }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        static NumericUpDown FirstNumber(Window w) { return w.GetLogicalDescendants().OfType<NumericUpDown>().First(); }

        /// <summary>Another game's and another tool's text are refused, and the window's settings stay as they were.</summary>
        static void Refuses(Func<bool, string> text, Func<string, string> load, SettingsTool tool)
        {
            string before = text(false);
            string uw2 = RandomizerSettings.Write("UW2", tool, Activator.CreateInstance(tool.Options));
            Assert.Equal("Settings not loaded: These settings are for Ultima Underworld 2, and the game open is Ultima Underworld 1.", load(uw2));
            var other = tool == RandomizerSettings.Mantras ? RandomizerSettings.VowelPlay : RandomizerSettings.Mantras;
            Assert.Equal("Settings not loaded: These settings are for " + other.Name + ", not " + tool.Name + "; load them in that window.", load(RandomizerSettings.Write("UW1", other, Activator.CreateInstance(other.Options))));
            Assert.StartsWith("Settings not loaded: This is not randomizer settings text", load("seed 4"));
            Assert.Equal(before, text(false));
        }

        [AvaloniaFact]
        public void Key_scatter_settings_save_load_paste_and_refuse()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet();
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.NotNull(kw.SettingsButtons);
            // settings set through the controls
            FirstNumber(kw).Value = 123;
            Box(kw, "Lock picking in logic").IsChecked = true;
            Box(kw, "Ring of Levitation").IsChecked = false;
            Box(kw, "Diagonals too").IsChecked = true;
            Box(kw, KeyShuffle.CategoryName("runes")).IsChecked = false;
            Box(kw, "Creature inventories").IsChecked = true;
            Box(kw, "A full torch").IsChecked = true;
            kw.StartXBox.Value = 30;
            kw.StartBox.SelectedIndex = (int)StartChoice.Given;
            Ui.Pump();
            string text = kw.SettingsText(false);
            Assert.Contains("\"tool\": \"key-scatter\"", text);
            Assert.Contains("\"seed\": 123", text);
            Assert.Contains("\"startAt\": {\n      \"level\": 1,\n      \"x\": 30,\n      \"y\": 32\n    }", text);
            Assert.Contains("\"zoning\": \"automatic zones\"", text);
            Assert.Equal("uw1-key-scatter-seed-123.json", RandomizerSettings.FileName("UW1", RandomizerSettings.KeyScatter, 123));

            // everything changed again, then loaded back
            FirstNumber(kw).Value = 9;
            Box(kw, "Lock picking in logic").IsChecked = false;
            Box(kw, "Ring of Levitation").IsChecked = true;
            Box(kw, "Diagonals too").IsChecked = false;
            Box(kw, KeyShuffle.CategoryName("runes")).IsChecked = true;
            Box(kw, "Creature inventories").IsChecked = false;
            Box(kw, "A full torch").IsChecked = false;
            kw.StartBox.SelectedIndex = (int)StartChoice.Random;
            kw.ScopeBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.NotEqual(text, kw.SettingsText(false));
            string msg = kw.LoadSettings(text);
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 123), written by editor " + RandomizerSettings.EditorVersion + ".", msg);
            Assert.Equal(msg, kw.SummaryText);
            Assert.Equal(text, kw.SettingsText(false));
            Assert.Equal(123, (int)FirstNumber(kw).Value);
            Assert.True(Box(kw, "Lock picking in logic").IsChecked);
            Assert.False(Box(kw, "Ring of Levitation").IsChecked);
            Assert.True(Box(kw, "Diagonals too").IsChecked);
            Assert.False(Box(kw, KeyShuffle.CategoryName("runes")).IsChecked);
            Assert.True(Box(kw, "Creature inventories").IsChecked);
            Assert.True(Box(kw, "A full torch").IsChecked);
            Assert.Equal((int)StartChoice.Given, kw.StartBox.SelectedIndex);
            Assert.Equal(0, kw.ScopeBox.SelectedIndex);
            Assert.Equal((1, 30, 32), ((int)kw.StartLevelBox.Value, (int)kw.StartXBox.Value, (int)kw.StartYBox.Value));
            Assert.Contains("level 1 (30, 32)", kw.StartNote);

            // a hand-written text: what it leaves out takes the defaults, an unknown key is a warning
            string hand = Head + "\"tool\": \"key-scatter\", \"seed\": 7, \"options\": {\"start\": \"random\", \"startScope\": \"anyLevel\", \"logic\": {\"magic\": false}, \"wings\": 1}}";
            msg = kw.LoadSettings(hand);
            Ui.Pump();
            Assert.Contains("Settings loaded (seed 7)", msg);
            Assert.Contains("\"options.wings\"", msg);
            Assert.Equal(7, (int)FirstNumber(kw).Value);
            Assert.False(Box(kw, "Magic (casting from runes)").IsChecked);
            Assert.False(Box(kw, "Lock picking in logic").IsChecked);       // the default
            Assert.True(Box(kw, KeyShuffle.CategoryName("runes")).IsChecked);
            Assert.Equal((int)StartChoice.Random, kw.StartBox.SelectedIndex);
            Assert.Equal(1, kw.ScopeBox.SelectedIndex);
            Assert.True(kw.ScopeBox.IsVisible);
            Assert.False(kw.Options.Logic.Magic);
            Assert.Equal(StartScope.AnyLevel, kw.Options.StartScope);

            Refuses(kw.SettingsText, kw.LoadSettings, RandomizerSettings.KeyScatter);
            Assert.StartsWith("Settings not loaded", kw.SummaryText);
            kw.Close();
        }

        [AvaloniaFact]
        public void Key_scatter_spoiler_log_starts_with_the_settings_that_make_it_again()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet();
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            kw.LoadSettings(Head + "\"tool\": \"key-scatter\", \"seed\": 4, \"options\": {}}");
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.StartsWith("Settings (Paste settings in the window makes this run again): {\"format\":\"uw-randomizer-settings\"", kw.Spoiler);
            string line = SettingsBar.Pick(kw.Spoiler);
            Assert.Equal(RandomizerSettings.FromLog(kw.Spoiler), line);
            Assert.Contains("\"seed\":4", line);
            Assert.Equal(kw.SettingsText(true), line);
            kw.Close();
        }

        [AvaloniaFact]
        public void Item_shuffle_settings_save_load_paste_and_refuse()
        {
            if (Uw1 == null) return;
            foreach (var f in new[] { "item-shuffle-uw1.json", "item-ignore-uw1.json" }) File.Delete(Path.Combine(Settings.Folder, f));
            var w = OpenQuiet();
            var iw = w.ShowItemShuffle();
            Ui.Pump();
            FirstNumber(iw).Value = 55;
            Box(iw, "Place anywhere").IsChecked = false;
            iw.Options.Axes = 90;
            iw.Options.WeaponEnchants[0].Probability = 33;
            string text = iw.SettingsText(false);
            Assert.Contains("\"tool\": \"item-shuffle\"", text);
            FirstNumber(iw).Value = 1;
            Box(iw, "Place anywhere").IsChecked = true;
            iw.Options.Axes = 10;
            string msg = iw.LoadSettings(text);
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 55)", msg);
            Assert.Equal(msg, iw.ReportText);
            Assert.Equal(text, iw.SettingsText(false));
            Assert.Equal(55, (int)FirstNumber(iw).Value);
            Assert.False(Box(iw, "Place anywhere").IsChecked);
            Assert.Equal(90, iw.Options.Axes);
            Assert.Equal(33, iw.Options.WeaponEnchants[0].Probability);
            // hand-written: the rest takes the game's defaults
            msg = iw.LoadSettings(Head + "\"tool\": \"item-shuffle\", \"seed\": 5, \"options\": {\"axes\": 12, \"includeInventories\": false}}");
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 5)", msg);
            Assert.Equal(12, iw.Options.Axes);
            Assert.False(Box(iw, "Include creatures' inventories").IsChecked);
            Assert.True(Box(iw, "Place anywhere").IsChecked);
            Assert.True(iw.Options.WeaponEnchants.Count > 0);
            Refuses(iw.SettingsText, iw.LoadSettings, RandomizerSettings.ItemShuffle);
            // the report of a preview starts with the settings
            iw.Shuffle();
            Assert.Contains("Settings (Paste settings in the window makes this run again): {\"format\"", iw.ReportText);
            iw.UndoAll();
            iw.Close();
        }

        [AvaloniaFact]
        public void Item_swap_settings_save_load_paste_and_refuse()
        {
            if (Uw1 == null) return;
            File.Delete(Path.Combine(Settings.Folder, "item-swap-uw1.json"));
            var w = OpenQuiet();
            var sw = w.ShowItemSwap();
            Ui.Pump();
            FirstNumber(sw).Value = 66;
            sw.ProgressionChoice.IsChecked = true;
            Box(sw, "Bones and skulls").IsChecked = true;
            Box(sw, "Food").IsChecked = false;
            Ui.Pump();
            string text = sw.SettingsText(false);
            FirstNumber(sw).Value = 2;
            sw.ProgressionChoice.IsChecked = false;
            Box(sw, "Bones and skulls").IsChecked = false;
            Box(sw, "Food").IsChecked = true;
            Ui.Pump();
            string msg = sw.LoadSettings(text);
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 66)", msg);
            Assert.Equal(text, sw.SettingsText(false));
            Assert.Equal(66, (int)FirstNumber(sw).Value);
            Assert.True(sw.ProgressionChoice.IsChecked);
            Assert.True(Box(sw, "Bones and skulls").IsChecked);
            Assert.False(Box(sw, "Food").IsChecked);
            Assert.False(sw.ScopeChoices.IsEnabled);
            msg = sw.LoadSettings(Head + "\"tool\": \"item-swap\", \"seed\": 3, \"options\": {\"anyKind\": true}}");
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 3)", msg);
            Assert.True(sw.Settings_.AnyKind);
            Assert.True(Box(sw, "Food").IsChecked);                       // the default
            Assert.NotEmpty(sw.Settings_.Start);                          // the game's starting equipment
            Refuses(sw.SettingsText, sw.LoadSettings, RandomizerSettings.ItemSwap);
            sw.Close();
        }

        [AvaloniaFact]
        public void Monster_swap_settings_save_load_paste_and_refuse()
        {
            if (Uw1 == null) return;
            File.Delete(Path.Combine(Settings.Folder, "monster-swap-uw1.json"));
            var w = OpenQuiet();
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            FirstNumber(mw).Value = 77;
            mw.RuleBox.SelectedIndex = 1;
            mw.SpecialKindsCheck.IsChecked = false;
            Box(mw, "Keep the hit points").IsChecked = true;
            Ui.Pump();
            string text = mw.SettingsText(false);
            FirstNumber(mw).Value = 1;
            mw.RuleBox.SelectedIndex = 0;
            mw.SpecialKindsCheck.IsChecked = true;
            Box(mw, "Keep the hit points").IsChecked = false;
            Ui.Pump();
            string msg = mw.LoadSettings(text);
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 77)", msg);
            Assert.Equal(text, mw.SettingsText(false));
            Assert.Equal(77, (int)FirstNumber(mw).Value);
            Assert.Equal(1, mw.RuleBox.SelectedIndex);
            Assert.False(mw.SpecialKindsCheck.IsChecked);
            Assert.True(Box(mw, "Keep the hit points").IsChecked);
            Assert.False(mw.ScopeRow.IsEnabled);
            msg = mw.LoadSettings(Head + "\"tool\": \"monster-swap\", \"seed\": 8, \"options\": {\"measure\": \"hp\", \"ignored\": [{\"level\": 2, \"index\": 300, \"label\": \"a test\"}]}}");
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 8)", msg);
            Assert.Equal("hp", mw.Options.Measure);
            Assert.Equal(0, mw.RuleBox.SelectedIndex);
            Assert.Single(mw.Options.Ignored);
            Assert.Equal(1, mw.IgnoredPanel.Count);
            Assert.NotEmpty(mw.Options.Whitelist);                         // the game's whitelist
            Refuses(mw.SettingsText, mw.LoadSettings, RandomizerSettings.MonsterSwap);
            mw.Close();
        }

        [AvaloniaFact]
        public void Challenges_mantras_vowel_play_and_automatic_zones_settings_load_back()
        {
            if (Uw1 == null) return;
            File.Delete(Path.Combine(Settings.Folder, "challenges-uw1.json"));
            File.Delete(Path.Combine(Settings.Folder, "vowel-play-uw1.json"));
            var w = OpenQuiet();

            var cw = w.ShowChallenges();
            Ui.Pump();
            foreach (var b in Challenges.All) cw.Box(b.Id).IsChecked = false;
            cw.Box(Challenge.Fasting).IsChecked = true;
            cw.FastingSlider.Value = 30;
            cw.RangerBoxes[1].Value = 5;
            FirstNumber(cw).Value = 88;
            string text = cw.SettingsText(false);
            cw.Box(Challenge.Fasting).IsChecked = false;
            cw.Box(Challenge.Naked).IsChecked = true;
            cw.FastingSlider.Value = 70;
            cw.RangerBoxes[1].Value = 1;
            FirstNumber(cw).Value = 2;
            Assert.StartsWith("Settings loaded (seed 88)", cw.LoadSettings(text));
            Ui.Pump();
            Assert.Equal(text, cw.SettingsText(false));
            Assert.True(cw.Box(Challenge.Fasting).IsChecked);
            Assert.False(cw.Box(Challenge.Naked).IsChecked);
            Assert.Equal(30, cw.FastingSlider.Value);
            Assert.Equal(5, (int)cw.RangerBoxes[1].Value);
            Assert.StartsWith("Settings loaded (seed 4)", cw.LoadSettings(Head + "\"tool\": \"challenges\", \"seed\": 4, \"options\": {\"selected\": [\"Monk\", \"Ranger\"]}}"));
            Ui.Pump();
            Assert.True(cw.Box(Challenge.Monk).IsChecked);
            Assert.False(cw.Box(Challenge.Fasting).IsChecked);
            Assert.Contains("Monk and Ranger", cw.ConflictText);
            Refuses(cw.SettingsText, cw.LoadSettings, RandomizerSettings.Challenges);
            cw.Close();

            var mt = w.ShowMantras();
            Ui.Pump();
            mt.SeedBox.Value = 4321;
            text = mt.SettingsText(false);
            Assert.Contains("\"seed\": 4321", text);
            mt.SeedBox.Value = 1;
            Assert.StartsWith("Settings loaded (seed 4321)", mt.LoadSettings(text));
            Assert.Equal(4321, (int)mt.SeedBox.Value);
            Refuses(mt.SettingsText, mt.LoadSettings, RandomizerSettings.Mantras);
            mt.Close();

            var vw = w.ShowVowelPlay();
            Ui.Pump();
            vw.SeedBox.Value = 17;
            vw.KeepFirstBox.IsChecked = true;
            vw.NamesBox.Text = "Garamon\nTyball";
            Ui.Pump();
            text = vw.SettingsText(false);
            vw.SeedBox.Value = 1;
            vw.KeepFirstBox.IsChecked = false;
            vw.NamesBox.Text = "Bragit";
            Ui.Pump();
            Assert.StartsWith("Settings loaded (seed 17)", vw.LoadSettings(text));
            Ui.Pump();
            Assert.Equal(text, vw.SettingsText(false));
            Assert.Equal("Garamon\nTyball", vw.NamesBox.Text);
            Assert.True(vw.KeepFirstBox.IsChecked);
            // names left out: the game's own
            vw.LoadSettings(Head + "\"tool\": \"vowel-play\", \"seed\": 2}");
            Ui.Pump();
            Assert.Null(vw.Options.Names);
            Assert.Contains("Garamon", vw.NamesBox.Text);
            Refuses(vw.SettingsText, vw.LoadSettings, RandomizerSettings.VowelPlay);
            vw.Close();

            var aw = w.ShowAutoZones();
            Ui.Pump();
            Box(aw, "Lock picking in logic").IsChecked = true;
            Box(aw, "Cut at level changes").IsChecked = false;
            Ui.Pump();
            text = aw.SettingsText(false);
            Assert.DoesNotContain("\"seed\"", text);
            Box(aw, "Lock picking in logic").IsChecked = false;
            Box(aw, "Cut at level changes").IsChecked = true;
            Assert.StartsWith("Settings loaded, written by editor", aw.LoadSettings(text));
            Ui.Pump();
            Assert.Equal(text, aw.SettingsText(false));
            Assert.True(Box(aw, "Lock picking in logic").IsChecked);
            Assert.False(Box(aw, "Cut at level changes").IsChecked);
            Assert.True(aw.LogicOptions.LockPicking);
            Refuses(aw.SettingsText, aw.LoadSettings, RandomizerSettings.AutoZones);
            aw.Close();
        }
    }
}
