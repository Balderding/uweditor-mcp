using System;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.Mcp
{
    /// <summary>picture: the editor's 2D map of a level (MapPicture), whole or a rectangle of tiles, as a PNG.</summary>
    public sealed partial class McpEditor
    {
        public byte[] PicturePng(int level, int? x1, int? y1, int? x2, int? y2, int cell = 8)
        {
            var m = ReadModel(level);
            if (cell < 4 || cell > 32) throw new ArgumentOutOfRangeException(null, "A tile is 4-32 pixels.");
            bool any = x1.HasValue || y1.HasValue || x2.HasValue || y2.HasValue;
            if (any && !(x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue))
                throw new ArgumentException("Give both corners of the rectangle, or none for the whole level.");
            int xa = 0, xb = 63, ya = 0, yb = 63;
            if (any)
            {
                CheckTile(x1.Value, y1.Value); CheckTile(x2.Value, y2.Value);
                xa = Math.Min(x1.Value, x2.Value); xb = Math.Max(x1.Value, x2.Value);
                ya = Math.Min(y1.Value, y2.Value); yb = Math.Max(y1.Value, y2.Value);
            }
            var img = MapPicture.Render(m, cell);
            // picture rows go down, the level's y goes north: tile row y is picture rows (63 - y) * cell
            int left = xa * cell, top = (63 - yb) * cell, w = (xb - xa + 1) * cell, h = (yb - ya + 1) * cell;
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
                Array.Copy(img.Pixels, ((top + r) * img.Width + left) * 4, px, r * w * 4, w * 4);
            return Png.EncodeRgba(w, h, px);
        }
    }
}
