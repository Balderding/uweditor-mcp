using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Prefabs (the copied tiles saved under a name, to place again) and Decorate (debris,
    /// plants, moss, bones and stalactites scattered over the selected tiles, on the floor or
    /// hanging from the ceiling as the game has them, rolled again until it looks right).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The folder the prefabs are kept in: "prefabs" under the editor's settings.</summary>
        public static string PrefabFolder { get { return Path.Combine(Settings.Folder, "prefabs"); } }

        PrefabsWindow prefabsWindow;
        DecorateWindow decorateWindow;

        void OnPrefabs(object sender, RoutedEventArgs e) { ShowPrefabs(); }
        void OnDecorate(object sender, RoutedEventArgs e) { ShowDecorate(); }

        public PrefabsWindow ShowPrefabs()
        {
            if (vm.Session == null) return null;
            if (prefabsWindow != null) { prefabsWindow.Activate(); prefabsWindow.Fill(); return prefabsWindow; }
            prefabsWindow = new PrefabsWindow(this);
            prefabsWindow.Closed += (s, e) => prefabsWindow = null;
            prefabsWindow.Show(this);
            return prefabsWindow;
        }

        public List<PrefabInfo> ListPrefabs() { return vm.Session == null ? new List<PrefabInfo>() : Prefabs.List(PrefabFolder, vm.Session.Kind); }

        /// <summary>Saves the copied tiles as a prefab of this name (one of the same name is replaced). True when saved.</summary>
        public bool SavePrefab(string name)
        {
            try
            {
                string path = Prefabs.Save(PrefabFolder, tileClip, name);
                vm.EditStatus = "Prefab \"" + name.Trim() + "\" saved (" + tileClip.Describe() + "): " + path;
                prefabsWindow?.Fill();
                return true;
            }
            catch (Exception ex) { vm.EditStatus = ex.Message; return false; }
        }

        /// <summary>Takes a prefab as the copied tiles: Paste tiles (Ctrl+Alt+V) then places it, turned or mirrored first if wanted. True when taken.</summary>
        public bool UsePrefab(PrefabInfo p)
        {
            try { tileClip = Prefabs.Load(p.Path); }
            catch (Exception ex) { vm.EditStatus = "Prefab \"" + p.Name + "\": " + ex.Message; return false; }
            tileClipShown = true;
            vm.EditStatus = "Prefab \"" + p.Name + "\" is ready (" + tileClip.Describe() + "): Ctrl+Alt+V pastes it with its south-west corner under the mouse; Ctrl+Alt+R turns it, Ctrl+Alt+M mirrors it."
                + (vm.Session != null && tileClip.Kind != vm.Session.Kind ? " It comes from the other game: its tiles are pasted without their objects." : "");
            UpdateMarkers();
            return true;
        }

        public bool DeletePrefab(PrefabInfo p)
        {
            try { File.Delete(p.Path); }
            catch (Exception ex) { vm.EditStatus = "Not deleted: " + ex.Message; return false; }
            vm.EditStatus = "Prefab \"" + p.Name + "\" deleted.";
            prefabsWindow?.Fill();
            return true;
        }

        // ---- decorate

        /// <summary>The undo step of the last decoration on its level, while it is still the newest step there: the next roll replaces it.</summary>
        object decorationStep;
        int decorationLevel = -1;
        List<(int X, int Y)> decorationTiles;

        public DecorateWindow ShowDecorate()
        {
            if (vm.Session == null) return null;
            if (decorateWindow != null) { decorateWindow.Activate(); return decorateWindow; }
            decorateWindow = new DecorateWindow(this);
            decorateWindow.Closed += (s, e) => decorateWindow = null;
            decorateWindow.Show(this);
            return decorateWindow;
        }

        /// <summary>Whether the newest undo step of the level shown is the last decoration (so the next roll takes its place).</summary>
        public bool DecorationCanBeReplaced
        {
            get { var e = vm.Level == null ? null : vm.Edits?.Existing(vm.Level.Level); return e != null && decorationStep != null && decorationLevel == e.Level && ReferenceEquals(e.UndoTop, decorationStep); }
        }

        /// <summary>
        /// Scatters decoration over the selected tiles. While the last decoration is still the
        /// newest step of the level, it is taken back first and the new roll goes on the same
        /// tiles: a new seed or density replaces what was scattered instead of adding to it.
        /// Null when nothing could be done (the reason is in the status line).
        /// </summary>
        public DecorationResult Decorate(DecorationOptions o)
        {
            var e = vm.Editor;
            if (e == null || vm.Level == null) return null;
            bool replace = DecorationCanBeReplaced;
            var tiles = replace ? decorationTiles : ChosenTiles();
            if (tiles == null || tiles.Count == 0) { vm.EditStatus = "Select the tiles to decorate first (Ctrl+drag a rectangle, or Ctrl+click tiles)."; return null; }
            if (replace) e.Undo();
            DecorationResult res;
            try { res = Decoration.Apply(e, tiles, o); }
            catch (Exception ex) { vm.EditStatus = ex.Message; decorationStep = null; vm.FlushRefresh(); UpdateMarkers(); return null; }
            decorationStep = res.Made.Count > 0 ? e.UndoTop : null;
            decorationLevel = e.Level;
            decorationTiles = tiles.ToList();
            vm.FlushRefresh();
            vm.EditStatus = string.Format("{0}: {1} (density {2} per 100 tiles, roll {3}). One undo step; another roll replaces it.", replace ? "Decorated again" : "Decorated", res.Describe(), o.PerHundred, o.Seed);
            UpdateMarkers();
            return res;
        }

        /// <summary>Takes the last decoration back, while it is still the newest step of its level. True when removed.</summary>
        public bool RemoveDecoration()
        {
            if (!DecorationCanBeReplaced) { vm.EditStatus = "The last decoration is no longer the newest step here: use Edit ▸ Undo."; return false; }
            vm.Editor.Undo();
            decorationStep = null;
            vm.FlushRefresh();
            vm.EditStatus = "The decoration is removed.";
            UpdateMarkers();
            return true;
        }
    }

    /// <summary>The prefabs of the open game: save the copied tiles as one, take one to paste, delete one.</summary>
    public sealed class PrefabsWindow : Window
    {
        readonly MainWindow main;
        readonly ListBox list = new ListBox { MinHeight = 200, MaxHeight = 420 };
        readonly TextBox name = new TextBox { Watermark = "A name for the copied tiles", MinWidth = 260 };
        readonly TextBlock note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12 };

        public IReadOnlyList<PrefabInfo> Shown { get { return (list.ItemsSource as IEnumerable<PrefabInfo> ?? Enumerable.Empty<PrefabInfo>()).ToList(); } }

        public PrefabsWindow(MainWindow main)
        {
            this.main = main;
            Title = "Prefabs";
            Width = 520; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var save = new Button { Content = "Save the copied tiles" };
            var take = new Button { Content = "Take it to paste" };
            var del = new Button { Content = "Delete" };
            var folder = new Button { Content = "Open the folder" };
            save.Click += (s, e) => { if (main.SavePrefab(name.Text ?? "")) name.Text = ""; Note(); };
            take.Click += (s, e) => { var p = list.SelectedItem as PrefabInfo; if (p != null) main.UsePrefab(p); Note(); };
            list.DoubleTapped += (s, e) => { var p = list.SelectedItem as PrefabInfo; if (p != null) main.UsePrefab(p); Note(); };
            del.Click += (s, e) => { var p = list.SelectedItem as PrefabInfo; if (p != null) main.DeletePrefab(p); Note(); };
            folder.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(MainWindow.PrefabFolder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = MainWindow.PrefabFolder, UseShellExecute = true }); }
                catch (Exception ex) { note.Text = "Could not open the folder: " + ex.Message; }
            };
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Prefabs", FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "A prefab is a piece of a level saved under a name: copy tiles (Ctrl+Alt+C), name them and save. Take one and paste it (Ctrl+Alt+V) on any level; turn (Ctrl+Alt+R) or mirror (Ctrl+Alt+M) it first if you like.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    list,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { take, del, folder } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, save } },
                    note,
                },
            };
            ManualWindow.Attach(this, "Selecting, copying and moving");
            Fill();
        }

        void Note() { note.Text = main.ViewModel.EditStatus; }

        public void Fill()
        {
            var all = main.ListPrefabs();
            list.ItemsSource = all;
            note.Text = all.Count == 0 ? "No prefab saved for this game yet. They are kept in " + MainWindow.PrefabFolder + "." : all.Count + " prefab" + (all.Count == 1 ? "" : "s") + " for this game, in " + MainWindow.PrefabFolder + ".";
        }
    }

    /// <summary>Decorate: the kinds, the density and the roll, with buttons to roll again, thicken, thin and remove.</summary>
    public sealed class DecorateWindow : Window
    {
        readonly MainWindow main;
        readonly Dictionary<string, CheckBox> kinds = new Dictionary<string, CheckBox>();
        readonly NumericUpDown density = new NumericUpDown { Minimum = 1, Maximum = 400, Value = 15, Increment = 5, FormatString = "0", MinWidth = 110 };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 1, Maximum = 999999, Value = 1, Increment = 1, FormatString = "0", MinWidth = 110 };
        readonly CheckBox liquid = new CheckBox { Content = "Leave water and lava bare", IsChecked = true };
        readonly CheckBox occupied = new CheckBox { Content = "Leave tiles bare that hold something" };
        readonly TextBlock note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, FontSize = 12 };

        public DecorationOptions Options
        {
            get
            {
                return new DecorationOptions
                {
                    Kinds = new HashSet<string>(kinds.Where(k => k.Value.IsChecked == true).Select(k => k.Key)),
                    PerHundred = (int)(density.Value ?? 15), Seed = (int)(seed.Value ?? 1),
                    AvoidLiquid = liquid.IsChecked == true, AvoidOccupied = occupied.IsChecked == true,
                };
            }
        }

        public int Density { get { return (int)(density.Value ?? 15); } set { density.Value = Math.Max(1, Math.Min(400, value)); } }
        public int Seed { get { return (int)(seed.Value ?? 1); } set { seed.Value = Math.Max(1, value); } }
        public void SetKind(string kind, bool on) { CheckBox c; if (kinds.TryGetValue(kind, out c)) c.IsChecked = on; }
        public IEnumerable<string> KindsOffered { get { return kinds.Keys; } }

        public DecorateWindow(MainWindow main)
        {
            this.main = main;
            Title = "Decorate";
            Width = 460; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var session = main.ViewModel.Session;
            var items = Decoration.Items(session);
            var kindRow = new WrapPanel();
            foreach (string k in Decoration.KindsOf(session))
            {
                var c = new CheckBox { Content = char.ToUpperInvariant(k[0]) + k.Substring(1), IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
                ToolTip.SetTip(c, string.Join(", ", items[k].Select(i => session.Names.Get(i)).Distinct()));
                kinds[k] = c;
                kindRow.Children.Add(c);
            }
            var apply = new Button { Content = "Scatter", FontWeight = FontWeight.SemiBold };
            var roll = new Button { Content = "New roll" };
            var more = new Button { Content = "Thicker" };
            var less = new Button { Content = "Thinner" };
            var remove = new Button { Content = "Remove" };
            apply.Click += (s, e) => Scatter();
            roll.Click += (s, e) => NewRoll();
            more.Click += (s, e) => Thicker();
            less.Click += (s, e) => Thinner();
            remove.Click += (s, e) => { main.RemoveDecoration(); Note(); };
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Decorate the selected tiles", FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Scatters small scenery over the tiles selected in the view; what the game hangs from the ceiling (stalactites, vines, one of the plants) hangs there. Each roll is one undo step, and while it is the newest step the next roll takes its place: try another roll, or thicker or thinner, until it looks right.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    kindRow,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Objects per 100 tiles", VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 }, density } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Roll (seed)", VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 }, seed } },
                    liquid, occupied,
                    new WrapPanel { Children = { apply, Gap(), roll, Gap(), more, Gap(), less, Gap(), remove } },
                    note,
                },
            };
            ManualWindow.Attach(this, "Selecting, copying and moving");
            note.Text = "Select tiles in the view (Ctrl+drag a rectangle), then Scatter.";
        }

        static Control Gap() { return new Border { Width = 8 }; }

        void Note() { note.Text = main.ViewModel.EditStatus; }

        /// <summary>Scatters with the options shown.</summary>
        public DecorationResult Scatter() { var r = main.Decorate(Options); Note(); return r; }
        /// <summary>The next seed, scattered.</summary>
        public DecorationResult NewRoll() { Seed = Seed + 1; return Scatter(); }
        /// <summary>Half as thick again (at least 5 more per 100 tiles), scattered.</summary>
        public DecorationResult Thicker() { Density = Math.Max(Density + 5, Density * 3 / 2); return Scatter(); }
        /// <summary>A third thinner (at least 1 per 100 tiles), scattered.</summary>
        public DecorationResult Thinner() { Density = Math.Max(1, Math.Min(Density - 1, Density * 2 / 3)); return Scatter(); }
    }
}
