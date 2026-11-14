using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The play test's default player (a game folder with no saved game), the play test for Underworld Exhumed, and the port's settings.</summary>
    public class DefaultPlayerAndExhumedConfigTests
    {
        static string Temp() { return Path.Combine(Path.GetTempPath(), "uwedit-defplay-" + Guid.NewGuid().ToString("N")); }

        static (int x, int y) OpenSquare(LevelModel m)
        {
            return Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => t.x > 1 && t.y > 1 && t.x < 62 && t.y < 62 && !m.Tiles[t.x, t.y].IsSolid);
        }

        [Theory]
        [InlineData(GameKind.UW1)]
        [InlineData(GameKind.UW2)]
        public void The_editor_has_a_default_player_of_each_game(GameKind kind)
        {
            byte[] d = DefaultPlayer.Bytes(kind);
            Assert.NotNull(d);
            Assert.True(d.Length > PlayerDat.RecordSizeFor(kind));
            Assert.Equal(kind, PlayerDat.Detect(d));
            var pd = PlayerDat.Parse(d, kind);
            Assert.False(string.IsNullOrWhiteSpace(pd.Name));
            Assert.InRange(pd.Level, 1, 16);
            // moved as a saved game's player is, and written back whole
            pd.SetPosition(2, 30, 31, 4, false, pd.Heading);
            var again = PlayerDat.Parse(pd.ToBytes(), kind);
            Assert.Equal(2, again.SavedLevel);
            Assert.Equal(30, again.TileX);
            Assert.Equal(31, again.TileY);
            Assert.Equal(d.Length, pd.ToBytes().Length);
            // a fresh copy each time
            d[0] ^= 0xFF;
            Assert.NotEqual(d[0], DefaultPlayer.Bytes(kind)[0]);
        }

        [Theory]
        [InlineData("UW", 3)]
        [InlineData("UW2", 3)]
        public void A_play_test_with_no_saved_game_makes_one_round_the_default_player(string game, int level)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))            // DATA and the executable: no saved game
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var open = OpenSquare(edits.Model(level));
                string to = Temp();
                try
                {
                    var plan = PlayTest.Prepare(s, edits, null, level, open.x, open.y, to);
                    Assert.Equal("SAVE1", plan.Slot);
                    Assert.True(plan.DefaultPlayer);
                    Assert.Contains("default character", plan.Note);
                    string slot = Path.Combine(to, "SAVE1");
                    foreach (var f in new[] { "PLAYER.DAT", "LEV.ARK", "BGLOBALS.DAT", "DESC" }.Concat(s.IsUw2 ? new[] { "SCD.ARK" } : new string[0]))
                        Assert.True(File.Exists(Path.Combine(slot, f)), f);
                    var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(slot, "PLAYER.DAT")), s.Kind);
                    Assert.Equal(level, pd.SavedLevel);
                    Assert.Equal(open.x, pd.TileX);
                    Assert.Equal(open.y, pd.TileY);
                    Assert.Equal(PlayerDat.Parse(DefaultPlayer.Bytes(s.Kind), s.Kind).Name, pd.Name);
                    // the levels are DATA's as edited, the schedules DATA's, the conversations' globals DATA's records with every word 0
                    Assert.Equal(edits.ToBytes(), File.ReadAllBytes(Path.Combine(slot, "LEV.ARK")));
                    Assert.Equal(File.ReadAllBytes(Path.Combine(to, "DATA", "LEV.ARK")), File.ReadAllBytes(Path.Combine(slot, "LEV.ARK")));
                    if (s.IsUw2) Assert.Equal(File.ReadAllBytes(Path.Combine(c.Folder, "DATA", "SCD.ARK")), File.ReadAllBytes(Path.Combine(slot, "SCD.ARK")));
                    var bab = BGlobals.Parse(File.ReadAllBytes(GameProfile.Find(s.DataFolder, "BABGLOBS.DAT")), false);
                    var bg = BGlobals.Parse(File.ReadAllBytes(Path.Combine(slot, "BGLOBALS.DAT")), true);
                    Assert.NotEmpty(bg.Records);
                    Assert.Empty(bg.Trailer);
                    Assert.Equal(bab.Records.Select(r => (r.Slot, r.Words.Length)), bg.Records.Select(r => (r.Slot, r.Words.Length)));
                    Assert.All(bg.Records, r => Assert.All(r.Words, w => Assert.Equal(0, w)));
                    Assert.Equal("Play test", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(slot, "DESC"))));
                    // the copy's saved game opens as any other, and the game folder got nothing
                    var save = GameSession.Open(slot);
                    Assert.True(save.IsSave);
                    Assert.Equal(edits.For(level).Data, save.LevArk.Read(save.Profile.LevelBlock(level)));
                    Assert.False(Directory.Exists(Path.Combine(c.Folder, "SAVE1")));
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_play_test_for_Exhumed_never_patches_the_executable_and_runs_in_the_copy(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                if (s.ExePath == null) return;
                var edits = new LevelEdits(s);
                var open = OpenSquare(edits.Model(1));
                string to = Temp();
                try
                {
                    // level 1 with DATA open: a saved game, as anywhere else
                    var plan = PlayTest.Prepare(s, edits, null, 1, open.x, open.y, to, true);
                    Assert.True(plan.Exhumed);
                    Assert.Equal("SAVE1", plan.Slot);
                    Assert.True(plan.DefaultPlayer);
                    Assert.Contains("Journey Onward", plan.Note);
                    Assert.Equal(File.ReadAllBytes(s.ExePath), File.ReadAllBytes(Path.Combine(to, Path.GetFileName(s.ExePath))));
                    var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(to, "SAVE1", "PLAYER.DAT")), s.Kind);
                    Assert.Equal(1, pd.SavedLevel);
                    Assert.Equal(open.x, pd.TileX);
                    Assert.Equal(open.y, pd.TileY);
                    Assert.Equal(new[] { "--data", plan.Folder, "--home", plan.Folder, "--scale", "4", "--mt32-roms", "C:\\my roms" },
                        PlayTest.ExhumedArguments(plan, "--scale 4 --mt32-roms \"C:\\my roms\""));
                    Assert.Equal(new[] { "--data", plan.Folder, "--home", plan.Folder }, PlayTest.ExhumedArguments(plan, null));
                    // DOSBox on the same square: a saved game too, and the executable as it was
                    var dos = PlayTest.Prepare(s, edits, null, 1, open.x, open.y, to);
                    Assert.Equal("SAVE1", dos.Slot);
                    Assert.True(dos.DefaultPlayer);
                    Assert.Contains("Restore saved game 1", dos.Note);
                    Assert.Equal(File.ReadAllBytes(s.ExePath), File.ReadAllBytes(Path.Combine(to, Path.GetFileName(s.ExePath))));
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Fact]
        public void A_folders_own_saved_game_is_used_before_the_default_player()
        {
            string g = GameData.Game("UW2"); if (g == null || !Directory.Exists(Path.Combine(g, "SAVE1"))) return;
            using (var c = new EditingTests.Copy(g, "SAVE1"))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var open = OpenSquare(edits.Model(1));
                string to = Temp();
                try
                {
                    var plan = PlayTest.Prepare(s, edits, null, 1, open.x, open.y, to, true);
                    Assert.Equal("SAVE1", plan.Slot);
                    Assert.False(plan.DefaultPlayer);
                    var own = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(g, "SAVE1", "PLAYER.DAT")), GameKind.UW2);
                    var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(to, "SAVE1", "PLAYER.DAT")), GameKind.UW2);
                    Assert.Equal(own.Name, pd.Name);
                    Assert.Equal(open.x, pd.TileX);
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_play_test_gives_a_player_without_a_lantern_a_full_one(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var open = OpenSquare(edits.Model(2));
                string to = Temp();
                try
                {
                    var plan = PlayTest.Prepare(s, edits, null, 2, open.x, open.y, to);
                    var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(to, "SAVE1", "PLAYER.DAT")), s.Kind);
                    var inv = UwEditor.Core.Player.PlayerInventory.Parse(pd.Rest);
                    var lanterns = inv.All().Where(i => i.ItemId == PlayTest.LanternItem || i.ItemId == PlayTest.LitLanternItem).ToList();
                    Assert.Single(lanterns);
                    Assert.Empty(inv.Problems());
                    var before = UwEditor.Core.Player.PlayerInventory.Parse(PlayerDat.Parse(DefaultPlayer.Bytes(s.Kind), s.Kind).Rest);
                    if (before.All().Any(i => i.ItemId == PlayTest.LanternItem || i.ItemId == PlayTest.LitLanternItem)) { Assert.Null(plan.Lantern); return; }
                    // the default player had none: one more object, full of oil, lit on a shoulder or unlit in the pack
                    Assert.NotNull(plan.Lantern);
                    Assert.Contains("lantern", plan.Note);
                    Assert.Equal(before.All().Count() + 1, inv.All().Count());
                    var l = lanterns[0];
                    Assert.Equal(0x3F, l.Quality);
                    Assert.Equal(1, l.Quantity);
                    if (l.ItemId == PlayTest.LitLanternItem) Assert.InRange(l.Slot, 5, 6); else Assert.InRange(l.Slot, UwEditor.Core.Player.PlayerInventory.PackFirst, UwEditor.Core.Player.PlayerInventory.PackLast);
                    // everything he had is still there, in its slot
                    Assert.Equal(before.Top.Select(t => (t.ItemId, t.Slot)), inv.Top.Where(t => t != l).Select(t => (t.ItemId, t.Slot)));
                    // a player who has one is left alone
                    string again;
                    Assert.Same(pd, PlayTest.WithLantern(pd, out again));
                    Assert.Null(again);
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Fact]
        public void A_folder_whose_files_are_only_in_the_cloud_is_named_before_the_port_runs()
        {
            Assert.True(ExhumedConfig.CloudOnly(FileAttributes.Archive | FileAttributes.Offline));
            Assert.True(ExhumedConfig.CloudOnly((FileAttributes)0x400000));     // recall on data access: OneDrive's online-only file
            Assert.True(ExhumedConfig.CloudOnly((FileAttributes)0x40000));
            Assert.False(ExhumedConfig.CloudOnly(FileAttributes.Archive | FileAttributes.ReadOnly));
            string dir = Temp();
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "DATA"));
                File.WriteAllText(Path.Combine(dir, "UW.EXE"), "x");
                File.WriteAllText(Path.Combine(dir, "DATA", "LEV.ARK"), "x");
                // all on this disk
                Assert.Empty(ExhumedConfig.NotOnDisk(dir));
                Assert.Null(ExhumedConfig.Problem(null, dir, dir));
                // one file a placeholder
                System.Func<string, FileAttributes> cloud = f => f.EndsWith("LEV.ARK") ? (FileAttributes)0x400000 | FileAttributes.Archive : FileAttributes.Archive;
                Assert.Equal(new[] { Path.Combine(dir, "DATA", "LEV.ARK") }, ExhumedConfig.NotOnDisk(dir, cloud));
                string p = ExhumedConfig.Problem(null, dir, dir, cloud);
                Assert.Contains("The game folder", p);
                Assert.Contains("LEV.ARK", p);
                Assert.Contains("not on this disk", p);
                // the port's own folder is looked at too
                string port = Path.Combine(dir, "port");
                Directory.CreateDirectory(port);
                File.WriteAllText(Path.Combine(port, "uw1port.exe"), "x");
                System.Func<string, FileAttributes> cloudPort = f => f.EndsWith("uw1port.exe") ? FileAttributes.Offline : FileAttributes.Archive;
                string other = Temp();
                Directory.CreateDirectory(other);
                try { Assert.Contains("The port's folder", ExhumedConfig.Problem(Path.Combine(port, "uw1port.exe"), other, other, cloudPort)); }
                finally { Directory.Delete(other, true); }
                // a game folder that is not there
                Assert.Contains("not there", ExhumedConfig.Problem(null, Path.Combine(dir, "nowhere"), dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void The_ports_settings_are_read_as_the_port_reads_them()
        {
            Assert.Equal("uw1port.cfg", ExhumedConfig.FileName(GameKind.UW1));
            Assert.Equal("uw2port.cfg", ExhumedConfig.FileName(GameKind.UW2));
            // the defaults set nothing
            Assert.Empty(ExhumedConfig.Keys(ExhumedConfig.Defaults(GameKind.UW1)));
            Assert.Empty(ExhumedConfig.Keys(ExhumedConfig.Defaults(GameKind.UW2)));
            Assert.Contains("uw2port.cfg", ExhumedConfig.Defaults(GameKind.UW2));
            var keys = ExhumedConfig.Keys("# mine\r\nenhance=skip-intro,fast-panels\r\n\r\nscale=2\n#volume=10\nnot a setting\nscale=4\ndata=C:\\GAMES\\UW\n  settings-at-start=0  \nmt32-roms=C:\\my roms\n");
            Assert.Equal(new[] { "enhance=skip-intro,fast-panels", "scale=4", "settings-at-start=0", "mt32-roms=C:\\my roms" }, keys.Select(k => k.Key + "=" + k.Value));
        }

        [Fact]
        public void The_editors_keys_go_into_the_homes_settings_file_and_the_rest_stays()
        {
            string home = Temp();
            try
            {
                // nothing set: the home is left alone
                Assert.Null(ExhumedConfig.Apply(home, GameKind.UW1, ExhumedConfig.Defaults(GameKind.UW1)));
                Assert.False(Directory.Exists(home));
                // a new home gets the file, started as the port starts one
                string path = ExhumedConfig.Apply(home, GameKind.UW1, "# mine\nenhance=skip-intro\nsettings-at-start=0\n");
                Assert.Equal(Path.Combine(home, "uw1port.cfg"), path);
                Assert.Equal("# uw1port settings\nenhance=skip-intro\nsettings-at-start=0\n", File.ReadAllText(path));
                // a home with a file keeps its other keys (the game folder the port remembers among them)
                File.WriteAllText(path, "# uw1port settings\r\nenhance=perspective\r\nsound-auto=1\r\nvolume=50\r\ndata=C:\\GOG Games\\UW\r\n");
                ExhumedConfig.Apply(home, GameKind.UW1, "volume=80\nenhance=skip-intro,fast-panels\ndata=D:\\elsewhere\n");
                Assert.Equal("# uw1port settings\nsound-auto=1\ndata=C:\\GOG Games\\UW\nvolume=80\nenhance=skip-intro,fast-panels\n", File.ReadAllText(path));
                // read back: the editor's keys are what the file now has
                var back = ExhumedConfig.Keys(File.ReadAllText(path));
                Assert.Equal("80", back.First(k => k.Key == "volume").Value);
                Assert.Equal("skip-intro,fast-panels", back.First(k => k.Key == "enhance").Value);
                // UW2's file is its own
                Assert.Equal(Path.Combine(home, "uw2port.cfg"), ExhumedConfig.Apply(home, GameKind.UW2, "fullscreen=1"));
                Assert.Equal("# uw2port settings\nfullscreen=1\n", File.ReadAllText(Path.Combine(home, "uw2port.cfg")));
                // a port's own file as settings for the editor: without the folder it remembers
                Assert.Equal("# uw1port settings\nsound-auto=1\nvolume=80\nenhance=skip-intro,fast-panels\n", ExhumedConfig.FromPortFile(File.ReadAllText(path)));
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        [Fact]
        public void The_port_gets_its_game_and_its_home_on_the_command_line()
        {
            Assert.Equal(new[] { "--data", @"C:\Games\UW", "--home", @"C:\Games\UW" }, ExhumedConfig.Arguments(@"C:\Games\UW", @"C:\Games\UW", ""));
            Assert.Equal(new[] { "--data", "/games/uw2", "--home", "/run/home", "--no-aspect", "--scale", "2" }, ExhumedConfig.Arguments("/games/uw2", "/run/home", " --no-aspect --scale 2 "));
        }
    }
}
