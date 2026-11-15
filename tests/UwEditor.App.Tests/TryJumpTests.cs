using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The Try Jump tool: two clicks, the flight drawn, the status line.</summary>
    public class TryJumpTests
    {
        [AvaloniaFact]
        public void Two_clicks_draw_the_flight_and_say_what_happened()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            vm.LoadLevel(5);
            Ui.Pump();
            Assert.Equal("Try Jump", vm.Tools[MainWindow.TryJumpTool]);
            int lines = view.LineFloats;
            vm.ToolIndex = MainWindow.TryJumpTool;
            Ui.Pump();
            Assert.Contains("Try Jump", vm.EditStatus);
            w.TryJumpAt(16, 25);
            Ui.Pump();
            Assert.Null(w.ShownJump);
            Assert.Contains("from (16, 25)", vm.EditStatus);
            Assert.True(view.LineFloats > lines, "the first square is outlined");
            int one = view.LineFloats;
            w.TryJumpAt(16, 27);
            Ui.Pump();
            Assert.NotNull(w.ShownJump);
            Assert.True(w.ShownJump.Lands, w.ShownJump.Text);
            Assert.Contains("Lands on the floor at height 8 of (16, 27)", vm.EditStatus);
            Assert.True(view.LineFloats > one + 14 * 20, "the flight is drawn");
            // a third click starts again; another tool clears it
            w.TryJumpAt(16, 25);
            Ui.Pump();
            Assert.Null(w.ShownJump);
            vm.ToolIndex = 0;
            Ui.Pump();
            Assert.Equal(lines, view.LineFloats);
            w.Close();
        }

        [AvaloniaFact]
        public void A_click_on_the_landing_square_again_takes_its_next_surface()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(65);
            Ui.Pump();
            vm.ToolIndex = MainWindow.TryJumpTool;
            w.TryJumpAt(29, 30);
            // the click hits the bridge's top (height 10): the jump spell is needed
            w.TryJumpAt(32, 27, 10);
            Ui.Pump();
            Assert.True(w.ShownJump.Lands, w.ShownJump.Text);
            Assert.True(w.ShownJump.Leap);
            Assert.Contains("bridge", vm.EditStatus);
            Assert.Contains("2 places to land", vm.EditStatus);
            // both flights are worked out and drawn: the plain jump falls short of the bridge, the spell's lands on it
            Assert.False(w.ShownPlainJump.Lands);
            Assert.False(w.ShownPlainJump.Leap);
            Assert.True(w.ShownSpellJump.Lands);
            Assert.Same(w.ShownSpellJump, w.ShownJump);
            Assert.True(w.ShownPlainJump.Path.Count > 0 && w.ShownSpellJump.Path.Count > 0);
            Assert.Contains("Try Jump, plain: Does not reach", vm.EditStatus);
            Assert.Contains("With the Jump spell: Lands on", vm.EditStatus);
            // again: the floor under it, with a plain jump
            w.TryJumpAt(32, 27, 10);
            Ui.Pump();
            Assert.True(w.ShownJump.Lands, w.ShownJump.Text);
            Assert.False(w.ShownJump.Leap);
            Assert.Equal(-1, w.ShownJump.On.Object);
            Assert.Same(w.ShownPlainJump, w.ShownJump);
            Assert.True(w.ShownSpellJump.Leap);
            // and round to the bridge again
            w.TryJumpAt(32, 27, 10);
            Ui.Pump();
            Assert.True(w.ShownJump.Leap);
            w.Close();
        }
        /// <summary>The four tool buttons: the one of the tool in use is down, a click takes its tool, a click on the one in use leaves it down; each is square with a tool tip.</summary>
        [AvaloniaFact]
        public void The_tool_buttons_follow_the_tool_and_set_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(1);
            Ui.Pump();
            var names = new[] { "ToolSelectButton", "ToolMoveButton", "ToolPaintButton", "ToolJumpButton" };
            var b = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(names, n => w.FindControl<Avalonia.Controls.Primitives.ToggleButton>(n)));
            for (int i = 0; i < 4; i++)
            {
                Assert.NotNull(b[i]);
                Assert.Equal(i == 0, b[i].IsChecked);
                Assert.Contains(vm.Tools[i], (string)Avalonia.Controls.ToolTip.GetTip(b[i]));
                Assert.Equal(b[i].Bounds.Width, b[i].Bounds.Height);
                Assert.True(b[i].Bounds.Width > 0);
            }
            vm.ToolIndex = 2;
            Ui.Pump();
            for (int i = 0; i < 4; i++) Assert.Equal(i == 2, b[i].IsChecked);
            // a capture when TOOLBAR_SHOTS names a folder (to look at the buttons)
            string shots = System.Environment.GetEnvironmentVariable("TOOLBAR_SHOTS");
            if (!string.IsNullOrEmpty(shots)) { System.IO.Directory.CreateDirectory(shots); new Ui.Codec().Save(Ui.Capture(w), System.IO.Path.Combine(shots, "tool-buttons.png")); }
            b[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.Pump();
            Assert.Equal(MainWindow.TryJumpTool, vm.ToolIndex);
            for (int i = 0; i < 4; i++) Assert.Equal(i == 3, b[i].IsChecked);
            b[3].IsChecked = false;      // as a click on the button already down would leave it
            b[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.Pump();
            Assert.Equal(MainWindow.TryJumpTool, vm.ToolIndex);
            Assert.True(b[3].IsChecked);
        }
    }
}
