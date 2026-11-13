using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// The Try Jump tool: click the square to jump from, then the square to land on. The flight
    /// (Core JumpTrace, the game's jump as the automatic zones use it) is drawn over the level:
    /// green when the player comes down where he was asked to, red up to where it ends when not.
    /// A square may have several things to stand on (a bridge and the floor under it): the click
    /// takes the one nearest to where it hit, and a click on the same square again takes the next
    /// one down.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The tool's index in the tool box (after Select, Move and Paint).</summary>
        public const int TryJumpTool = 3;

        int jumpLevel = -1, jumpFromX = -1, jumpFromY, jumpToX = -1, jumpToY;
        /// <summary>Which of the square's surfaces (highest first) the jump leaves and lands on.</summary>
        int jumpFromSurface, jumpToSurface;

        /// <summary>The jump now shown, or null (for tests).</summary>
        public JumpTrace ShownJump { get; private set; }

        void ClearJump()
        {
            jumpLevel = jumpFromX = jumpToX = -1;
            ShownJump = null;
        }

        List<Hitbox> JumpBoxes(LevelModel level) { return JumpTrace.Obstacles(level, ComObj()); }

        List<JumpSurface> JumpSurfaces(LevelModel level, List<Hitbox> boxes, int x, int y)
        {
            var names = vm.Session.Names;
            return JumpTrace.Surfaces(level, boxes, x, y, i => names.Get(i));
        }

        /// <summary>The surface of a square nearest to a height in floor levels (NaN: the highest).</summary>
        static int NearestSurface(List<JumpSurface> list, double height)
        {
            if (list.Count == 0 || double.IsNaN(height)) return 0;
            int best = 0;
            for (int i = 1; i < list.Count; i++) if (Math.Abs(list[i].Top - height) < Math.Abs(list[best].Top - height)) best = i;
            return best;
        }

        /// <summary>A click of the Try Jump tool on a square of the level shown, at a height in floor levels (what the view's click does; NaN: the square's highest surface).</summary>
        public void TryJumpAt(int x, int y, double height = double.NaN) { if (vm.Level != null && vm.ToolIndex == TryJumpTool) JumpClick(vm.Level, x, y, height); }

        /// <summary>
        /// A click with the Try Jump tool: the first square, then the second. A click on the
        /// second square again takes its next surface down (round to the highest); a click
        /// anywhere else starts again.
        /// </summary>
        void JumpClick(LevelModel level, int tx, int ty, double height)
        {
            if (tx < 0) { vm.EditStatus = "Try Jump: click a square of the level."; return; }
            var boxes = JumpBoxes(level);
            var here = JumpSurfaces(level, boxes, tx, ty);
            string note = "";
            if (jumpFromX >= 0 && jumpToX == tx && jumpToY == ty && jumpLevel == level.Level && here.Count > 1)
            {
                jumpToSurface = (jumpToSurface + 1) % here.Count;
            }
            else if (jumpFromX < 0 || jumpToX >= 0 || jumpLevel != level.Level)
            {
                jumpLevel = level.Level; jumpFromX = tx; jumpFromY = ty; jumpToX = -1;
                jumpFromSurface = NearestSurface(here, height);
                ShownJump = null;
                vm.EditStatus = string.Format("Try Jump: from {0}({1}, {2}). Click the square to land on.", here.Count > 1 ? here[jumpFromSurface] + " of " : "", tx, ty);
            }
            else
            {
                jumpToX = tx; jumpToY = ty;
                jumpToSurface = NearestSurface(here, height);
            }
            if (jumpToX >= 0 && here.Count > 1)
                note = string.Format(" This square has {0} places to land: {1}. Click it again for the next one.", here.Count, string.Join(", ", here.Select(s => s.ToString())));
            UpdateMarkers();
            if (ShownJump != null) vm.EditStatus = "Try Jump: " + ShownJump.Text + note;
        }

        /// <summary>The jump's squares and flight, for the markers (worked out again each time, so an edit of the level shows).</summary>
        void AddJump(List<float> lines, List<float> tris, LevelModel level)
        {
            ShownJump = null;
            if (vm.ToolIndex != TryJumpTool || jumpFromX < 0 || jumpLevel != level.Level) return;
            var white = new Vector4(1f, 1f, 1f, 1f);
            var boxes = JumpBoxes(level);
            var fromList = JumpSurfaces(level, boxes, jumpFromX, jumpFromY);
            if (fromList.Count == 0) return;
            var from = fromList[Math.Min(jumpFromSurface, fromList.Count - 1)];
            SurfaceOutline(lines, level, jumpFromX, jumpFromY, from, white);
            if (jumpToX < 0) return;
            var toList = JumpSurfaces(level, boxes, jumpToX, jumpToY);
            // with one surface anything on the square will do; with several, the one chosen
            var to = toList.Count > 1 ? toList[Math.Min(jumpToSurface, toList.Count - 1)] : null;
            NavOptions nav; AbilityOptions abil; string why;
            ZoningOptions(out nav, out abil, out why);
            var names = vm.Session.Names;
            var t = JumpTrace.Try(level, jumpFromX, jumpFromY, jumpToX, jumpToY, nav, boxes, from, to, i => names.Get(i));
            ShownJump = t;
            var colour = t.Lands ? new Vector4(0.2f, 1f, 0.3f, 1f) : new Vector4(1f, 0.15f, 0.1f, 1f);
            SurfaceOutline(lines, level, jumpToX, jumpToY, to, colour);
            if (t.Path.Count == 0) return;
            // the flight, a little above the feet so it does not sink into the floor it leaves
            var lift = new Vector3(0, 0.02f, 0);
            for (int i = 1; i < t.Path.Count; i++) LevelScene.Line(lines, t.Path[i - 1] + lift, t.Path[i] + lift, colour);
            // drop lines to the floor every half tile or so, so the height reads from above
            for (int i = 0; i < t.Path.Count; i += 25)
            {
                var p = t.Path[i];
                int px = (int)Math.Floor(p.X), py = (int)Math.Floor(p.Z);
                if (px < 0 || py < 0 || px > 63 || py > 63 || level.Tiles[px, py].IsSolid) continue;
                float f = (float)level.FloorAt(px, py, p.X - px, p.Z - py);
                if (f < p.Y) LevelScene.Line(lines, p, new Vector3(p.X, f, p.Z), new Vector4(colour.X, colour.Y, colour.Z, 0.35f));
            }
            var end = t.End + lift;
            if (t.Lands) Markers.Bullseye(tris, end, 0.25f, Markers.Green);
            else
            {
                // where it ends: a cross
                float r = 0.18f;
                LevelScene.Line(lines, end + new Vector3(-r, 0, -r), end + new Vector3(r, 0, r), colour);
                LevelScene.Line(lines, end + new Vector3(-r, 0, r), end + new Vector3(r, 0, -r), colour);
                LevelScene.Line(lines, end + new Vector3(0, -r, 0), end + new Vector3(0, r, 0), colour);
                Markers.ArrowDown(tris, end + new Vector3(0, r, 0), Markers.Red, 0.6f);
            }
        }

        /// <summary>The outline of a square at the surface chosen: its floor, or a square at an object's top.</summary>
        void SurfaceOutline(List<float> lines, LevelModel level, int x, int y, JumpSurface s, Vector4 colour)
        {
            if (s == null || s.Object < 0) { scene.TileOutline(lines, x, y, colour); return; }
            float h = (float)(s.Top * LevelGeometry.Step) + 0.01f;
            Vector3 a = new Vector3(x, h, y), b = new Vector3(x + 1, h, y), c = new Vector3(x + 1, h, y + 1), d = new Vector3(x, h, y + 1);
            LevelScene.Line(lines, a, b, colour); LevelScene.Line(lines, b, c, colour); LevelScene.Line(lines, c, d, colour); LevelScene.Line(lines, d, a, colour);
        }
    }
}
