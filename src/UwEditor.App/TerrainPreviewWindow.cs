using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>A line of the Preview changed terrain window: a terrain change, its tick, and an oscillator's height.</summary>
    public sealed class TerrainPreviewRow
    {
        public TerrainChange Change;
        public CheckBox Check;
        /// <summary>The height an oscillator is shown at (null for a change-terrain trap).</summary>
        public NumericUpDown Height;
    }

    /// <summary>
    /// Tools ▸ Preview changed terrain: every change-terrain trap of the level shown (and UW2's
    /// oscillators in height mode), each with a tick. The main view shows a copy of the level with
    /// the ticked ones applied (TerrainPreview); the level itself is never changed. Closing the
    /// window, unticking everything, another level or an edit shows the level itself again.
    /// </summary>
    public sealed class TerrainPreviewWindow : Window
    {
        readonly MainWindow main;
        readonly MainViewModel vm;
        readonly StackPanel list = new StackPanel { Spacing = 2 };
        readonly TextBlock heading = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        bool filling;

        public List<TerrainPreviewRow> Rows { get; } = new List<TerrainPreviewRow>();
        public string Status { get { return status.Text; } }

        public TerrainPreviewWindow(MainWindow main)
        {
            this.main = main;
            vm = main.ViewModel;
            Title = "Preview changed terrain";
            Width = 760; Height = 520; MinWidth = 420; MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(heading);
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Tick the traps to see the level as it is after they have run. The view shows a copy: the level itself is not changed, and the first edit ends the preview. The ticked traps are applied in the order of their numbers.",
            });
            var all = new Button { Content = "All" };
            all.Click += (o, e) => SetAll(true);
            var none = new Button { Content = "None" };
            none.Click += (o, e) => SetAll(false);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            bar.Children.Add(all); bar.Children.Add(none);

            var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            var scroll = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            Grid.SetRow(head, 0); grid.Children.Add(head);
            Grid.SetRow(bar, 1); grid.Children.Add(bar);
            Grid.SetRow(scroll, 2); grid.Children.Add(scroll);
            Grid.SetRow(status, 3); grid.Children.Add(status);
            Content = grid;

            Refresh();
            vm.LevelChanged += OnLevel;
            vm.LevelEdited += Refresh;
            Closed += (s, e) =>
            {
                vm.LevelChanged -= OnLevel;
                vm.LevelEdited -= Refresh;
                main.SetTerrainPreview(null, 0);
            };
        }

        void OnLevel(LevelModel l) { Refresh(); }

        /// <summary>The list again, for the level the main window shows now, with nothing ticked (the preview ends).</summary>
        public void Refresh()
        {
            filling = true;
            Rows.Clear();
            list.Children.Clear();
            var m = vm.Level;
            heading.Text = m == null ? "Preview changed terrain" : "Changed terrain of level " + m.Level;
            if (m != null && vm.Session != null)
                foreach (var c in TerrainPreview.List(vm.Session, m))
                {
                    var row = new TerrainPreviewRow { Change = c, Check = new CheckBox { Content = c.Text } };
                    row.Check.IsCheckedChanged += (o, e) => Changed();
                    Control line = row.Check;
                    if (c.Oscillator)
                    {
                        row.Height = new NumericUpDown { Minimum = c.Low, Maximum = c.High, Value = c.Height, Increment = 1, FormatString = "0", Width = 110, VerticalAlignment = VerticalAlignment.Center };
                        ToolTip.SetTip(row.Height, "The floor height to show this oscillator at.");
                        row.Height.ValueChanged += (o, e) => Changed();
                        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        p.Children.Add(row.Check); p.Children.Add(row.Height);
                        line = p;
                    }
                    Rows.Add(row);
                    list.Children.Add(line);
                }
            filling = false;
            Changed();
        }

        /// <summary>Ticks or unticks every row.</summary>
        public void SetAll(bool on)
        {
            filling = true;
            foreach (var r in Rows) r.Check.IsChecked = on;
            filling = false;
            Changed();
        }

        /// <summary>The ticks changed: the main view shows the level with the ticked changes, or the level itself when there are none.</summary>
        void Changed()
        {
            if (filling) return;
            var m = vm.Level;
            var chosen = new List<TerrainChange>();
            foreach (var r in Rows)
            {
                if (r.Check.IsChecked != true) continue;
                if (r.Height != null) r.Change.Height = (int)(r.Height.Value ?? r.Change.Height);
                chosen.Add(r.Change);
            }
            if (m == null || chosen.Count == 0)
            {
                main.SetTerrainPreview(null, 0);
                status.Text = Rows.Count == 0 ? "This level has no change-terrain traps." : Rows.Count + " terrain changes on this level, none ticked: the view shows the level itself.";
                return;
            }
            var copy = TerrainPreview.Apply(m, chosen);
            int squares = TerrainPreview.Differences(m, copy).Count;
            main.SetTerrainPreview(copy, chosen.Count);
            status.Text = string.Format("{0} of {1} terrain changes applied in the view; {2} square{3} differ{4} from the level itself.", chosen.Count, Rows.Count, squares, squares == 1 ? "" : "s", squares == 1 ? "s" : "");
        }
    }
}
