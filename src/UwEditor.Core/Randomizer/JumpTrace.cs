using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace UwEditor.Core.Randomizer
{
    /// <summary>Something to stand on in a square: its floor, or the top of a solid object over it (a bridge).</summary>
    public sealed class JumpSurface
    {
        /// <summary>The height of the top in floor levels (8 height units each).</summary>
        public double Top;
        /// <summary>The object, or -1 for the floor.</summary>
        public int Object = -1;
        public int Item = -1;
        public string Name = "the floor";
        public override string ToString() { return string.Format("{0} at height {1:0.##}", Name, Top); }
    }

    /// <summary>One jump from a square towards another, followed along its flight (the editor's Try Jump tool).</summary>
    public sealed class JumpTrace
    {
        /// <summary>The squares.</summary>
        public int FromX, FromY, ToX, ToY;
        /// <summary>The player's centre along the flight, in the view's coordinates (x, height, y), feet height.</summary>
        public readonly List<Vector3> Path = new List<Vector3>();
        /// <summary>Whether the player comes down on the far square (on the surface asked for, when one was).</summary>
        public bool Lands;
        public JumpKind Kind;
        /// <summary>With the jump spell.</summary>
        public bool Leap;
        /// <summary>Where the flight ends: the landing, or where it hits or comes down short.</summary>
        public Vector3 End;
        /// <summary>The square the flight ended over.</summary>
        public int EndX = -1, EndY = -1;
        /// <summary>What it came down on (null when it hit something): the floor or an object's top.</summary>
        public JumpSurface On;
        /// <summary>What the head met on the way up (the ceiling, an object's underside), cutting the jump there; null when nothing.</summary>
        public string Bumped;
        /// <summary>What happened, as a sentence.</summary>
        public string Text = "";

        const double Step = 0.02;
        /// <summary>The player's height in floor levels (COMOBJ.DAT item 0x7F: 23 height units, both games).</summary>
        public const double PlayerHeight = 23 / 8.0;

        /// <summary>
        /// The static objects that stand in a jump's way (motion/COLLIDE.C obj_coll_check: a box
        /// from the object's height up by COMOBJ's height, the whole tile for radius 4): those with
        /// a height and the Touch flag. Left out: creatures (they move), open doors, and the
        /// switches, levers and buttons on the walls.
        /// </summary>
        public static List<Hitbox> Obstacles(LevelModel m, byte[] comobj)
        {
            return Hitboxes.Of(m, comobj).Where(h => !h.Creature && !(h.Item >= 0x148 && h.Item <= 0x14F) && h.Item != 0x161 && h.Item != 0x162 && !(h.Item >= 0x170 && h.Item <= 0x17F)).ToList();
        }

        /// <summary>
        /// What there is to stand on in a square, highest first: the tops of the solid objects
        /// over its middle (get_pcoll lets the player onto an object only when COMOBJ's Solid is
        /// set), then its floor.
        /// </summary>
        public static List<JumpSurface> Surfaces(LevelModel m, IEnumerable<Hitbox> boxes, int x, int y, Func<int, string> name = null)
        {
            var list = new List<JumpSurface>();
            if (m == null || !Open(m, x, y)) return list;
            if (boxes != null)
                foreach (var b in boxes)
                    if (b.Solid && b.Top + 23 <= 128 && Covers(b, x + 0.5, y + 0.5) && !list.Any(s => Math.Abs(s.Top - b.Top / 8.0) < 0.2))
                        list.Add(new JumpSurface { Top = b.Top / 8.0, Object = b.Index, Item = b.Item, Name = "the " + (name != null ? name(b.Item) : "object") + " #" + b.Index });
            list.Add(new JumpSurface { Top = m.FloorAt(x, y, 0.5, 0.5) / LevelGeometry.Step });
            return list.OrderByDescending(s => s.Top).ToList();
        }

        static bool Covers(Hitbox b, double px, double py)
        {
            int x0, y0, x1, y1;
            b.Bounds(false, out x0, out y0, out x1, out y1);
            return px * 8 >= x0 && px * 8 < x1 + 1 && py * 8 >= y0 && py * 8 < y1 + 1;
        }

        /// <summary>
        /// Follows the jumps the player has from the middle of one square towards the middle of
        /// another, in the order the automatic zones try them: the standing long jump, the sprint
        /// jump (where the ground behind gives the run-up), then both with the jump spell; each
        /// from the square's edge, and from further back when that carries too far. The first
        /// that comes down on the far square is the answer; else the plain jump from the edge,
        /// up to where it ends. The rules are JumpModel's and the game's (MOTION.C set_targz,
        /// get_pcoll): a floor above the feet is a wall, except that on the way up the feet
        /// snap onto one within a level; on the way down the player lands on the highest floor
        /// under his footprint (the centre and four corners, a quarter tile out). Where the head
        /// (23 height units over the feet) meets the ceiling or an object's underside the rise
        /// stops and he bounces down (JumpFlight). An object in
        /// boxes is a box: the player lands on its top if it is solid, passes under it when his
        /// head clears its bottom, and is stopped by its side or its underside otherwise.
        /// from and to (of <see cref="Surfaces"/>) choose the surface to leave and the one to
        /// land on where a square has several (a bridge and the floor under it); null is the
        /// floor to leave, and anything to land on.
        /// </summary>
        public static JumpTrace Try(LevelModel m, int x0, int y0, int x1, int y1, NavOptions opt = null, IList<Hitbox> boxes = null, JumpSurface from = null, JumpSurface to = null, Func<int, string> name = null)
        {
            opt = opt ?? new NavOptions();
            boxes = boxes ?? new List<Hitbox>();
            var none = new JumpTrace { FromX = x0, FromY = y0, ToX = x1, ToY = y1 };
            if (m == null || !Open(m, x0, y0)) { none.Text = "The first square is rock."; return none; }
            if (!Open(m, x1, y1)) { none.Text = "The second square is rock."; return none; }
            if (x0 == x1 && y0 == y1) { none.Text = "Click another square for the landing."; return none; }
            int dt = Math.Max(1, Math.Min(64, opt.TickLength));
            double margin = Math.Max(0, opt.JumpMargin) / 8.0;
            double dx = x1 - x0, dy = y1 - y0, dist = Math.Sqrt(dx * dx + dy * dy);
            dx /= dist; dy /= dist;
            var w = new World { M = m, Boxes = boxes, Name = name, X0 = x0, Y0 = y0 };
            // the surface left: the floor, or the solid object whose top was asked for
            if (from != null && from.Object >= 0)
            {
                var b = boxes.Where(h => h.Solid && Covers(h, x0 + 0.5, y0 + 0.5) && Math.Abs(h.Top / 8.0 - from.Top) < 0.2).FirstOrDefault();
                if (b != null) { w.FromBox = b.Index; w.FromTop = b.Top / 8.0; }
            }
            // where the line from the middle leaves the first square
            double edge = 0.5 / Math.Max(Math.Abs(dx), Math.Abs(dy));
            // the run-up: ground a walking step apart at most behind the take-off, along the line
            double room = edge + 0.5;
            {
                double h = w.FromBox >= 0 ? w.FromTop : m.FloorAt(x0, y0, 0.5, 0.5) / LevelGeometry.Step;
                for (double b = 0.5; b < 9; b += 0.25)
                {
                    double px = x0 + 0.5 - dx * b, py = y0 + 0.5 - dy * b;
                    double f = w.Ground(px, py, h);
                    if (double.IsNaN(f)) break;
                    h = f; room = edge + b;
                }
            }
            bool canSprint = room >= JumpModel.RunUp(dt);
            JumpTrace shown = null;
            for (int pass = 0; pass < 4; pass++)
            {
                var kind = (pass & 1) == 0 ? JumpKind.Standing : JumpKind.Sprint;
                bool leap = pass >= 2;
                if (kind == JumpKind.Sprint && !canSprint) continue;
                // from the edge (the centre a little past it: the back corners are still on), then from further back
                for (double back = 0; back <= 1.0001; back += 0.125)
                {
                    double start = edge + JumpModel.Radius - margin / 2 - back;
                    if (start < -0.5) break;
                    var t = Fly(w, x1, y1, dx, dy, start, kind, leap, dt);
                    bool there = t.On != null && t.EndX == x1 && t.EndY == y1;
                    t.Lands = there && (to == null || (to.Object < 0 ? t.On.Object < 0 : t.On.Object >= 0 && Math.Abs(t.On.Top - to.Top) < 0.2));
                    if (t.Lands)
                    {
                        t.Text = string.Format("Lands on {0} of ({1}, {2}): {3}{4}{5}{6}.", t.On, x1, y1, kind == JumpKind.Sprint ? "a sprint jump" : "a standing long jump", leap ? " with the jump spell" : "",
                            back > 0 ? string.Format(", taken {0:0.##} tile back from the edge", back) : "", t.Bumped != null ? ", the head meeting " + t.Bumped + " on the way up" : "");
                        return t;
                    }
                    if (there) t.Text = string.Format("{0} comes down on {1} of ({2}, {3}), not on {4}", JumpName(kind), t.On, x1, y1, to);
                    // the plain jump from the edge is the one shown when nothing lands
                    if (!leap && back == 0) shown = t;
                }
            }
            shown.Text = string.Format("Does not reach {0}({1}, {2}): {3}{4}. The jump spell does not help either.", to != null ? to + " of " : "", x1, y1, shown.Text,
                canSprint ? "" : string.Format(" (no room for a sprint: {0:0.#} of {1:0.#} tiles of run-up)", room, JumpModel.RunUp(dt)));
            return shown;
        }

        static string JumpName(JumpKind kind) { return kind == JumpKind.Sprint ? "the sprint jump" : "the standing long jump"; }

        static bool Open(LevelModel m, int x, int y) { return x >= 0 && y >= 0 && x < 64 && y < 64 && !m.Tiles[x, y].IsSolid; }

        /// <summary>The level, its objects' boxes, and the surface the jump leaves.</summary>
        sealed class World
        {
            public LevelModel M;
            public IList<Hitbox> Boxes;
            public Func<int, string> Name;
            public int X0, Y0, FromBox = -1;
            public double FromTop;

            public string NameOf(Hitbox b) { return "the " + (Name != null ? Name(b.Item) : "object") + " #" + b.Index; }

            /// <summary>The ground at a point a walking step at most from height h (a floor, or a solid object's top), or NaN.</summary>
            public double Ground(double px, double py, double h)
            {
                int tx = (int)Math.Floor(px), ty = (int)Math.Floor(py);
                if (!Open(M, tx, ty)) return double.NaN;
                double best = double.NaN;
                double f = M.FloorAt(tx, ty, px - tx, py - ty) / LevelGeometry.Step;
                if (Math.Abs(f - h) <= JumpModel.WalkStep) best = f;
                foreach (var b in Boxes)
                    if (b.Solid && Covers(b, px, py) && Math.Abs(b.Top / 8.0 - h) <= JumpModel.WalkStep && (double.IsNaN(best) || Math.Abs(b.Top / 8.0 - h) < Math.Abs(best - h))) best = b.Top / 8.0;
                return best;
            }
        }

        static JumpTrace Fly(World w, int x1, int y1, double dx, double dy, double start, JumpKind kind, bool leap, int dt, double cap = double.PositiveInfinity, string bumped = null)
        {
            var m = w.M;
            int x0 = w.X0, y0 = w.Y0;
            var t = new JumpTrace { FromX = x0, FromY = y0, ToX = x1, ToY = y1, Kind = kind, Leap = leap, Bumped = bumped };
            double sx = x0 + 0.5 + dx * start, sy = y0 + 0.5 + dy * start;
            // the take-off height: the object's top, or the floor under the centre (the first square's edge when the centre is already past it)
            double h0 = w.FromBox >= 0 ? w.FromTop
                : m.FloorAt(x0, y0, Math.Min(1, Math.Max(0, sx - x0)), Math.Min(1, Math.Max(0, sy - y0))) / LevelGeometry.Step;
            int floor = (int)Math.Ceiling(h0 - 1e-9);
            var flight = new JumpFlight(kind, leap, floor, dt, cap);
            double top = flight.PeakAt;
            string name = JumpName(kind);
            double r = JumpModel.Radius;
            for (double x = 0; x < 16; x += Step)
            {
                double px = sx + dx * x, py = sy + dy * x;
                double feet = h0 + flight.Height(x), next = h0 + flight.Height(x + Step), before = h0 + flight.Height(Math.Max(0, x - Step));
                // the head against the ceiling: the jump is flown again, cut under it
                if (feet + PlayerHeight > 16 && 16 - PlayerHeight - h0 - 0.05 < cap - 1e-6)
                    return Fly(w, x1, y1, dx, dy, start, kind, leap, dt, 16 - PlayerHeight - h0 - 0.05, "the ceiling");
                var here = new Vector3((float)px, (float)(feet * LevelGeometry.Step), (float)py);
                t.Path.Add(here);
                t.End = here;
                bool rising = x <= top;
                // under the footprint: the highest thing to come down on, and what is in the way
                JumpSurface under = null, snap = null;
                int ux = -1, uy = -1, sx2 = -1, sy2 = -1;
                string hit = null;
                for (int c = 0; c < 5 && hit == null; c++)
                {
                    double qx = px + (c == 1 || c == 2 ? r : c == 3 || c == 4 ? -r : 0), qy = py + (c == 1 || c == 3 ? r : c == 2 || c == 4 ? -r : 0);
                    int tx = (int)Math.Floor(qx), ty = (int)Math.Floor(qy);
                    if (!Open(m, tx, ty)) { hit = string.Format("{0} hits the rock at ({1}, {2})", name, tx, ty); t.EndX = tx; t.EndY = ty; break; }
                    // the floor (not the one being left), then the boxes over this point
                    for (int k = -1; k < w.Boxes.Count && hit == null; k++)
                    {
                        double sTop, sBottom;
                        JumpSurface s;
                        bool solid;
                        if (k < 0)
                        {
                            if (tx == x0 && ty == y0 && w.FromBox < 0) continue;
                            sTop = m.FloorAt(tx, ty, qx - tx, qy - ty) / LevelGeometry.Step; sBottom = double.MinValue; solid = true;
                            s = new JumpSurface { Top = sTop };
                        }
                        else
                        {
                            var b = w.Boxes[k];
                            if (b.Index == w.FromBox || !Covers(b, qx, qy)) continue;
                            // a second bridge on the square being left, at the same height, is the same surface
                            if (w.FromBox >= 0 && b.TileX == x0 && b.TileY == y0 && Math.Abs(b.Top / 8.0 - w.FromTop) < 0.2) continue;
                            sTop = b.Top / 8.0; sBottom = b.Bottom / 8.0; solid = b.Solid;
                            s = new JumpSurface { Top = sTop, Object = b.Index, Item = b.Item, Name = w.NameOf(b) };
                        }
                        if (sTop <= feet + (rising ? 0.25 : 1e-9))
                        {
                            // under the feet (set_targz: on the way up a floor counts only from a quarter step above them)
                            if (solid && (under == null || sTop > under.Top)) { under = s; ux = tx; uy = ty; }
                            continue;
                        }
                        if (feet + PlayerHeight <= sBottom) continue;          // over the head: he passes under it
                        if (k >= 0 && x <= top && before + PlayerHeight <= sBottom + 1e-9 && sBottom - PlayerHeight - h0 - 0.05 < cap - 1e-6)
                            return Fly(w, x1, y1, dx, dy, start, kind, leap, dt, sBottom - PlayerHeight - h0 - 0.05, "the underside of " + s.Name);      // the head comes up under it: flown again, cut there
                        if (rising && solid && sTop - feet <= JumpModel.WalkStep)
                        {
                            // within a step, going up: the feet snap onto it
                            if (snap == null || sTop > snap.Top) { snap = s; sx2 = tx; sy2 = ty; }
                            continue;
                        }
                        t.EndX = tx; t.EndY = ty;
                        hit = k < 0 ? string.Format("{0} hits the side of ({1}, {2}), {3:0.#} floor steps above the feet", name, tx, ty, sTop - feet)
                            : feet < sBottom ? string.Format("{0} hits the underside of {1} at ({2}, {3})", name, s.Name, tx, ty)
                            : string.Format("{0} hits the side of {1} at ({2}, {3})", name, s.Name, tx, ty);
                    }
                }
                if (hit != null) { t.Text = hit; return t; }
                JumpSurface on = null;
                int ox = -1, oy = -1;
                if (snap != null) { on = snap; ox = sx2; oy = sy2; }
                else if (!rising && under != null && next < under.Top) { on = under; ox = ux; oy = uy; }
                if (on == null) continue;
                t.On = on; t.EndX = ox; t.EndY = oy;
                t.End = new Vector3((float)px, (float)(on.Top * LevelGeometry.Step), (float)py);
                t.Path[t.Path.Count - 1] = t.End;
                t.Text = string.Format("{0} comes down on {1} of ({2}, {3})", name, on, ox, oy);
                return t;
            }
            t.Text = name + " never comes down";
            return t;
        }
    }
}
