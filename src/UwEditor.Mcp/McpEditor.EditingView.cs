using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.Mcp
{
    /// <summary>What the app shows and has selected (attached only).</summary>
    public sealed class EditorSelection
    {
        /// <summary>The level shown, or -1.</summary>
        public int Level = -1;
        public int Object = -1, TileX = -1, TileY = -1;
        /// <summary>The tiles chosen together (Ctrl+click, Ctrl+drag), or the selected one.</summary>
        public List<(int X, int Y)> Tiles = new List<(int, int)>();
        /// <summary>The objects copied in the app (its clipboard), described, or null.</summary>
        public string Copied;
    }

    /// <summary>The app's 3D view camera (attached only).</summary>
    public sealed class EditorCamera
    {
        public int Level = -1;
        public Vector3 Target;
        public float Yaw, Pitch, Distance, FieldOfView = 60;
        public bool Orthographic;
        public SceneOptions Options;
    }

    /// <summary>
    /// The app's selection and view (go to a square), and the 3D picture: the level as the 3D view
    /// draws it (LevelScene: the level mesh, the object models and sprites), drawn here without GL
    /// by a small software rasteriser, so it works headless too.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>Attached: what the app shows and has selected.</summary>
        public Func<EditorSelection> HostSelection { get; set; }
        /// <summary>Attached: shows a level in the app, a square selected (or an object), looking at it; answers what it did.</summary>
        public Func<int, int, int, int, string> HostShow { get; set; }
        /// <summary>Attached: the app's 3D view camera and what it shows.</summary>
        public Func<EditorCamera> HostCamera { get; set; }

        /// <summary>What the user has selected in the app.</summary>
        public string EditorSelectionGet()
        {
            Require();
            if (HostSelection == null) throw new McpEditorException("Only the editor's window has a selection: this server runs without it (start it from Level > Let an AI assistant work here).");
            var s = HostSelection();
            if (s == null || s.Level < 0) return "No level is shown.";
            var m = ReadModel(s.Level);
            var b = new StringBuilder("Level " + s.Level + " is shown.\n");
            if (s.Object >= 0 && s.Object < m.Objects.Length && !m.Objects[s.Object].Free)
            {
                var o = m.Objects[s.Object];
                b.Append("Selected object: ").Append(o).Append(' ').Append(Hex(o.Item)).Append(o.TileX >= 0 ? " at (" + o.TileX + ", " + o.TileY + ")" : "").Append(o.Parent >= 0 ? " inside #" + o.Parent : "").Append('\n');
            }
            if (s.TileX >= 0) b.Append("Selected tile: (").Append(s.TileX).Append(", ").Append(s.TileY).Append(")\n");
            if (s.Tiles != null && s.Tiles.Count > 1)
            {
                int x0 = 64, y0 = 64, x1 = -1, y1 = -1;
                foreach (var t in s.Tiles) { x0 = Math.Min(x0, t.X); y0 = Math.Min(y0, t.Y); x1 = Math.Max(x1, t.X); y1 = Math.Max(y1, t.Y); }
                b.Append(s.Tiles.Count).Append(" tiles chosen, within (").Append(x0).Append(", ").Append(y0).Append(")-(").Append(x1).Append(", ").Append(y1).Append(")");
                if (s.Tiles.Count <= 40) { b.Append(": "); foreach (var t in s.Tiles) b.Append('(').Append(t.X).Append(", ").Append(t.Y).Append(") "); }
                b.Append('\n');
            }
            if (!string.IsNullOrEmpty(s.Copied)) b.Append("The editor's clipboard: ").Append(s.Copied).Append('\n');
            return b.ToString();
        }

        /// <summary>Shows a level in the app, with a square or an object selected (moves the user's view: only when asked).</summary>
        public string EditorShow(int level, int? x, int? y, int? index)
        {
            Require();
            if (HostShow == null) throw new McpEditorException("Only the editor's window shows levels: this server runs without it.");
            CheckLevelNumber(Session, level);
            int ox = -1, oy = -1, obj = -1;
            if (index.HasValue)
            {
                var m = ReadModel(level);
                if (index < 2 || index >= m.Objects.Length || m.Objects[index.Value].Free) throw new ArgumentException("#" + index + " is not an object of level " + level + ".");
                obj = index.Value; ox = m.Objects[obj].TileX; oy = m.Objects[obj].TileY;
            }
            if (x.HasValue || y.HasValue)
            {
                var p = Pair(x, y, "");
                ox = p.Item1; oy = p.Item2;
            }
            return HostShow(level, ox, oy, obj);
        }

        // ---- the 3D picture

        readonly Dictionary<GameSession, SpriteAtlas> edAtlases = new Dictionary<GameSession, SpriteAtlas>();

        SpriteAtlas EdAtlas()
        {
            SpriteAtlas a;
            if (!edAtlases.TryGetValue(Session, out a))
            {
                edAtlases.Clear();
                a = SpriteAtlas.Build(Session.Graphics.ObjectPictures);
                edAtlases[Session] = a;
            }
            return a;
        }

        /// <summary>
        /// The level seen in 3D: from a square (first person, eye height above its floor, looking along
        /// a heading), or round it from a distance, or (fromEditor) as the app's 3D view looks now.
        /// </summary>
        public byte[] ViewPicturePng(int level, int? x, int? y, double heading, double pitch, double? eyeHeight, double? distance, double fov,
            int width, int height, bool ceiling, bool objects, bool invisible, bool traps, bool models, bool fromEditor, out string said)
        {
            Require();
            if (width < 64 || width > 1600 || height < 64 || height > 1200) throw new ArgumentOutOfRangeException(null, "The picture is 64-1600 by 64-1200 pixels.");
            var cam = new OrbitCamera();
            var opt = new SceneOptions { Ceiling = ceiling, Objects = objects, Invisible = invisible, TrapsAndTriggers = traps, Models = models, Grid = false };
            if (fromEditor)
            {
                if (HostCamera == null) throw new McpEditorException("Only the editor's window has a view: give a square instead.");
                var c = HostCamera();
                if (c == null || c.Level < 0) throw new McpEditorException("No level is shown in the editor.");
                level = c.Level;
                cam.Target = c.Target; cam.Yaw = c.Yaw; cam.Pitch = c.Pitch; cam.Distance = c.Distance; cam.FieldOfView = c.FieldOfView; cam.Orthographic = c.Orthographic;
                if (c.Options != null) opt = new SceneOptions { Ceiling = c.Options.Ceiling, Objects = c.Options.Objects, Invisible = c.Options.Invisible, TrapsAndTriggers = c.Options.TrapsAndTriggers, Models = c.Options.Models, Grid = false };
            }
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            if (!fromEditor)
            {
                var p = Pair(x, y, "");
                if (fov < 20 || fov > 120) throw new ArgumentOutOfRangeException(null, "The field of view is 20-120 degrees.");
                if (pitch < -80 || pitch > 89) throw new ArgumentOutOfRangeException(null, "Pitch is -80 (up) to 89 (straight down) degrees.");
                cam.FieldOfView = (float)fov;
                cam.Yaw = (float)(((heading % 360) + 360) % 360);
                cam.Pitch = (float)pitch;
                float floor = (float)m.FloorAt(p.Item1, p.Item2, 0.5, 0.5);
                if (distance.HasValue)
                {
                    if (distance < 0.5 || distance > 120) throw new ArgumentOutOfRangeException(null, "The distance is 0.5-120 tiles.");
                    cam.Distance = (float)distance.Value;
                    cam.Target = new Vector3(p.Item1 + 0.5f, floor, p.Item2 + 0.5f);
                }
                else
                {
                    if (m.Tiles[p.Item1, p.Item2].IsSolid) throw new ArgumentException("(" + p.Item1 + ", " + p.Item2 + ") is solid rock: stand on an open square, or give a distance to look at it from afar.");
                    double eh = eyeHeight ?? 2.5;
                    if (eh < 0 || eh > 16) throw new ArgumentOutOfRangeException(null, "The eye height is 0-16 floor steps above the floor.");
                    var eye = new Vector3(p.Item1 + 0.5f, Math.Min(LevelGeometry.Ceiling - 0.02f, floor + (float)eh * LevelGeometry.Step), p.Item2 + 0.5f);
                    cam.Distance = 0.5f;
                    cam.Target = eye + cam.Forward * cam.Distance;
                }
            }
            var scene = LevelScene.Build(m, opt.Objects ? EdAtlas() : null, opt);
            var r = new EdRaster(width, height, cam);
            r.DrawLevel(scene.Mesh, scene.Layers);
            if (scene.Models != null) r.DrawModels(scene.Models);
            if (scene.Sprites != null && scene.Atlas != null) r.DrawSprites(scene.Sprites, scene.Atlas);
            var e = cam.Eye;
            said = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Level {0} from ({1:0.##}, {2:0.##}) at height {3:0.##} floor steps, heading {4:0} degrees, pitch {5:0}.", level, e.X, e.Z, e.Y / LevelGeometry.Step, cam.Yaw, cam.Pitch);
            return Png.EncodeRgba(width, height, r.Pixels);
        }

        /// <summary>A small z-buffered, perspective-correct, nearest-texel rasteriser for the 3D picture.</summary>
        sealed class EdRaster
        {
            readonly int w, h;
            readonly float[] depth;
            public readonly byte[] Pixels;
            readonly Matrix4x4 vp;
            readonly OrbitCamera cam;
            readonly Vector3 eye;

            public EdRaster(int width, int height, OrbitCamera camera)
            {
                w = width; h = height; cam = camera; eye = camera.Eye;
                depth = new float[w * h];
                Pixels = new byte[w * h * 4];
                for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;
                for (int i = 0; i < Pixels.Length; i += 4) { Pixels[i] = 14; Pixels[i + 1] = 14; Pixels[i + 2] = 18; Pixels[i + 3] = 255; }
                vp = camera.View * camera.Projection(width / (float)height);
            }

            struct V { public Vector4 C; public float U, Vt; }

            /// <summary>The texel for (u, v): rgba, or false where it is see-through.</summary>
            delegate bool Sampler(float u, float v, out byte r, out byte g, out byte b, out byte a);

            static Sampler Image(RgbaImage img, bool wrap)
            {
                if (img == null) return (float u, float v, out byte r, out byte g, out byte b, out byte a) => { r = g = b = 255; a = 255; return true; };
                int iw = img.Width, ih = img.Height; var px = img.Pixels;
                return (float u, float v, out byte r, out byte g, out byte b, out byte a) =>
                {
                    int tx, ty;
                    if (wrap) { tx = (int)Math.Floor((u - Math.Floor(u)) * iw); ty = (int)Math.Floor((v - Math.Floor(v)) * ih); }
                    else { tx = (int)(Math.Clamp(u, 0f, 1f) * iw); ty = (int)(Math.Clamp(v, 0f, 1f) * ih); }
                    tx = Math.Clamp(tx, 0, iw - 1); ty = Math.Clamp(ty, 0, ih - 1);
                    int k = (ty * iw + tx) * 4;
                    r = px[k]; g = px[k + 1]; b = px[k + 2]; a = px[k + 3];
                    return a >= 128;
                };
            }

            public void DrawLevel(LevelMesh mesh, List<RgbaImage> layers)
            {
                var samplers = new Sampler[layers.Count];
                for (int i = 0; i < layers.Count; i++) samplers[i] = Image(layers[i], true);
                var vs = mesh.Vertices;
                for (int t = 0; t + 2 < vs.Count; t += 3)
                {
                    var a = vs[t]; var b = vs[t + 1]; var c = vs[t + 2];
                    // a ceiling (negative shade) shows only from below, as in the game
                    if (a.Shade < 0 && eye.Y > 3.98f) continue;
                    int layer = Math.Clamp((int)(a.Layer + 0.5f), 0, layers.Count - 1);
                    float s = Math.Abs(a.Shade);
                    Tri(a.Position, b.Position, c.Position, a.U, a.V, b.U, b.V, c.U, c.V, samplers[layer], s, s, s, 1f);
                }
            }

            public void DrawModels(ModelMesh mm)
            {
                var samplers = new Sampler[mm.Layers.Count];
                for (int i = 0; i < mm.Layers.Count; i++) samplers[i] = Image(mm.Layers[i], false);
                var plain = Image(null, false);
                var vs = mm.Vertices;
                for (int t = 0; t + 2 < vs.Count; t += 3)
                {
                    var a = vs[t]; var b = vs[t + 1]; var c = vs[t + 2];
                    var smp = a.Layer < 0 || mm.Layers.Count == 0 ? plain : samplers[Math.Clamp((int)(a.Layer + 0.5f), 0, mm.Layers.Count - 1)];
                    Tri(a.Position, b.Position, c.Position, a.U, a.V, b.U, b.V, c.U, c.V, smp, a.R, a.G, a.B, 1f);
                }
            }

            public void DrawSprites(List<SpriteInstance> sprites, SpriteAtlas atlas)
            {
                var smp = Image(atlas.Image, false);
                float aw = atlas.Image.Width, ah = atlas.Image.Height;
                var right = cam.Right; var up = cam.Up;
                // the far ones first, so the near ones blend over them
                var order = new List<SpriteInstance>(sprites);
                order.Sort((p, q) => Vector3.DistanceSquared(q.Centre, eye).CompareTo(Vector3.DistanceSquared(p.Centre, eye)));
                foreach (var s in order)
                {
                    float u0 = s.Rect.X / aw, u1 = (s.Rect.X + s.Rect.W) / aw, v0 = s.Rect.Y / ah, v1 = (s.Rect.Y + s.Rect.H) / ah;
                    // a hair inside the rectangle, so a texel of the neighbour in the atlas never shows
                    float du = 0.25f / aw, dv = 0.25f / ah;
                    u0 += du; u1 -= du; v0 += dv; v1 -= dv;
                    float hw = s.Width / 2, hh = s.Height / 2;
                    var c = s.Centre;
                    Vector3 bl = c - right * hw - up * hh, br = c + right * hw - up * hh, tr = c + right * hw + up * hh, tl = c - right * hw + up * hh;
                    float alpha = s.Alpha <= 0 ? 1f : Math.Min(1f, s.Alpha);
                    Tri(bl, br, tr, u0, v1, u1, v1, u1, v0, smp, 1, 1, 1, alpha);
                    Tri(bl, tr, tl, u0, v1, u1, v0, u0, v0, smp, 1, 1, 1, alpha);
                }
            }

            readonly List<V> poly = new List<V>(8), clipped = new List<V>(8);

            void Tri(Vector3 pa, Vector3 pb, Vector3 pc, float ua, float va, float ub, float vb, float uc, float vc, Sampler smp, float kr, float kg, float kb, float alpha)
            {
                poly.Clear();
                poly.Add(new V { C = Vector4.Transform(new Vector4(pa, 1), vp), U = ua, Vt = va });
                poly.Add(new V { C = Vector4.Transform(new Vector4(pb, 1), vp), U = ub, Vt = vb });
                poly.Add(new V { C = Vector4.Transform(new Vector4(pc, 1), vp), U = uc, Vt = vc });
                // all three outside one side of the view: nothing to draw
                if (Out(p => p.C.X > p.C.W) || Out(p => p.C.X < -p.C.W) || Out(p => p.C.Y > p.C.W) || Out(p => p.C.Y < -p.C.W) || Out(p => p.C.Z > p.C.W)) return;
                // the near plane (z = -w, the GL convention) cuts what lies behind the eye
                clipped.Clear();
                for (int i = 0; i < poly.Count; i++)
                {
                    var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                    float dp = p.C.Z + p.C.W, dq = q.C.Z + q.C.W;
                    if (dp >= 0) clipped.Add(p);
                    if ((dp >= 0) != (dq >= 0))
                    {
                        float t = dp / (dp - dq);
                        clipped.Add(new V { C = Vector4.Lerp(p.C, q.C, t), U = p.U + (q.U - p.U) * t, Vt = p.Vt + (q.Vt - p.Vt) * t });
                    }
                }
                for (int i = 1; i + 1 < clipped.Count; i++) Raster(clipped[0], clipped[i], clipped[i + 1], smp, kr, kg, kb, alpha);
            }

            bool Out(Func<V, bool> outside) { return outside(poly[0]) && outside(poly[1]) && outside(poly[2]); }

            void Raster(V a, V b, V c, Sampler smp, float kr, float kg, float kb, float alpha)
            {
                float wa = Math.Max(1e-6f, a.C.W), wb = Math.Max(1e-6f, b.C.W), wc = Math.Max(1e-6f, c.C.W);
                float ax = (a.C.X / wa + 1) * 0.5f * w, ay = (1 - a.C.Y / wa) * 0.5f * h;
                float bx = (b.C.X / wb + 1) * 0.5f * w, by = (1 - b.C.Y / wb) * 0.5f * h;
                float cx = (c.C.X / wc + 1) * 0.5f * w, cy = (1 - c.C.Y / wc) * 0.5f * h;
                float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Math.Abs(area) < 1e-7f) return;
                float za = a.C.Z / wa, zb = b.C.Z / wb, zc = c.C.Z / wc;
                float ia = 1 / wa, ib = 1 / wb, ic = 1 / wc;
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                float inv = 1 / area;
                for (int py = y0; py <= y1; py++)
                {
                    float sy = py + 0.5f;
                    for (int px = x0; px <= x1; px++)
                    {
                        float sx = px + 0.5f;
                        float l0 = ((bx - sx) * (cy - sy) - (by - sy) * (cx - sx)) * inv;
                        float l1 = ((cx - sx) * (ay - sy) - (cy - sy) * (ax - sx)) * inv;
                        float l2 = 1 - l0 - l1;
                        if (l0 < 0 || l1 < 0 || l2 < 0) continue;
                        float z = l0 * za + l1 * zb + l2 * zc;
                        if (z < -1 || z > 1) continue;
                        int k = py * w + px;
                        if (z >= depth[k]) continue;
                        float iw = l0 * ia + l1 * ib + l2 * ic;
                        float u = (l0 * a.U * ia + l1 * b.U * ib + l2 * c.U * ic) / iw;
                        float v = (l0 * a.Vt * ia + l1 * b.Vt * ib + l2 * c.Vt * ic) / iw;
                        byte r, g, bl, al;
                        if (!smp(u, v, out r, out g, out bl, out al)) continue;
                        int o = k * 4;
                        float nr = Math.Min(255, r * kr), ng = Math.Min(255, g * kg), nb = Math.Min(255, bl * kb);
                        if (alpha < 0.999f)
                        {
                            nr = nr * alpha + Pixels[o] * (1 - alpha); ng = ng * alpha + Pixels[o + 1] * (1 - alpha); nb = nb * alpha + Pixels[o + 2] * (1 - alpha);
                        }
                        Pixels[o] = (byte)nr; Pixels[o + 1] = (byte)ng; Pixels[o + 2] = (byte)nb; Pixels[o + 3] = 255;
                        depth[k] = z;
                    }
                }
            }
        }
    }
}
