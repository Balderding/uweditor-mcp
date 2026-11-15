using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A start teleport found in a level: where it is and where it sends the player.</summary>
    public sealed class StartTeleportInfo
    {
        /// <summary>The square of the new-game start it rings, and its level.</summary>
        public int Level, X, Y;
        /// <summary>Where it sends the player.</summary>
        public int ToLevel, ToX, ToY;
        /// <summary>The teleport trap, and the move triggers that run it.</summary>
        public int Trap;
        public readonly List<int> Triggers = new List<int>();
        public StartPoint Destination(string description = null)
        {
            return new StartPoint { Level = ToLevel, X = ToX + 0.5, Y = ToY + 0.5, Z = double.NaN, Description = description ?? string.Format("the start teleport's square, level {0} ({1}, {2}) (the first step from level {3} ({4}, {5}) teleports there)", ToLevel, ToX, ToY, Level, X, Y) };
        }
        public override string ToString() { return string.Format("L{0} ({1}, {2}) -> L{3} ({4}, {5}): teleport trap #{6}, move triggers {7}", Level, X, Y, ToLevel, ToX, ToY, Trap, string.Join(", ", Triggers.Select(t => "#" + t))); }
    }

    /// <summary>
    /// A teleport at the new-game start that sends the player once to another square (the random
    /// start of the key item shuffle), for both games. Built from the games' sources:
    /// <list type="bullet">
    /// <item>The player is placed on the start square by player_setup (motion/PHYSICS.C of both
    /// games), which runs no collision check: a move trigger on the square does not go off then.
    /// Move triggers go off when the player's body touches them as he moves (motion/OBJPHYS.C
    /// do_objhit: any trap-class object hit, UseTrigger with mode 0; uw1 PHYSICS.C step move also
    /// tests ITEM_MOVE_TRIGGER). UW2's enter and pressure triggers do go off on placement
    /// (change_GrSq runs check_pplate 6), but they are not used: a teleport from inside the
    /// new-game set-up is untried, and loading a saved game runs change_GrSq again.</item>
    /// <item>So the start is ringed: a move trigger on the start square and on each open square
    /// around it (no door there), all aimed at the start square, where one teleport trap lies
    /// (TRAP_TELEPORT: quality x, owner y, z the level, 0 this one; event/TRIGGER.C UseTrap, then
    /// event/WORLDEV.C do_teleport records the move for the main loop, so a level change happens
    /// after the chain).</item>
    /// <item>Once: the triggers lack flag 10, so after the chain runs UseTrigger deletes it
    /// (delete_trap on the target square's list), and delete_trap first removes every trigger
    /// in the level whose link is the trap, as many as the trap's flags 9-12 count (kill_triggers):
    /// the trap's flags hold the number of triggers (at most 9 here, 15 fit). Flag 11 lets the
    /// player set them off; the enchanted bit is clear, so creatures do not. UW1 lets other
    /// objects (a thrown item) set off a trigger without the enchanted bit: such an object would
    /// use the teleport up without moving the player (do_teleport refuses a level change for
    /// anything but the player). UW2 asks flag 9 of objects, which is clear.</item>
    /// <item>UW2's teleport takes its fine x as arrival flags (bit 0 a flag, bit 1 a heading, bit
    /// 2 forgets the map): 0 here.</item>
    /// </list>
    /// The solver starts at the destination (NavOptions.StartOf with the levels) and does not make
    /// the teleport a way of the level (NavGraph.ReadTriggers skips it). Not played yet.
    /// </summary>
    public static class StartTeleport
    {
        public const int MoveTrigger = TrapRecipes.MoveTrigger, TeleportTrap = TrapRecipes.TeleportTrap;

        /// <summary>The start square and the open squares around it with no door, the start first: where the triggers go.</summary>
        public static List<(int X, int Y)> Ring(LevelModel m, int x, int y)
        {
            var list = new List<(int, int)>();
            foreach (var d in new[] { (0, 0), (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (1, -1), (-1, -1), (-1, 1) })
            {
                int a = x + d.Item1, b = y + d.Item2;
                if (a < 0 || b < 0 || a > 63 || b > 63 || m.Tiles[a, b].IsSolid) continue;
                if (m.TileObjects(a, b).Any(o => o.Item >= 0x140 && o.Item < 0x150)) continue;
                list.Add((a, b));
            }
            return list;
        }

        static bool OneShotMove(ObjectInfo o) { return o.Item == MoveTrigger && ((o.Record[1] >> 1) & 2) == 0; }

        /// <summary>
        /// The start teleport on (x, y) of a level, or null: a teleport trap lying on the square,
        /// with one or more move triggers that link to it, every one used once (no flag 10), aimed
        /// at the square and lying on it or beside it. A teleport trap any other trigger runs is
        /// not one.
        /// </summary>
        public static StartTeleportInfo Find(LevelModel m, int x, int y)
        {
            if (m == null || x < 0 || y < 0 || x > 63 || y > 63) return null;
            foreach (var trap in m.TileObjects(x, y))
            {
                if (trap.Item != TeleportTrap) continue;
                var trigs = m.Objects.Where(o => !o.Free && o.TileX >= 0 && Triggers.IsTrigger(o.Item) && o.IsQuantity && o.Link == trap.Index).ToList();
                if (trigs.Count == 0 || trigs.Any(t => !OneShotMove(t) || !t.OnTile || t.Quality != x || t.Owner != y || Math.Abs(t.TileX - x) > 1 || Math.Abs(t.TileY - y) > 1)) continue;
                var info = new StartTeleportInfo { Level = m.Level, X = x, Y = y, ToLevel = trap.Z == 0 ? m.Level : trap.Z, ToX = trap.Quality, ToY = trap.Owner, Trap = trap.Index };
                info.Triggers.AddRange(trigs.Select(t => t.Index).OrderBy(i => i));
                return info;
            }
            return null;
        }

        /// <summary>The start teleport at the square the game starts on (NavOptions.BaseStartOf: the new-game square), or null.</summary>
        public static StartTeleportInfo Find(GameSession s, Func<int, LevelModel> level, NavOptions nav = null)
        {
            var b = (nav ?? new NavOptions()).BaseStartOf(s);
            if (b == null || level == null) return null;
            LevelModel m;
            try { m = level(b.Level); } catch (Exception) { return null; }
            return Find(m, (int)b.X, (int)b.Y);
        }

        /// <summary>
        /// Puts the start teleport on the square from (its level) sending the player to to: a
        /// teleport trap on from and a move trigger, used once, on from and each open square round
        /// it (Ring), one undoable step on that level. A start teleport already there is taken off
        /// first, so a second Put replaces it. Returns what was put.
        /// </summary>
        public static StartTeleportInfo Put(GameSession s, LevelEdits edits, StartPoint from, StartPoint to)
        {
            if (from == null || to == null) throw new ArgumentNullException(from == null ? "from" : "to");
            int fx = (int)from.X, fy = (int)from.Y, tx = (int)to.X, ty = (int)to.Y;
            if (to.Level < 1 || to.Level > 127 || tx < 1 || tx > 62 || ty < 0 || ty > 62) throw new InvalidOperationException(string.Format("The start teleport cannot send the player to level {0} ({1}, {2}): a teleport takes x 1-62, y 0-62 and a level of 1-127.", to.Level, tx, ty));
            if (to.Level == from.Level && tx == fx && ty == fy) throw new InvalidOperationException("The start teleport would send the player where he starts.");
            var e = edits.For(from.Level);
            if (e.Model.Tiles[fx, fy].IsSolid) throw new InvalidOperationException(string.Format("The start square ({0}, {1}) is solid rock.", fx, fy));
            StartTeleportInfo made = null;
            e.Batch(string.Format("Start teleport to level {0} ({1}, {2})", to.Level, tx, ty), () =>
            {
                RemoveFrom(e, fx, fy);
                var tp = TrapRecipes.TrapSpec(TeleportTrap, to.Level == from.Level ? 0 : to.Level);
                tp.Quality = tx; tp.Owner = ty;
                int trap = e.AddObject(tp, fx, fy);
                var once = new TriggerOptions { Reusable = false, Creatures = false };
                foreach (var sq in Ring(e.Model, fx, fy))
                {
                    int trig = e.AddObject(TrapRecipes.TriggerSpec(MoveTrigger, fx, fy, once, null), sq.X, sq.Y);
                    TrapRecipes.SetLink(e, trig, trap);
                }
                TrapRecipes.CountTriggers(e, trap);
                bool ok;
                string report = e.Check(out ok);
                if (!ok) throw new InvalidOperationException("The level's list check failed after the start teleport (" + report + "); nothing was changed.");
                made = Find(e.Model, fx, fy);
            });
            return made;
        }

        /// <summary>Takes the start teleport off the square (one undoable step); false when there was none.</summary>
        public static bool Remove(LevelEdits edits, StartPoint from)
        {
            var e = edits.For(from.Level);
            if (Find(e.Model, (int)from.X, (int)from.Y) == null) return false;
            e.Batch("Start teleport removed", () => RemoveFrom(e, (int)from.X, (int)from.Y));
            return true;
        }

        static void RemoveFrom(LevelEditor e, int x, int y)
        {
            var old = Find(e.Model, x, y);
            if (old == null) return;
            foreach (int t in old.Triggers) e.DeleteObject(t);
            e.DeleteObject(old.Trap);
        }
    }
}
