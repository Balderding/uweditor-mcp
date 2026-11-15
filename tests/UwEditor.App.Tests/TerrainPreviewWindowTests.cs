using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Preview changed terrain: the view shows a changed copy, the level itself stays as it is.</summary>
    public class TerrainPreviewWindowTests
    {
        static TerrainPreviewRow Opener(MainWindow w, TerrainPreviewWindow p)
        {
            var s = w.ViewModel.Session;
            return p.Rows.First(r => { int t, h, f, wl; if (r.Change.Oscillator || !r.Change.Covers(33, 33)) return false; TerrainPreview.Values(s, r.Change.Trap, out t, out h, out f, out wl); return t > 0; });
        }

        [AvaloniaFact]
        public void Ticking_a_trap_shows_a_changed_copy_and_closing_shows_the_level_again()
        {
            string g = GameData.Game("UW2");
            if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(26);
            Ui.Pump();
            var view = w.FindControl<LevelView>("View");
            var p = w.ShowTerrainPreview();
            Assert.NotNull(p);
            Assert.Same(p, w.ShowTerrainPreview());                  // one window
            Ui.Pump();
            Assert.NotEmpty(p.Rows);
            Assert.Same(vm.Level, w.ShownLevel);
            Assert.Equal("", vm.PreviewStatus);
            var real = vm.Level;
            Assert.True(real.Tiles[33, 33].IsSolid);

            var row = Opener(w, p);
            row.Check.IsChecked = true;
            Ui.Pump();
            Assert.NotSame(vm.Level, w.ShownLevel);
            Assert.Same(w.ShownLevel, view.Scene.Level);             // the view draws the copy
            Assert.False(w.ShownLevel.Tiles[33, 33].IsSolid);
            Assert.Same(real, vm.Level);                             // the level itself is as it was
            Assert.True(vm.Level.Tiles[33, 33].IsSolid);
            Assert.False(vm.HasLevelChanges);
            Assert.Contains("Preview: 1 terrain change applied; the level itself is unchanged", vm.PreviewStatus);
            Assert.True(w.FindControl<Border>("PreviewBanner").IsVisible);
            Assert.Contains("1 of " + p.Rows.Count, p.Status);

            // All and None
            p.SetAll(true);
            Ui.Pump();
            Assert.All(p.Rows, r => Assert.True(r.Check.IsChecked));
            Assert.Contains("Preview: " + p.Rows.Count + " terrain changes applied", vm.PreviewStatus);
            p.SetAll(false);
            Ui.Pump();
            Assert.Same(vm.Level, w.ShownLevel);
            Assert.Same(vm.Level, view.Scene.Level);
            Assert.Equal("", vm.PreviewStatus);

            // closing the window shows the level itself again
            row.Check.IsChecked = true;
            Ui.Pump();
            Assert.False(view.Scene.Level.Tiles[33, 33].IsSolid);
            p.Close();
            Ui.Pump();
            Assert.Same(vm.Level, w.ShownLevel);
            Assert.Same(vm.Level, view.Scene.Level);
            Assert.True(view.Scene.Level.Tiles[33, 33].IsSolid);
            Assert.Equal("", vm.PreviewStatus);
            Assert.False(vm.HasLevelChanges);
            w.Close();
        }

        [AvaloniaFact]
        public void An_edit_or_another_level_ends_the_preview_and_never_writes_into_the_copy()
        {
            string g = GameData.Game("UW2");
            if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(26);
            Ui.Pump();
            var view = w.FindControl<LevelView>("View");
            var p = w.ShowTerrainPreview();
            var row = Opener(w, p);
            row.Check.IsChecked = true;
            Ui.Pump();
            var copy = w.ShownLevel;
            Assert.NotSame(vm.Level, copy);

            // an edit goes to the level, not to the copy, and the preview ends
            int x = -1, y = -1;
            for (int ty = 2; ty < 62 && x < 0; ty++)
                for (int tx = 2; tx < 62 && x < 0; tx++)
                    if (vm.Level.Tiles[tx, ty].Type == TileType.Open && vm.Level.Tiles[tx, ty].Height < 12 && !row.Change.Covers(tx, ty)) { x = tx; y = ty; }
            int before = vm.Level.Tiles[x, y].Height;
            Assert.True(vm.Edit(e => e.RaiseFloor(x, y, 1)));
            vm.FlushRefresh();
            Ui.Pump();
            Assert.Equal(before + 1, vm.Level.Tiles[x, y].Height);
            Assert.Equal(before, copy.Tiles[x, y].Height);          // the copy was not written into
            Assert.Same(vm.Level, w.ShownLevel);
            Assert.Same(vm.Level, view.Scene.Level);
            Assert.True(view.Scene.Level.Tiles[33, 33].IsSolid);
            Assert.Equal("", vm.PreviewStatus);
            Assert.Contains("Preview ended", vm.EditStatus);
            Assert.All(p.Rows, r => Assert.NotEqual(true, r.Check.IsChecked));

            // another level: the list is that level's, nothing previewed
            Opener(w, p).Check.IsChecked = true;
            Ui.Pump();
            Assert.NotSame(vm.Level, w.ShownLevel);
            vm.LoadLevel(1);
            Ui.Pump();
            Assert.Equal(1, vm.Level.Level);
            Assert.Same(vm.Level, w.ShownLevel);
            Assert.Same(vm.Level, view.Scene.Level);
            Assert.Equal("", vm.PreviewStatus);
            Assert.All(p.Rows, r => Assert.NotEqual(true, r.Check.IsChecked));
            p.Close();
            w.Close();
        }
    }
}
