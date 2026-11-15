using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The start teleport (StartTeleport): a teleport trap on the new-game square and a move
    /// trigger used once on it and each open square round it; read back, replaced, undone; the
    /// solver starts where it sends the player and does not take it as a way. Not played.
    /// </summary>
    public class StartTeleportTests
    {
        static void Written(LevelModel m, StartTeleportInfo t, int fx, int fy, int toLevel, int tx, int ty, int ring)
        {
            Assert.NotNull(t);
            var trap = m.Objects[t.Trap];
            Assert.Equal(0x181, trap.Item);
            Assert.Equal((fx, fy), (trap.TileX, trap.TileY));
            Assert.Equal((tx, ty), (trap.Quality, trap.Owner));
            Assert.Equal(toLevel == m.Level ? 0 : toLevel, trap.Z);
            Assert.Equal(0, trap.FineX);                                   // UW2: no arrival flags
            Assert.Equal(ring, ((trap.Record[0] | trap.Record[1] << 8) >> 9) & 15);     // the triggers delete_trap removes with it
            Assert.Equal(ring, t.Triggers.Count);
            foreach (int i in t.Triggers)
            {
                var g = m.Objects[i];
                Assert.Equal(0x1A0, g.Item);
                Assert.True(g.IsQuantity);
                Assert.Equal(t.Trap, g.Link);
                Assert.Equal((fx, fy), (g.Quality, g.Owner));
                Assert.Equal(4, (g.Record[1] >> 1) & 7);                   // flag 11 (the player), not flag 10 (used once)
                Assert.False(g.Enchanted);                                  // creatures do not set it off
                Assert.InRange(g.TileX, fx - 1, fx + 1);
                Assert.InRange(g.TileY, fy - 1, fy + 1);
            }
            Assert.Equal(ring, t.Triggers.Select(i => (m.Objects[i].TileX, m.Objects[i].TileY)).Distinct().Count());
        }

        [Fact]
        public void UW1_the_teleport_is_written_read_back_replaced_and_undone()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var s = GameSession.Open(game);
            var edits = new LevelEdits(s);
            var from = s.ShippedNewGameStart();
            Assert.Null(StartTeleport.Find(s, edits.Model));
            byte[] before = (byte[])edits.For(1).Data.Clone();
            var t = StartTeleport.Put(s, edits, from, new StartPoint { Level = 2, X = 9.5, Y = 35.5 });
            // the start and its eight neighbours are open at UW1 level 1 (32, 2)
            Written(edits.Model(1), t, 32, 2, 2, 9, 35, 9);
            var found = StartTeleport.Find(s, edits.Model);
            Assert.Equal(t.ToString(), found.ToString());
            Assert.Equal((2, 9.5, 35.5), (found.Destination().Level, found.Destination().X, found.Destination().Y));
            // a second one replaces the first
            int used = edits.Model(1).Objects.Count(o => !o.Free);
            var t2 = StartTeleport.Put(s, edits, from, new StartPoint { Level = 1, X = 40.5, Y = 31.5 });
            Written(edits.Model(1), t2, 32, 2, 1, 40, 31, 9);
            Assert.Equal(used, edits.Model(1).Objects.Count(o => !o.Free));
            // a reusable trigger is no start teleport
            var e = edits.For(1);
            e.SetObjectField(t2.Triggers[0], UltimateEditor.Uw1.Uw1Schemas.StaticObject["Flag 10"], 1);
            Assert.Null(StartTeleport.Find(s, edits.Model));
            e.Undo();
            Assert.NotNull(StartTeleport.Find(s, edits.Model));
            // undo both
            e.Undo(); e.Undo();
            Assert.Null(StartTeleport.Find(s, edits.Model));
            Assert.Equal(before, edits.For(1).Data);
        }

        [Fact]
        public void UW1_the_solver_starts_at_the_destination_and_takes_no_way_from_the_teleport()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var s = GameSession.Open(game);
            var edits = new LevelEdits(s);
            var t = StartTeleport.Put(s, edits, s.ShippedNewGameStart(), new StartPoint { Level = 2, X = 9.5, Y = 35.5 });
            var nav = new NavOptions();
            var st = nav.StartOf(s, edits.Model);
            Assert.Equal((2, 9, 35), (st.Level, (int)st.X, (int)st.Y));
            // StartOverride still wins; without the levels the game's square
            Assert.Equal(1, nav.StartOf(s).Level);
            var tl = Timeline.Build(s, edits.Model, nav, new AbilityOptions(), null, null, variants: false);
            Assert.Equal(tl.Set.ZoneAt(2, 9, 35), tl.Set.StartZone);
            Assert.Equal(st.Level, tl.Abilities.Auto.Start.Level);
            Assert.DoesNotContain(tl.Abilities.Auto.Graph.Edges, x => x.Level == 1 && t.Triggers.Contains(x.Trigger));
            Assert.DoesNotContain(tl.Abilities.Auto.Graph.Mechanisms, x => x.Level == 1 && t.Triggers.Contains(x.Trigger));
            Assert.True(tl.Solve.GoalReached, string.Join(", ", tl.Solve.GoalMissing));
            Assert.Empty(tl.Solve.CutOff);
        }

        [Fact]
        public void UW2_the_teleport_at_the_bedroom_is_written_and_the_solver_follows_it()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var edits = new LevelEdits(s);
            var from = s.ShippedNewGameStart();
            Assert.Equal((1, 19, 48), (from.Level, (int)from.X, (int)from.Y));
            // the Pits' landing, level 57 (59, 20): a teleport's z holds the level (7 bits)
            var t = StartTeleport.Put(s, edits, from, new StartPoint { Level = 57, X = 59.5, Y = 20.5 });
            Written(edits.Model(1), t, 19, 48, 57, 59, 20, 9);
            var st = new NavOptions().StartOf(s, edits.Model);
            Assert.Equal((57, 59, 20), (st.Level, (int)st.X, (int)st.Y));
            var tl = Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.Equal(tl.Set.ZoneAt(57, 59, 20), tl.Set.StartZone);
            Assert.DoesNotContain(tl.Abilities.Auto.Graph.Edges, x => x.Level == 1 && t.Triggers.Contains(x.Trigger));
            Assert.True(tl.Solve.GoalReached, string.Join(", ", tl.Solve.GoalMissing));
            Assert.Empty(tl.Solve.CutOff);
            // Lord British's speech comes when the Avatar first meets him, wherever he starts
            Assert.Contains("flag:lord_british_speech", tl.Solve.TokenSphere.Keys);
            Assert.True(tl.Solve.TokenSphere["flag:lord_british_speech"] <= tl.Solve.TokenSphere["npc:nystul"]);
            edits.For(1).Undo();
            Assert.Null(StartTeleport.Find(s, edits.Model));
        }
    }
}
