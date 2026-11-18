using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Graphics (Tools ▸ Graphics, the pixel editor), palettes, critters, cutscenes and fonts.</summary>
    public sealed partial class EditorTools
    {
        /// <summary>Makes a PNG and says what it is (said); null with said alone when it wrote a file instead.</summary>
        public delegate byte[] PngMaker(out string said);

        /// <summary>A tool's answer with a picture: its text, then the PNG (when one was made).</summary>
        CallToolResult CtImage(PngMaker make, [System.Runtime.CompilerServices.CallerMemberName] string method = null)
        {
            byte[] png = null;
            var r = Answer(() => { string said; png = make(out said); return said; }, method);
            if (png == null || r.IsError == true) return r;
            var content = new List<ContentBlock>(r.Content) { ImageContentBlock.FromBytes(png, "image/png") };
            return new CallToolResult { Content = content };
        }

        const string FolderHelp = "Write the picture into this folder as an indexed PNG at its own size (the import tools take it back), instead of showing it.";

        [McpServerTool(Name = "graphics_files"), Description("DATA's picture files (.GR pictures, .TR textures, .BYT and BYT.ARK screens) with how many pictures each holds and how many are changed, not saved.")]
        public CallToolResult GraphicsFiles() { return Answer(() => ed.GraphicsFiles()); }

        [McpServerTool(Name = "graphics_list"), Description("A picture file's pictures: index, size and kind (8-bit, 4-bit with an auxiliary palette, texture, screen), empty ones, and the item each is for in OBJECTS.GR.")]
        public CallToolResult GraphicsList([Description("The file, e.g. OBJECTS.GR, W64.TR, OPSCR.BYT.")] string file,
            [Description("The first index (default 0).")] int from = 0, [Description("How many, 1-1000 (default 500).")] int count = 500)
        {
            return Answer(() => ed.GraphicsList(file, from, count));
        }

        [McpServerTool(Name = "graphics_image"), Description("One picture of a file as a PNG (scaled up to see, by whole pixels), unsaved changes included; or written into a folder.")]
        public CallToolResult GraphicsImage([Description("The file.")] string file, [Description("The picture's index.")] int index,
            [Description("The palette (PALS.DAT), default the one the game uses.")] int? palette = null,
            [Description("Scale 1-16 (default about 256 pixels wide).")] int? scale = null,
            [Description(FolderHelp)] string folder = null)
        {
            return CtImage((out string said) => ed.GraphicsImagePng(file, index, palette, scale, folder, out said));
        }

        [McpServerTool(Name = "graphics_sheet"), Description("Many pictures of a file side by side in one PNG (left to right, then down), to find one.")]
        public CallToolResult GraphicsSheet([Description("The file.")] string file, [Description("The first index (default 0).")] int from = 0,
            [Description("How many, 1-512 (default 64).")] int count = 64, [Description("The palette, default the game's.")] int? palette = null,
            [Description("Scale 1-8 (default 2).")] int scale = 2)
        {
            return CtImage((out string said) => ed.GraphicsSheetPng(file, from, count, palette, scale, out said));
        }

        [McpServerTool(Name = "graphics_import"), Description("Replaces a picture from a PNG of the same size whose colours are the picture's own (as graphics_image's folder writes it); nearest maps other colours to the nearest. An empty OBJECTS.GR slot takes the shape of its neighbour. Not written until save.")]
        public CallToolResult GraphicsImport([Description("The file.")] string file, [Description("The picture's index.")] int index,
            [Description("The PNG file.")] string png, [Description("Map each colour to the nearest of the picture's.")] bool nearest = false,
            [Description("The palette the PNG's colours are of (default the game's).")] int? palette = null)
        {
            return Answer(() => ed.GraphicsImport(file, index, png, nearest, palette));
        }

        [McpServerTool(Name = "graphics_pixels"), Description("A picture's pixels as colour indexes (hex), row by row from the top, with the colours used; at most 64 × 64 at a time.")]
        public CallToolResult GraphicsPixels([Description("The file.")] string file, [Description("The picture's index.")] int index,
            [Description("The palette, default the game's.")] int? palette = null,
            [Description("Left edge (default 0).")] int x = 0, [Description("Top edge (default 0).")] int y = 0,
            [Description("Width (default to the right edge).")] int? w = null, [Description("Height (default to the bottom).")] int? h = null)
        {
            return Answer(() => ed.GraphicsPixels(file, index, palette, x, y, w, h));
        }

        [McpServerTool(Name = "graphics_palette_of"), Description("The colours a picture may use (its palette, or for a 4-bit picture its auxiliary palette's 16), index: #RRGGBB.")]
        public CallToolResult GraphicsPaletteOf([Description("The file.")] string file, [Description("The picture's index.")] int index, [Description("The palette, default the game's.")] int? palette = null)
        {
            return Answer(() => ed.GraphicsPaletteOf(file, index, palette));
        }

        [McpServerTool(Name = "graphics_draw"), Description("Draws on a picture as the pixel editor does: op pixel (dabs at the points), line (through the points), rect (two corners, filled or not), fill (the area of one colour around the point) or clear (the whole picture). Points are \"x,y;x,y\" with y 0 the top row. Not written until save; graphics_revert takes it back.")]
        public CallToolResult GraphicsDraw([Description("The file.")] string file, [Description("The picture's index.")] int index,
            [Description("pixel, line, rect, fill or clear.")] string op,
            [Description("The points, \"x,y;x,y\".")] string points = null,
            [Description("The colour: an index of the picture's colours (decimal or 0x), #RRGGBB for the nearest, or transparent.")] string colour = "0",
            [Description("The pen's width, 1-16 (default 1).")] int width = 1,
            [Description("For rect: filled.")] bool filled = false,
            [Description("The palette, default the game's.")] int? palette = null)
        {
            return Answer(() => ed.GraphicsDraw(file, index, palette, op, points, colour, width, filled));
        }

        [McpServerTool(Name = "graphics_revert"), Description("Drops the unsaved changes of one picture, or of every picture of a file.")]
        public CallToolResult GraphicsRevert([Description("The file.")] string file, [Description("The picture's index; leave out for all of the file.")] int? index = null)
        {
            return Answer(() => ed.GraphicsRevert(file, index));
        }

        [McpServerTool(Name = "palette_get"), Description("A palette of PALS.DAT as text (256 colours, index: #RRGGBB) with the colour ranges the game cycles; or an auxiliary palette of ALLPALS.DAT (16 indexes); or with image the 256 colours as a picture.")]
        public CallToolResult PaletteGet([Description("The palette (default 0).")] int palette = 0,
            [Description("An auxiliary palette 0-31 instead.")] int? aux = null, [Description("Show the palette as a picture.")] bool image = false)
        {
            if (image && aux == null) return CtImage((out string said) => ed.PalettePng(palette, out said));
            return Answer(() => ed.PaletteGet(palette, aux));
        }

        [McpServerTool(Name = "critter_list"), Description("The critters' art files (CRIT): name, pages and frame counts, animations, and the creature kinds drawn with each (with their auxiliary palette).")]
        public CallToolResult CritterList() { return Answer(() => ed.CritterList()); }

        [McpServerTool(Name = "critter_animations"), Description("A critter file's animations, each as its frames (page:frame).")]
        public CallToolResult CritterAnimations([Description("The critter file's number.")] int file) { return Answer(() => ed.CritterAnimations(file)); }

        [McpServerTool(Name = "critter_image"), Description("One frame of a critter as a PNG (or written into a folder).")]
        public CallToolResult CritterImage([Description("The critter file's number.")] int file, [Description("The page.")] int page, [Description("The frame.")] int frame,
            [Description("The auxiliary palette (default the first creature kind's).")] int? palette = null,
            [Description("Scale 1-16 (default about 256 wide).")] int? scale = null, [Description(FolderHelp)] string folder = null)
        {
            return CtImage((out string said) => ed.CritterImagePng(file, page, frame, palette, scale, folder, out said));
        }

        [McpServerTool(Name = "critter_import"), Description("Replaces a critter frame from a PNG of its size in its colours (critter_image's folder writes one). Refused when the page would pass the 0x7FFF bytes the game reads. Not written until save.")]
        public CallToolResult CritterImport([Description("The critter file's number.")] int file, [Description("The page.")] int page, [Description("The frame.")] int frame,
            [Description("The PNG file.")] string png, [Description("Map colours to the nearest.")] bool nearest = false,
            [Description("The auxiliary palette of the PNG's colours (default the first creature kind's).")] int? palette = null)
        {
            return Answer(() => ed.CritterImport(file, page, frame, png, nearest, palette));
        }

        [McpServerTool(Name = "cutscene_list"), Description("The cutscenes (CUTS): each one's script and animation files (CSnnn.Nxx) with their frame counts and size.")]
        public CallToolResult CutsceneList() { return Answer(() => ed.CutsceneList()); }

        [McpServerTool(Name = "cutscene_frame"), Description("One frame of a cutscene animation as a PNG (or written into a folder).")]
        public CallToolResult CutsceneFrame([Description("The animation file, e.g. CS000.N01.")] string file, [Description("The frame, from 0.")] int frame,
            [Description("Scale 1-16 (default 1).")] int? scale = null, [Description(FolderHelp)] string folder = null)
        {
            return CtImage((out string said) => ed.CutsceneFramePng(file, frame, scale, folder, out said));
        }

        [McpServerTool(Name = "cutscene_import"), Description("Replaces a frame of a cutscene animation from a PNG of its size in its palette's colours. Not written until save.")]
        public CallToolResult CutsceneImport([Description("The animation file.")] string file, [Description("The frame.")] int frame,
            [Description("The PNG file.")] string png, [Description("Map colours to the nearest.")] bool nearest = false)
        {
            return Answer(() => ed.CutsceneImport(file, frame, png, nearest));
        }

        [McpServerTool(Name = "font_list"), Description("The game's fonts (DATA\\*.SYS): what each is for, its size and codes.")]
        public CallToolResult FontList() { return Answer(() => ed.FontList()); }

        [McpServerTool(Name = "font_image"), Description("A font's sheet (codes 0-127, 16 a row; the line under each glyph its width) as a PNG, or written into a folder for font_import; or with text, that line drawn as the game draws it.")]
        public CallToolResult FontImage([Description("The font, e.g. FONT5X6P.SYS.")] string font, [Description("A line to draw instead of the sheet.")] string text = null,
            [Description("Scale 1-16.")] int? scale = null, [Description("Write the sheet into this folder instead.")] string folder = null)
        {
            return CtImage((out string said) => ed.FontPng(font, text, scale, folder, out said));
        }

        [McpServerTool(Name = "font_import"), Description("Replaces a font from its sheet PNG (as font_image's folder writes it, changed). Not written until save.")]
        public CallToolResult FontImport([Description("The font.")] string font, [Description("The sheet PNG.")] string png) { return Answer(() => ed.FontImport(font, png)); }

        [McpServerTool(Name = "font_glyph"), Description("One glyph of a font as text (# drawn, . clear); set changes its pixels (\"x,y,1;x,y,0\") and width its width. Not written until save.")]
        public CallToolResult FontGlyph([Description("The font.")] string font, [Description("The code, 0-127 (a letter's code is its ASCII).")] int code,
            [Description("Pixels to set, \"x,y,1\" drawn or \"x,y,0\" clear, separated by ;.")] string set = null,
            [Description("The glyph's new width.")] int? width = null)
        {
            return Answer(() => ed.FontGlyph(font, code, set, width));
        }
    }
}
