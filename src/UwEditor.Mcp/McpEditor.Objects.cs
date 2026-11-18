using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>Items by name and the objects of a level: list, add, move, delete.</summary>
    public sealed partial class McpEditor
    {
        static string Hex(int item) { return "0x" + item.ToString("X3"); }

        /// <summary>The items whose number or name matches (a number: 0x1A2 or 418; else a part of the name).</summary>
        List<int> Matches(string text)
        {
            Require();
            var names = Session.Names;
            string t = (text ?? "").Trim();
            if (t.Length == 0) throw new ArgumentException("Give an item's name or number.");
            int n;
            if ((t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                || int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            {
                if (n < 0 || n >= names.Count) throw new ArgumentOutOfRangeException(null, "Items are 0-" + Hex(names.Count - 1) + ".");
                return new List<int> { n };
            }
            var all = Enumerable.Range(0, names.Count).Where(i => names.Raw(i) != null).ToList();
            var exact = all.Where(i => string.Equals(names.Get(i), t, StringComparison.OrdinalIgnoreCase)).ToList();
            return exact.Count > 0 ? exact : all.Where(i => names.Get(i).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// <summary>One item from a name or number; none or several matching is refused, naming them.</summary>
        int ItemOf(string text)
        {
            var m = Matches(text);
            if (m.Count == 1) return m[0];
            if (m.Count == 0) throw new ArgumentException("No item is called " + text + ".");
            throw new ArgumentException(text + " is " + m.Count + " items; give the number: " + string.Join(", ", m.Take(20).Select(i => Hex(i) + " " + Session.Names.Get(i))) + (m.Count > 20 ? ", ..." : "") + ".");
        }

        /// <summary>The items matching a name or number, one a line (at most 40).</summary>
        public string ItemFind(string text)
        {
            var m = Matches(text);
            if (m.Count == 0) return "No item matches " + text + ".";
            var b = new StringBuilder();
            foreach (var i in m.Take(40)) b.Append(Hex(i)).Append(' ').Append(Session.Names.Get(i)).Append('\n');
            if (m.Count > 40) b.Append("... and ").Append(m.Count - 40).Append(" more\n");
            return b.ToString();
        }

        static void Line(StringBuilder b, LevelModel m, ObjectInfo o, int depth)
        {
            b.Append(new string(' ', depth * 2)).Append('#').Append(o.Index).Append(' ').Append(Hex(o.Item)).Append(' ').Append(o.Name);
            if (depth == 0) b.Append(" at (").Append(o.TileX).Append(", ").Append(o.TileY).Append(") fine ").Append(o.FineX).Append(',').Append(o.FineY);
            b.Append(" z ").Append(o.Z).Append(" quality ").Append(o.Quality).Append(" owner ").Append(o.Owner);
            b.Append(o.IsQuantity ? " quantity/special " : " link ").Append(o.Link).Append('\n');
            foreach (int c in o.Children) Line(b, m, m.Objects[c], depth + 1);
        }

        /// <summary>The objects on one tile, or on every tile of the level, with what they hold indented.</summary>
        public string ObjectList(int level, int? x, int? y)
        {
            var m = Level(level).Model;
            var b = new StringBuilder();
            IEnumerable<ObjectInfo> on;
            if (x.HasValue || y.HasValue)
            {
                if (!x.HasValue || !y.HasValue) throw new ArgumentException("Give both x and y, or neither for the whole level.");
                CheckTile(x.Value, y.Value);
                on = m.TileObjects(x.Value, y.Value);
            }
            else on = m.Objects.Where(o => o != null && !o.Free && o.OnTile).OrderBy(o => o.TileY).ThenBy(o => o.TileX);
            foreach (var o in on) Line(b, m, o, 0);
            return b.Length == 0 ? "No objects." : b.ToString();
        }

        int Index(LevelEditor e, int index)
        {
            if (index < 2 || index >= LevelBlock.Objects) throw new ArgumentOutOfRangeException(null, "Objects are #2-#" + (LevelBlock.Objects - 1) + ".");
            if (e.Model.Objects[index].Free) throw new ArgumentException("#" + index + " is a free slot, not an object.");
            return index;
        }

        /// <summary>Adds an item on a tile with the editor's defaults for it; answers its index.</summary>
        public string ObjectAdd(int level, int x, int y, string item, int? quality, int? owner, int? z, int? heading, int? fineX, int? fineY)
        {
            var e = Editable(level);
            CheckTile(x, y);
            int it = ItemOf(item);
            var spec = ObjectSpec.Default(it, Session.Kind);
            if (quality.HasValue) spec.Quality = Range(quality.Value, 0, 63, "quality");
            if (owner.HasValue) spec.Owner = Range(owner.Value, 0, 63, "owner");
            if (z.HasValue) spec.Z = Range(z.Value, 0, 127, "z");
            if (heading.HasValue) spec.Heading = Range(heading.Value, 0, 7, "heading");
            if (fineX.HasValue) spec.FineX = Range(fineX.Value, 0, 7, "fine x");
            if (fineY.HasValue) spec.FineY = Range(fineY.Value, 0, 7, "fine y");
            int i = e.AddObject(spec, x, y);
            return "Added #" + i + " " + Hex(it) + " " + Session.Names.Get(it) + " at (" + x + ", " + y + ") of level " + level + ".";
        }

        static int Range(int v, int lo, int hi, string what)
        {
            if (v < lo || v > hi) throw new ArgumentOutOfRangeException(null, "The " + what + " is " + lo + "-" + hi + ".");
            return v;
        }

        /// <summary>Moves an object lying on a tile to another tile (and fine position).</summary>
        public string ObjectMove(int level, int index, int x, int y, int? fineX, int? fineY)
        {
            var e = Editable(level);
            Index(e, index);
            CheckTile(x, y);
            if (fineX.HasValue) Range(fineX.Value, 0, 7, "fine x");
            if (fineY.HasValue) Range(fineY.Value, 0, 7, "fine y");
            e.MoveObject(index, x, y, fineX, fineY);
            return "Moved #" + index + " to (" + x + ", " + y + ").";
        }

        /// <summary>Deletes an object and everything inside it.</summary>
        public string ObjectDelete(int level, int index)
        {
            var e = Editable(level);
            Index(e, index);
            string what = e.Model.Objects[index].Name;
            e.DeleteObject(index);
            return "Deleted #" + index + " " + what + ".";
        }
    }
}
