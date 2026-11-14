using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Run and Test (DOSBox and Underworld Exhumed on the folder being edited) and File ▸ Save all changes / Restore all.</summary>
    public class RunAndTestTests
    {
        static T Wait<T>(System.Threading.Tasks.Task<T> t)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!t.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(10); }
            Assert.True(t.IsCompleted);
            return t.Result;
        }

        [AvaloniaFact]
        public void DOSBox_and_Exhumed_run_on_the_save_being_edited()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            string save = Path.Combine(g, "SAVE1"); if (!Directory.Exists(save)) return;
            var w = Ui.Open(save);
            w.AskBeforeDiscarding = false;
            w.RunLaunch = false;
            Assert.True(Wait(w.RunDosBox()));
            var run = w.LastRun;
            Assert.Equal("-conf", run.Arguments[0]);
            string conf = File.ReadAllText(run.Arguments[1]);
            Assert.Contains("mount c \"" + Path.GetFullPath(g).TrimEnd('/', '\\') + "\"", conf);
            Assert.Contains("\nUW2", conf.Replace("\r", ""));
            Assert.Contains("saved game 1", run.Note);
            Assert.True(Wait(w.RunExhumed()));
            run = w.LastRun;
            Assert.Equal(new[] { "--data", w.ViewModel.Session.GameFolder, "--home", w.ViewModel.Session.GameFolder }, run.Arguments);
            Assert.Contains("slot 1", run.Note);
            w.Close();
        }

        [AvaloniaFact]
        public void A_save_kept_elsewhere_runs_from_a_copy()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var save = GameData.Saves("UW").FirstOrDefault(); if (save == null) return;
            var w = Ui.Open(null);
            w.AskBeforeDiscarding = false;
            w.RunLaunch = false;
            w.OpenSession(UwEditor.Core.GameSession.Open(save, g));
            Ui.Pump();
            Assert.True(Wait(w.RunExhumed()));
            var home = w.LastRun.Folder;
            Assert.StartsWith(MainWindow.RunFolder, home);
            Assert.True(File.Exists(Path.Combine(home, "SAVE1", "LEV.ARK")));
            Assert.True(Wait(w.RunDosBox()));
            Assert.True(File.Exists(Path.Combine(w.LastRun.Folder, "SAVE1", "PLAYER.DAT")));
            Assert.True(File.Exists(Path.Combine(w.LastRun.Folder, "UW.EXE")));
            w.Close();
        }

        [AvaloniaFact]
        public void Restore_all_drops_every_change_and_keeps_a_checkpoint()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(2);
            Ui.Pump();
            var before = (byte[])vm.Session.LoadLevel(2).Block.Data.Clone();
            var t = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !vm.Level.Tiles[k.X, k.Y].IsSolid && vm.Level.Tiles[k.X, k.Y].Height < 10);
            vm.Edit(e => e.SetTileField(t.X, t.Y, "Floor height", vm.Level.Tiles[t.X, t.Y].Height + 1));
            Assert.NotEmpty(w.UnsavedParts());
            int checkpoints = w.CheckpointStore.List().Count;
            Assert.True(Wait(w.RestoreAll()));
            Ui.Pump();
            Assert.Empty(w.UnsavedParts());
            Assert.Equal(2, vm.Level.Level);
            Assert.Equal(before, vm.Level.Block.Data);
            Assert.Equal(checkpoints + 1, w.CheckpointStore.List().Count);
            Assert.True(w.SaveAllChanges());       // nothing to save
            Assert.Equal("Nothing to save.", vm.EditStatus);
            w.Close();
        }

        [AvaloniaFact]
        public void The_programs_window_opens()
        {
            var w = Ui.Open(GameData.Game("UW"));
            var p = w.ShowRunPaths();
            Ui.Pump();
            Assert.NotNull(p);
            p.Close();
            w.Close();
        }

        static (int X, int Y) OpenTile(UwEditor.Core.LevelModel m)
        {
            return Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(t => t.X > 1 && t.Y > 1 && t.X < 62 && t.Y < 62 && !m.Tiles[t.X, t.Y].IsSolid);
        }

        /// <summary>The editor's settings for the ports and the extra arguments, put back as they were (the tests share one settings folder).</summary>
        static void ForgetExhumedSettings(MainWindow w)
        {
            w.ExhumedArgs = "";
            foreach (var k in new[] { UltimateEditor.Games.GameKind.UW1, UltimateEditor.Games.GameKind.UW2 })
                if (File.Exists(MainWindow.ExhumedSettingsPath(k))) File.Delete(MainWindow.ExhumedSettingsPath(k));
        }

        [AvaloniaTheory]
        [InlineData("UW", false)]
        [InlineData("UW", true)]
        [InlineData("UW2", false)]
        [InlineData("UW2", true)]
        public void A_play_test_without_a_saved_game_uses_the_default_player_in_DOSBox_and_in_Exhumed(string game, bool exhumed)
        {
            string g = GameData.Game(game); if (g == null) return;
            // a game folder with no saved game: DATA and the executable
            string folder = Path.Combine(Path.GetTempPath(), "uwedit-nosave-" + Guid.NewGuid().ToString("N"));
            string to = Path.Combine(Path.GetTempPath(), "uwedit-playdef-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "DATA"));
            foreach (var f in Directory.GetFiles(Path.Combine(g, "DATA"))) File.Copy(f, Path.Combine(folder, "DATA", Path.GetFileName(f)));
            foreach (var f in Directory.GetFiles(g, "*.EXE")) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
            var w = Ui.Open(folder);
            try
            {
                w.AskBeforeDiscarding = false;
                w.PlayTestFolder = to;
                w.PlayTestLaunch = false;
                ForgetExhumedSettings(w);
                var kind = w.ViewModel.Session.Kind;
                w.SetExhumedSettingsText(kind, "# for the test\nenhance=skip-intro\nsettings-at-start=0\n");
                w.ExhumedArgs = "--scale 2";
                w.ViewModel.LoadLevel(2);
                Ui.Pump();
                var t = OpenTile(w.ViewModel.Level);
                var plan = Wait(w.PlayTest(t.X, t.Y, exhumed));
                Assert.NotNull(plan);                               // no message: the default player stands in
                Assert.Equal("SAVE1", plan.Slot);
                Assert.True(plan.DefaultPlayer);
                Assert.Equal(exhumed, plan.Exhumed);
                var pd = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(Path.Combine(to, "SAVE1", "PLAYER.DAT")), kind);
                Assert.Equal(2, pd.SavedLevel);
                Assert.Equal(t.X, pd.TileX);
                Assert.Equal(t.Y, pd.TileY);
                Assert.True(File.Exists(Path.Combine(to, "SAVE1", "LEV.ARK")));
                Assert.True(File.Exists(Path.Combine(to, "SAVE1", "BGLOBALS.DAT")));
                Assert.False(Directory.Exists(Path.Combine(folder, "SAVE1")));       // the game folder is untouched
                string cfg = Path.Combine(to, UwEditor.Core.ExhumedConfig.FileName(kind));
                if (exhumed)
                {
                    // the port's game and home are the copy, and the editor's settings are in the copy's settings file
                    Assert.Equal(new[] { "--data", plan.Folder, "--home", plan.Folder, "--scale", "2" }, w.LastRun.Arguments);
                    Assert.Equal(plan.Folder, w.LastRun.Folder);
                    var keys = UwEditor.Core.ExhumedConfig.Keys(File.ReadAllText(cfg));
                    Assert.Equal(new[] { "enhance=skip-intro", "settings-at-start=0" }, keys.Select(k => k.Key + "=" + k.Value));
                }
                else Assert.False(File.Exists(cfg));
            }
            finally
            {
                ForgetExhumedSettings(w);
                w.Close();
                if (Directory.Exists(to)) Directory.Delete(to, true);
                Directory.Delete(folder, true);
            }
        }

        [AvaloniaFact]
        public void Exhumeds_settings_are_edited_kept_and_used_by_a_run()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            w.RunLaunch = false;
            ForgetExhumedSettings(w);
            try
            {
                var kind = UltimateEditor.Games.GameKind.UW1;
                // the defaults are written the first time they are asked for, and set nothing
                string path = MainWindow.ExhumedSettingsPath(kind);
                Assert.False(File.Exists(path));
                Assert.Equal(UwEditor.Core.ExhumedConfig.Defaults(kind), w.ExhumedSettingsText(kind));
                Assert.True(File.Exists(path));
                Assert.StartsWith(Settings.Folder, path);
                Assert.Null(w.ApplyExhumedSettings(Path.Combine(MainWindow.RunFolder, "nothing-set"), kind));
                // edited in the window opened from Programs and paths, and read back
                var p = w.ShowRunPaths();
                var c = p.ShowExhumedSettings(kind);
                Ui.Pump();
                Assert.Equal(UwEditor.Core.ExhumedConfig.Defaults(kind), c.Text);
                c.Text = "enhance=skip-intro,fast-panels\r\nvolume=70\r\n";
                Assert.True(c.Save());
                c.Close();
                Ui.Pump();
                Assert.Equal("enhance=skip-intro,fast-panels\nvolume=70\n", File.ReadAllText(path));
                Assert.Equal("enhance=skip-intro,fast-panels\nvolume=70\n", w.ExhumedSettingsText(kind));
                // UW2's are its own
                Assert.Empty(UwEditor.Core.ExhumedConfig.Keys(w.ExhumedSettingsText(UltimateEditor.Games.GameKind.UW2)));
                p.Close();
                w.ExhumedArgs = "--no-aspect";
                // a run on a saved game kept elsewhere: the home is a folder of the editor's, with the settings and the extra arguments
                string save = Path.Combine(GameData.Root, "UW1Saves", "SAVE1");
                if (File.Exists(Path.Combine(save, "PLAYER.DAT")))
                {
                    w.OpenSession(UwEditor.Core.GameSession.Open(save, g));
                    Ui.Pump();
                    Assert.True(Wait(w.RunExhumed()));
                    var run = w.LastRun;
                    Assert.StartsWith(MainWindow.RunFolder, run.Folder);
                    Assert.Equal(new[] { "--data", w.ViewModel.Session.GameFolder, "--home", run.Folder, "--no-aspect" }, run.Arguments);
                    Assert.Equal("# uw1port settings\nenhance=skip-intro,fast-panels\nvolume=70\n", File.ReadAllText(Path.Combine(run.Folder, "uw1port.cfg")));
                    Assert.True(File.Exists(Path.Combine(run.Folder, "SAVE1", "PLAYER.DAT")));
                }
            }
            finally
            {
                ForgetExhumedSettings(w);
                w.Close();
            }
        }
    }
}
