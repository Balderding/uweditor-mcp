using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UltimateEditor.Games;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>Run and Test ▸ Programs and paths: DOSBox (and its extra arguments, and the configuration a run starts from) and Underworld Exhumed's two ports (and their settings and extra arguments).</summary>
    public sealed class RunPathsWindow : Window
    {
        readonly MainWindow main;
        readonly TextBox dosbox = new TextBox(), dosboxArgs = new TextBox(), uw1 = new TextBox(), uw2 = new TextBox(), exhumedArgs = new TextBox();
        readonly TextBlock dosboxFound = Note(), confFound = Note(), uw1Found = Note(), uw2Found = Note(), uw1Set = Note(), uw2Set = Note();
        ExhumedConfigWindow configWindow;

        static TextBlock Note() { return new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) }; }

        public RunPathsWindow(MainWindow main)
        {
            this.main = main;
            Title = "Programs and paths";
            Width = 760; Height = 600; MinWidth = 520; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Spacing = 2, Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = "Programs and paths", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10),
                Text = "The programs that Run and Test starts. Leave a path empty to let the editor look for the program itself (DOSBox next to the game, as GOG installs it, or where it is usually installed; the ports next to the game, next to the editor or in Downloads).",
            });

            panel.Children.Add(Label("DOSBox (DOSBox, DOSBox-X or DOSBox Staging)"));
            panel.Children.Add(Row(dosbox, () => Pick(dosbox, "Choose DOSBox")));
            panel.Children.Add(dosboxFound);
            panel.Children.Add(Label("More DOSBox arguments (optional, for example -noconsole)"));
            panel.Children.Add(dosboxArgs);
            panel.Children.Add(confFound);
            var showConf = new Button { Content = "Show the configuration a run uses", Margin = new Thickness(0, 0, 0, 12) };
            showConf.Click += async (s, e) => await ShowConf();
            panel.Children.Add(showConf);

            panel.Children.Add(Label("Underworld Exhumed for Ultima Underworld (uw1port)"));
            panel.Children.Add(Row(uw1, () => Pick(uw1, "Choose uw1port")));
            panel.Children.Add(uw1Found);
            panel.Children.Add(SettingsButton(GameKind.UW1));
            panel.Children.Add(uw1Set);
            panel.Children.Add(Label("Underworld Exhumed for Ultima Underworld II (uw2port)"));
            panel.Children.Add(Row(uw2, () => Pick(uw2, "Choose uw2port")));
            panel.Children.Add(uw2Found);
            panel.Children.Add(SettingsButton(GameKind.UW2));
            panel.Children.Add(uw2Set);
            panel.Children.Add(Label("More Underworld Exhumed arguments (optional, for example --scale 4)"));
            panel.Children.Add(exhumedArgs);
            panel.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10),
                Text = "Run in Underworld Exhumed and Play-test in Exhumed start the port with --data and --home on the folder the game runs in (the game folder, or the play test's copy), so its saved games and its settings file are there and not in the port's own home folder.",
            });

            dosbox.Text = main.DosBoxPath;
            dosboxArgs.Text = main.DosBoxArgs;
            exhumedArgs.Text = main.ExhumedArgs;
            exhumedArgs.LostFocus += (s, e) => { main.ExhumedArgs = exhumedArgs.Text?.Trim(); };
            uw1.Text = main.ExhumedChosen(GameKind.UW1);
            uw2.Text = main.ExhumedChosen(GameKind.UW2);
            dosbox.LostFocus += (s, e) => { main.DosBoxPath = dosbox.Text?.Trim(); Refresh(); };
            dosboxArgs.LostFocus += (s, e) => { main.DosBoxArgs = dosboxArgs.Text?.Trim(); Refresh(); };
            uw1.LostFocus += (s, e) => { main.SetExhumedPort(GameKind.UW1, uw1.Text?.Trim()); Refresh(); };
            uw2.LostFocus += (s, e) => { main.SetExhumedPort(GameKind.UW2, uw2.Text?.Trim()); Refresh(); };
            Content = new ScrollViewer { Content = panel };
            ManualWindow.Attach(this, "Run and Test");
            Refresh();
        }

        Control SettingsButton(GameKind kind)
        {
            var b = new Button { Content = "Edit the settings it runs with (" + ExhumedConfig.FileName(kind) + ")…", Margin = new Thickness(0, 0, 0, 2) };
            b.Click += (s, e) => ShowExhumedSettings(kind);
            return b;
        }

        /// <summary>The window that edits the editor's settings for a port, one at a time.</summary>
        public ExhumedConfigWindow ShowExhumedSettings(GameKind kind)
        {
            configWindow?.Close();
            var w = configWindow = new ExhumedConfigWindow(main, kind);
            w.Closed += (s, e) => { if (configWindow == w) configWindow = null; Refresh(); };
            w.Show(this);
            return w;
        }

        static TextBlock Label(string text) { return new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 4) }; }

        static Control Row(TextBox box, Func<Task> browse)
        {
            var b = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
            b.Click += async (s, e) => await browse();
            var d = new DockPanel();
            DockPanel.SetDock(b, Dock.Right);
            d.Children.Add(b); d.Children.Add(box);
            return d;
        }

        async Task Pick(TextBox box, string title)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
            string p = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (p == null) return;
            box.Text = p;
            main.DosBoxPath = dosbox.Text?.Trim();
            main.SetExhumedPort(GameKind.UW1, uw1.Text?.Trim());
            main.SetExhumedPort(GameKind.UW2, uw2.Text?.Trim());
            Refresh();
        }

        /// <summary>What each program resolves to now.</summary>
        public void Refresh()
        {
            Say(dosboxFound, dosbox.Text, main.DosBoxFound());
            Say(uw1Found, uw1.Text, main.ExhumedPortFor(GameKind.UW1));
            Say(uw2Found, uw2.Text, main.ExhumedPortFor(GameKind.UW2));
            SaySettings(uw1Set, GameKind.UW1);
            SaySettings(uw2Set, GameKind.UW2);
            string from;
            main.DosBoxBaseConf(out from);
            confFound.Text = from != null
                ? "A run uses GOG's configuration, " + from + ", with its [autoexec] replaced: C: is the folder being edited, and the game starts at once."
                : "There is no GOG configuration next to the game, so a run uses the editor's defaults (an SVGA machine with EMS, a Sound Blaster 16 at 220, IRQ 7, DMA 1, and an MPU-401 for the MT-32).";
        }

        void SaySettings(TextBlock t, GameKind kind)
        {
            var keys = ExhumedConfig.Keys(main.ExhumedSettingsText(kind));
            t.Text = keys.Count == 0 ? "No setting is set here: the port runs with what its settings file in the game folder has."
                : "Written into " + ExhumedConfig.FileName(kind) + " of the folder the game runs in, before each start: " + string.Join(", ", keys.Select(k => k.Key + "=" + k.Value)) + ".";
        }

        static void Say(TextBlock t, string chosen, string resolved)
        {
            if (!string.IsNullOrWhiteSpace(chosen) && !File.Exists(chosen.Trim())) t.Text = "✖ Not found: " + chosen.Trim();
            else if (resolved != null) t.Text = (string.IsNullOrWhiteSpace(chosen) ? "Found: " : "✓ ") + resolved;
            else t.Text = "Not found: choose it with Browse…";
        }

        async Task ShowConf()
        {
            var s = main.ViewModel.Session;
            string from;
            string text = DosBoxConfig.Build(main.DosBoxBaseConf(out from), s?.GameFolder ?? "C:\\GAMES\\UW", s?.ExePath ?? (s != null && s.IsUw2 ? "UW2.EXE" : "UW.EXE"));
            await MessageWindow.Show(this, "DOSBox configuration", text, true);
        }
    }
}
