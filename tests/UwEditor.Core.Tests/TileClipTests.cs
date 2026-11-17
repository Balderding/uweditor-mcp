using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Copying tiles with what is on them, turning and mirroring the clip, and pasting it (LevelEditor.CopyTiles, PasteTiles).</summary>
    public class TileClipTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        static IEnumerable<(int X, int Y)> Rect(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) yield return (x, y);
        }

        /// <summary>A tile without its object list, and the items lying on it with what they hold, in order.</summary>
        static string Tile(LevelModel m, int x, int y)
        {
            var rec = (byte[])m.Tiles[x, y].Record.Clone();
            Uw1Schemas.Tile["First object"].Set(rec, 0);
            var parts = new List<string>();
            Action<ObjectInfo, int> walk = null;
            walk = (o, d) =>
            {
                parts.Add(d + ":" + o.Item + "@" + o.FineX + "," + o.FineY + "," + o.Z + "," + o.Heading + (o.IsQuantity && !o.IsTrapOrTrigger ? "q" + o.Link : ""));
                foreach (int c in o.Children) walk(m.Objects[c], d + 1);
            };
            foreach (var o in m.TileObjects(x, y)) walk(o, 0);
            return BitConverter.ToString(rec) + " " + string.Join(" ", parts);
        }

        /// <summary>The w × h rectangle of the level with the most objects on it, away from the border.</summary>
        static (int X, int Y) Busiest(LevelModel m, int w, int h)
        {
            int best = -1, bx = 1, by = 1;
            for (int y = 1; y + h < 63; y++)
                for (int x = 1; x + w < 63; x++)
                {
                    int n = Rect(x, y, w, h).Sum(t => m.TileObjects(t.X, t.Y).Count);
                    if (n > best) { best = n; bx = x; by = y; }
                }
            return (bx, by);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Tiles_pasted_where_they_were_copied_give_the_same_level_and_undo_exactly(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var at = Busiest(e.Model, 8, 8);
            var tiles = Rect(at.X, at.Y, 8, 8).ToList();
            var was = tiles.Select(t => Tile(e.Model, t.X, t.Y)).ToList();
            Assert.Contains(was, t => t.Contains(":"));
            var clip = e.CopyTiles(tiles);
            Assert.Equal(64, clip.TileCount);
            var before = (byte[])e.Data.Clone();
            var res = e.PasteTiles(clip, at.X, at.Y);
            Assert.True(Ok(e));
            Assert.Equal(64, res.Tiles);
            Assert.Equal(res.Objects, res.Removed);
            Assert.Equal(0, res.TexturesKept);
            Assert.Equal(was, tiles.Select(t => Tile(e.Model, t.X, t.Y)).ToList());
            e.Undo();
            Assert.Equal(before, e.Data);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Tiles_paste_elsewhere_and_on_another_level_with_their_objects(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            var at = Busiest(e.Model, 6, 5);
            var tiles = Rect(at.X, at.Y, 6, 5).ToList();
            var was = tiles.Select(t => Tile(e.Model, t.X, t.Y)).ToList();
            var clip = e.CopyTiles(tiles);
            // elsewhere on the same level: a corner of solid rock becomes the copied room
            int px = at.X < 30 ? 50 : 3, py = at.Y < 30 ? 50 : 3;
            var res = e.PasteTiles(clip, px, py);
            Assert.True(Ok(e));
            Assert.Equal(30, res.Tiles);
            Assert.Equal(was, tiles.Select(t => Tile(e.Model, t.X - at.X + px, t.Y - at.Y + py)).ToList());
            Assert.Equal(res.Made.Count, tiles.Sum(t => e.Model.TileObjects(t.X - at.X + px, t.Y - at.Y + py).Count));
            // a creature copied is at home on its new tile
            foreach (int i in res.Made.Where(i => e.Model.Objects[i].IsCritter))
            {
                var o = e.Model.Objects[i];
                Assert.Equal(o.TileX, Uw1Schemas.MobileObject["Home X"].Get(o.Record));
                Assert.Equal(o.TileY, Uw1Schemas.MobileObject["Home Y"].Get(o.Record));
            }
            // another level: the textures go by their numbers where that level has them
            int other = s.LevelSlots().First(x => x.Present && x.Level != 1).Level;
            var e2 = edits.For(other);
            var res2 = e2.PasteTiles(clip, 20, 20);
            Assert.True(Ok(e2));
            var m1 = s.LoadLevel(1); var m2 = e2.Model;
            int same = 0;
            foreach (var t in tiles)
            {
                var a = m1.Tiles[t.X, t.Y]; var b = m2.Tiles[t.X - at.X + 20, t.Y - at.Y + 20];
                Assert.Equal(a.Type, b.Type);
                Assert.Equal(a.Height, b.Height);
                if (Array.IndexOf(m2.FloorMap, m1.FloorTextureNumber(a.FloorIndex)) >= 0) { Assert.Equal(m1.FloorTextureNumber(a.FloorIndex), m2.FloorTextureNumber(b.FloorIndex)); same++; }
                if (Array.IndexOf(m2.WallMap, m1.WallTextureNumber(a.WallIndex)) >= 0) Assert.Equal(m1.WallTextureNumber(a.WallIndex), m2.WallTextureNumber(b.WallIndex));
            }
            Assert.Equal(res.Objects, res2.Objects);
            // off the map: the tiles beyond the edge are left out
            var res3 = e2.PasteTiles(clip, 61, 61);
            Assert.Equal(9, res3.Tiles);
            Assert.Equal(21, res3.OffMap);
            Assert.True(Ok(e2));
        }

        [Fact]
        public void A_quarter_turn_and_a_mirror_move_the_tiles_and_turn_what_is_on_them()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var T = Uw1Schemas.Tile; var S = Uw1Schemas.StaticObject;
            // a 3 × 2 room made by hand in solid rock: a slope rising north, a diagonal open to the north-east, and an object in a corner
            var rock = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(t => t.X > 0 && t.Y > 0 && t.X < 58 && t.Y < 58 && Rect(t.X, t.Y, 3, 2).All(k => e.Model.Tiles[k.X, k.Y].IsSolid));
            int x0 = rock.X, y0 = rock.Y;
            foreach (var t in Rect(x0, y0, 3, 2)) e.SetTileField(t.X, t.Y, "Type", 1);
            e.SetTileField(x0, y0, "Type", (int)TileType.SlopeUpN);
            e.SetTileField(x0 + 2, y0 + 1, "Type", (int)TileType.OpenNE);
            int item = e.Model.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && !o.IsTrapOrTrigger && o.Item < 0x100).Item;
            int obj = e.AddObject(new ObjectSpec { Item = item, FineX = 1, FineY = 6 }, x0 + 1, y0);
            e.SetObjectField(obj, S["Heading"], 1);
            var clip = e.CopyTiles(Rect(x0, y0, 3, 2));
            Assert.Equal(3, clip.Width); Assert.Equal(2, clip.Height);

            clip.RotateClockwise();
            Assert.Equal(2, clip.Width); Assert.Equal(3, clip.Height);
            // (x, y) goes to (y, width − 1 − x): the south-west tile (0, 0) to the north-west (0, 2), north now east
            Assert.Equal((int)TileType.SlopeUpE, T["Type"].Get(clip.Cells[0, 2].Record));
            Assert.Equal((int)TileType.OpenSE, T["Type"].Get(clip.Cells[1, 0].Record));
            var o = Assert.Single(clip.Cells[0, 1].Objects);
            Assert.Equal(6, S["Fine X"].Get(o.Record));
            Assert.Equal(6, S["Fine Y"].Get(o.Record));
            Assert.Equal(3, S["Heading"].Get(o.Record));

            clip.Mirror();
            Assert.Equal((int)TileType.SlopeUpW, T["Type"].Get(clip.Cells[1, 2].Record));
            Assert.Equal((int)TileType.OpenSW, T["Type"].Get(clip.Cells[0, 0].Record));
            o = Assert.Single(clip.Cells[1, 1].Objects);
            Assert.Equal(1, S["Fine X"].Get(o.Record));
            Assert.Equal(5, S["Heading"].Get(o.Record));

            // four quarter turns, and two mirrors, bring a clip back
            var again = e.CopyTiles(Rect(x0, y0, 3, 2));
            for (int i = 0; i < 4; i++) again.RotateClockwise();
            again.Mirror(); again.Mirror();
            var first = e.CopyTiles(Rect(x0, y0, 3, 2));
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                {
                    Assert.Equal(first.Cells[x, y].Record, again.Cells[x, y].Record);
                    Assert.Equal(first.Cells[x, y].Objects.Select(k => BitConverter.ToString(k.Record)), again.Cells[x, y].Objects.Select(k => BitConverter.ToString(k.Record)));
                }

            // pasted turned: the level holds the turned room and passes the game's check
            var turned = e.CopyTiles(Rect(x0, y0, 3, 2));
            turned.RotateClockwise();
            int qx = x0 < 30 ? 55 : 5, qy = y0 < 30 ? 55 : 5;
            e.PasteTiles(turned, qx, qy);
            Assert.True(Ok(e));
            Assert.Equal(TileType.SlopeUpE, e.Model.Tiles[qx, qy + 2].Type);
            var put = Assert.Single(e.Model.TileObjects(qx, qy + 1));
            Assert.Equal(item, put.Item);
            Assert.Equal(3, put.Heading);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_trigger_copied_with_its_target_square_and_its_trap_points_at_the_copies(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            // a trigger on a tile whose target square is another tile near it, on any level
            foreach (var slot in s.LevelSlots().Where(x => x.Present).Take(12))
            {
                var e = edits.For(slot.Level);
                var m = e.Model;
                var trig = m.Objects.FirstOrDefault(o => !o.Free && o.OnTile && Triggers.IsTrigger(o.Item) && o.Link > 0
                    && (o.Quality != o.TileX || o.Owner != o.TileY) && Math.Abs(o.Quality - o.TileX) < 6 && Math.Abs(o.Owner - o.TileY) < 6
                    && o.Quality > 0 && o.Owner > 0 && o.TileX > 0 && o.TileY > 0);
                if (trig == null) continue;
                int x0 = Math.Min(trig.TileX, trig.Quality), y0 = Math.Min(trig.TileY, trig.Owner), w = Math.Abs(trig.TileX - trig.Quality) + 1, h = Math.Abs(trig.TileY - trig.Owner) + 1;
                var linked = m.Objects[trig.Link];
                bool trapInClip = !linked.Free && linked.TileX >= x0 && linked.TileX < x0 + w && linked.TileY >= y0 && linked.TileY < y0 + h;
                var clip = e.CopyTiles(Rect(x0, y0, w, h));
                // into the far corner of the map, where nothing of the original lies
                int px = x0 < 32 ? 63 - w : 1, py = y0 < 32 ? 63 - h : 1;
                var res = e.PasteTiles(clip, px, py);
                Assert.True(Ok(e));
                var copy = e.Model.TileObjects(trig.TileX - x0 + px, trig.TileY - y0 + py).First(o => o.Item == trig.Item && o.Quality == trig.Quality - x0 + px);
                Assert.Equal(trig.Owner - y0 + py, copy.Owner);
                Assert.NotEqual(trig.Index, copy.Index);
                if (trapInClip)
                {
                    // the copy runs the copy of the trap, not the original
                    Assert.NotEqual(trig.Link, copy.Link);
                    Assert.Equal(linked.Item, e.Model.Objects[copy.Link].Item);
                    Assert.Contains(copy.Link, res.Made.SelectMany(i => Tree(e.Model, i)));
                }
                return;
            }
        }

        static IEnumerable<int> Tree(LevelModel m, int i)
        {
            yield return i;
            foreach (int c in m.Objects[i].Children) foreach (int k in Tree(m, c)) yield return k;
        }

        [Fact]
        public void Tiles_go_to_the_other_game_without_their_objects()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2"); if (g1 == null || g2 == null) return;
            var s1 = GameSession.Open(g1); var s2 = GameSession.Open(g2);
            var e1 = new LevelEdits(s1).For(1); var e2 = new LevelEdits(s2).For(1);
            var at = Busiest(e1.Model, 5, 5);
            var clip = e1.CopyTiles(Rect(at.X, at.Y, 5, 5));
            var res = e2.PasteTiles(clip, 30, 30);
            Assert.True(Ok(e2));
            Assert.Equal(25, res.Tiles);
            Assert.Equal(0, res.Objects);
            Assert.True(res.ObjectsLeftOut > 0);
            foreach (var t in Rect(0, 0, 5, 5))
            {
                Assert.Equal(e1.Model.Tiles[at.X + t.X, at.Y + t.Y].Type, e2.Model.Tiles[30 + t.X, 30 + t.Y].Type);
                Assert.Empty(e2.Model.TileObjects(30 + t.X, 30 + t.Y));
            }
        }
    }
}
