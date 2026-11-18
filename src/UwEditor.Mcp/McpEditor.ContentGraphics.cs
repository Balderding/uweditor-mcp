using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Graphics (Tools ▸ Graphics and its pixel editor): DATA's picture files (.GR, .TR, .BYT,
    /// BYT.ARK) through the session's PictureStore (the one the app's graphics window, pixel
    /// editor and item slots change, so either side's change shows on both), the palettes, the
    /// critters' art (CRIT), the cutscenes' animations (CUTS) and the fonts (*.SYS).
    /// </summary>
    public sealed partial class McpEditor
    {
        List<CritterFile> ctCritters;
        readonly Dictionary<string, byte[]> ctCritterDisk = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        List<Cutscene> ctCutscenes;
        readonly Dictionary<string, LpfAnimation> ctLpf = new Dictionary<string, LpfAnimation>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, byte[]> ctLpfDisk = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, UwFont> ctFonts = new Dictionary<string, UwFont>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, byte[]> ctFontDisk = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> ctFontChanged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- picture files

        PictureFile CtPictures(string file)
        {
            CtFresh();
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("No picture file given (graphics_files lists them).");
            string name = Path.GetFileName(file.Trim());
            var all = PictureFile.Find(Session.DataFolder).Select(Path.GetFileName).ToList();
            string hit = all.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(n => Path.GetFileNameWithoutExtension(n).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit == null) throw new McpEditorException("DATA has no picture file " + name + ". There are: " + string.Join(", ", all) + ".");
            var f = Session.Pictures.Get(hit);
            if (f == null) throw new McpEditorException("DATA has no picture file " + hit + ".");
            return f;
        }

        int CtPalette(int? palette, PictureFile f, int index)
        {
            int n = palette ?? f.DefaultPalette(index);
            int count = Session.Pictures.Palettes.Main.Count;
            if (n < 0 || n >= count) throw new ArgumentOutOfRangeException(null, "Palettes are 0-" + (count - 1) + " (PALS.DAT).");
            return n;
        }

        IndexedImage CtPicture(PictureFile f, int index, int? palette, bool blank = false)
        {
            if (index < 0 || index >= f.Count) throw new ArgumentOutOfRangeException(null, f.Name + " has pictures 0-" + (f.Count - 1) + ".");
            int pal = CtPalette(palette, f, index);
            var img = blank ? f.GetOrBlank(index, Session.Pictures.Palettes, pal) : f.Get(index, Session.Pictures.Palettes, pal);
            if (img == null) throw new McpEditorException("Picture " + index + " of " + f.Name + " is empty" + (blank ? " and has no neighbour to take a shape from." : "."));
            return img;
        }

        public string GraphicsFiles()
        {
            CtFresh();
            var b = new StringBuilder("The picture files of " + Session.DataFolder + " (graphics_list shows one file's pictures):\n");
            foreach (var p in PictureFile.Find(Session.DataFolder))
            {
                string n = Path.GetFileName(p);
                try
                {
                    var f = Session.Pictures.Get(n);
                    int changed = Enumerable.Range(0, f.Count).Count(f.IsChanged);
                    b.AppendFormat(Ci, "{0}: {1}, {2} picture{3}{4}{5}\n", n, f.Kind == PictureFileKind.Gr ? "pictures (.GR)" : f.Kind == PictureFileKind.Tr ? "textures (.TR)" : "320 × 200 screen" + (f.Kind == PictureFileKind.BytArk ? "s" : ""),
                        f.Count, f.Count == 1 ? "" : "s", f.Kind == PictureFileKind.Tr ? ", " + f.TextureEdge + " × " + f.TextureEdge : "", changed > 0 ? ", " + changed + " changed, not saved" : "");
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException) { b.Append(n).Append(": cannot be read (").Append(e.Message).Append(")\n"); }
            }
            return b.ToString();
        }

        public string GraphicsList(string file, int from, int count)
        {
            var f = CtPictures(file);
            if (from < 0) from = 0;
            if (count < 1 || count > 1000) throw new ArgumentOutOfRangeException(null, "count is 1-1000.");
            var b = new StringBuilder();
            b.AppendFormat(Ci, "{0}: {1} pictures; {2}-{3}:\n", f.Name, f.Count, from, Math.Min(f.Count, from + count) - 1);
            bool items = f.Name.Equals("OBJECTS.GR", StringComparison.OrdinalIgnoreCase);
            for (int i = from; i < f.Count && i < from + count; i++)
            {
                IndexedImage img = null;
                string desc;
                try { img = f.Get(i, Session.Pictures.Palettes, f.DefaultPalette(i)); desc = f.Describe(i, img); }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is IndexOutOfRangeException || e is ArgumentException) { desc = "cannot be read: " + e.Message; }
                b.Append(i).Append(": ").Append(desc);
                if (items && i < 0x200) b.Append("  (").Append(Session.Names.Get(i)).Append(')');
                if (f.IsChanged(i)) b.Append("  [changed, not saved]");
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>A picture as a PNG for looking at (scaled), or written into a folder at its own size as an indexed PNG (graphics_import takes it back).</summary>
        public byte[] GraphicsImagePng(string file, int index, int? palette, int? scale, string folder, out string said)
        {
            var f = CtPictures(file);
            var img = CtPicture(f, index, palette);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                string p = Path.Combine(CtFolder(folder), string.Format(Ci, "{0}-{1:D3}.png", Path.GetFileNameWithoutExtension(f.Name), index));
                File.WriteAllBytes(p, img.ToPng());
                said = "Written: " + p + " (" + f.Describe(index, img) + ", indexed in the picture's own colours).";
                return null;
            }
            said = string.Format(Ci, "Picture {0} of {1}: {2}{3}.", index, f.Name, f.Describe(index, img), f.IsChanged(index) ? ", changed, not saved" : "");
            return CtPng(img, CtAutoScale(img.Width, img.Height, scale));
        }

        /// <summary>Many pictures of a file side by side (left to right, then down), for finding one.</summary>
        public byte[] GraphicsSheetPng(string file, int from, int count, int? palette, int scale, out string said)
        {
            var f = CtPictures(file);
            if (count < 1 || count > 512) throw new ArgumentOutOfRangeException(null, "count is 1-512.");
            if (scale < 1 || scale > 8) throw new ArgumentOutOfRangeException(null, "The scale is 1-8.");
            from = Math.Max(0, from);
            var pics = new List<IndexedImage>();
            for (int i = from; i < f.Count && i < from + count; i++)
            {
                IndexedImage img = null;
                try { img = f.Get(i, Session.Pictures.Palettes, palette ?? f.DefaultPalette(i)); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException) { img = null; }
                pics.Add(img);
            }
            if (pics.Count == 0) throw new ArgumentOutOfRangeException(null, f.Name + " has pictures 0-" + (f.Count - 1) + ".");
            int cw = pics.Where(p => p != null).Select(p => p.Width).DefaultIfEmpty(8).Max() + 2, ch = pics.Where(p => p != null).Select(p => p.Height).DefaultIfEmpty(8).Max() + 2;
            int cols = Math.Max(1, Math.Min(pics.Count, Math.Max(1, 1024 / (cw * scale))));
            int rows = (pics.Count + cols - 1) / cols;
            int W = cols * cw, H = rows * ch;
            var px = new byte[W * H * 4];
            for (int k = 0; k < px.Length; k += 4) { px[k] = 0x40; px[k + 1] = 0x40; px[k + 2] = 0x48; px[k + 3] = 0xFF; }
            for (int n = 0; n < pics.Count; n++)
            {
                var p = pics[n];
                if (p == null) continue;
                var rgba = p.ToRgba();
                int ox = (n % cols) * cw + 1, oy = (n / cols) * ch + 1;
                for (int y = 0; y < p.Height; y++)
                    for (int x = 0; x < p.Width; x++)
                    {
                        int s = (y * p.Width + x) * 4, d = ((oy + y) * W + ox + x) * 4;
                        if (rgba[s + 3] == 0) { px[d] = 0x20; px[d + 1] = 0x20; px[d + 2] = 0x24; continue; }
                        px[d] = rgba[s]; px[d + 1] = rgba[s + 1]; px[d + 2] = rgba[s + 2];
                    }
            }
            said = string.Format(Ci, "Pictures {0}-{1} of {2}, {3} a row, left to right then down (empty ones left blank).", from, from + pics.Count - 1, f.Name, cols);
            return CtScaled(W, H, px, scale);
        }

        public string GraphicsImport(string file, int index, string png, bool nearest, int? palette)
        {
            var f = CtPictures(file);
            var like = CtPicture(f, index, palette, true);
            var r = PictureImport.FromPng(CtInput(png, "PNG"), like, nearest);
            if (!r.Ok) throw new McpEditorException("Not imported: " + r.Summary.Replace("\n", " ") + (r.Missing.Count > 0 && !nearest ? " (nearest true maps each colour to the nearest of the picture's)" : ""));
            f.Replace(index, like, r.Pixels);
            return string.Format(Ci, "Picture {0} of {1} replaced ({2}). Not written until save.{3}", index, f.Name, f.Describe(index, like), r.Notes.Count > 0 ? " " + string.Join(" ", r.Notes) : "");
        }

        public string GraphicsPixels(string file, int index, int? palette, int x, int y, int? w, int? h)
        {
            var f = CtPictures(file);
            var img = CtPicture(f, index, palette);
            int ww = w ?? img.Width - x, hh = h ?? img.Height - y;
            if (x < 0 || y < 0 || ww < 1 || hh < 1 || x + ww > img.Width || y + hh > img.Height) throw new ArgumentOutOfRangeException(null, "The picture is " + img.Width + " × " + img.Height + ": the area must lie inside it.");
            if (ww * hh > 64 * 64) throw new ArgumentOutOfRangeException(null, "At most 64 × 64 pixels at a time: give x, y, w and h of a part (the picture is " + img.Width + " × " + img.Height + ").");
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Picture {0} of {1} ({2}): colour indexes (hex) of x {3}-{4}, y {5}-{6} (y 0 the top row):\n", index, f.Name, f.Describe(index, img), x, x + ww - 1, y, y + hh - 1);
            var used = new SortedSet<int>();
            for (int r = y; r < y + hh; r++)
            {
                for (int c = x; c < x + ww; c++) { int v = img.Pixels[r * img.Width + c]; used.Add(v); b.Append(v.ToString("X2", Ci)).Append(c + 1 < x + ww ? " " : ""); }
                b.Append('\n');
            }
            b.Append("Colours used (index: #RRGGBB, or transparent):\n");
            foreach (int v in used) b.Append(CtColour(img.Colors, v)).Append('\n');
            b.Append("The picture has ").Append(img.Colors.Length).Append(" colours; graphics_palette_of lists them all.");
            return b.ToString();
        }

        static string CtColour(uint[] colors, int v)
        {
            if (v < 0 || v >= colors.Length) return v.ToString("X2", Ci) + ": outside the colours";
            uint c = colors[v];
            return v.ToString("X2", Ci) + ": " + ((c >> 24) == 0 ? "transparent" : "#" + (c & 0xFFFFFF).ToString("X6", Ci));
        }

        public string GraphicsPaletteOf(string file, int index, int? palette)
        {
            var f = CtPictures(file);
            var img = CtPicture(f, index, palette);
            var b = new StringBuilder();
            b.AppendFormat(Ci, "The {0} colours picture {1} of {2} may use (index: #RRGGBB):\n", img.Colors.Length, index, f.Name);
            for (int i = 0; i < img.Colors.Length; i++) b.Append(CtColour(img.Colors, i)).Append(i % 8 == 7 ? "\n" : "   ");
            return b.ToString();
        }

        /// <summary>A colour: an index into the picture's colours (decimal or 0x hex), or #RRGGBB for the nearest of them.</summary>
        static int CtColourIndex(string colour, uint[] colors)
        {
            string c = (colour ?? "").Trim();
            if (c.StartsWith("#"))
            {
                uint rgb;
                if (c.Length != 7 || !uint.TryParse(c.Substring(1), NumberStyles.HexNumber, Ci, out rgb)) throw new ArgumentException("A colour is an index or #RRGGBB.");
                return PictureImport.Nearest(colors, rgb);
            }
            if (c.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < colors.Length; i++) if ((colors[i] >> 24) == 0) return i;
                throw new ArgumentException("This picture has no transparent colour.");
            }
            int v = CtInt(c, "The colour");
            if (v < 0 || v >= colors.Length) throw new ArgumentOutOfRangeException(null, "The picture has colours 0-" + (colors.Length - 1) + ".");
            return v;
        }

        public string GraphicsDraw(string file, int index, int? palette, string op, string points, string colour, int width, bool filled)
        {
            var f = CtPictures(file);
            var img = CtPicture(f, index, palette, true);
            int c = CtColourIndex(colour, img.Colors);
            if (width < 1 || width > 16) throw new ArgumentOutOfRangeException(null, "The pen is 1-16 pixels wide.");
            var pts = new List<int[]>();
            foreach (var p in (points ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = p.Split(',');
                if (xy.Length != 2) throw new ArgumentException("Points are \"x,y\", separated by ; (\"3,4;10,4\").");
                int px = CtInt(xy[0], "x"), py = CtInt(xy[1], "y");
                if (px < 0 || py < 0 || px >= img.Width || py >= img.Height) throw new ArgumentOutOfRangeException(null, "(" + px + ", " + py + ") is outside the picture's " + img.Width + " × " + img.Height + ".");
                pts.Add(new[] { px, py });
            }
            var doc = new PixelDocument(img);
            doc.Begin();
            string o = (op ?? "").Trim().ToLowerInvariant();
            int changedFill = -1;
            switch (o)
            {
                case "pixel":
                case "pixels":
                    if (pts.Count == 0) throw new ArgumentException("Give the pixels' points.");
                    foreach (var p in pts) doc.Dab(p[0], p[1], width, c);
                    break;
                case "line":
                    if (pts.Count < 2) throw new ArgumentException("A line takes two points or more (a line through them).");
                    for (int i = 1; i < pts.Count; i++) doc.Line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], width, c);
                    break;
                case "rect":
                case "rectangle":
                    if (pts.Count != 2) throw new ArgumentException("A rectangle takes two corners.");
                    doc.Rectangle(pts[0][0], pts[0][1], pts[1][0], pts[1][1], width, c, filled);
                    break;
                case "fill":
                    if (pts.Count != 1) throw new ArgumentException("A fill takes one point: the area of its colour around it is filled.");
                    changedFill = doc.Fill(pts[0][0], pts[0][1], c);
                    break;
                case "clear":
                    doc.Rectangle(0, 0, img.Width - 1, img.Height - 1, 1, c, true);
                    break;
                default: throw new ArgumentException("op is pixel, line, rect (filled or not), fill or clear.");
            }
            doc.End();
            int n = 0;
            for (int i = 0; i < doc.Pixels.Length; i++) if (doc.Pixels[i] != img.Pixels[i]) n++;
            if (n == 0) return "Nothing changed: those pixels have that colour already.";
            f.Replace(index, img, doc.Pixels);
            return string.Format(Ci, "Picture {0} of {1}: {2} pixel{3} set to {4}. Not written until save; graphics_revert takes the picture back.", index, f.Name, n, n == 1 ? "" : "s", CtColour(img.Colors, c));
        }

        public string GraphicsRevert(string file, int? index)
        {
            var f = CtPictures(file);
            if (index.HasValue)
            {
                if (!f.IsChanged(index.Value)) return "Picture " + index + " of " + f.Name + " has no unsaved change.";
                f.Revert(index.Value);
                return "Picture " + index + " of " + f.Name + " is back as saved.";
            }
            var l = Enumerable.Range(0, f.Count).Where(f.IsChanged).ToList();
            foreach (var i in l) f.Revert(i);
            return l.Count == 0 ? f.Name + " has no unsaved change." : "Pictures " + string.Join(", ", l) + " of " + f.Name + " are back as saved.";
        }

        // ---- palettes

        public string PaletteGet(int palette, int? aux)
        {
            CtFresh();
            var pals = Session.Pictures.Palettes;
            var b = new StringBuilder();
            if (aux.HasValue)
            {
                if (aux < 0 || aux > 31) throw new ArgumentOutOfRangeException(null, "Auxiliary palettes are 0-31 (ALLPALS.DAT).");
                var main = pals.Get(palette);
                b.AppendFormat(Ci, "Auxiliary palette {0} (ALLPALS.DAT): 16 indexes into palette {1}; 0 is transparent in sprites.\n", aux, palette);
                for (int k = 0; k < 16; k++) b.AppendFormat(Ci, "{0:X}: {1:X2} #{2:X6}\n", k, pals.Aux[aux.Value][k], main[pals.Aux[aux.Value][k] & 255] & 0xFFFFFF);
                return b.ToString();
            }
            if (palette < 0 || palette >= pals.Main.Count) throw new ArgumentOutOfRangeException(null, "Palettes are 0-" + (pals.Main.Count - 1) + " (PALS.DAT).");
            var p = pals.Get(palette);
            b.AppendFormat(Ci, "Palette {0} of {1} (PALS.DAT), index: #RRGGBB, 8 a line:\n", palette, pals.Main.Count);
            for (int i = 0; i < 256; i++) b.AppendFormat(Ci, "{0:X2} #{1:X6}{2}", i, p[i] & 0xFFFFFF, i % 8 == 7 ? "\n" : "  ");
            b.Append("Colours the game cycles (animated):\n");
            foreach (var c in PaletteCycles.InGame(Session.IsUw2)) b.AppendFormat(Ci, "  {0}, {1}, every {2:0.###} s: {3}\n", c.Range, c.Up ? "up" : "down", c.Period, c.Where);
            return b.ToString();
        }

        public byte[] PalettePng(int palette, out string said)
        {
            CtFresh();
            var pals = Session.Pictures.Palettes;
            if (palette < 0 || palette >= pals.Main.Count) throw new ArgumentOutOfRangeException(null, "Palettes are 0-" + (pals.Main.Count - 1) + " (PALS.DAT).");
            var p = pals.Get(palette);
            var img = new IndexedImage(16, 16, Enumerable.Range(0, 256).ToArray(), p);
            said = "Palette " + palette + ": index 0 top left, 16 a row.";
            return CtPng(img, 16);
        }

        // ---- critters (CRIT)

        List<CritterFile> CtCritterFiles()
        {
            CtFresh();
            if (ctCritters == null)
            {
                ctCritters = CritterArt.Load(Session.GameFolder, Session.Kind, Session.Names);
                foreach (var c in ctCritters) foreach (var p in c.Pages) ctCritterDisk[p.Path] = CtRead(p.Path);
            }
            if (ctCritters.Count == 0) throw new McpEditorException("There is no critter art (a CRIT folder) in " + Session.GameFolder + ".");
            return ctCritters;
        }

        CritterFile CtCritter(int number)
        {
            var c = CtCritterFiles().FirstOrDefault(x => x.Number == number);
            if (c == null) throw new McpEditorException("There is no critter file " + number + " (critter_list lists them).");
            return c;
        }

        public string CritterList()
        {
            var b = new StringBuilder("The critters' art (CRIT), file: name, pages and frames, animations, used by (creature class: auxiliary palette):\n");
            foreach (var c in CtCritterFiles())
                b.AppendFormat(Ci, "{0}: {1}; pages {2}; {3} animations; used by {4}{5}\n", c.Number, c.Name,
                    string.Join(", ", c.Pages.Select(p => p.PageNumber + " (" + p.Frames.Count + " frames" + (p.HasChanges ? ", changed" : "") + ")")), c.Animations.Count,
                    c.Users.Count == 0 ? "none" : string.Join(", ", c.Users.Select(u => string.Format(Ci, "0x{0:X2} {1}: {2}", 0x40 + u.Item1, Session.Names.Get(0x40 + u.Item1), u.Item2))), "");
            return b.ToString();
        }

        public string CritterAnimations(int number)
        {
            var c = CtCritter(number);
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Critter file {0} ({1}): animations, each as its frames (page:frame):\n", c.Number, c.Name);
            for (int i = 0; i < c.Animations.Count; i++)
                b.AppendFormat(Ci, "{0}. {1}: {2}\n", i, c.Animations[i].Name, string.Join(" ", c.Animations[i].Frames.Select(f => f.Item1.PageNumber + ":" + f.Item2)));
            return b.ToString();
        }

        int CtCritterPalette(CritterFile c, int? palette)
        {
            if (palette.HasValue) return palette.Value;
            return c.Users.Count > 0 ? c.Users[0].Item2 : 0;
        }

        CritterPage CtCritterPage(CritterFile c, int page, int frame)
        {
            var p = c.Pages.FirstOrDefault(x => x.PageNumber == page);
            if (p == null) throw new McpEditorException("Critter file " + c.Number + " has pages " + string.Join(", ", c.Pages.Select(x => x.PageNumber)) + ".");
            if (!p.Frames.ContainsKey(frame)) throw new McpEditorException("Page " + page + " of critter file " + c.Number + " has frames " + string.Join(", ", p.Frames.Keys) + ".");
            return p;
        }

        public byte[] CritterImagePng(int number, int page, int frame, int? palette, int? scale, string folder, out string said)
        {
            var c = CtCritter(number);
            var p = CtCritterPage(c, page, frame);
            int pal = CtCritterPalette(c, palette);
            var img = p.Image(frame, pal, Session.Pictures.Palettes);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                string path = Path.Combine(CtFolder(folder), string.Format(Ci, "critter{0:D2}-page{1}-frame{2:D3}.png", number, page, frame));
                File.WriteAllBytes(path, img.ToPng());
                said = "Written: " + path + string.Format(Ci, " ({0} × {1}, auxiliary palette {2}).", img.Width, img.Height, pal);
                return null;
            }
            said = string.Format(Ci, "Critter file {0} ({1}), page {2}, frame {3}: {4} × {5}, hot spot ({6}, {7}), auxiliary palette {8}{9}.", number, c.Name, page, frame, img.Width, img.Height, img.HotX, img.HotY, pal, p.IsChanged(frame) ? ", changed, not saved" : "");
            return CtPng(img, CtAutoScale(img.Width, img.Height, scale));
        }

        public string CritterImport(int number, int page, int frame, string png, bool nearest, int? palette)
        {
            var c = CtCritter(number);
            var p = CtCritterPage(c, page, frame);
            var like = p.Image(frame, CtCritterPalette(c, palette), Session.Pictures.Palettes);
            var r = PictureImport.FromPng(CtInput(png, "PNG"), like, nearest);
            if (!r.Ok) throw new McpEditorException("Not imported: " + r.Summary.Replace("\n", " "));
            p.Replace(frame, r.Pixels);
            return string.Format(Ci, "Frame {0} of critter file {1}, page {2}, replaced. Not written until save.{3}", frame, number, page, r.Notes.Count > 0 ? " " + string.Join(" ", r.Notes) : "");
        }

        // ---- cutscenes (CUTS)

        List<Cutscene> CtCuts()
        {
            CtFresh();
            if (ctCutscenes == null) ctCutscenes = Cutscene.Find(Session.GameFolder);
            if (ctCutscenes.Count == 0) throw new McpEditorException("There are no cutscenes (a CUTS folder) in " + Session.GameFolder + ".");
            return ctCutscenes;
        }

        LpfAnimation CtLpf(string file)
        {
            var cuts = CtCuts();
            string name = Path.GetFileName((file ?? "").Trim());
            string path = cuts.SelectMany(c => c.Animations).FirstOrDefault(a => Path.GetFileName(a).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (path == null) throw new McpEditorException("No cutscene animation " + name + " (cutscene_list lists them, e.g. CS000.N01).");
            LpfAnimation a;
            if (!ctLpf.TryGetValue(path, out a))
            {
                a = LpfAnimation.Load(path);
                ctLpf[path] = a;
                ctLpfDisk[path] = CtRead(path);
            }
            return a;
        }

        public string CutsceneList()
        {
            var b = new StringBuilder("The cutscenes (CUTS); cutscene_frame shows a frame of an animation:\n");
            foreach (var c in CtCuts())
            {
                b.Append(c.Label).Append(": script ").Append(c.Script == null ? "none" : Path.GetFileName(c.Script));
                foreach (var a in c.Animations)
                {
                    try { var l = CtLpf(Path.GetFileName(a)); b.AppendFormat(Ci, "; {0} ({1} frames, {2} × {3}{4})", Path.GetFileName(a), l.FrameCount, l.Width, l.Height, l.Changed ? ", changed" : ""); }
                    catch (Exception e) when (e is IOException || e is InvalidDataException) { b.Append("; ").Append(Path.GetFileName(a)).Append(" (cannot be read)"); }
                }
                b.Append('\n');
            }
            return b.ToString();
        }

        public byte[] CutsceneFramePng(string file, int frame, int? scale, string folder, out string said)
        {
            var a = CtLpf(file);
            if (frame < 0 || frame >= a.FrameCount) throw new ArgumentOutOfRangeException(null, a.Name + " has frames 0-" + (a.FrameCount - 1) + ".");
            var img = a.Frame(frame);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                string path = Path.Combine(CtFolder(folder), string.Format(Ci, "{0}-frame{1:D3}.png", a.Name.Replace('.', '-'), frame));
                File.WriteAllBytes(path, img.ToPng());
                said = "Written: " + path + ".";
                return null;
            }
            said = string.Format(Ci, "{0}, frame {1} of {2} ({3} × {4}).", a.Name, frame, a.FrameCount, img.Width, img.Height);
            return CtPng(img, scale ?? 1);
        }

        public string CutsceneImport(string file, int frame, string png, bool nearest)
        {
            var a = CtLpf(file);
            if (frame < 0 || frame >= a.FrameCount) throw new ArgumentOutOfRangeException(null, a.Name + " has frames 0-" + (a.FrameCount - 1) + ".");
            var like = a.Frame(frame);
            var r = PictureImport.FromPng(CtInput(png, "PNG"), like, nearest);
            if (!r.Ok) throw new McpEditorException("Not imported: " + r.Summary.Replace("\n", " "));
            a.Replace(frame, r.Pixels);
            return "Frame " + frame + " of " + a.Name + " replaced. Not written until save." + (r.Notes.Count > 0 ? " " + string.Join(" ", r.Notes) : "");
        }

        // ---- fonts (DATA\*.SYS)

        string CtFontPath(string name) { return GameProfile.Find(Session.DataFolder, name); }

        UwFont CtFont(string name)
        {
            CtFresh();
            string n = (name ?? "").Trim().ToUpperInvariant();
            string hit = FontFiles.Names.FirstOrDefault(x => x == n || Path.GetFileNameWithoutExtension(x) == n);
            if (hit == null) throw new McpEditorException("The fonts are " + string.Join(", ", FontFiles.Names) + ".");
            UwFont f;
            if (ctFonts.TryGetValue(hit, out f))
            {
                if (ctFontChanged.Contains(hit) || !CtDiffers(CtFontPath(hit), ctFontDisk[hit])) return f;
            }
            string p = CtFontPath(hit);
            if (p == null) throw new McpEditorException("DATA has no " + hit + ".");
            f = UwFont.Load(p);
            f.Name = hit;
            ctFonts[hit] = f;
            ctFontDisk[hit] = CtRead(p);
            return f;
        }

        public string FontList()
        {
            CtFresh();
            var b = new StringBuilder("The fonts of DATA:\n");
            foreach (var n in FontFiles.Names)
            {
                if (CtFontPath(n) == null) { b.Append(n).Append(": not there\n"); continue; }
                try
                {
                    var f = CtFont(n);
                    b.AppendFormat(Ci, "{0}: {1}; {2} pixels high, up to {3} wide, space {4}, {5} codes{6}\n", n, FontFiles.Use(n), f.Height, f.Columns, f.SpaceWidth, f.Count, ctFontChanged.Contains(n) ? " (changed, not saved)" : "");
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException) { b.Append(n).Append(": cannot be read (").Append(e.Message).Append(")\n"); }
            }
            return b.ToString();
        }

        /// <summary>The font's sheet (16 codes a row, as Export sheet writes it), or a line of text drawn as the game draws it.</summary>
        public byte[] FontPng(string name, string text, int? scale, string folder, out string said)
        {
            var f = CtFont(name);
            if (string.IsNullOrEmpty(text))
            {
                var png = FontSheet.Export(f);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    string p = Path.Combine(CtFolder(folder), Path.GetFileNameWithoutExtension(f.Name) + ".png");
                    File.WriteAllBytes(p, png);
                    said = "Written: " + p + " (the sheet font_import takes back: black pixels drawn, the row below each glyph its width).";
                    return null;
                }
                said = f.Name + ": codes 0-127, 16 a row (black drawn, the line under a glyph its width).";
                var d = Png.Decode(png);
                return CtScaled(d.Width, d.Height, d.Rgba, scale ?? 2);
            }
            string game;
            try { game = (Session.Accents ?? new AccentTable()).ToGame(text); }
            catch (FormatException e) { throw new ArgumentException(e.Message); }
            var codes = game.Select(ch => (int)ch & 0x7F).ToList();
            int w;
            var px = f.Render(codes, out w);
            int W = Math.Max(1, w) + 2, H = f.Height + 2;
            var rgba = new byte[W * H * 4];
            for (int k = 0; k < rgba.Length; k += 4) { rgba[k] = 0xE8; rgba[k + 1] = 0xDC; rgba[k + 2] = 0xC0; rgba[k + 3] = 0xFF; }
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < w && x < px.GetLength(0); x++)
                    if (px[x, y]) { int d = ((y + 1) * W + x + 1) * 4; rgba[d] = 0x20; rgba[d + 1] = 0x18; rgba[d + 2] = 0x10; }
            said = string.Format(Ci, "\"{0}\" in {1}: {2} pixels wide as the game measures it ({3} drawn).", text, f.Name, f.StringWidth(codes), w);
            return CtScaled(W, H, rgba, scale ?? 4);
        }

        public string FontImport(string name, string png)
        {
            var f = CtFont(name);
            var warnings = new List<string>();
            UwFont n;
            try { n = FontSheet.Import(f, CtInput(png, "PNG"), warnings); }
            catch (InvalidDataException e) { throw new McpEditorException("Not imported: " + e.Message); }
            n.Name = f.Name;
            ctFonts[f.Name] = n;
            ctFontChanged.Add(f.Name);
            return f.Name + " replaced from the sheet. Not written until save." + (warnings.Count > 0 ? " " + string.Join(" ", warnings) : "");
        }

        public string FontGlyph(string name, int code, string set, int? width)
        {
            var f = CtFont(name);
            if (code < 0 || code >= UwFont.Codes) throw new ArgumentOutOfRangeException(null, "Codes are 0-127.");
            int changed = 0;
            foreach (var part in (set ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Split(',');
                if (t.Length != 3) throw new ArgumentException("Pixels are \"x,y,1\" (drawn) or \"x,y,0\" (clear), separated by ;.");
                int x = CtInt(t[0], "x"), y = CtInt(t[1], "y"), on = CtInt(t[2], "on");
                if (x < 0 || y < 0 || x >= f.Columns || y >= f.Height) throw new ArgumentOutOfRangeException(null, f.Name + "'s glyphs are " + f.Columns + " × " + f.Height + ".");
                f.SetPixel(code, x, y, on != 0);
                changed++;
            }
            if (width.HasValue)
            {
                if (width < 0 || width > f.Columns) throw new ArgumentOutOfRangeException(null, "A glyph is 0-" + f.Columns + " pixels wide.");
                f.Glyphs[code].Width = width.Value;
                if (!f.Glyphs[code].Present) f.SetPixel(code, 0, 0, f.Pixel(code, 0, 0));
                changed++;
            }
            if (changed > 0) ctFontChanged.Add(f.Name);
            var b = new StringBuilder();
            b.AppendFormat(Ci, "{0} code {1} (0x{1:X2}{2}), width {3}{4}:\n", f.Name, code, code >= 32 && code < 127 ? " '" + (char)code + "'" : "", f.Glyphs[code].Width, changed > 0 ? " (changed, not saved)" : "");
            for (int y = 0; y < f.Height; y++)
            {
                for (int x = 0; x < f.Columns; x++) b.Append(f.Pixel(code, x, y) ? '#' : '.');
                b.Append('\n');
            }
            return b.ToString();
        }
    }
}
