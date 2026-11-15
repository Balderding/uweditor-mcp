using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Randomizer ▸ Key item shuffle: the options shared with the Automatic zones window, preview, shuffle, undo and the spoiler log.</summary>
    public class KeyShuffleWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        [AvaloniaFact]
        public void Preview_shuffle_and_undo()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.Same(kw, w.ShowKeyShuffle());
            // the logic options are the Automatic zones window's, with the orb option
            Assert.True(Box(kw, "Ring of Levitation").IsChecked);
            Assert.False(Box(kw, "Require breaking the orb").IsChecked);
            Box(kw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(kw.Options.Logic.RequireOrb);
            foreach (var c in KeyShuffle.Categories) Assert.True(Box(kw, KeyShuffle.CategoryName(c)).IsChecked);
            Assert.False(Box(kw, "Creature inventories").IsChecked);
            Assert.Empty(kw.Options.Extra);                                  // no additional items at first
            Assert.True(Box(kw, "Place anywhere").IsChecked);
            kw.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 11;
            Ui.Pump();
            Assert.Equal(11, kw.Options.Seed);

            var edits = w.ViewModel.Edits;
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.NotNull(r);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Contains("WHERE EACH KEY ITEM WENT", kw.Spoiler);
            Assert.Contains("the goal needs the orb broken", kw.Spoiler);
            Assert.Empty(edits.DirtyLevels);
            string dir = Environment.GetEnvironmentVariable("KEYSHUFFLE_SHOTS");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); new Ui.Codec().Save(Ui.Capture(kw), Path.Combine(dir, "keyshuffle.png")); }

            // Shuffle writes the preview on show (the same seed and levels)
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            Assert.Same(r, kw.Result);
            Assert.Equal(r.After.Keys.OrderBy(x => x), edits.DirtyLevels.OrderBy(x => x));
            foreach (var kv in r.After) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value));
            string log = Path.Combine(Path.GetTempPath(), "uwedit-keyshuffle-" + Guid.NewGuid().ToString("N") + ".txt");
            try { kw.ExportTo(log); Assert.Contains("KEY ITEM SHUFFLE", File.ReadAllText(log)); }
            finally { File.Delete(log); }

            // Undo shuffle puts every byte back
            Assert.True(kw.UndoNow());
            Ui.Pump();
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            kw.Close();

            // the Automatic zones window shows the same options, the orb one included
            var aw = w.ShowAutoZones();
            Ui.Pump();
            Assert.False(Box(aw, "Require breaking the orb").IsChecked);
            Box(aw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(aw.LogicOptions.RequireOrb);
            aw.Close();
            w.Close();
        }

        /// <summary>UW2: the window shows UW2's four categories in UW2's words, no longer says UW1 only, and Preview runs from the game's own start and from a random castle start.</summary>
        [AvaloniaFact]
        public void Uw2_shows_its_own_categories_and_previews_from_the_games_start()
        {
            if (Uw2 == null) return;
            var w = Ui.Open(Uw2);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            var labels = kw.GetLogicalDescendants().OfType<CheckBox>().Select(c => c.Content as string ?? "").ToList();
            foreach (var c in KeyShuffle.Uw2Categories) Assert.True(Box(kw, KeyShuffle.CategoryName(c, true)).IsChecked);
            Assert.Contains(labels, l => l.StartsWith("Plot items (key gems"));
            Assert.DoesNotContain(labels, l => l.StartsWith("Key of Infinity parts") || l.StartsWith("Talismans") || l.StartsWith("Dragon skin"));
            var texts = kw.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.DoesNotContain(texts, t => t.Contains("Ultima Underworld 1 only"));
            Assert.Contains(texts, t => t.Contains("plot at 16"));
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.NotNull(r);
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.Contains("reaches the end", kw.SummaryText);
            Assert.Contains("The castle plot reaches 16", kw.Spoiler);
            // a random start works on UW2 (0.1.49), with the scope in UW2's words: the castle, any world
            kw.StartBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal(new[] { "the castle", "any world" }, kw.ScopeBox.Items.Cast<string>().ToArray());
            Assert.Contains("the castle", kw.StartNote);
            kw.ScopeBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Contains("any world", kw.StartNote);
            kw.ScopeBox.SelectedIndex = 0;
            Ui.Pump();
            var u = kw.PreviewNow();
            Ui.Pump();
            Assert.True(u.Success, u.Refused ?? string.Join("; ", u.Failures));
            Assert.InRange(u.Start.Level, 1, 5);
            Assert.Contains("the first step at level 1 (19, 48) teleports the player there", kw.SummaryText);
            Assert.Contains("random (the castle)", kw.Spoiler);
            Assert.DoesNotContain("You start in", kw.SummaryText);      // a castle start: no note about a world the gem keeps shut
            // the spoiler log in the map notes is UW1's only: the box is off and greyed out
            Assert.False(Box(kw, "Spoiler log in the map notes").IsEnabled);
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_pits_gem_option_is_in_both_windows_with_its_tooltip()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            Ui.Pump();
            var box = Box(aw, "The Pits' key gem only by Blog's way");
            Assert.False(box.IsChecked);
            Assert.Contains("Killing Dorstag", ToolTip.GetTip(box) as string);
            box.IsChecked = true;
            Ui.Pump();
            Assert.True(aw.LogicOptions.PitsGemByBlog);
            Assert.Contains("pits_gem_by_blog", aw.LogicOptions.Tokens());
            box.IsChecked = false;
            Ui.Pump();
            Assert.False(aw.LogicOptions.PitsGemByBlog);
            aw.Close();
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.False(Box(kw, "The Pits' key gem").IsChecked);
            Box(kw, "The Pits' key gem").IsChecked = true;
            Ui.Pump();
            Assert.True(kw.Options.Logic.PitsGemByBlog);
            kw.Close();
            w.Close();
        }

        /// <summary>The circle a new character casts from runes (AbilityOptions.NewCharacterCircle): a number in both windows, 1 at first, with its tooltip.</summary>
        [AvaloniaFact]
        public void The_new_character_circle_is_in_both_windows_with_its_tooltip()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            Func<Window, NumericUpDown> circle = win => win.GetLogicalDescendants().OfType<NumericUpDown>().First(n => (ToolTip.GetTip(n) as string ?? "").Contains("start's free area"));
            var aw = w.ShowAutoZones();
            Ui.Pump();
            var n = circle(aw);
            Assert.Equal(1, (int)n.Value);
            Assert.Contains("level 2 x circle - 1", ToolTip.GetTip(n) as string);
            n.Value = 8;
            Ui.Pump();
            Assert.Equal(8, aw.LogicOptions.NewCharacterCircle);
            Assert.Equal(int.MaxValue, aw.LogicOptions.ToSolver().NewCharacterCircle);
            n.Value = 1;
            Ui.Pump();
            aw.Close();
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            var k = circle(kw);
            Assert.Equal(1, (int)k.Value);
            k.Value = 6;
            Ui.Pump();
            Assert.Equal(6, kw.Options.Logic.NewCharacterCircle);
            kw.Close();
            w.Close();
        }

        static void Wait(System.Threading.Tasks.Task task)
        {
            var until = DateTime.UtcNow.AddMinutes(2);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
            Assert.True(task.IsCompleted);
        }

        [AvaloniaFact]
        public void Additional_items_are_picked_by_class_or_kind_in_their_own_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.Empty(kw.Options.Extra);
            var ew = kw.ShowExtra();
            Ui.Pump();
            Assert.Same(ew, kw.ShowExtra());
            Assert.Contains(ew.Offered, x => x.Class == "containers");
            ew.SetClass("containers", true);
            Assert.Contains(0x080, kw.Options.Extra);                 // the sack
            Assert.DoesNotContain(0x121, kw.Options.Extra);           // not the bedroll, which is "other"
            Box(ew, "bedroll").IsChecked = true;
            Ui.Pump();
            Assert.Contains(0x121, kw.Options.Extra);
            ew.SetClass("containers", false);
            Assert.Equal(new[] { 0x121 }, kw.Options.Extra.ToArray());
            ew.Close();
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Apply_uses_the_zones_windows_zoning_and_quick_autozone_makes_one_when_there_is_none()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            Assert.False(z.HasZones);                       // a new zoning: nothing painted
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.False(kw.UsesZoning);
            Assert.True(kw.QuickAutozoneButton.IsEnabled);
            // Quick autozone: automatic zones in the Zones window, and the button goes faint
            var task = kw.QuickAutozoneAsync();
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            Wait(task);
            Assert.True(task.Result);
            Assert.True(z.HasZones);
            Assert.True(AutoZones.IsAutomatic(z.Set));
            Assert.Equal("uw1-zones-auto.json", Path.GetFileName(z.Path));
            Assert.True(kw.UsesZoning);
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            var r = kw.PreviewNow();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.NotNull(r.Options.Zoning);
            Assert.Contains("Zones: uw1-zones-auto.json with its unsaved changes (the Zones window's)", kw.Spoiler);
            // a painted zoning in the Zones window: Apply follows it, and names the places by it
            z.LoadExample();
            Ui.Pump();
            Assert.True(kw.UsesZoning);
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            var p = kw.PreviewNow();
            Assert.True(p.Success, string.Join("; ", p.Failures));
            Assert.Contains("Places are named by the zoning", kw.Spoiler);
            Assert.False(AutoZones.IsAutomatic(p.Options.Zoning));
            // a new, empty zoning: automatic zones made by Apply again, and Quick autozone comes back
            z.NewFile();
            Ui.Pump();
            Assert.False(kw.UsesZoning);
            Assert.True(kw.QuickAutozoneButton.IsEnabled);
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_options_line_up_and_apply_puts_a_full_torch_and_the_map_and_explores_the_maps()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            kw.Width = 1000; kw.Height = 940;
            Ui.Pump();
            // checkboxes in the same column start at the same place, in every group
            Func<string, double> x = t => Box(kw, t).TranslatePoint(new Avalonia.Point(0, 0), kw).Value.X;
            double first = x("Keys");
            foreach (var t in new[] { "Runestones", "Plot items", "Creature inventories", "A full torch", "Magic", "Ring of Levitation", "Cut at level changes", "Consider the current starting square" })
                Assert.Equal(first, x(t), 0);
            Assert.Equal(x("Key of Infinity parts"), x("Ability items"), 0);
            Assert.Equal(x("Spoiler log in the map notes"), x("Every map square explored"), 0);   // the longer choices: a grid of their own
            Assert.Equal(x("Talismans"), x("Dragon skin"), 0);
            Assert.Equal(x("Single-use items count"), x("Killing NPCs"), 0);
            Assert.Equal(x("Single-use items count"), x("Require breaking the orb"), 0);
            Assert.Equal(x("Single-use items count"), x("Retry until found"), 0);
            // the helps: off at first, ticked here, done at Apply
            Assert.False(Box(kw, "A full torch").IsChecked);
            Assert.False(Box(kw, "Every map square explored").IsChecked);
            Box(kw, "A full torch").IsChecked = true;
            Box(kw, "Every map square explored").IsChecked = true;
            Ui.Pump();
            var vm = w.ViewModel;
            var level1 = (byte[])vm.Edits.For(1).Data.Clone();
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            Assert.Contains("AT APPLY", kw.Spoiler);
            Assert.Contains("explored", kw.Spoiler);
            var m = vm.Edits.Model(1);
            Assert.Contains(m.Objects, o => !o.Free && o.Item == UwEditor.Core.Randomizer.StartAids.Map);
            Assert.Contains(m.Objects, o => !o.Free && (o.Item == 0x91 || o.Item == 0x95) && o.Quality == 63);
            Assert.True(UltimateEditor.Games.AutomapLevel.Seen(vm.Edits.AutomapFor(1).Map[2 * 64 + 32]));
            // Undo apply takes them back
            Assert.True(kw.UndoNow());
            Assert.True(vm.Edits.For(1).Data.AsSpan().SequenceEqual(level1));
            Assert.False(UltimateEditor.Games.AutomapLevel.Seen(vm.Edits.AutomapFor(1).Map[2 * 64 + 32]));
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Edit_undo_on_one_level_takes_the_whole_scatter_back_so_it_applies_again()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var before = vm.Session.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])vm.Edits.Model(x.Level).Block.Data.Clone());
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            var changed = vm.Edits.DirtyLevels.ToList();
            Assert.True(changed.Count > 3);
            // Edit > Undo with one of them shown: all of them go back
            vm.LoadLevel(changed[1]);
            Ui.Pump();
            w.Undo();
            Ui.Pump();
            Assert.Contains("made together", vm.EditStatus);
            foreach (var kv in before) Assert.True(vm.Edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            // and the scatter applies again, as the first time
            var r = kw.PreviewNow();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Equal(1, r.Attempts);
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_start_is_the_games_own_a_random_or_a_given_square_and_a_refused_square_says_why()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            // the game's own square at first: no scope, no square
            Assert.Equal(StartChoice.Own, kw.Options.Start);
            Assert.False(kw.ScopeBox.IsVisible);
            Assert.False(kw.StartLevelBox.IsEffectivelyVisible);
            Assert.Contains("level 1 (32, 2)", kw.StartNote);
            // random: the scope in the game's words
            kw.StartBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal(StartChoice.Random, kw.Options.Start);
            Assert.True(kw.ScopeBox.IsVisible);
            Assert.False(kw.StartLevelBox.IsEffectivelyVisible);
            Assert.Equal(new[] { "level 1 only", "any level" }, kw.ScopeBox.ItemsSource.Cast<string>());
            Assert.Equal(StartScope.FirstLevels, kw.Options.StartScope);
            kw.ScopeBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal(StartScope.AnyLevel, kw.Options.StartScope);
            Assert.Contains("teleports the player there", kw.StartNote);
            // this square: the one selected in the main window
            var vm = w.ViewModel;
            vm.LoadLevel(3);
            Ui.Pump();
            vm.Select(-1, 9, 55, true);
            Ui.Pump();
            Assert.True(kw.UseSelectedSquare());
            Ui.Pump();
            Assert.Equal(StartChoice.Given, kw.Options.Start);
            Assert.False(kw.ScopeBox.IsVisible);
            Assert.True(kw.StartLevelBox.IsEffectivelyVisible);
            Assert.Equal((3, 9, 55), (kw.Options.StartAt.Level, (int)kw.Options.StartAt.X, (int)kw.Options.StartAt.Y));
            Assert.Contains("level 3 (9, 55)", kw.StartNote);
            // a square on water: refused, with the picker's reason, under the start and under the buttons
            kw.StartLevelBox.Value = 1; kw.StartXBox.Value = 40; kw.StartYBox.Value = 30;
            Ui.Pump();
            Assert.Equal((1, 40, 30), (kw.Options.StartAt.Level, (int)kw.Options.StartAt.X, (int)kw.Options.StartAt.Y));
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.False(r.Success);
            Assert.Contains("water", r.Refused);
            Assert.Contains("water", kw.StartNote);
            Assert.Contains("water", kw.SummaryText);
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Apply_with_a_random_start_writes_the_teleport_and_the_kit_there_and_undo_takes_all_back()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var before = vm.Session.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])vm.Edits.Model(x.Level).Block.Data.Clone());
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            kw.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 3;
            kw.StartBox.SelectedIndex = 1;
            Box(kw, "A full torch").IsChecked = true;
            Ui.Pump();
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.Equal(1, r.Start.Level);
            Assert.Contains(string.Format("The game starts at level 1 ({0}, {1}): the first step at level 1 (32, 2) teleports the player there", (int)r.Start.X, (int)r.Start.Y), kw.SummaryText);
            // Apply writes the preview on show: the teleport to the start, the torch and the map beside it
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            Assert.Same(r, kw.Result);
            var tele = StartTeleport.Find(vm.Session, vm.Edits.Model);
            Assert.NotNull(tele);
            Assert.Equal((r.Start.Level, (int)r.Start.X, (int)r.Start.Y), (tele.ToLevel, tele.ToX, tele.ToY));
            Assert.Contains("teleports the player there", kw.Spoiler);
            var m = vm.Edits.Model(1);
            Assert.Contains(m.Objects, o => !o.Free && o.TileX >= 0 && o.Item == StartAids.Map && Math.Abs(o.TileX - (int)r.Start.X) <= 1 && Math.Abs(o.TileY - (int)r.Start.Y) <= 1);
            // Undo apply puts every byte of every level back, the teleport and the kit with the items
            Assert.True(kw.UndoNow());
            Ui.Pump();
            foreach (var kv in before) Assert.True(vm.Edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            Assert.Null(StartTeleport.Find(vm.Session, vm.Edits.Model));
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_zones_windows_split_by_level_option_reaches_apply_and_the_logic_check()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            z.LoadExample();
            var zw = w.ShowZones();
            Ui.Pump();
            Assert.False(zw.SplitByLevelBox.IsChecked == true);
            zw.SplitByLevelBox.IsChecked = true;
            Ui.Pump();
            Assert.True(z.SplitByLevel);
            Assert.True(w.ZoneSplitByLevel);                // remembered in the settings
            Assert.False(z.Dirty && z.Set.Links.Any(k => k.Notes.Contains("split by level")));   // the zoning itself is not changed
            Assert.Equal(7, z.Set.LevelsOf(2).Count);
            // Apply gets the split copy
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            var r = kw.PreviewNow();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Single(r.Options.Zoning.LevelsOf(2));
            Assert.Contains("multi-level zones split by level", kw.Spoiler);
            // so does the zone graph's logic check
            var g = w.ShowZoneGraph();
            Ui.Pump();
            string text = g.CheckLogic();
            Assert.Contains("Split by level: 5 zone(s) on several levels split into", text);
            Assert.True(g.LastCheck.Solve.GoalReached);
            zw.SplitByLevelBox.IsChecked = false;
            Ui.Pump();
            Assert.False(z.SplitByLevel);
            g.Close(); kw.Close(); zw.Close();
            w.Close();
        }
    }
}
