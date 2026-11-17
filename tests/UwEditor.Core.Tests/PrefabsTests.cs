using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Prefabs: a tile clip saved under a name and placed again (Prefabs).</summary>
    public class PrefabsTests
    {
        static IEnumerable<(int X, int Y)> Rect(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) yield return (x, y);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_saved_prefab_pastes_the_same_level_bytes_as_the_clip_it_was_made_from(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            // the 7 × 6 block with the most objects, a trigger's chain among them when there is one
            var m = e.Model;
            int best = -1, bx = 1, by = 1;
            for (int y = 1; y < 56; y++) for (int x = 1; x < 56; x++) { int n = Rect(x, y, 7, 6).Sum(t => m.TileObjects(t.X, t.Y).Count); if (n > best) { best = n; bx = x; by = y; } }
            var clip = e.CopyTiles(Rect(bx, by, 7, 6));
            string folder = Path.Combine(Path.GetTempPath(), "uwed-prefabs-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Prefabs.Save(folder, clip, "A test: room/1");
                Assert.EndsWith(Prefabs.Extension, path);
                Assert.Contains(Prefabs.GameFolder(s.Kind), path);
                var info = Assert.Single(Prefabs.List(folder, s.Kind));
                Assert.Equal("A test: room/1", info.Name);
                Assert.Equal(42, info.Tiles);
                Assert.Equal(clip.AllObjects().Count(), info.Objects);
                Assert.Empty(Prefabs.List(folder, s.IsUw2 ? UltimateEditor.Games.GameKind.UW1 : UltimateEditor.Games.GameKind.UW2));
                var loaded = Prefabs.Load(path);

                int px = bx < 28 ? 55 : 2, py = by < 28 ? 56 : 2;
                e.PasteTiles(clip, px, py);
                var fromClip = (byte[])e.Data.Clone();
                e.Undo();
                var res = e.PasteTiles(loaded, px, py);
                bool ok; e.Check(out ok); Assert.True(ok);
                Assert.Equal(fromClip, e.Data);
                Assert.Equal(42, res.Tiles);

                // turned and saved again under the same name: the file is replaced, the size turned
                loaded.RotateClockwise();
                Prefabs.Save(folder, loaded, "A test: room/1");
                var turned = Assert.Single(Prefabs.List(folder, s.Kind));
                Assert.Equal(6, turned.Width);
                Assert.Equal(7, turned.Height);
                // a file that is no prefab is not listed, and says why when loaded
                File.WriteAllText(Path.Combine(folder, Prefabs.GameFolder(s.Kind), "junk" + Prefabs.Extension), "{ \"format\": \"other\" }");
                Assert.Single(Prefabs.List(folder, s.Kind));
                Assert.Throws<FormatException>(() => Prefabs.Load(Path.Combine(folder, Prefabs.GameFolder(s.Kind), "junk" + Prefabs.Extension)));
                Assert.Throws<InvalidOperationException>(() => Prefabs.Save(folder, clip, "  "));
            }
            finally { try { Directory.Delete(folder, true); } catch (Exception) { } }
        }
    }
}
