using System;
using System.Linq;
using System.Text;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Level-wide and game-wide settings: a level's texture slots (Level ▸ Level textures), clearing
    /// a level and making an empty slot one (the Level menu), the new-game start square (patched in
    /// the executable), the tile flags fill leaves out, and the automap and its notes.
    /// </summary>
    public sealed partial class McpEditor
    {
        // ---- texture slots

        /// <summary>wall, floor or door. UW2 has one list (T64.TR) whose first 16 slots are the floors too.</summary>
        TextureMap.Kind ParseKind(string kind, bool forSlots)
        {
            string k = (kind ?? "").Trim().ToLowerInvariant();
            if (k == "wall" || k == "walls") return TextureMap.Kind.Wall;
            if (k == "door" || k == "doors") return TextureMap.Kind.Door;
            if (k == "floor" || k == "floors" || k == "ceiling")
            {
                if (Session.IsUw2)
                {
                    if (forSlots) throw new ArgumentException("UW2 has one texture list (T64.TR): its wall slots 0-15 are the floors too (15 the ceiling); use kind wall.");
                    return TextureMap.Kind.Wall;
                }
                return TextureMap.Kind.Floor;
            }
            throw new ArgumentException("A kind is wall, floor or door.", "kind");
        }

        static string KindName(TextureMap.Kind k) { return k == TextureMap.Kind.Door ? "door" : k == TextureMap.Kind.Floor ? "floor" : "wall"; }

        TerrainTable Terrain()
        {
            try { var t = TerrainTable.Load(Session); return t.Exists ? t : null; }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException) { return null; }
        }

        /// <summary>A texture's TERRAIN.DAT class in words (water, lava, ...), "normal" for 0, or null: doors, or no word for it.</summary>
        string TerrainOf(TerrainTable terrain, TextureMap.Kind k, int texture)
        {
            if (terrain == null || k == TextureMap.Kind.Door) return null;
            bool floor = k == TextureMap.Kind.Floor;
            if (texture < 0 || texture >= terrain.Textures(floor)) return null;
            return TerrainTable.Describe(Session.Kind, floor, terrain.Get(terrain.EntryOf(texture, floor)));
        }

        static int SlotTexture(LevelModel m, TextureMap.Kind k, int slot)
        {
            var map = k == TextureMap.Kind.Door ? m.DoorMap : k == TextureMap.Kind.Floor ? m.FloorMap : m.WallMap;
            return map != null && slot < map.Length ? map[slot] : -1;
        }

        /// <summary>The level's texture slots: each one's global texture, its terrain and how many open tiles use it.</summary>
        public string LevelTextures(int level, string kind = null)
        {
            var m = ReadModel(level);
            var s = Session;
            var kinds = string.IsNullOrWhiteSpace(kind)
                ? (s.IsUw2 ? new[] { TextureMap.Kind.Wall, TextureMap.Kind.Door } : new[] { TextureMap.Kind.Wall, TextureMap.Kind.Floor, TextureMap.Kind.Door })
                : new[] { ParseKind(kind, false) };
            var terrain = Terrain();
            var b = new StringBuilder();
            b.Append("Level ").Append(level).Append(" textures").Append(m.Present ? "" : " (an empty slot)").Append(": ");
            b.Append(s.IsUw2
                ? "wall slots 0-63 of T64.TR, the first 16 also the floors (15 the ceiling); door slots 0-5 of DOORS.GR"
                : "wall slots 0-47 of W64.TR, floor slots 0-9 of F32.TR (9 the ceiling), door slots 0-5 of DOORS.GR");
            b.Append(". A tile's Wall texture / Floor texture is a slot here.\n");
            foreach (var k in kinds)
            {
                int n = TextureMap.Slots(s, k);
                b.Append('\n').Append(KindName(k)).Append(" slots (").Append(n).Append(", textures 0-").Append(TextureMap.Choices(s, k) - 1).Append(" to choose from)\n");
                for (int i = 0; i < n; i++)
                {
                    int tex = SlotTexture(m, k, i);
                    b.Append(KindName(k)).Append(' ').Append(i).Append(": ");
                    if (tex < 0) { b.Append("none\n"); continue; }
                    b.Append("texture ").Append(tex);
                    if ((k == TextureMap.Kind.Floor || s.IsUw2 && k == TextureMap.Kind.Wall) && i == m.CeilingFloorIndex) b.Append(", the ceiling");
                    if (k != TextureMap.Kind.Door)
                    {
                        int u = TextureMap.Uses(m, k, i);
                        b.Append(", ").Append(u).Append(u == 1 ? " tile" : " tiles");
                        string t = TerrainOf(terrain, k, tex);
                        if (t != null) b.Append(", terrain ").Append(t);
                    }
                    b.Append('\n');
                }
            }
            return b.ToString();
        }

        /// <summary>Points one of the level's slots at another global texture, one undo step (Level ▸ Level textures).</summary>
        public string LevelTextureSet(int level, string kind, int slot, int texture)
        {
            var e = Editable(level);
            var k = ParseKind(kind, true);
            int before = SlotTexture(e.Model, k, slot);
            TextureMap.Set(e, k, slot, texture);
            var m = e.Model;
            string t = TerrainOf(Terrain(), k, texture);
            var b = new StringBuilder();
            b.Append("Level ").Append(level).Append(' ').Append(KindName(k)).Append(" slot ").Append(slot).Append(": texture ");
            if (before == texture) return b.Append(texture).Append(" already; nothing changed.").ToString();
            b.Append(before).Append(" -> ").Append(texture);
            if (t != null) b.Append(" (terrain ").Append(t).Append(')');
            if (k != TextureMap.Kind.Door) { int u = TextureMap.Uses(m, k, slot); b.Append("; ").Append(u).Append(u == 1 ? " tile shows" : " tiles show").Append(" it"); }
            return b.Append(". One undo step; written with the level by save.").ToString();
        }

        /// <summary>The game's textures of a kind with their TERRAIN.DAT class, optionally only those whose class contains a word.</summary>
        public string TexturesFind(string kind, string terrain = null)
        {
            Require();
            var k = ParseKind(kind, false);
            var s = Session;
            var table = Terrain();
            string want = string.IsNullOrWhiteSpace(terrain) ? null : terrain.Trim();
            if (want != null && k == TextureMap.Kind.Door) throw new ArgumentException("Door textures have no terrain.");
            if (want != null && table == null) throw new McpEditorException("The game has no TERRAIN.DAT, so textures have no terrain to look for.");
            int n = TextureMap.Choices(s, k);
            var b = new StringBuilder();
            int found = 0;
            for (int i = 0; i < n; i++)
            {
                string t = TerrainOf(table, k, i);
                if (want != null && (t == null || t.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                b.Append("texture ").Append(i);
                if (t != null) b.Append(": ").Append(t);
                b.Append('\n');
                found++;
            }
            string file = k == TextureMap.Kind.Door ? "DOORS.GR" : s.IsUw2 ? "T64.TR" : k == TextureMap.Kind.Floor ? "F32.TR" : "W64.TR";
            string head = found + " of " + n + " " + KindName(k) + " textures (" + file + ")" + (want != null ? " with terrain \"" + want + "\"" : "")
                + ". Put one in a level's slot with level_texture_set.\n";
            return head + b;
        }

        // ---- clearing and making levels

        /// <summary>Level ▸ Clear the level (and its tiles): every object gone, the free lists as in a new level; one undo step.</summary>
        public string LevelClear(int level, bool tilesToo)
        {
            var e = Editable(level);
            var kind = Session.Kind;
            e.EditBytes(tilesToo ? "Clear the level and its tiles" : "Clear the level", (d, t) =>
            {
                NewLevels.ResetObjects(d, kind);
                if (tilesToo) NewLevels.MakeAllSolid(d);
            });
            bool ok;
            string report = e.Check(out ok);
            return "Level " + level + " cleared" + (tilesToo ? " to solid rock" : "") + " (one undo step; written by save)"
                + (ok ? "." : "; the list check fails: " + report);
        }

        /// <summary>Level ▸ Make this slot a level: an empty slot becomes a level with no objects, a room in the middle or all rock; one undo step.</summary>
        public string LevelMake(int level, bool room)
        {
            Require();
            CheckLevelNumber(Session, level);
            var s = Session;
            var slot = s.LevelSlots().Find(x => x.Level == level);
            if (slot != null && slot.Damage != null) throw new McpEditorException("Level " + level + " is damaged (" + slot.Damage + "): put back LEV.ARK (its .BKP or .ORIG copy) rather than making a new level over it.");
            var m = Edits.Model(level);
            if (m.Present) throw new McpEditorException("Slot " + level + " already holds a level; level_clear empties it instead.");
            var tex = NewLevels.TextureMap(s, level, x => Edits.Existing(x) != null ? Edits.Existing(x).TextureBytes : (s.LevArk.Has(s.Profile.TextureBlock(x)) ? s.LevArk.Read(s.Profile.TextureBlock(x)) : null));
            Edits.CreateLevel(level, NewLevels.EmptyBlock(s.Kind, room), tex, "Make slot " + level + " a level");
            return "Slot " + level + " is a new level" + (room
                ? " with an open room at (" + NewLevels.RoomFrom + ", " + NewLevels.RoomFrom + ")-(" + NewLevels.RoomTo + ", " + NewLevels.RoomTo + "), floor height " + NewLevels.RoomHeight
                : " of solid rock") + ". One undo step; written by save.";
        }

        // ---- the new-game start square

        /// <summary>Where a new game starts (level 1, a square patched in the executable) and, in a saved game, where the player is.</summary>
        public string StartGet()
        {
            Require();
            var s = Session;
            var st = s.NewGameStart;
            var b = new StringBuilder();
            b.Append("New game: level ").Append(st.Level).Append(", square (").Append((int)Math.Floor(st.X)).Append(", ").Append((int)Math.Floor(st.Y)).Append(')');
            var shipped = s.ShippedNewGameStart();
            if ((int)st.X == (int)shipped.X && (int)st.Y == (int)shipped.Y) b.Append(", as shipped");
            b.Append('\n');
            b.Append(s.ExePath != null
                ? "Stored in " + s.ExePath + "; start_set moves it (level 1 only)."
                : "No " + (s.IsUw2 ? "UW2.EXE" : "UW.EXE") + " beside DATA: this is the shipped square, and start_set cannot move it.").Append('\n');
            if (s.PlayerPoint != null)
                b.Append("Saved game: the player is on level ").Append(s.PlayerPoint.Level).Append(" at (").Append(s.PlayerPoint.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                 .Append(", ").Append(s.PlayerPoint.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(") (").Append(s.PlayerPoint.Description).Append(")\n");
            return b.ToString();
        }

        /// <summary>
        /// Moves the new-game start square on level 1: patches the executable at once (GameSession.SetNewGameStart,
        /// the old file kept as .BKP and the first as .ORIG). Not a level edit: no undo step, not part of save.
        /// </summary>
        public string StartSet(int x, int y)
        {
            Require();
            CheckTile(x, y);
            var s = Session;
            if (s.ExePath == null) throw new McpEditorException("No " + (s.IsUw2 ? "UW2.EXE" : "UW.EXE") + " beside DATA: the new-game start square is stored in the executable, so it cannot be moved here.");
            var m = ReadModel(1);
            if (!m.Present || m.Tiles[x, y].IsSolid) throw new McpEditorException("(" + x + ", " + y + ") of level 1 is rock, so there is nowhere to stand.");
            var old = s.NewGameStart;
            s.SetNewGameStart(x, y);
            return "New-game start moved from (" + (int)old.X + ", " + (int)old.Y + ") to (" + x + ", " + y + ") on level 1 and written to " + System.IO.Path.GetFileName(s.ExePath)
                + " now (the old file kept as .BKP, the first original as .ORIG). It is not a level edit: undo and save do not touch it; start_set again moves it back.";
        }

        // ---- tile flags fill leaves out

        /// <summary>
        /// Sets the light field (0-3, bit 0 the light flag), no magic and the door bit on every tile of a
        /// rectangle, one undo step, all checked first. Leave a value out to keep it.
        /// </summary>
        public string FillFields(int level, int x1, int y1, int x2, int y2, int? light, bool? noMagic, bool? door, bool openOnly)
        {
            var e = Editable(level);
            CheckTile(x1, y1); CheckTile(x2, y2);
            if (light.HasValue && (light < 0 || light > 3)) throw new ArgumentOutOfRangeException(null, "Light is 0-3 (bit 0 the light flag, bit 1 unknown).");
            if (!light.HasValue && !noMagic.HasValue && !door.HasValue) throw new ArgumentException("Nothing to set: give light, noMagic or door.");
            int xa = Math.Min(x1, x2), xb = Math.Max(x1, x2), ya = Math.Min(y1, y2), yb = Math.Max(y1, y2);
            var m = e.Model;
            var f = Uw1Schemas.Tile;
            int n = 0;
            for (int y = ya; y <= yb; y++) for (int x = xa; x <= xb; x++) if (!openOnly || !m.Tiles[x, y].IsSolid) n++;
            e.EditBytes("Tile flags (" + xa + ", " + ya + ")-(" + xb + ", " + yb + ")", (data, tex) =>
            {
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++)
                    {
                        if (openOnly && m.Tiles[x, y].IsSolid) continue;
                        int at = TileAt(x, y);
                        if (light.HasValue) f["Light"].Set(data, light.Value, at);
                        if (noMagic.HasValue) f["No magic"].Set(data, noMagic.Value ? 1 : 0, at);
                        if (door.HasValue) f["Door"].Set(data, door.Value ? 1 : 0, at);
                    }
            });
            return "Set " + n + " tiles, (" + xa + ", " + ya + ") to (" + xb + ", " + yb + ") of level " + level + (openOnly ? ", open tiles only" : "") + ". One undo step.";
        }

        /// <summary>The light flag on every open square of the level, or off everywhere (LightFlags.SetAll); one undo step.</summary>
        public string LightFlagsAll(int level, bool on)
        {
            var e = Editable(level);
            LightFlags.SetAll(e, on);
            int open;
            int n = LightFlags.Count(e.Model, out open);
            return "Level " + level + ": the light flag is now on " + n + " of " + open + " open squares"
                + (Session.IsUw2 ? " (UW2 applies DL.DAT's minimum light by it)" : " (UW1 keeps the bit; its light does not use it)") + ". One undo step.";
        }

        // ---- the automap and its notes

        Automap AutomapOf(int level)
        {
            Require();
            CheckLevelNumber(Session, level);
            return Edits.AutomapFor(level);
        }

        /// <summary>A level's automap: how many tiles are marked seen, and its notes with their tiles.</summary>
        public string AutomapNotes(int level)
        {
            var a = AutomapOf(level);
            var b = new StringBuilder();
            b.Append("Level ").Append(level).Append(" automap: ").Append(a.SeenCount).Append(" tiles seen, ").Append(a.Notes.Count).Append(" of ").Append(Automap.MaxNotes).Append(" notes")
             .Append(a.Dirty ? " (unsaved changes)" : "").Append('\n');
            for (int i = 0; i < a.Notes.Count; i++)
            {
                var n = a.Notes[i];
                double tx, ty;
                Automap.PixelToTile(n.X, n.Y, out tx, out ty);
                b.Append(i).Append(": tile (").Append((int)Math.Floor(tx)).Append(", ").Append((int)Math.Floor(ty)).Append(") \"").Append(n.Text).Append("\"\n");
            }
            return b.ToString();
        }

        /// <summary>Marks every open tile of the level's automap seen (as the game writes them) or none.</summary>
        public string AutomapAll(int level, bool seen)
        {
            var a = AutomapOf(level);
            if (a.NotesOnly) throw new McpEditorException("Page " + level + " has notes only, no map.");
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot: there is no map to mark.");
            a.Snapshot(seen ? "Mark all seen" : "Mark all unseen");
            a.All(Session, m, seen);
            Edits.AutomapEdited(a);
            return "Level " + level + " automap: " + a.SeenCount + " tiles seen. automap_undo takes it back (the level's undo does not); written by save.";
        }

        /// <summary>Adds a note to the automap on a tile (the text as the game can draw it: capitals, at most 49 characters).</summary>
        public string AutomapNoteAdd(int level, int x, int y, string text)
        {
            var a = AutomapOf(level);
            CheckTile(x, y);
            if (a.Notes.Count >= Automap.MaxNotes) throw new McpEditorException("The automap holds " + Automap.MaxNotes + " notes at most.");
            string t = Automap.Clean(text);
            if (t.Length == 0) throw new ArgumentException("A note needs text.");
            double px, py;
            Automap.TileToPixel(x, y, out px, out py);
            a.Snapshot("Add a note");
            a.Notes.Add(new MapNote { Text = t, X = (int)Math.Round(Math.Max(0, Math.Min(319, px))), Y = (int)Math.Round(Math.Max(0, Math.Min(199, py))) });
            Edits.AutomapEdited(a);
            return "Note " + (a.Notes.Count - 1) + " on level " + level + " at tile (" + x + ", " + y + "): \"" + t + "\"" + (t != text ? " (as the game can draw it)" : "") + ". Written by save.";
        }

        /// <summary>Deletes a note of the automap by its number (automap_notes).</summary>
        public string AutomapNoteDelete(int level, int index)
        {
            var a = AutomapOf(level);
            if (index < 0 || index >= a.Notes.Count) throw new ArgumentOutOfRangeException(null, a.Notes.Count == 0 ? "Level " + level + " has no notes." : "Notes are 0-" + (a.Notes.Count - 1) + ".");
            string t = a.Notes[index].Text;
            a.Snapshot("Delete a note");
            a.Notes.RemoveAt(index);
            Edits.AutomapEdited(a);
            return "Deleted note " + index + " (\"" + t + "\") of level " + level + ". Written by save.";
        }

        /// <summary>Takes back the last automap change of a level (the automap has its own undo).</summary>
        public string AutomapUndo(int level)
        {
            var a = AutomapOf(level);
            if (!a.CanUndo) throw new McpEditorException("Level " + level + "'s automap has nothing to undo.");
            string label = a.UndoLabel;
            a.Undo();
            Edits.AutomapEdited(a);
            return "Undid on level " + level + "'s automap: " + label + ".";
        }
    }
}
