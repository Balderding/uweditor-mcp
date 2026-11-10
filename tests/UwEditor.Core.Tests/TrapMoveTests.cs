using System.Collections.Generic;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// A trap's or trigger's height, and for some kinds its fine place, are settings, not where it
    /// stands (Triggers.HeightIsData, FinePositionIsData). Moving, dragging out of a chain, pasting and
    /// raising the floor must change only its square, as the Unity editor did.
    /// </summary>
    public class TrapMoveTests
    {
        const int FineX = 5, FineY = 6, Z = 77;

        static List<(int x, int y)> OpenTiles(LevelModel m, int count)
        {
            var r = new List<(int, int)>();
            for (int y = 2; y < 62 && r.Count < count; y++)
                for (int x = 2; x < 62 && r.Count < count; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0) r.Add((x, y));
            return r;
        }

        static void Expect(LevelEditor e, int i, int item, GameKind kind, int droppedFx, int droppedFy, string what)
        {
            var o = e.Model.Objects[i];
            Assert.True(o.Z == Z, string.Format("{0} 0x{1:X3}: height {2}, expected {3} kept", what, item, o.Z, Z));
            bool data = Triggers.FinePositionIsData(item, kind);
            int fx = data ? FineX : droppedFx, fy = data ? FineY : droppedFy;
            Assert.True(o.FineX == fx && o.FineY == fy, string.Format("{0} 0x{1:X3}: fine ({2}, {3}), expected ({4}, {5})", what, item, o.FineX, o.FineY, fx, fy));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Moving_a_trap_or_trigger_keeps_its_settings(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var S = Uw1Schemas.StaticObject;
            for (int item = 0x180; item < 0x1C0; item++)
            {
                var e = new LevelEditor(s, 1);
                var t = OpenTiles(e.Model, 4);
                int i = e.AddObject(ObjectSpec.Default(item, s.Kind), t[0].x, t[0].y);
                e.SetObjectField(i, S["Fine X"], FineX);
                e.SetObjectField(i, S["Fine Y"], FineY);
                e.SetObjectField(i, S["Z"], Z);
                int quality = e.Model.Objects[i].Quality, owner = e.Model.Objects[i].Owner;

                // dropped on another tile at fine (1, 2)
                e.MoveObject(i, t[1].x, t[1].y, 1, 2);
                Assert.Equal(t[1].x, e.Model.Objects[i].TileX);
                Expect(e, i, item, s.Kind, 1, 2, "moved");
                Assert.Equal(quality, e.Model.Objects[i].Quality);
                Assert.Equal(owner, e.Model.Objects[i].Owner);

                // the floor under it raised
                e.RaiseFloor(t[1].x, t[1].y, 2);
                Expect(e, i, item, s.Kind, 1, 2, "floor raised under");

                // copied and pasted: the paste keeps the record's fine place and, for traps, its height
                var clip = e.Copy(new[] { i });
                var pasted = e.Paste(clip, t[2].x, t[2].y);
                Expect(e, pasted[0], item, s.Kind, 1, 2, "pasted");

                // taken out of a trigger's chain onto the floor at fine (4, 4)
                if (Triggers.IsTrap(item))
                {
                    // a trigger whose link is a list, so the editor's put-inside takes the trap
                    // (shipped triggers have is-quantity set: ROADMAP.md §0, "Make it run a trap")
                    var spec = ObjectSpec.Default(0x1A0, s.Kind);
                    spec.IsQuantity = false; spec.Link = 0;
                    int trigger = e.AddObject(spec, t[3].x, t[3].y);
                    int trap = e.AddObject(ObjectSpec.Default(item, s.Kind), t[3].x, t[3].y, trigger);
                    e.SetObjectField(trap, S["Fine X"], FineX);
                    e.SetObjectField(trap, S["Fine Y"], FineY);
                    e.SetObjectField(trap, S["Z"], Z);
                    e.MoveObjectAnywhere(trap, t[0].x, t[0].y, 4, 4);
                    Assert.True(e.Model.Objects[trap].OnTile);
                    Expect(e, trap, item, s.Kind, 4, 4, "taken out of a chain");
                }
                bool ok;
                string report = e.Check(out ok);
                Assert.True(ok, report);
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Other_objects_still_follow_the_floor(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEditor(s, 1);
            var t = OpenTiles(e.Model, 2);
            int coins = e.AddObject(ObjectSpec.Default(0xA0, s.Kind), t[0].x, t[0].y);
            e.MoveObject(coins, t[1].x, t[1].y, 1, 2);
            Assert.Equal(e.FloorZ(t[1].x, t[1].y, 1, 2), e.Model.Objects[coins].Z);
            Assert.Equal(1, e.Model.Objects[coins].FineX);
            e.RaiseFloor(t[1].x, t[1].y, 2);
            Assert.Equal(e.FloorZ(t[1].x, t[1].y, 1, 2), e.Model.Objects[coins].Z);
        }
    }
}
