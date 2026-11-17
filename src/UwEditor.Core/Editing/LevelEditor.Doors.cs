using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    public sealed partial class LevelEditor
    {
        static bool IsDoor(GameSession s, ObjectInfo o) { return !o.Free && (ItemCategories.Of(o.Item, s.Kind) & ItemCategory.Door) != 0; }

        /// <summary>The lock a door (or a chest) holds, or null.</summary>
        public ObjectInfo LockOf(int holder)
        {
            var m = Model;
            if (holder <= 0 || holder >= m.Objects.Length) return null;
            return m.Objects[holder].Children.Select(c => m.Objects[c]).FirstOrDefault(c => !c.Free && c.Item == 0x10F);
        }

        void MustHoldLocks(ObjectInfo d)
        {
            bool container = (ItemCategories.Of(d.Item, Session.Kind) & ItemCategory.Container) != 0;
            if (!IsDoor(Session, d) && !container) throw new InvalidOperationException(d.Name + " takes no lock: doors and containers do.");
        }

        /// <summary>
        /// Puts a lock on a door or a container: a lock object (0x10F) inside it with this number
        /// (the keys whose owner field is the number open it), locked or not, and its difficulty to
        /// pick (0-15: 14 needs a skill of 31, 15 cannot be picked). One undo step; returns the lock.
        /// </summary>
        public int AddLock(int holder, int number, bool locked = true, int difficulty = 0)
        {
            ListsMustPass();
            var d = Model.Objects[holder];
            MustHoldLocks(d);
            if (number < 0 || number > 0x1FF) throw new ArgumentOutOfRangeException("number", "A lock's number is 0-511.");
            if (difficulty < 0 || difficulty > 127) throw new ArgumentOutOfRangeException("difficulty", "The difficulty is 0-127 (15 cannot be picked).");
            if (LockOf(holder) != null) throw new InvalidOperationException("#" + holder + " " + d.Name + " has a lock already: change its number in the panel, or remove it first.");
            int made = -1;
            Batch("Lock " + number + " on #" + holder + " " + d.Name, () =>
            {
                var S = Uw1Schemas.StaticObject;
                // the holder's link becomes a list (a door without a lock is often a quantity of nothing)
                if (Model.Objects[holder].IsQuantity) SetObjectField(holder, S["Is quantity"], 0);
                made = AddObject(new ObjectSpec { Item = 0x10F, IsQuantity = true, Link = 0x200 + number, Flags = locked ? 1 : 0, Z = difficulty, Quality = 0x28 }, d.TileX, d.TileY, holder);
            });
            return made;
        }

        /// <summary>Takes the lock off a door or a container (the lock object is deleted). One undo step; false when it has none.</summary>
        public bool RemoveLock(int holder)
        {
            var l = LockOf(holder);
            if (l == null) return false;
            DeleteObject(l.Index);
            return true;
        }

        /// <summary>
        /// Gives a door or a container a copy of another one's lock (its number, whether it is
        /// locked, its difficulty and flags); the lock it had goes. One undo step; returns the new lock.
        /// </summary>
        public int CopyLock(int from, int to)
        {
            ListsMustPass();
            if (from == to) throw new InvalidOperationException("Choose another door to copy the lock from.");
            var src = LockOf(from);
            if (src == null) throw new InvalidOperationException("#" + from + " " + Model.Objects[from].Name + " has no lock to copy.");
            var d = Model.Objects[to];
            MustHoldLocks(d);
            int made = -1;
            var clip = Copy(new[] { src.Index });
            Batch("Lock of #" + from + " copied to #" + to + " " + d.Name, () =>
            {
                var old = LockOf(to);
                if (old != null) DeleteObject(old.Index);
                if (Model.Objects[to].IsQuantity) SetObjectField(to, Uw1Schemas.StaticObject["Is quantity"], 0);
                made = Paste(clip, d.TileX, d.TileY, to)[0];
            });
            return made;
        }

        /// <summary>
        /// Puts an object that hangs on a wall (a lever, a button, a writing, a picture: 0x160 to
        /// 0x17F) against the nearest wall of its tile: at the edge of the tile that has solid
        /// rock (or the map's edge) behind it and is nearest to where the object is, in the
        /// middle of that edge unless it is nearer one end, its heading into the wall. One undo
        /// step; false when its tile has no wall.
        /// </summary>
        public bool SnapToWall(int index)
        {
            var o = Model.Objects[index];
            if (o.Free || !o.OnTile) throw new InvalidOperationException("#" + index + " must lie on a tile.");
            var m = Model;
            Func<int, int, bool> solid = (x, y) => x < 0 || y < 0 || x > 63 || y > 63 || m.Tiles[x, y].IsSolid;
            // north, east, south, west: the neighbour, the fine place on that edge, the heading into the wall
            var sides = new[]
            {
                new { Wall = solid(o.TileX, o.TileY + 1), Dist = 7 - o.FineY, Fx = o.FineX, Fy = 7, Heading = 0 },
                new { Wall = solid(o.TileX + 1, o.TileY), Dist = 7 - o.FineX, Fx = 7, Fy = o.FineY, Heading = 2 },
                new { Wall = solid(o.TileX, o.TileY - 1), Dist = o.FineY, Fx = o.FineX, Fy = 0, Heading = 4 },
                new { Wall = solid(o.TileX - 1, o.TileY), Dist = o.FineX, Fx = 0, Fy = o.FineY, Heading = 6 },
            };
            var best = sides.Where(s => s.Wall).OrderBy(s => s.Dist).FirstOrDefault();
            if (best == null) return false;
            var S = Uw1Schemas.StaticObject;
            if (o.FineX == best.Fx && o.FineY == best.Fy && o.Heading == best.Heading) return true;
            Batch("Snap #" + index + " " + o.Name + " to the wall", () =>
            {
                SetObjectField(index, S["Fine X"], best.Fx);
                SetObjectField(index, S["Fine Y"], best.Fy);
                SetObjectField(index, S["Heading"], best.Heading);
            });
            return true;
        }
    }
}
