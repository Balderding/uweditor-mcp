using Avalonia.Headless.XUnit;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The Go to box: "level, x, y" or "x, y", Enter goes there.</summary>
    public class GoToBoxTests
    {
        [AvaloniaFact]
        public void The_box_goes_to_a_square_of_this_level_or_of_another()
        {
            string g = GameData.Game("UW1");
            if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(1);
            Ui.Pump();

            // the digit typed is in the box already
            var box = w.ShowGoTo("3");
            Assert.NotNull(box);
            Ui.Pump();
            Assert.Equal("3", box.Text);
            box.Text = "30,12";
            Assert.True(box.Go());
            Ui.Pump();
            Assert.Equal(1, vm.Level.Level);
            Assert.Equal(30, vm.SelectedTileX);
            Assert.Equal(12, vm.SelectedTileY);

            // another level
            Assert.Null(w.GoToText("4, 20, 41"));
            Ui.Pump();
            Assert.Equal(4, vm.Level.Level);
            Assert.Equal(20, vm.SelectedTileX);
            Assert.Equal(41, vm.SelectedTileY);

            // a text that does not read leaves the box open and the place as it was
            box = w.ShowGoTo("");
            Ui.Pump();
            box.Text = "99,1,1";
            Assert.False(box.Go());
            Assert.Contains("no level 99", box.Hint);
            Assert.Equal(4, vm.Level.Level);
            box.Text = "5,70";
            Assert.False(box.Go());
            Assert.Equal(20, vm.SelectedTileX);
            box.Close();
            w.Close();
        }
    }
}
