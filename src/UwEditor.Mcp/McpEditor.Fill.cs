using System;
using System.Collections.Generic;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>fill: a rectangle of tiles set at once, one undo step, all or nothing.</summary>
    public sealed partial class McpEditor
    {
        /// <summary>A tile type from a map character (# . 1-4 n s e w) or "solid" / "open"; null leaves the type.</summary>
        public static int? ParseType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            string t = type.Trim().ToLowerInvariant();
            if (t == "solid") return 0;
            if (t == "open") return 1;
            if (t.Length == 1 && TypeChars.IndexOf(t[0]) >= 0) return TypeChars.IndexOf(t[0]);
            throw new ArgumentException("A type is one of " + TypeChars + ", solid or open.");
        }

        static int TileAt(int x, int y) { return LevelBlock.TilesAt + (y * 64 + x) * 4; }

        /// <summary>
        /// Sets the given fields on every tile of the rectangle (corners in any order). Every value is
        /// checked first; the change is then one edit of the level's bytes (one undo step, one redraw),
        /// raising or lowering the objects on a tile with its floor as LevelEditor.RaiseFloor does.
        /// </summary>
        public string Fill(int level, int x1, int y1, int x2, int y2, string type, int? height, int? floorTexture, int? wallTexture)
        {
            var e = Editable(level);
            CheckTile(x1, y1); CheckTile(x2, y2);
            int? t = ParseType(type);
            var m = e.Model;
            if (height.HasValue && (height < 0 || height > 15)) throw new ArgumentOutOfRangeException(null, "Floor height is 0-15.");
            if (floorTexture.HasValue && (floorTexture < 0 || floorTexture >= m.FloorMap.Length)) throw new ArgumentOutOfRangeException(null, "The level has floor textures 0-" + (m.FloorMap.Length - 1) + ".");
            if (wallTexture.HasValue && (wallTexture < 0 || wallTexture >= m.WallMap.Length)) throw new ArgumentOutOfRangeException(null, "The level has wall textures 0-" + (m.WallMap.Length - 1) + ".");
            if (!t.HasValue && !height.HasValue && !floorTexture.HasValue && !wallTexture.HasValue)
                throw new ArgumentException("Nothing to set: give a type, height, floor texture or wall texture.");
            int xa = Math.Min(x1, x2), xb = Math.Max(x1, x2), ya = Math.Min(y1, y2), yb = Math.Max(y1, y2);

            // the objects lying on each tile whose floor moves, and how far (8 z a step)
            var lift = new List<KeyValuePair<int, int>>();
            if (height.HasValue)
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++)
                    {
                        int d = (height.Value - m.Tiles[x, y].Height) * 8;
                        if (d == 0) continue;
                        foreach (var o in m.TileObjects(x, y))
                            if (!Triggers.HeightIsData(o.Item)) lift.Add(new KeyValuePair<int, int>(o.Index, d));
                    }

            var f = Uw1Schemas.Tile;
            e.EditBytes("Fill (" + xa + ", " + ya + ")-(" + xb + ", " + yb + ")", (data, tex) =>
            {
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++)
                    {
                        int at = TileAt(x, y);
                        if (t.HasValue) f["Type"].Set(data, t.Value, at);
                        if (height.HasValue) f["Floor height"].Set(data, height.Value, at);
                        if (floorTexture.HasValue) f["Floor texture"].Set(data, floorTexture.Value, at);
                        if (wallTexture.HasValue) f["Wall texture"].Set(data, wallTexture.Value, at);
                    }
                foreach (var kv in lift)
                {
                    int at = LevelBlock.ObjectOffset(kv.Key) + 2;
                    int w = data[at] | data[at + 1] << 8;
                    int z = Math.Max(0, Math.Min(127, (w & 0x7F) + kv.Value));
                    w = (w & ~0x7F) | z;
                    data[at] = (byte)w; data[at + 1] = (byte)(w >> 8);
                }
            });
            int n = (xb - xa + 1) * (yb - ya + 1);
            return "Set " + n + " tiles, (" + xa + ", " + ya + ") to (" + xb + ", " + yb + ") of level " + level + ".";
        }
    }
}
