using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// The Go to box: typing a digit in the main window (or Ctrl+G, or View ▸ Go to a square) opens
    /// a small box that takes "level, x, y" or "x, y"; Enter selects that square and looks at it.
    /// </summary>
    public sealed class GoToBox : Window
    {
        readonly MainWindow main;
        readonly TextBox box = new TextBox { Watermark = "level, x, y   or   x, y", MinWidth = 220 };
        readonly TextBlock hint = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        bool closing;

        public string Text { get { return box.Text; } set { box.Text = value; box.CaretIndex = (value ?? "").Length; } }
        public string Hint { get { return hint.Text; } }

        public GoToBox(MainWindow main, string start)
        {
            this.main = main;
            Title = "Go to";
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            hint.Text = "Enter goes there, Esc closes.";
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(box);
            panel.Children.Add(hint);
            Content = panel;
            Text = start ?? "";
            box.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Enter) { Go(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            Opened += (o, e) => { box.Focus(); box.CaretIndex = (box.Text ?? "").Length; };
            Deactivated += (o, e) => { if (!closing) Close(); };
            Closing += (o, e) => closing = true;
        }

        /// <summary>Reads the text and goes there; on a text that does not read, says why and stays open.</summary>
        public bool Go()
        {
            string problem = main.GoToText(box.Text);
            if (problem != null) { hint.Text = problem; hint.Opacity = 1; hint.Foreground = Brushes.OrangeRed; return false; }
            Close();
            return true;
        }
    }

    public partial class MainWindow
    {
        GoToBox goToBox;

        void OnGoToSquare(object sender, RoutedEventArgs e) { ShowGoTo(""); }

        /// <summary>Opens the Go to box with <paramref name="start"/> already typed.</summary>
        public GoToBox ShowGoTo(string start)
        {
            if (vm.Session == null) return null;
            if (goToBox != null) { goToBox.Text = (goToBox.Text ?? "") + start; goToBox.Activate(); return goToBox; }
            var w = new GoToBox(this, start);
            goToBox = w;
            w.Closed += (s, e) => { if (goToBox == w) goToBox = null; };
            w.Show(this);
            return w;
        }

        /// <summary>Goes to "level, x, y" or "x, y" (GoToSpot); returns null, or what is wrong with the text.</summary>
        public string GoToText(string text)
        {
            if (vm.Session == null) return "No game is open.";
            var slots = vm.Session.LevelSlots();
            int level, x, y;
            string problem = GoToSpot.Parse(text, vm.Level != null ? vm.Level.Level : 0, l => slots.Any(s => s.Level == l && s.Present), out level, out x, out y);
            if (problem != null) return problem;
            GoToTile(level, x, y);
            vm.EditStatus = string.Format("Level {0} ({1}, {2}).", level, x, y);
            return null;
        }

        /// <summary>The digit a key types (the top row or the number pad), or null.</summary>
        static string DigitOf(Key k)
        {
            if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((char)('0' + (k - Key.NumPad0))).ToString();
            return null;
        }
    }
}
