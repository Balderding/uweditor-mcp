using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>A saved prefab: its file, name, game and size.</summary>
    public sealed class PrefabInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public GameKind Kind { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Tiles { get; set; }
        public int Objects { get; set; }
        public string Label { get { return string.Format("{0}   ({1} × {2}, {3} tile{4}{5})", Name, Width, Height, Tiles, Tiles == 1 ? "" : "s", Objects > 0 ? ", " + Objects + " object" + (Objects == 1 ? "" : "s") : ""); } }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Prefabs (stamps): a tile clip (TileClip) saved under a name as a small JSON file, to be
    /// placed again on any level, in any session. A prefab holds what the clip holds: the tiles
    /// with their texture numbers, the objects with what they hold, their links among each
    /// other and their triggers' target squares. The files live in a "prefabs" folder under the
    /// editor's settings, one folder a game ("uw1", "uw2"), and can be handed to someone else.
    /// </summary>
    public static class Prefabs
    {
        public const string Format = "uw-prefab";
        public const string Extension = ".uwprefab.json";

        static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", ""); }

        static byte[] Unhex(string s)
        {
            s = s ?? "";
            if (s.Length % 2 != 0) throw new FormatException("A record's hex text has an odd length.");
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
            return b;
        }

        public static string GameFolder(GameKind kind) { return kind == GameKind.UW2 ? "uw2" : "uw1"; }

        /// <summary>The clip as a prefab's JSON text.</summary>
        public static string ToJson(TileClip clip, string name)
        {
            // every object gets a number, so that a link can name another object of the prefab
            var ids = new Dictionary<TileClipObject, int>();
            foreach (var o in clip.AllObjects()) ids[o] = ids.Count;
            Func<TileClipObject, JsonObject> obj = null;
            obj = o =>
            {
                var j = new JsonObject { ["id"] = ids[o], ["record"] = Hex(o.Record) };
                if (o.Mobile) j["mobile"] = true;
                if (o.LinkTo != null && ids.ContainsKey(o.LinkTo)) j["linkTo"] = ids[o.LinkTo];
                if (o.TargetDx >= 0) { j["targetX"] = o.TargetDx; j["targetY"] = o.TargetDy; }
                if (o.Children.Count > 0) { var kids = new JsonArray(); foreach (var c in o.Children) kids.Add(obj(c)); j["holds"] = kids; }
                return j;
            };
            var cells = new JsonArray();
            for (int y = 0; y < clip.Height; y++)
                for (int x = 0; x < clip.Width; x++)
                {
                    var c = clip.Cells[x, y];
                    if (c == null) continue;
                    var j = new JsonObject { ["x"] = x, ["y"] = y, ["tile"] = Hex(c.Record), ["floorTexture"] = c.FloorTexture, ["wallTexture"] = c.WallTexture };
                    if (c.Objects.Count > 0) { var os = new JsonArray(); foreach (var o in c.Objects) os.Add(obj(o)); j["objects"] = os; }
                    cells.Add(j);
                }
            var root = new JsonObject
            {
                ["format"] = Format, ["version"] = 1, ["name"] = name ?? "", ["game"] = GameFolder(clip.Kind),
                ["width"] = clip.Width, ["height"] = clip.Height, ["tiles"] = cells,
            };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        /// <summary>A prefab's JSON text as a clip (and its name). Throws FormatException, saying what is wrong, for a file that is not one.</summary>
        public static TileClip FromJson(string json, out string name)
        {
            JsonNode root;
            try { root = JsonNode.Parse(json); } catch (Exception ex) { throw new FormatException("Not a prefab: " + ex.Message); }
            if (root == null || (string)root["format"] != Format) throw new FormatException("Not a prefab: its \"format\" is not \"" + Format + "\".");
            name = (string)root["name"] ?? "";
            int w = (int?)root["width"] ?? 0, h = (int?)root["height"] ?? 0;
            if (w < 1 || h < 1 || w > 64 || h > 64) throw new FormatException("A prefab is 1-64 tiles each way, not " + w + " × " + h + ".");
            string game = (string)root["game"];
            var clip = new TileClip { Kind = game == "uw2" ? GameKind.UW2 : GameKind.UW1, FromLevel = -1, Width = w, Height = h, Cells = new TileClipCell[w, h] };
            var byId = new Dictionary<int, TileClipObject>();
            var links = new List<KeyValuePair<TileClipObject, int>>();
            Func<JsonNode, int, TileClipObject> obj = null;
            obj = (j, depth) =>
            {
                if (depth > 64) throw new FormatException("Objects are nested too deep.");
                var rec = Unhex((string)j["record"]);
                bool mobile = (bool?)j["mobile"] ?? false;
                if (rec.Length != (mobile ? LevelBlock.MobileSize : LevelBlock.StaticSize)) throw new FormatException("An object's record is " + rec.Length + " bytes, not " + (mobile ? LevelBlock.MobileSize : LevelBlock.StaticSize) + ".");
                var o = new TileClipObject { Record = rec, Mobile = mobile };
                int? id = (int?)j["id"];
                if (id != null) byId[id.Value] = o;
                int? to = (int?)j["linkTo"];
                if (to != null) links.Add(new KeyValuePair<TileClipObject, int>(o, to.Value));
                int? tx = (int?)j["targetX"], ty = (int?)j["targetY"];
                if (tx != null && ty != null && tx >= 0 && ty >= 0 && tx < w && ty < h) { o.TargetDx = tx.Value; o.TargetDy = ty.Value; }
                foreach (var c in j["holds"] as JsonArray ?? new JsonArray()) o.Children.Add(obj(c, depth + 1));
                return o;
            };
            foreach (var j in root["tiles"] as JsonArray ?? new JsonArray())
            {
                int x = (int?)j["x"] ?? -1, y = (int?)j["y"] ?? -1;
                if (x < 0 || y < 0 || x >= w || y >= h) throw new FormatException("A tile lies outside the prefab: (" + x + ", " + y + ").");
                var rec = Unhex((string)j["tile"]);
                if (rec.Length != 4) throw new FormatException("A tile's record is " + rec.Length + " bytes, not 4.");
                UltimateEditor.Uw1.Uw1Schemas.Tile["First object"].Set(rec, 0);
                var cell = new TileClipCell { Record = rec, FloorTexture = (int?)j["floorTexture"] ?? -1, WallTexture = (int?)j["wallTexture"] ?? -1 };
                foreach (var o in j["objects"] as JsonArray ?? new JsonArray()) cell.Objects.Add(obj(o, 0));
                clip.Cells[x, y] = cell;
            }
            foreach (var l in links) { TileClipObject to; if (byId.TryGetValue(l.Value, out to)) l.Key.LinkTo = to; }
            if (clip.TileCount == 0) throw new FormatException("The prefab holds no tile.");
            if (clip.AllObjects().Count() > LevelBlock.Objects) throw new FormatException("The prefab holds more objects than a level can.");
            return clip;
        }

        static string Safe(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            string s = new string((name ?? "").Trim().Select(c => bad.Contains(c) || c == '.' ? '_' : c).ToArray());
            return s.Length == 0 ? "prefab" : s.Length > 60 ? s.Substring(0, 60) : s;
        }

        /// <summary>Saves a clip as a prefab in the folder (one folder a game under it); a prefab of the same name is replaced. Returns the file.</summary>
        public static string Save(string folder, TileClip clip, string name)
        {
            if (clip == null || clip.TileCount == 0) throw new InvalidOperationException("Copy tiles first: a prefab is made of the copied tiles.");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Give the prefab a name.");
            string dir = Path.Combine(folder, GameFolder(clip.Kind));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Safe(name) + Extension);
            Conversations.SafeFile.WriteAtomic(path, ToJson(clip, name.Trim()));
            return path;
        }

        /// <summary>The prefabs saved for a game, by name. A file that is not a prefab is left out.</summary>
        public static List<PrefabInfo> List(string folder, GameKind kind)
        {
            var list = new List<PrefabInfo>();
            string dir = Path.Combine(folder, GameFolder(kind));
            if (!Directory.Exists(dir)) return list;
            foreach (string f in Directory.GetFiles(dir, "*" + Extension))
            {
                try
                {
                    string name;
                    var c = FromJson(File.ReadAllText(f), out name);
                    list.Add(new PrefabInfo { Path = f, Name = name.Length > 0 ? name : Path.GetFileName(f).Replace(Extension, ""), Kind = c.Kind, Width = c.Width, Height = c.Height, Tiles = c.TileCount, Objects = c.AllObjects().Count() });
                }
                catch (Exception) { }
            }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static TileClip Load(string path)
        {
            string name;
            return FromJson(File.ReadAllText(path), out name);
        }
    }
}
