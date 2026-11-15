using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// One terrain change a level can undergo: a change-terrain trap (0x185) run at a square, or
    /// (UW2) an oscillator trap (0x195) in height mode at a square. A trap that several triggers
    /// run at different squares gives one change for each square.
    /// </summary>
    public sealed class TerrainChange
    {
        /// <summary>The trap, in the level the list was made from.</summary>
        public ObjectInfo Trap;
        /// <summary>The triggers whose chains run the trap at this square (empty: no trigger reaches it, and it runs at its own square).</summary>
        public readonly List<ObjectInfo> Triggers = new List<ObjectInfo>();
        /// <summary>The first square of the rectangle, and its size in squares.</summary>
        public int X, Y, W = 1, H = 1;
        /// <summary>An oscillator in height mode: the floor of its square goes to <see cref="Height"/>.</summary>
        public bool Oscillator;
        /// <summary>An oscillator's lowest and highest floor height (its quality and owner, in order, kept to 0-15).</summary>
        public int Low, High;
        /// <summary>The height an oscillator is shown at (Low to High).</summary>
        public int Height;
        /// <summary>What it does, in words: the squares and what they become.</summary>
        public string What;

        /// <summary>The triggers that run it, in words (index, kind, where each lies); "no trigger" when there is none.</summary>
        public string TriggerText
        {
            get
            {
                if (Triggers.Count == 0) return "no trigger (a schedule, a conversation or the game's own code may run it)";
                return string.Join(", ", Triggers.Select(t => string.Format("#{0} {1} at ({2}, {3})", t.Index, t.Name, t.TileX, t.TileY)));
            }
        }

        /// <summary>The whole row: the trap, what runs it, what it does.</summary>
        public string Text { get { return string.Format("#{0} {1}  ·  run by {2}  ·  {3}", Trap.Index, Trap.Name, TriggerText, What); } }

        /// <summary>Whether (x, y) lies in the rectangle.</summary>
        public bool Covers(int x, int y) { return x >= X && x < X + W && y >= Y && y < Y + H; }
    }

    /// <summary>
    /// The level as it would look after some of its change-terrain traps have run (both games'
    /// event/TRIGGER.C TRAP_CHANGE_TERRAIN, event/WORLDEV.C change_terrain): the changes a level
    /// holds, and a changed copy of the level for a chosen set. The level given is never changed.
    /// </summary>
    public static class TerrainPreview
    {
        public const int ChangeTerrainTrap = 0x185, OscillatorTrap = 0x195;

        static bool IsChange(GameSession s, ObjectInfo o)
        {
            if (o.Item == ChangeTerrainTrap) return true;
            return s.IsUw2 && o.Item == OscillatorTrap && ((o.FineX & 6) >> 1) == 0;
        }

        /// <summary>
        /// Every terrain change of the level, by trap index and then by square. A trap in a trigger's
        /// chain runs at that trigger's target square (its quality and owner; a trigger met along a
        /// chain runs the rest at its own target); a trap no trigger reaches is listed at the square
        /// it lies on.
        /// </summary>
        public static List<TerrainChange> List(GameSession s, LevelModel m)
        {
            var list = new List<TerrainChange>();
            if (s == null || m == null || !m.Present) return list;
            var reached = new HashSet<int>();
            foreach (var trig in Triggers.TriggersOf(m))
            {
                int rx = trig.Quality, ry = trig.Owner;
                var seen = new HashSet<int> { trig.Index };
                var next = Triggers.LinksTo(m, trig);
                while (next != null && seen.Add(next.Index) && seen.Count < 256)
                {
                    if (Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Triggers.LinksTo(m, next); continue; }
                    if (!Triggers.IsTrap(next.Item)) break;
                    if (IsChange(s, next))
                    {
                        reached.Add(next.Index);
                        var c = list.FirstOrDefault(x => x.Trap == next && x.X == rx && x.Y == ry);
                        if (c == null) list.Add(c = Make(s, m, next, rx, ry));
                        if (!c.Triggers.Contains(trig)) c.Triggers.Add(trig);
                    }
                    int it = next.Item;
                    // create object, delete object and ward end a chain, and so do UW1's skill and door traps
                    if (it == 0x187 || it == 0x18B || it == 0x189 || (!s.IsUw2 && (it == 0x18A || it == 0x188))) break;
                    next = Triggers.LinksTo(m, next);
                }
            }
            foreach (var o in m.Objects)
                if (!o.Free && o.TileX >= 0 && IsChange(s, o) && !reached.Contains(o.Index)) list.Add(Make(s, m, o, o.TileX, o.TileY));
            return list.OrderBy(c => c.Trap.Index).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
        }

        static TerrainChange Make(GameSession s, LevelModel m, ObjectInfo trap, int x, int y)
        {
            var c = new TerrainChange { Trap = trap, X = x, Y = y };
            if (trap.Item == OscillatorTrap)
            {
                c.Oscillator = true;
                c.Low = Math.Min(15, Math.Min(trap.Quality, trap.Owner));
                c.High = Math.Min(15, Math.Max(trap.Quality, trap.Owner));
                // shown at the end farther from the floor as it is
                int now = x >= 0 && y >= 0 && x < 64 && y < 64 ? m.Tiles[x, y].Height : c.Low;
                c.Height = Math.Abs(c.High - now) >= Math.Abs(c.Low - now) ? c.High : c.Low;
                c.What = string.Format("({0}, {1}) rises and falls between height {2} and {3} (an oscillator)", x, y, c.Low, c.High);
                return c;
            }
            c.W = trap.FineX + 1; c.H = trap.FineY + 1;
            int type, height, floor, wall;
            Values(s, trap, out type, out height, out floor, out wall);
            var parts = new List<string>();
            if (type >= 0) parts.Add(type == 0 ? "rock" : TileInfo.TypeName((TileType)type));
            if (height >= 0) parts.Add("height " + height);
            if (floor >= 0) parts.Add("floor texture " + floor);
            if (wall >= 0) parts.Add("wall texture " + wall);
            c.What = string.Format("({0}, {1}){2} becomes {3}", x, y, c.W * c.H > 1 ? string.Format(" to ({0}, {1})", x + c.W - 1, y + c.H - 1) : "", parts.Count == 0 ? "the same" : string.Join(", ", parts));
            return c;
        }

        /// <summary>
        /// What a change-terrain trap sets, -1 for what it leaves alone: the tile type is heading * 2 +
        /// (quality &amp; 1), 15 read as 10 and 10 or more unchanged; the height z &gt;&gt; 3 (UW1 14 and 15,
        /// UW2 15 unchanged); the floor quality &gt;&gt; 1 (UW1 11 or more, UW2 15 unchanged); the wall
        /// the owner (UW1 48 or more, UW2 63 unchanged).
        /// </summary>
        public static void Values(GameSession s, ObjectInfo trap, out int type, out int height, out int floor, out int wall)
        {
            bool uw2 = s.IsUw2;
            type = trap.Heading * 2 + (trap.Quality & 1);
            if (type >= 10) type = -1;
            height = trap.Z >> 3;
            if (height >= (uw2 ? 15 : 14)) height = -1;
            floor = trap.Quality >> 1;
            if (floor >= (uw2 ? 15 : 11)) floor = -1;
            wall = trap.Owner;
            if (wall >= (uw2 ? 63 : 48)) wall = -1;
        }

        /// <summary>A copy of the level, with nothing shared (LevelModel.FromBytes copies the arrays).</summary>
        public static LevelModel Copy(LevelModel m)
        {
            return LevelModel.FromBytes(m.Session, m.Level, m.Block.Data, m.TextureBytes);
        }

        /// <summary>
        /// A copy of the level with the chosen changes applied, in trap index order (then by square)
        /// whatever order they are given in. The level itself is untouched. Only the tiles change:
        /// the objects stay at their heights (the game moves them with the floor).
        /// </summary>
        public static LevelModel Apply(LevelModel m, IEnumerable<TerrainChange> chosen)
        {
            var copy = Copy(m);
            var s = m.Session;
            foreach (var c in (chosen ?? Enumerable.Empty<TerrainChange>()).Distinct().OrderBy(c => c.Trap.Index).ThenBy(c => c.Y).ThenBy(c => c.X))
            {
                int type = -1, height = -1, floor = -1, wall = -1;
                if (c.Oscillator) height = Math.Max(c.Low, Math.Min(c.High, c.Height));
                else Values(s, c.Trap, out type, out height, out floor, out wall);
                for (int y = c.Y; y < c.Y + c.H; y++)
                    for (int x = c.X; x < c.X + c.W; x++)
                    {
                        if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                        var tile = copy.Tiles[x, y];
                        if (height >= 0) tile.Height = height;
                        if (floor >= 0) tile.FloorIndex = floor;
                        if (wall >= 0) tile.WallIndex = wall;
                        if (type >= 0) tile.Type = (TileType)type;
                    }
            }
            return copy;
        }

        /// <summary>The squares (y * 64 + x) where two levels' tiles differ in type, height, floor or wall.</summary>
        public static List<int> Differences(LevelModel a, LevelModel b)
        {
            var list = new List<int>();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var p = a.Tiles[x, y]; var q = b.Tiles[x, y];
                    if (p.Type != q.Type || p.Height != q.Height || p.FloorIndex != q.FloorIndex || p.WallIndex != q.WallIndex) list.Add(y * 64 + x);
                }
            return list;
        }
    }
}
