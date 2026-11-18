using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>Tiles with what is on them: copy, turn, mirror, paste; prefabs (saved tile copies); decorating a region.</summary>
    public sealed partial class McpEditor
    {
        TileClip edTileClip;

        /// <summary>Edit ▸ Copy tiles with what is on them: a rectangle, kept for tiles_paste and prefab_save.</summary>
        public string TilesCopy(int level, int x1, int y1, int x2, int y2)
        {
            var e = Level(level);
            if (!e.Model.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            edTileClip = e.CopyTiles(EdRect(x1, y1, x2, y2));
            return "Copied " + edTileClip.Describe() + " from " + EdRectText(x1, y1, x2, y2) + " of level " + level + ". tiles_paste puts the copy's south-west corner on a tile; tiles_turn and tiles_mirror change it first.";
        }

        TileClip EdTileClip()
        {
            Require();
            if (edTileClip == null) throw new McpEditorException("Nothing copied: copy tiles first (tiles_copy, or prefab_load).");
            return edTileClip;
        }

        /// <summary>Turns the copied tiles clockwise (seen from above, north up) by quarters.</summary>
        public string TilesTurn(int quarters)
        {
            var c = EdTileClip();
            if (quarters < 1 || quarters > 3) throw new ArgumentOutOfRangeException(null, "Turn 1-3 quarters clockwise.");
            for (int i = 0; i < quarters; i++) c.RotateClockwise();
            return "Turned " + quarters * 90 + " degrees clockwise: " + c.Describe() + ".";
        }

        /// <summary>Mirrors the copied tiles east to west.</summary>
        public string TilesMirror()
        {
            var c = EdTileClip();
            c.Mirror();
            return "Mirrored east to west: " + c.Describe() + ".";
        }

        /// <summary>What tiles_copy holds now.</summary>
        public string TilesClip()
        {
            Require();
            if (edTileClip == null) return "No tiles copied.";
            var c = edTileClip;
            return c.Describe() + (c.FromLevel > 0 ? ", copied from level " + c.FromLevel + " at (" + c.FromX + ", " + c.FromY + ")" : ", from a prefab") + "; " + c.Count(true) + " mobile and " + c.Count(false) + " static objects.";
        }

        /// <summary>Pastes the copied tiles with their south-west corner on (x, y): each tile copied replaces the tile there, with what lies on it. One undo step.</summary>
        public string TilesPaste(int level, int x, int y)
        {
            var c = EdTileClip();
            return EdPasteTiles(level, c, x, y);
        }

        string EdPasteTiles(int level, TileClip c, int x, int y)
        {
            var e = Editable(level);
            CheckTile(x, y);
            var under = new List<int>();
            for (int cy = 0; cy < c.Height; cy++)
                for (int cx = 0; cx < c.Width; cx++)
                    if (c.Cells[cx, cy] != null && x + cx < 64 && y + cy < 64) under.AddRange(e.Model.TileObjects(x + cx, y + cy).Select(o => o.Index));
            List<TextSlot> texts = null;
            var st = EdStrings();
            if (st != null) { try { texts = ObjectTextEdits.TextsUnder(e.Model, under, st); } catch (Exception) { texts = null; } }
            TilePasteResult res = null;
            EdWithTexts(() => res = e.PasteTiles(c, x, y),
                s => ObjectTextEdits.GiveCopiesOwnTexts(Edits, level, res.Made, s, out _),
                s => texts == null ? EdNoTexts : ObjectTextEdits.FreeUnused(Edits, s, texts));
            return "Pasted " + res.Describe() + " at (" + x + ", " + y + ") of level " + level + ", one undo step." + edTextNote;
        }

        // ---- prefabs

        string prefabFolder;

        /// <summary>
        /// Where the prefabs are kept (one folder a game under it): the app's (its settings folder's
        /// "prefabs"), else UW_MCP_PREFABS, else the editor's settings folder's as the app finds it.
        /// </summary>
        public string PrefabFolder
        {
            get
            {
                if (prefabFolder != null) return prefabFolder;
                string env = Environment.GetEnvironmentVariable("UW_MCP_PREFABS");
                if (!string.IsNullOrEmpty(env)) return env;
                string dir = Environment.GetEnvironmentVariable("UWEDITOR_SETTINGS_DIR");
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UwEditorAvalonia");
                return Path.Combine(dir, "prefabs");
            }
            set { prefabFolder = value; }
        }

        /// <summary>The prefabs saved for the open game.</summary>
        public string PrefabList()
        {
            Require();
            var list = Prefabs.List(PrefabFolder, Session.Kind);
            if (list.Count == 0) return "No prefab saved for this game yet (they are kept in " + Path.Combine(PrefabFolder, Prefabs.GameFolder(Session.Kind)) + ").";
            var b = new StringBuilder();
            foreach (var p in list) b.Append(p.Label).Append('\n');
            return b.ToString();
        }

        PrefabInfo EdPrefab(string name)
        {
            Require();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the prefab's name.");
            var list = Prefabs.List(PrefabFolder, Session.Kind);
            var p = list.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? (list.Count(x => x.Name.IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) == 1 ? list.First(x => x.Name.IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) : null);
            if (p == null) throw new ArgumentException("No prefab is called " + name + (list.Count > 0 ? "; there are: " + string.Join(", ", list.Select(x => x.Name)) : "") + ".");
            return p;
        }

        /// <summary>Saves the copied tiles (or, with a rectangle, those tiles of a level) as a prefab; one of the same name is replaced.</summary>
        public string PrefabSave(string name, int? level, int? x1, int? y1, int? x2, int? y2)
        {
            Require();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the prefab a name.");
            bool any = level.HasValue || x1.HasValue || y1.HasValue || x2.HasValue || y2.HasValue;
            if (any)
            {
                if (!(level.HasValue && x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue)) throw new ArgumentException("Give the level and both corners, or none to save the copied tiles.");
                TilesCopy(level.Value, x1.Value, y1.Value, x2.Value, y2.Value);
            }
            var c = EdTileClip();
            string path = Prefabs.Save(PrefabFolder, c, name);
            return "Saved prefab \"" + name.Trim() + "\" (" + c.Describe() + ") as " + path + ".";
        }

        /// <summary>Loads a prefab as the copied tiles (to turn, mirror and paste).</summary>
        public string PrefabLoad(string name)
        {
            var p = EdPrefab(name);
            var c = Prefabs.Load(p.Path);
            if (c.Kind != Session.Kind) throw new McpEditorException("Prefab " + p.Name + " is of the other game.");
            edTileClip = c;
            return "Loaded prefab " + p.Label + " as the copied tiles.";
        }

        /// <summary>Places a prefab with its south-west corner on (x, y), turned and mirrored first if asked. The copied tiles stay as they were.</summary>
        public string PrefabPlace(int level, string name, int x, int y, int quarters, bool mirror)
        {
            var p = EdPrefab(name);
            if (quarters < 0 || quarters > 3) throw new ArgumentOutOfRangeException(null, "Turn 0-3 quarters clockwise.");
            var c = Prefabs.Load(p.Path);
            if (c.Kind != Session.Kind) throw new McpEditorException("Prefab " + p.Name + " is of the other game.");
            if (mirror) c.Mirror();
            for (int i = 0; i < quarters; i++) c.RotateClockwise();
            return "Prefab " + p.Name + ": " + EdPasteTiles(level, c, x, y);
        }

        // ---- decoration

        /// <summary>The decoration kinds this game has items for, with the items.</summary>
        public string DecorationKinds()
        {
            Require();
            var items = Decoration.Items(Session);
            var hang = Decoration.Hanging(Session);
            var b = new StringBuilder();
            foreach (var k in Decoration.Kinds)
            {
                b.Append(k).Append(": ");
                b.Append(items[k].Count == 0 ? "(none in this game)" : string.Join(", ", items[k].Select(i => Hex(i) + " " + Session.Names.Get(i) + (hang.ContainsKey(i) ? " (hangs at z " + hang[i] + ")" : ""))));
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Edit ▸ Decorate: scatters scenery over a rectangle's open tiles, one undo step.</summary>
        public string Decorate(int level, int x1, int y1, int x2, int y2, string kinds, int perHundred, int seed, bool avoidLiquid, bool avoidOccupied)
        {
            var e = Editable(level);
            var tiles = EdRect(x1, y1, x2, y2);
            if (perHundred < 1 || perHundred > 400) throw new ArgumentOutOfRangeException(null, "The density is 1-400 objects a hundred tiles.");
            var o = new DecorationOptions { PerHundred = perHundred, Seed = seed, AvoidLiquid = avoidLiquid, AvoidOccupied = avoidOccupied };
            if (!string.IsNullOrWhiteSpace(kinds))
            {
                o.Kinds.Clear();
                foreach (var k in kinds.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var n = Decoration.Kinds.FirstOrDefault(x => string.Equals(x, k.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (n == null) throw new ArgumentException("Kinds are " + string.Join(", ", Decoration.Kinds) + ".");
                    o.Kinds.Add(n);
                }
            }
            var r = Decoration.Apply(e, tiles, o);
            return "Decorated " + EdRectText(x1, y1, x2, y2) + " of level " + level + ": " + r.Describe() + (r.Made.Count > 0 ? ". One undo step; another seed rolls it differently." : "");
        }
    }
}
