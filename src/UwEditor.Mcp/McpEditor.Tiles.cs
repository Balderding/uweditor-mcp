using System;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>Tiles: the level as text, one tile's fields, and changing them.</summary>
    public sealed partial class McpEditor
    {
        /// <summary>The tile fields tile_set may change (the schema's, but the tile's object list).</summary>
        public static readonly string[] TileFields = { "Type", "Floor height", "Light", "Floor texture", "No magic", "Door", "Wall texture" };

        /// <summary>One map character a tile type: solid, open, diagonals open SE SW NE NW, slopes rising N S E W.</summary>
        public const string TypeChars = "#.1234nsew";
        const string HeightChars = "0123456789abcdef";

        static void CheckTile(int x, int y)
        {
            if (x < 0 || x > 63) throw new ArgumentOutOfRangeException(null, "Tiles are 0-63 (x " + x + ").");
            if (y < 0 || y > 63) throw new ArgumentOutOfRangeException(null, "Tiles are 0-63 (y " + y + ").");
        }

        /// <summary>The level as two 64x64 grids, north (y 63) at the top: tile types, then floor heights.</summary>
        public string LevelMap(int level)
        {
            var m = ReadModel(level);
            var b = new StringBuilder();
            b.Append("Level ").Append(level).Append(" types (").Append(TypeChars[0]).Append(" solid, . open, 1-4 diagonal open SE SW NE NW, n s e w slope rising N S E W); top row y 63, left x 0\n");
            for (int y = 63; y >= 0; y--)
            {
                for (int x = 0; x < 64; x++) { int t = (int)m.Tiles[x, y].Type; b.Append(t < TypeChars.Length ? TypeChars[t] : '?'); }
                b.Append('\n');
            }
            b.Append("\nFloor heights 0-f (# solid); same layout\n");
            for (int y = 63; y >= 0; y--)
            {
                for (int x = 0; x < 64; x++) { var t = m.Tiles[x, y]; b.Append(t.IsSolid ? '#' : HeightChars[t.Height]); }
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>One tile's fields, its type's name, its textures' numbers and its objects.</summary>
        public string TileGet(int level, int x, int y)
        {
            var m = ReadModel(level);
            CheckTile(x, y);
            var t = m.Tiles[x, y];
            var b = new StringBuilder();
            b.Append("Tile (").Append(x).Append(", ").Append(y).Append(") of level ").Append(level).Append(": ").Append(TileInfo.TypeName(t.Type)).Append('\n');
            foreach (var f in TileFields)
            {
                int v = Field(t, f);
                b.Append(f).Append(": ").Append(v);
                if (f == "Floor texture" && v < m.FloorMap.Length) b.Append(" (floor texture ").Append(m.FloorMap[v]).Append(')');
                if (f == "Wall texture" && v < m.WallMap.Length) b.Append(" (wall texture ").Append(m.WallMap[v]).Append(')');
                b.Append('\n');
            }
            b.Append("Objects: ").Append(m.TileObjects(x, y).Count).Append('\n');
            return b.ToString();
        }

        static int Field(TileInfo t, string f)
        {
            switch (f)
            {
                case "Type": return (int)t.Type;
                case "Floor height": return t.Height;
                case "Light": return t.Light;
                case "Floor texture": return t.FloorIndex;
                case "No magic": return t.NoMagic ? 1 : 0;
                case "Door": return t.DoorBit ? 1 : 0;
                default: return t.WallIndex;
            }
        }

        /// <summary>Sets one field of a tile. A floor height carries the objects on the tile with it.</summary>
        public string TileSet(int level, int x, int y, string field, int value)
        {
            var e = Editable(level);
            CheckTile(x, y);
            string f = TileFields.FirstOrDefault(n => string.Equals(n, field, StringComparison.OrdinalIgnoreCase));
            if (f == null) throw new ArgumentException("Tile fields are " + string.Join(", ", TileFields) + ".", "field");
            SetField(e, x, y, f, value);
            return TileGet(level, x, y);
        }

        static void SetField(LevelEditor e, int x, int y, string f, int value)
        {
            if (f == "Floor height")
            {
                if (value < 0 || value > 15) throw new ArgumentOutOfRangeException(null, "Floor height is 0-15.");
                e.RaiseFloor(x, y, value - e.TileField(x, y, f));
            }
            else e.SetTileField(x, y, f, value);
        }
    }
}
