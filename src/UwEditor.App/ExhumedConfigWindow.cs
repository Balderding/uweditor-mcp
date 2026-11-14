using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UltimateEditor.Games;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>
    /// Programs and paths ▸ Edit the settings: the settings a port runs with when the editor
    /// starts it (Run in Underworld Exhumed, Play-test in Exhumed), as the lines of key=value
    /// of the port's own settings file. Kept in the editor's settings folder.
    /// </summary>
    public sealed class ExhumedConfigWindow : Window
    {
        readonly MainWindow main;
        readonly GameKind kind;
        readonly TextBox box;
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        public ExhumedConfigWindow(MainWindow main, GameKind kind)
        {
            this.main = main;
            this.kind = kind;
            string file = ExhumedConfig.FileName(kind);
            Title = "Underworld Exhumed's settings (" + file + ")";
            Width = 760; Height = 620; MinWidth = 480; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            box = new TextBox
            {
                Text = main.ExhumedSettingsText(kind), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                FontFamily = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default, FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

            var save = new Button { Content = "Save", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = false };
            save.Click += (s, e) => Save();
            var defaults = new Button { Content = "Defaults", Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(defaults, "Puts the editor's defaults in the box (every key commented out). Nothing is saved until you click Save.");
            defaults.Click += (s, e) => { box.Text = ExhumedConfig.Defaults(kind); status.Text = "The defaults are in the box; Save keeps them."; };
            var take = new Button { Content = "Take the port's own settings", Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(take, "Puts the lines of the port's settings file in the box: the one in the game folder when it has one, else the one in the port's own home folder. Nothing is saved until you click Save.");
            take.Click += (s, e) => TakePortSettings();
            var close = new Button { Content = "Close", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            close.Click += (s, e) => Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { save, defaults, take, close } };

            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Underworld Exhumed's settings: " + (kind == GameKind.UW2 ? "Ultima Underworld II" : "Ultima Underworld"), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
                Text = "The port reads its settings from " + file + " in its home folder, one key=value a line. When the editor starts the port (Run in Underworld Exhumed, Play-test in Exhumed), the home is the folder the game runs in (the game folder, or the play test's copy), and the keys set here are written into that folder's " + file + " first. Keys not set here stay as the file has them. A line starting with # sets nothing. Kept in " + MainWindow.ExhumedSettingsPath(kind) + ".",
            });

            var bottom = new StackPanel();
            bottom.Children.Add(status);
            bottom.Children.Add(buttons);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(top); dock.Children.Add(bottom); dock.Children.Add(box);
            Content = dock;
            ManualWindow.Attach(this, "Run and Test");
            Say();
        }

        /// <summary>The text in the box (the tests set it).</summary>
        public string Text { get { return box.Text ?? ""; } set { box.Text = value; } }

        /// <summary>Keeps the text as the editor's settings for the port.</summary>
        public bool Save()
        {
            try
            {
                main.SetExhumedSettingsText(kind, Text);
                Say("Saved. ");
                return true;
            }
            catch (Exception ex) { status.Text = "Could not save: " + ex.Message; return false; }
        }

        /// <summary>The port's own settings file put in the box: the game folder's, else the port's home's. False when there is neither.</summary>
        public bool TakePortSettings()
        {
            string game = main.ViewModel.Session != null && main.ViewModel.Session.Kind == kind ? main.ViewModel.Session.GameFolder : null;
            string path = new[] { game, ExhumedCheck.PortHome(kind) }.Where(d => !string.IsNullOrEmpty(d)).Select(d => Path.Combine(d, ExhumedConfig.FileName(kind))).FirstOrDefault(File.Exists);
            if (path == null) { status.Text = "No " + ExhumedConfig.FileName(kind) + " was found in the game folder or in " + ExhumedCheck.PortHome(kind) + "."; return false; }
            try
            {
                box.Text = ExhumedConfig.FromPortFile(File.ReadAllText(path));
                status.Text = "The settings of " + path + " are in the box (without the game folder it remembers); Save keeps them.";
                return true;
            }
            catch (Exception ex) { status.Text = "Could not read " + path + ": " + ex.Message; return false; }
        }

        void Say(string before = "")
        {
            var keys = ExhumedConfig.Keys(main.ExhumedSettingsText(kind));
            status.Text = before + (keys.Count == 0 ? "No key is set: the port runs with the settings its home folder already has." : "Set at each start: " + string.Join(", ", keys.Select(k => k.Key + "=" + k.Value)) + ".");
        }
    }
}
