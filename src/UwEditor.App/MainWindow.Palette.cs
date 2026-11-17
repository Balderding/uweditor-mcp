using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace UwEditor.App
{
    /// <summary>One command of the palette: a menu item with the menus it lies under.</summary>
    public sealed class PaletteCommand
    {
        public string Path { get; set; }
        public string Gesture { get; set; }
        public bool Enabled { get; set; }
        public MenuItem Item { get; set; }
        public string Label { get { return Path + (string.IsNullOrEmpty(Gesture) ? "" : "    " + Gesture); } }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// The command palette (Ctrl+K): type part of any menu command's name, pick it with the arrow
    /// keys and press Enter. It lists every command of the main menu with the menus it lies
    /// under; commands that cannot run now are greyed out.
    /// </summary>
    public sealed class CommandPalette : Window
    {
        readonly MainWindow main;
        readonly TextBox box = new TextBox { Watermark = "Type part of a command: \"paste tiles\", \"strings\", \"key scatter\"…", MinWidth = 520 };
        readonly ListBox list = new ListBox { MaxHeight = 380, Margin = new Thickness(0, 8, 0, 0) };
        readonly List<PaletteCommand> all;
        bool closing;

        public string Text { get { return box.Text; } set { box.Text = value; } }
        public IReadOnlyList<PaletteCommand> Shown { get { return (list.ItemsSource as IEnumerable<PaletteCommand> ?? Enumerable.Empty<PaletteCommand>()).ToList(); } }

        public CommandPalette(MainWindow main)
        {
            this.main = main;
            all = main.PaletteCommands();
            Title = "Commands";
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PaletteCommand>((c, ns) =>
                new TextBlock { Text = c == null ? "" : c.Label, Opacity = c != null && c.Enabled ? 1 : 0.45, FontSize = 13 });
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(box);
            panel.Children.Add(list);
            Content = panel;
            box.TextChanged += (o, e) => Filter();
            box.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Enter) { Run(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Down) { Move(1); e.Handled = true; }
                else if (e.Key == Key.Up) { Move(-1); e.Handled = true; }
            };
            list.DoubleTapped += (o, e) => Run();
            Opened += (o, e) => box.Focus();
            Deactivated += (o, e) => { if (!closing) Close(); };
            Closing += (o, e) => closing = true;
            Filter();
        }

        void Move(int d)
        {
            int n = Shown.Count;
            if (n == 0) return;
            list.SelectedIndex = Math.Max(0, Math.Min(n - 1, list.SelectedIndex + d));
            list.ScrollIntoView(list.SelectedIndex);
        }

        /// <summary>The commands whose path holds every word typed, those that start a word with it first.</summary>
        public static List<PaletteCommand> Match(IEnumerable<PaletteCommand> all, string text)
        {
            var words = (text ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Func<PaletteCommand, int> rank = c =>
            {
                string p = c.Path.ToLowerInvariant();
                int score = 0;
                foreach (var w in words)
                {
                    int i = p.IndexOf(w, StringComparison.Ordinal);
                    if (i < 0) return -1;
                    score += i == 0 || !char.IsLetterOrDigit(p[i - 1]) ? 0 : 1;      // the start of a word counts for more
                }
                // the command's own name (after the last ▸) before the menus above it
                string own = p.Substring(p.LastIndexOf('▸') + 1);
                return score * 2 + (words.Length > 0 && words.All(w => own.Contains(w)) ? 0 : 1);
            };
            return all.Select(c => new { c, r = rank(c) }).Where(x => x.r >= 0).OrderBy(x => x.r).ThenBy(x => x.c.Enabled ? 0 : 1).Select(x => x.c).ToList();
        }

        void Filter()
        {
            list.ItemsSource = Match(all, box.Text);
            list.SelectedIndex = Shown.Count > 0 ? 0 : -1;
        }

        /// <summary>Runs the selected command (the first one when none is) and closes. False when it cannot run now.</summary>
        public bool Run()
        {
            var shown = Shown;
            var c = list.SelectedIndex >= 0 && list.SelectedIndex < shown.Count ? shown[list.SelectedIndex] : shown.FirstOrDefault();
            if (c == null) return false;
            if (!c.Enabled) { Title = "Commands: \"" + c.Path + "\" cannot run now"; return false; }
            Close();
            main.RunPaletteCommand(c);
            return true;
        }
    }

    public partial class MainWindow
    {
        CommandPalette palette;

        void OnCommandPalette(object sender, RoutedEventArgs e) { ShowCommandPalette(); }

        public CommandPalette ShowCommandPalette()
        {
            if (palette != null) { palette.Activate(); return palette; }
            palette = new CommandPalette(this);
            palette.Closed += (s, e) => palette = null;
            palette.Show(this);
            return palette;
        }

        static string Plain(object header) { return (header as string ?? (header as TextBlock)?.Text ?? header?.ToString() ?? "").Replace("_", "").Trim(); }

        /// <summary>Every command of the main menu: its name under the menus it lies in ("Edit ▸ Paste tiles"), its key, and whether it can run now.</summary>
        public List<PaletteCommand> PaletteCommands()
        {
            var list = new List<PaletteCommand>();
            Action<MenuItem, string, bool> walk = null;
            walk = (m, path, enabled) =>
            {
                string name = Plain(m.Header);
                if (name.Length == 0) return;
                string here = path.Length == 0 ? name : path + " ▸ " + name;
                bool on = enabled && m.IsEnabled;
                var subs = m.Items.OfType<MenuItem>().ToList();
                if (subs.Count > 0) { foreach (var s in subs) walk(s, here, on); return; }
                if (path.Length == 0) return;                                 // a top menu with nothing in it
                list.Add(new PaletteCommand { Path = here, Gesture = m.InputGesture?.ToString() ?? "", Enabled = on, Item = m });
            };
            foreach (var top in MainMenu.Items.OfType<MenuItem>()) walk(top, "", true);
            return list;
        }

        /// <summary>Runs a menu command as a click on it does: a ticked item is toggled, any other gets its Click.</summary>
        public void RunPaletteCommand(PaletteCommand c)
        {
            var m = c.Item;
            if (m.ToggleType != MenuItemToggleType.None) m.IsChecked = !m.IsChecked;
            m.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, m));
            if (string.IsNullOrEmpty(vm.EditStatus)) vm.EditStatus = c.Path + ".";
        }
    }
}
