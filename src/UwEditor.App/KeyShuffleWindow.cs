using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Scatter ▸ Apply (the key item shuffle): the key items (keys, the Key of Infinity's parts, the
    /// talismans lying as objects, runestones, ability items, the boots' ingredients, plot items)
    /// move to new places so that the game stays beatable under the chosen logic (assumed fill on
    /// the automatic zones' time-aware solver), written into the levels as whole records, one
    /// undoable step a level; Undo shuffle puts every byte back. Preview shows the spoiler log
    /// without changing anything. Both games; on UW2 the categories are UW2's (KeyShuffle.Uw2Categories),
    /// the goal is the castle's plot 16, a random start lies in the castle or any world, and the
    /// spoiler log in the map notes is UW1's only.
    /// </summary>
    public sealed class KeyShuffleWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly KeyShuffleOptions opt = new KeyShuffleOptions();
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly NumericUpDown retries = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 1, FormatString = "0", Width = 110 };
        readonly Button previewButton = new Button { Content = "Preview" };
        readonly Button shuffleButton = new Button { Content = "Apply", FontWeight = FontWeight.SemiBold };
        readonly Button undoButton = new Button { Content = "Undo apply", IsEnabled = false };
        readonly Button exportButton = new Button { Content = "Export spoiler log…", IsEnabled = false };
        readonly List<KeyShuffleResult> applied = new List<KeyShuffleResult>();
        bool busy;
        // the zoning the solver uses: the Zones window's
        readonly ZoningState zoning;
        readonly TextBlock zoningText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        readonly Button quickButton = new Button { Content = "Quick autozone" };
        bool quickRunning, namesFromZoning;
        readonly CheckBox kitBox = new CheckBox { Content = "A full torch (quality 63) and the map beside the start" };
        readonly CheckBox mapsBox = new CheckBox { Content = "Every map square explored" };
        readonly TextBlock extraText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        KeyShuffleExtraWindow extraWindow;
        readonly CheckBox anywhereBox = new CheckBox { Content = "Place anywhere (random squares, not where items lay)" };
        readonly CheckBox notesBox = new CheckBox { Content = "Spoiler log in the map notes (pages 9, 10 …)" };
        // where the game starts: the game's own square, a random one (level 1 only or any level), or one given
        readonly ComboBox startBox = new ComboBox { ItemsSource = new[] { "The game's own square", "A random square", "This square" }, SelectedIndex = 0, Width = 190 };
        readonly ComboBox scopeBox = new ComboBox { Width = 150 };
        readonly NumericUpDown startLevel = new NumericUpDown { Minimum = 1, Maximum = 9, Increment = 1, FormatString = "0", Width = 100 };
        readonly NumericUpDown startX = new NumericUpDown { Minimum = 1, Maximum = 62, Increment = 1, FormatString = "0", Width = 100 };
        readonly NumericUpDown startY = new NumericUpDown { Minimum = 1, Maximum = 62, Increment = 1, FormatString = "0", Width = 100 };
        readonly Button selectedButton = new Button { Content = "The selected square" };
        readonly StackPanel givenRow;
        readonly TextBlock startNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, MaxWidth = 760 };
        // the controls Load settings sets
        readonly LogicOptionsPanel logicPanel;
        readonly Dictionary<string, CheckBox> moveBoxes = new Dictionary<string, CheckBox>();
        readonly CheckBox creaturesBox = new CheckBox { Content = "Creature inventories (creatures without a conversation)" };

        /// <summary>The last preview or shuffle, or null.</summary>
        public KeyShuffleResult Result { get; private set; }
        /// <summary>The options of the next run.</summary>
        public KeyShuffleOptions Options { get { return opt; } }
        /// <summary>The spoiler log on show.</summary>
        public string Spoiler { get { return report.Text ?? ""; } }
        /// <summary>The line under the buttons: what the last run did, or why it could not.</summary>
        public string SummaryText { get { return summary.Text ?? ""; } }

        public KeyShuffleWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Apply";
            Width = 1000; Height = 960; MinWidth = 680; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var top = new StackPanel { Spacing = 6 };
            top.Children.Add(new TextBlock { Text = "Apply", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Moves the key items to new places so that the game stays beatable with the logic chosen below. The zones (the Zones window's zoning, or automatic zones when there is none) and the time-aware solver decide what is reachable. " +
                       "The key items are placed one by one, each somewhere the solver can reach while it counts the items not yet placed as held (assumed fill); the other items fill the rest. " +
                       "A fresh solve of the shuffled levels must then reach " + (s.IsUw2 ? "the end (the castle's plot at 16, Nystul's ceremony)" : "level 9") + " without a one-way trap, or the next seed is tried. Items move whole (keys keep their lock numbers); with Place anywhere, the places on the floor are random squares the logic reaches. " +
                       "Start: the game can begin on another square, random or given; its first step then teleports the player there, and the items the way out needs are placed within reach. " +
                       "Nothing is written until you press Apply, which makes one undoable step per level; the changes are saved with the levels.",
            });
            // the zoning: the Zones window's, or Quick autozone when there is none
            zoning = main.Zoning();
            quickButton.Click += async (o, e) => await QuickAutozoneAsync();
            ToolTip.SetTip(quickButton, "Makes automatic zones with the movement options below and puts them in the Zones window, where you can look at and change them; Apply then uses them. Available only when there is no zoning yet.");
            var zonesButton = new Button { Content = "Zones window…" };
            zonesButton.Click += (o, e) => main.ShowZones();
            var head = new OptionGrid(1);
            head.AddRow("Zones", LogicOptionsPanel.Row(zoningText, quickButton, zonesButton));
            top.Children.Add(head);
            if (zoning != null)
            {
                zoning.Changed += OnZoningChanged;
                Closed += (o, e) => zoning.Changed -= OnZoningChanged;
            }
            RefreshZoning();
            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            retries.Value = opt.Retries;
            retries.ValueChanged += (o, e) => { opt.Retries = (int)(retries.Value ?? 20); };
            head.AddRow("Seed", LogicOptionsPanel.Row(seed, newSeed, LogicOptionsPanel.Label("Retries", 60), retries, LogicOptionsPanel.Note("How many further seeds to try when a fill fails the check.")));
            SettingsButtons = new SettingsBar(() => RandomizerSettings.FileName(RandomizerSettings.GameOf(session), RandomizerSettings.KeyScatter, opt.Seed), SettingsText, LoadSettings, t => summary.Text = t);
            head.AddRow("Settings", SettingsButtons);
            // the logic options, as the Automatic zones window has them
            logicPanel = new LogicOptionsPanel(opt.Nav, opt.Logic, true);
            top.Children.Add(logicPanel);
            // what moves, three to a row, lined up with the options above
            top.Children.Add(LogicOptionsPanel.Heading("THE SCATTER", "what moves, and what Apply adds"));
            var moves = new OptionGrid(3);
            var boxes = KeyShuffle.CategoriesOf(s).Select(c =>
            {
                string cat = c;
                var box = new CheckBox { Content = KeyShuffle.CategoryName(c, s.IsUw2), IsChecked = opt.Move.Contains(c) };
                box.IsCheckedChanged += (o, e) => { if (box.IsChecked == true) opt.Move.Add(cat); else opt.Move.Remove(cat); };
                moveBoxes[cat] = box;
                return (Control)box;
            }).ToList();
            for (int i = 0; i < boxes.Count; i += 3) moves.AddRow(i == 0 ? "Move" : "", boxes.Skip(i).Take(3).Concat(Enumerable.Repeat<Control>(null, Math.Max(0, 3 - (boxes.Count - i)))).ToArray());
            var creatures = creaturesBox;
            creatures.IsChecked = opt.CreatureInventories;
            creatures.IsCheckedChanged += (o, e) => opt.CreatureInventories = creatures.IsChecked == true;
            // additional items: a row with what is picked and the picker
            var extraButton = new Button { Content = "…", Padding = new Thickness(10, 2) };
            ToolTip.SetTip(extraButton, "Picks other kinds of items to move with the key items: weapons, armour, coins, food, containers, bedrolls…");
            extraButton.Click += (o, e) => ShowExtra();
            var filler = LogicOptionsPanel.Row(new TextBlock { Text = "Additional items to include in the shuffle:", VerticalAlignment = VerticalAlignment.Center }, extraText, extraButton);
            RefreshExtra();
            top.Children.Add(moves);
            // the longer choices in a grid of their own, so that the categories' columns stay close
            var more = new OptionGrid(2);
            more.AddRow("Also", creatures, filler);
            kitBox.IsChecked = opt.StartKit;
            kitBox.IsCheckedChanged += (o, e) => opt.StartKit = kitBox.IsChecked == true;
            ToolTip.SetTip(kitBox, "At Apply, a torch of quality 63 and the map are placed beside the start square (the editor's, or the game's own when \"Consider the current starting square\" is off). A torch or map already there is filled up or kept; a missing one is put on the floor beside the start.");
            mapsBox.IsChecked = opt.ExploreMaps;
            mapsBox.IsCheckedChanged += (o, e) => opt.ExploreMaps = mapsBox.IsChecked == true;
            ToolTip.SetTip(mapsBox, "At Apply, every level's map shows every square, as if the player had seen it all; the map notes are kept.");
            more.AddRow("At the start", kitBox, mapsBox);
            anywhereBox.IsChecked = opt.Anywhere;
            anywhereBox.IsCheckedChanged += (o, e) => opt.Anywhere = anywhereBox.IsChecked == true;
            ToolTip.SetTip(anywhereBox, "On: the places on the floor become random squares of their level that the logic reaches: open floor, slopes and the open half of a diagonal, but not water or lava, and not a square with a door, a trap or a trigger. Items in containers and creatures keep those places. Off: the items trade the places where items lay.");
            notesBox.IsChecked = opt.SpoilerNotes;
            notesBox.IsCheckedChanged += (o, e) => opt.SpoilerNotes = notesBox.IsChecked == true;
            // the notes' pages past the last level are UW1's (the map screen's up arrow goes on past level 9); UW2's eighty levels leave no such pages
            if (s.IsUw2) { notesBox.IsChecked = false; notesBox.IsEnabled = false; notesBox.Content = "Spoiler log in the map notes (not on Ultima Underworld 2)"; }
            ToolTip.SetTip(notesBox, s.IsUw2 ? "Ultima Underworld 1 only: its map screen goes on past level 9 to pages that can hold the log. On Ultima Underworld 2 the log is in the window and the exported file only." : "At Apply, the log goes into the game's own map notes, which a new game copies from DATA: page 9 (the Ethereal Void's map) gets a warning, from page 10 the hints (each artifact, and each item the game cannot be finished without, by the name of the zone it lies in, or its level and the part of it), then a page saying SOLUTIONS NEXT, then the solutions (level and square). The map screen's up arrow goes past level 9 to these pages.");
            more.AddRow("Places", anywhereBox, notesBox);
            top.Children.Add(more);
            // where the game starts
            scopeBox.ItemsSource = s.IsUw2 ? new[] { "the castle", "any world" } : new[] { "level 1 only", "any level" };
            scopeBox.SelectedIndex = opt.StartScope == StartScope.AnyLevel ? 1 : 0;
            startLevel.Maximum = Math.Max(1, s.LevelSlots().Where(x => x.Present).Select(x => x.Level).DefaultIfEmpty(9).Max());
            startLevel.Value = 1; startX.Value = 32; startY.Value = 32;
            givenRow = LogicOptionsPanel.Row(LogicOptionsPanel.Label("Level", 40), startLevel, LogicOptionsPanel.Label("X", 14), startX, LogicOptionsPanel.Label("Y", 14), startY, selectedButton);
            startBox.SelectionChanged += (o, e) => { opt.Start = (StartChoice)Math.Max(0, startBox.SelectedIndex); RefreshStart(); };
            scopeBox.SelectionChanged += (o, e) => { opt.StartScope = scopeBox.SelectedIndex == 1 ? StartScope.AnyLevel : StartScope.FirstLevels; RefreshStart(); };
            startLevel.ValueChanged += (o, e) => SetStartAt();
            startX.ValueChanged += (o, e) => SetStartAt();
            startY.ValueChanged += (o, e) => SetStartAt();
            selectedButton.Click += (o, e) => UseSelectedSquare();
            ToolTip.SetTip(startBox, "Where the game starts. The game still puts the player on its own square; Apply puts a teleport there that his first step takes, once, to the start chosen. A random start is drawn from each attempt's seed among the squares the game can be finished from; the key items the way out needs are placed within reach.");
            ToolTip.SetTip(scopeBox, s.IsUw2 ? "Where a random start may lie: the castle (levels 1 to 5), or any of the nine worlds." : "Where a random start may lie: level 1, or any level but level 9 (reaching it is the goal).");
            ToolTip.SetTip(selectedButton, "Takes the square selected in the main window, on the level shown there.");
            var startGrid = new OptionGrid(1);
            startGrid.AddRow("Start", LogicOptionsPanel.Row(startBox, scopeBox, givenRow));
            startGrid.AddRow("", startNote);
            top.Children.Add(startGrid);
            RefreshStart();

            previewButton.Click += async (o, e) => await RunAsync(false);
            shuffleButton.Click += async (o, e) => await RunAsync(true);
            undoButton.Click += (o, e) => UndoNow();
            exportButton.Click += (o, e) => Export();
            ToolTip.SetTip(previewButton, "Computes a shuffle and shows its spoiler log; nothing changes.");
            ToolTip.SetTip(shuffleButton, "Writes the shuffle shown (or a new one for these settings) into the levels.");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            buttons.Children.Add(previewButton); buttons.Children.Add(shuffleButton); buttons.Children.Add(undoButton); buttons.Children.Add(exportButton);
            var actions = new StackPanel();
            actions.Children.Add(buttons);
            actions.Children.Add(summary);

            // the settings scroll when the window is short; the buttons stay in sight, the log takes the rest
            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            var scroll = new ScrollViewer { Content = top, MaxHeight = 560, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            // the settings take what the window can spare, the log keeps at least 220 pixels
            SizeChanged += (o, e) => scroll.MaxHeight = Math.Max(240, e.NewSize.Height - 300);
            Grid.SetRow(actions, 1);
            Grid.SetRow(report, 2);
            root.Children.Add(scroll); root.Children.Add(actions); root.Children.Add(report);
            Content = root;
            ManualWindow.Attach(this, "Apply");
        }

        /// <summary>The additional items' picker (one at a time), editing the options' set in place.</summary>
        public KeyShuffleExtraWindow ShowExtra()
        {
            if (extraWindow != null) { extraWindow.Activate(); return extraWindow; }
            extraWindow = new KeyShuffleExtraWindow(session, l => main.ViewModel.Edits.Model(l), opt.Extra);
            extraWindow.Changed += RefreshExtra;
            extraWindow.Closed += (o, e) => { extraWindow = null; RefreshExtra(); };
            extraWindow.Show(this);
            return extraWindow;
        }

        void RefreshExtra()
        {
            extraText.Text = opt.Extra.Count == 0 ? "none" : KeyShuffleExtraWindow.Summary(ExtraItems.Offered(session, l => main.ViewModel.Edits.Model(l)), opt.Extra);
        }

        // ---- the start

        /// <summary>The start's choice (the game's own square, a random one, this square), its scope (shown with a random start) and the square's level, x and y (shown with a given one).</summary>
        public ComboBox StartBox { get { return startBox; } }
        public ComboBox ScopeBox { get { return scopeBox; } }
        public NumericUpDown StartLevelBox { get { return startLevel; } }
        public NumericUpDown StartXBox { get { return startX; } }
        public NumericUpDown StartYBox { get { return startY; } }
        /// <summary>The line under the start's controls: what the choice does, or why a given square was refused.</summary>
        public string StartNote { get { return startNote.Text ?? ""; } }

        void SetStartAt()
        {
            int l = (int)(startLevel.Value ?? 1), x = (int)(startX.Value ?? 32), y = (int)(startY.Value ?? 32);
            opt.StartAt = KeyShuffleOptions.GivenStart(l, x, y);
            RefreshStart();
        }

        /// <summary>This square: the square selected in the main window, on the level shown there. False (and a note) when none is selected.</summary>
        public bool UseSelectedSquare()
        {
            var vm = main.ViewModel;
            if (vm.Level == null || vm.SelectedTileX < 0) { startNote.Text = "Select a square in the main window first."; return false; }
            startBox.SelectedIndex = (int)StartChoice.Given;
            startLevel.Value = vm.Level.Level; startX.Value = Math.Max(1, Math.Min(62, vm.SelectedTileX)); startY.Value = Math.Max(1, Math.Min(62, vm.SelectedTileY));
            SetStartAt();
            return true;
        }

        void RefreshStart()
        {
            scopeBox.IsVisible = opt.Start == StartChoice.Random;
            givenRow.IsVisible = opt.Start == StartChoice.Given;
            if (opt.Start == StartChoice.Given && opt.StartAt == null) SetStartAt();
            var own = opt.Nav.BaseStartOf(session);
            string from = own != null ? string.Format("level {0} ({1}, {2})", own.Level, (int)own.X, (int)own.Y) : "the game's square";
            switch (opt.Start)
            {
                case StartChoice.Random:
                    startNote.Text = "Each attempt draws a square from its seed (" + (session.IsUw2 ? (opt.StartScope == StartScope.AnyLevel ? "any world" : "the castle") : (opt.StartScope == StartScope.AnyLevel ? "any level but level 9" : "level 1")) +
                        ") among those the game can be finished from. The first step at " + from + " teleports the player there, once.";
                    break;
                case StartChoice.Given:
                    startNote.Text = string.Format("The game starts at level {0} ({1}, {2}): the first step at {3} teleports the player there, once. Preview says whether the square can be used.", opt.StartAt.Level, (int)opt.StartAt.X, (int)opt.StartAt.Y, from);
                    break;
                default:
                    startNote.Text = "The game starts where it always does (" + from + ").";
                    break;
            }
        }

        KeyShuffleResult Compute()
        {
            PrepareZoning();
            string levels = RandomizerSettings.LevelsHash(session, main.ViewModel.Edits);
            return Stamp(KeyShuffle.Run(session, main.ViewModel.Edits, opt), levels);
        }

        // ---- the settings file

        /// <summary>Save, Load, Copy and Paste settings.</summary>
        public SettingsBar SettingsButtons { get; private set; }

        /// <summary>The zoning a run uses, as the settings file records it: automatic zones, or the Zones window's file and a hash of its zones.</summary>
        Dictionary<string, string> ZoningInputs()
        {
            var d = new Dictionary<string, string>();
            if (UsesZoning) { d["zoning"] = Path.GetFileName(zoning.Path) + (zoning.Dirty ? " with unsaved changes" : ""); d["zoningHash"] = RandomizerSettings.ShortHash(zoning.ForSolver().ToJson()); }
            else d["zoning"] = "automatic zones";
            return d;
        }

        /// <summary>The settings on screen now (every option, the seed, the levels' hash and the zoning), pretty or on one line.</summary>
        public string SettingsText(bool compact)
        {
            return RandomizerSettings.Write(RandomizerSettings.GameOf(session), RandomizerSettings.KeyScatter, opt, RandomizerSettings.LevelsHash(session, main.ViewModel.Edits), ZoningInputs(), compact);
        }

        /// <summary>Reads settings text into the options and every control (the logic panel too); the summary line says what was loaded and every warning. Gives that message.</summary>
        public string LoadSettings(string text)
        {
            var r = RandomizerSettings.Read(text, RandomizerSettings.GameOf(session), RandomizerSettings.KeyScatter, new KeyShuffleOptions { Seed = opt.Seed });
            if (!r.Ok) return summary.Text = SettingsBar.Refused(r);
            var o = r.As<KeyShuffleOptions>();
            extraWindow?.Close();
            RandomizerSettings.CopyInto(o, opt);
            // the controls follow; their handlers write the same values back
            seed.Value = opt.Seed;
            retries.Value = opt.Retries;
            logicPanel.Refresh();
            foreach (var kv in moveBoxes) kv.Value.IsChecked = opt.Move.Contains(kv.Key);
            creaturesBox.IsChecked = opt.CreatureInventories;
            kitBox.IsChecked = opt.StartKit;
            mapsBox.IsChecked = opt.ExploreMaps;
            anywhereBox.IsChecked = opt.Anywhere;
            notesBox.IsChecked = opt.SpoilerNotes;
            RefreshExtra();
            var at = o.StartAt;
            if (at != null) { startLevel.Value = at.Level; startX.Value = (int)at.X; startY.Value = (int)at.Y; }
            opt.StartAt = at;
            scopeBox.SelectedIndex = opt.StartScope == StartScope.AnyLevel ? 1 : 0;
            startBox.SelectedIndex = (int)opt.Start;
            RefreshStart();
            var more = new List<string> { RandomizerSettings.LevelsWarning(r, RandomizerSettings.LevelsHash(session, main.ViewModel.Edits)) };
            string was, wasHash;
            r.Inputs.TryGetValue("zoning", out was);
            r.Inputs.TryGetValue("zoningHash", out wasHash);
            var now = ZoningInputs();
            if (was != null && (was == "automatic zones") != (now["zoning"] == "automatic zones"))
                more.Add("The settings were saved with " + (was == "automatic zones" ? "automatic zones" : "the zoning " + was) + ", and Apply now uses " + (UsesZoning ? "the Zones window's zoning (" + now["zoning"] + ")" : "automatic zones") + ": the result may differ.");
            else if (!string.IsNullOrEmpty(wasHash) && now.ContainsKey("zoningHash") && wasHash != now["zoningHash"])
                more.Add("The Zones window's zoning differs from the one the settings were saved with (" + was + "): the result may differ.");
            return summary.Text = SettingsBar.Loaded(r, more);
        }

        /// <summary>Puts the settings of a run at the top of its log, so that the log alone makes it again.</summary>
        KeyShuffleResult Stamp(KeyShuffleResult r, string levels)
        {
            if (r?.Options == null) return r;
            r.Log.Insert(0, RandomizerSettings.LogLine(RandomizerSettings.Write(RandomizerSettings.GameOf(session), RandomizerSettings.KeyScatter, r.Options, levels, ZoningInputs(), true)) + "\n\n");
            return r;
        }

        // ---- the zoning

        /// <summary>Whether the next run uses the Zones window's zoning (it has zones), rather than automatic zones.</summary>
        public bool UsesZoning { get { return zoning != null && zoning.HasZones; } }

        public Button QuickAutozoneButton { get { return quickButton; } }

        void OnZoningChanged(string what) { if (what != "notes") RefreshZoning(); }

        void RefreshZoning()
        {
            if (UsesZoning)
            {
                zoningText.Text = string.Format("The Zones window's zoning: {0}{1}, {2} zones{3}.", Path.GetFileName(zoning.Path), zoning.Dirty ? " (its unsaved changes included)" : "", zoning.ZoneCount,
                    (AutoZones.IsAutomatic(zoning.Set) ? ", made by the automatic zones" : "") + (zoning.SplitByLevel ? "; multi-level zones split by level (the Zones window's option)" : ""));
                quickButton.IsEnabled = false;
            }
            else
            {
                zoningText.Text = "There is no zoning yet, so Apply makes automatic zones from the movement options below. Quick autozone makes them now and puts them in the Zones window.";
                quickButton.IsEnabled = !quickRunning && !session.IsUw2 && zoning != null;
            }
        }

        /// <summary>Sets the options' zoning (a copy of the Zones window's, or none) and the names of the places in the log.</summary>
        void PrepareZoning()
        {
            if (UsesZoning)
            {
                opt.Zoning = zoning.ForSolver();
                opt.ZoningFrom = Path.GetFileName(zoning.Path) + (zoning.Dirty ? " with its unsaved changes" : "") + (zoning.SplitByLevel ? ", multi-level zones split by level" : "");
                // a painted zoning names the places itself; an automatic one by the hand-made zones, as before
                if (!AutoZones.IsAutomatic(opt.Zoning)) { opt.Names = opt.Zoning; opt.NamesFrom = "the zoning (" + opt.ZoningFrom + ")"; namesFromZoning = true; return; }
            }
            else { opt.Zoning = null; opt.ZoningFrom = ""; }
            if (opt.Names == null || namesFromZoning) { namesFromZoning = false; LoadNames(); }
        }

        /// <summary>Quick autozone: the automatic zones with the movement options here, put in the Zones window (only when it has no zoning).</summary>
        public async Task<bool> QuickAutozoneAsync()
        {
            if (quickRunning || zoning == null || UsesZoning || session.IsUw2) return false;
            quickRunning = true;
            quickButton.IsEnabled = false;
            summary.Text = "Making the automatic zones…";
            ZoneSet made = null;
            string error = null;
            var snap = KeyShuffle.Snapshot(session, main.ViewModel.Edits);
            var nav = opt.Nav.Clone();
            var logic = opt.Logic.Clone();
            await Task.Run(() => { try { made = Timeline.AutoZoning(session, snap.Level, nav, logic); } catch (Exception ex) { error = ex.Message; } });
            quickRunning = false;
            if (error != null) { summary.Text = "Could not make the zones: " + error; RefreshZoning(); return false; }
            // a zoning opened or painted in the Zones window while these were made is not replaced without asking
            if (UsesZoning)
            {
                string a = await MessageWindow.Ask(this, "Quick autozone", "The Zones window got a zoning while the automatic zones were being made (" + Path.GetFileName(zoning.Path) + "). Replace it with the automatic zones?", "Keep it", "Replace");
                if (a != "Replace") { summary.Text = "The automatic zones were made, but the Zones window's zoning was kept."; RefreshZoning(); return false; }
            }
            zoning.Adopt(made,Path.Combine(session.DataFolder, AutoZones.DefaultFileName(session.IsUw2)));
            summary.Text = string.Format("{0} automatic zones made and put in the Zones window. They are not saved yet; saving there asks for a name. Apply now uses them.", zoning.ZoneCount);
            RefreshZoning();
            return true;
        }

        /// <summary>The hand-made zones whose names label the spoiler log: the zone file in DATA, else Karl's painted UW1 map shipped with the editor.</summary>
        void LoadNames()
        {
            try
            {
                string path = System.IO.Path.Combine(session.DataFolder, ZoneSet.DefaultFileName(session.IsUw2));
                if (System.IO.File.Exists(path)) { opt.Names = ZoneSet.Load(path, session.Profile.LevelSlots); opt.NamesFrom = "the zone file in DATA (" + System.IO.Path.GetFileName(path) + ")"; return; }
                if (session.IsUw2) return;
                using (var st = typeof(ZoningState).Assembly.GetManifestResourceStream("uw1-zones.example.json"))
                using (var r = new System.IO.StreamReader(st))
                {
                    opt.Names = ZoneSet.FromJson(r.ReadToEnd(), session.Profile.LevelSlots);
                    opt.NamesFrom = "Karl's painted UW1 map";
                }
            }
            catch (Exception) { opt.Names = null; }
        }

        /// <summary>The last result still fits: computed with these settings from the levels as they are now.</summary>
        bool Fits(KeyShuffleResult r)
        {
            if (r == null || !r.Success || r.IsApplied) return false;
            var o = r.Options;
            if (o.Seed != opt.Seed || o.Retries != opt.Retries || o.Anywhere != opt.Anywhere || o.Filler != opt.Filler || !o.Extra.SetEquals(opt.Extra) || o.CreatureInventories != opt.CreatureInventories || !o.Move.SetEquals(opt.Move)) return false;
            if (o.Start != opt.Start || (opt.Start == StartChoice.Random && o.StartScope != opt.StartScope)) return false;
            if (opt.Start == StartChoice.Given && (o.StartAt == null || opt.StartAt == null || o.StartAt.Level != opt.StartAt.Level || (int)o.StartAt.X != (int)opt.StartAt.X || (int)o.StartAt.Y != (int)opt.StartAt.Y)) return false;
            if (o.Logic.ToString() != opt.Logic.ToString() || o.Nav.TickLength != opt.Nav.TickLength || o.Nav.JumpMargin != opt.Nav.JumpMargin || o.Nav.GapJumps != opt.Nav.GapJumps
                || o.Nav.DiagonalJumps != opt.Nav.DiagonalJumps
                || o.Nav.CutAtLevelChanges != opt.Nav.CutAtLevelChanges || o.Nav.FoldEmptyDeadEnds != opt.Nav.FoldEmptyDeadEnds || o.Nav.ShippedStart != opt.Nav.ShippedStart) return false;
            // the zoning as it is now
            if ((o.Zoning != null) != UsesZoning || (o.Zoning != null && o.Zoning.ToJson() != zoning.ForSolver().ToJson())) return false;
            var edits = main.ViewModel.Edits;
            foreach (var kv in r.Before)
            {
                var e = edits.Existing(kv.Key);
                byte[] now = e != null ? e.Data : session.LoadLevel(kv.Key).Block.Data;
                if (!now.AsSpan().SequenceEqual(kv.Value)) return false;
            }
            return true;
        }

        void Show(KeyShuffleResult r, string prefix)
        {
            Result = r;
            report.Text = r.Log.ToString();
            exportButton.IsEnabled = r.Log.Length > 0;
            if (r.Refused != null)
            {
                summary.Text = r.Refused;
                if (r.Options != null && r.Options.Start == StartChoice.Given) startNote.Text = r.Refused;
                return;
            }
            summary.Text = prefix + (r.Success
                ? string.Format("Seed {0}{1}: {2} items move ({3} key items), {4} levels change; the shuffled game reaches {9} in {5} spheres (the original in {6}).{7}{8}",
                    r.UsedSeed, r.UsedSeed != r.Options.Seed ? " (after " + (r.Attempts - 1) + " failed)" : "", r.Moves.Count(m => m.Item != m.Place), r.Moves.Count(m => m.ByFill && m.Item != m.Place), r.After.Count,
                    r.SpheresAfter, r.SpheresBefore, r.Unlocked.Count > 0 ? " " + r.Unlocked.Count + " door(s) unlocked." : "", StartLine(r), session.IsUw2 ? "the end" : "level 9")
                : "No shuffle passed the check in " + r.Attempts + " attempts; see the log.");
        }

        /// <summary>Where a shuffled game with a random or given start begins, in a sentence (empty with the game's own start).</summary>
        string StartLine(KeyShuffleResult r)
        {
            if (r.Start == null || r.StartFrom == null) return "";
            return string.Format(" The game starts at level {0} ({1}, {2}): the first step at level {3} ({4}, {5}) teleports the player there{6}.", r.Start.Level, (int)r.Start.X, (int)r.Start.Y,
                r.StartFrom.Level, (int)r.StartFrom.X, (int)r.StartFrom.Y, r.MovedIntoReach.Count > 0 ? "; " + r.MovedIntoReach.Count + " key item(s) went to squares within its reach, as no place there was left" : "")
                + (KeyShuffle.LockedWorldNote(session, r.Start.Level) is string locked ? " " + locked : "");
        }

        /// <summary>Preview: computes a shuffle and shows its spoiler log; nothing changes.</summary>
        public KeyShuffleResult PreviewNow()
        {
            try { var r = Compute(); Show(r, "Preview. "); return r; }
            catch (Exception ex) { summary.Text = "Could not apply: " + ex.Message; return null; }
        }

        /// <summary>Shuffle: writes the result on show when it still fits, or a new one, into the levels (one undoable step a level).</summary>
        public bool ShuffleNow()
        {
            try
            {
                var r = Fits(Result) ? Result : Compute();
                return ApplyResult(r);
            }
            catch (Exception ex) { summary.Text = "Could not apply: " + ex.Message; return false; }
        }

        bool ApplyResult(KeyShuffleResult r)
        {
            if (r == null || !r.Success) { if (r != null) Show(r, ""); return false; }
            // the helps are done at Apply, with the choices as they are now
            r.Options.StartKit = opt.StartKit;
            r.Options.ExploreMaps = opt.ExploreMaps;
            r.Options.SpoilerNotes = opt.SpoilerNotes;
            KeyShuffle.Apply(r, main.ViewModel.Edits);
            if (r.AppliedAids.Count > 0)
            {
                r.Log.AppendLine();
                r.Log.AppendLine("AT APPLY");
                foreach (var l in r.AppliedAids) r.Log.AppendLine("  " + l);
            }
            applied.Add(r);
            undoButton.IsEnabled = true;
            Show(r, "Shuffled (not saved yet). ");
            return true;
        }

        async Task RunAsync(bool apply)
        {
            if (busy) return;
            if (apply && Fits(Result)) { ApplyResult(Result); return; }
            busy = true;
            previewButton.IsEnabled = shuffleButton.IsEnabled = false;
            summary.Text = "Applying… (the solver runs once per key item, then checks the result)";
            try
            {
                KeyShuffleResult r = null;
                string error = null;
                var snap = KeyShuffle.Snapshot(session, main.ViewModel.Edits);
                string levels = RandomizerSettings.LevelsHash(session, main.ViewModel.Edits);
                PrepareZoning();
                var o = opt.Clone();
                await Task.Run(() => { try { r = KeyShuffle.RunOn(session, snap, o); } catch (Exception ex) { error = ex.Message; } });
                if (error != null) { summary.Text = "Could not apply: " + error; return; }
                Stamp(r, levels);
                if (apply) ApplyResult(r); else Show(r, "Preview. ");
            }
            finally
            {
                busy = false;
                previewButton.IsEnabled = shuffleButton.IsEnabled = true;
            }
        }

        /// <summary>Undo shuffle: puts the levels of the last shuffle back byte for byte (a level edited since is left alone; Edit ▸ Undo still steps back there).</summary>
        public bool UndoNow()
        {
            if (applied.Count == 0) return false;
            var r = applied[applied.Count - 1];
            applied.RemoveAt(applied.Count - 1);
            var skipped = KeyShuffle.Undo(r, main.ViewModel.Edits);
            undoButton.IsEnabled = applied.Count > 0;
            summary.Text = skipped.Count == 0 ? "The shuffle of seed " + r.UsedSeed + " is undone." :
                "Undone, except level(s) " + string.Join(", ", skipped) + ", which were edited since; use Edit ▸ Undo there.";
            Result = null;
            return skipped.Count == 0;
        }

        async void Export()
        {
            if (Result == null || Result.Log.Length == 0) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the spoiler log", SuggestedFileName = "uw1-key-shuffle-" + (Result.Success ? Result.UsedSeed : Result.Options.Seed) + ".txt", DefaultExtension = "txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path == null) return;
            try { ExportTo(path); summary.Text = "Exported " + path + "."; }
            catch (Exception ex) { summary.Text = "Could not export: " + ex.Message; }
        }

        /// <summary>Writes the spoiler log on show to a text file.</summary>
        public void ExportTo(string path) { File.WriteAllText(path, Result?.Log.ToString() ?? ""); }
    }
}
