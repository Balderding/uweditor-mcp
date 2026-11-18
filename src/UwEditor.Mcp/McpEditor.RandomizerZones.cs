using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The zoning the randomizer's solver works on (Randomizer > Scatter > Zones): what the app's
    /// ZoningState offers, so the tools work on the app's own zoning when attached (its Zones
    /// window and the view's overlay follow) and on one of this server's otherwise.
    /// </summary>
    public interface IRandomizerZoning
    {
        GameSession Session { get; }
        ZoneSet Set { get; }
        string Path { get; }
        bool Dirty { get; }
        bool HasZones { get; }
        bool AskForName { get; }
        bool SplitByLevel { get; set; }
        int CurrentZone { get; set; }
        int MaxClimb { get; }
        ZoneSet ForSolver(out string note);
        Zone AddZone(string name, int level1);
        void Rename(int id, string name);
        void SetColor(int id, string hex);
        void SetNotes(int id, string notes);
        void RemoveZone(int id);
        void EnsureLevel(int level1);
        bool PaintTile(int level1, int x, int y, bool start);
        int FillArea(int level1, int x, int y, int zone);
        bool CanUndo { get; }
        void Undo();
        ZoneLink Connect(int from, int to, bool twoWay);
        void LinkChanged();
        void RemoveLink(ZoneLink k);
        void Mark(int level1, int index, bool? important);
        List<KeyItem> Items { get; }
        List<DoorPassage> Doors { get; }
        void Save(string path);
        void Open(string path);
        void NewFile();
        void Adopt(ZoneSet set, string path);
        /// <summary>The UW1 zoning shipped with the editor (Karl's maps); false when there is none here.</summary>
        bool LoadExample();
    }

    public static class RandomizerZoningExtensions
    {
        public static ZoneSet ForSolver(this IRandomizerZoning z) { string note; return z.ForSolver(out note); }
    }

    public sealed partial class McpEditor
    {
        /// <summary>Attached: the app's zoning of the open game (the Zones window's), or null.</summary>
        public Func<IRandomizerZoning> HostZoning { get; set; }

        RzOwnZoning rzOwnZoning;

        /// <summary>The zoning: the app's when attached, else this server's (read from DATA's zone file, or a new one).</summary>
        IRandomizerZoning RzZoning()
        {
            RzFresh();
            var h = HostZoning?.Invoke();
            if (h != null && h.Session == Session) return h;
            if (rzOwnZoning == null) rzOwnZoning = new RzOwnZoning(Session, RzLevels);
            return rzOwnZoning;
        }

        /// <summary>This server's zoning: as ZoningState, without a view.</summary>
        sealed class RzOwnZoning : IRandomizerZoning
        {
            readonly Func<int, LevelModel> level;
            readonly List<byte[][]> undo = new List<byte[][]>();
            public GameSession Session { get; private set; }
            public ZoneSet Set { get; private set; }
            public string Path { get; private set; }
            public bool Dirty { get; private set; }
            public bool AskForName { get; private set; }
            public bool SplitByLevel { get; set; }
            public int CurrentZone { get; set; }
            public int MaxClimb { get { return ZoneFlood.DefaultMaxClimb; } }

            public RzOwnZoning(GameSession s, Func<int, LevelModel> level)
            {
                Session = s;
                this.level = level;
                Path = System.IO.Path.Combine(s.DataFolder, ZoneSet.DefaultFileName(s.IsUw2));
                if (File.Exists(Path)) { try { Set = ZoneSet.Load(Path, s.Profile.LevelSlots); } catch (Exception) { Set = null; } }
                if (Set == null) Set = New();
                CurrentZone = First();
            }

            int First() { return Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).Select(z => z.Id).DefaultIfEmpty(2).First(); }

            ZoneSet New()
            {
                var zs = new ZoneSet(Session.Profile.LevelSlots) { PerLevel = Session.IsUw2, Game = Session.IsUw2 ? "UW2" : "UW1", Source = "made in Underworld Editor" };
                int first = zs.Global(1, 2);
                zs.EnsureZone(first).Name = "Start";
                zs.StartZone = first;
                return zs;
            }

            public bool HasZones { get { return Set.Tiles.Any(t => t != null && t.Any(b => b > ZoneSet.Unassigned)); } }

            public ZoneSet ForSolver(out string note)
            {
                note = "";
                return SplitByLevel ? Set.SplitByLevel(out note) : ZoneSet.FromJson(Set.ToJson(), Session.Profile.LevelSlots);
            }

            void Changed(string what)
            {
                Dirty = true;
                if ((what == "tiles" || what == "links") && AutoZones.IsAutomatic(Set)) Set.Source = "hand-edited automatic zones (Underworld Editor)";
            }

            public Zone AddZone(string name, int level1)
            {
                if (Set.PerLevel && (level1 < 1 || level1 > Set.Levels)) throw new InvalidOperationException("Give the level the zone is for: each level has its own zones.");
                int id = Set.NextZoneId(level1);
                if (id < 0) throw new InvalidOperationException(Set.PerLevel ? "All 254 zone numbers (2-255) of level " + level1 + " are in use." : "All 254 zone ids (2-255) are in use.");
                var z = Set.EnsureZone(id);
                z.Name = string.IsNullOrWhiteSpace(name) ? "Zone " + Set.Label(id) : name.Trim();
                z.Status = "unfinished";
                CurrentZone = id;
                Changed("zones");
                return z;
            }

            public void Rename(int id, string name) { Set.EnsureZone(id).Name = (name ?? "").Trim(); Changed("zones"); }

            public void SetColor(int id, string hex)
            {
                var z = Set.EnsureZone(id);
                uint c;
                z.Color = string.IsNullOrWhiteSpace(hex) ? "" : ZoneSet.TryParseColor(hex, out c) ? ZoneSet.ColorString(c) : throw new FormatException("Write a colour as #RRGGBB.");
                Changed("tiles");
            }

            public void SetNotes(int id, string notes) { var z = Set.GetZone(id); if (z == null || z.Notes == notes) return; z.Notes = notes ?? ""; Changed("notes"); }

            public void RemoveZone(int id) { Snapshot(); Set.Remove(id); if (CurrentZone == id) CurrentZone = First(); Changed("tiles"); }

            void Snapshot()
            {
                undo.Add(Set.Tiles.Select(t => t == null ? null : (byte[])t.Clone()).ToArray());
                if (undo.Count > 50) undo.RemoveAt(0);
            }

            public bool CanUndo { get { return undo.Count > 0; } }

            public void Undo()
            {
                if (undo.Count == 0) return;
                var t = undo[undo.Count - 1];
                undo.RemoveAt(undo.Count - 1);
                for (int i = 0; i < t.Length && i < Set.Tiles.Length; i++) Set.Tiles[i] = t[i];
                Changed("tiles");
            }

            public void EnsureLevel(int level1)
            {
                if (Set.Tiles[level1 - 1] != null) return;
                var m = level(level1);
                Set.InitLevel(level1, (x, y) => !m.Tiles[x, y].IsSolid);
            }

            public bool PaintTile(int level1, int x, int y, bool start)
            {
                EnsureLevel(level1);
                if (start) Snapshot();
                if (Set.ZoneAt(level1, x, y) == ZoneSet.Solid || Set.ZoneAt(level1, x, y) == CurrentZone) return false;
                if (!Set.OnLevel(level1, CurrentZone)) return false;
                Set.EnsureZone(CurrentZone);
                Set.SetZone(level1, x, y, CurrentZone);
                Changed("tiles");
                return true;
            }

            public int FillArea(int level1, int x, int y, int zone)
            {
                EnsureLevel(level1);
                Snapshot();
                Set.EnsureZone(zone);
                int n = Set.Fill(level1, x, y, zone, new ZoneFlood(level(level1), MaxClimb));
                if (n == 0) undo.RemoveAt(undo.Count - 1);
                else Changed("tiles");
                return n;
            }

            public List<KeyItem> Items { get { return KeyItems.Find(Session, level, Set); } }
            public List<DoorPassage> Doors { get { return ZoneDoors.Find(Session, level, Set); } }

            public void Mark(int level1, int index, bool? important)
            {
                string k = level1 + ":" + index;
                if (important == null) Set.Importance.Remove(k); else Set.Importance[k] = important.Value;
                Changed("items");
            }

            public ZoneLink Connect(int from, int to, bool twoWay)
            {
                if (from == to) throw new InvalidOperationException("A zone does not need a link to itself.");
                List<DoorPassage> used;
                string req = ZoneDoors.Suggest(Doors, from, to, out used);
                var d = used.FirstOrDefault();
                var k = new ZoneLink
                {
                    Id = Set.NextLinkId(), From = from, To = to, TwoWay = twoWay, Requires = req ?? "free",
                    Kind = d != null ? d.Kind : "walk", Confidence = req == null ? "todo" : "certain",
                    Notes = d != null ? string.Join("; ", used.Select(u => string.Format("L{0} ({1}, {2}) {3}: {4}", u.Level, u.X, u.Y, u.Kind, u.Notes))) : "",
                };
                if (d != null) { k.Level = d.Level; k.X = d.X; k.Y = d.Y; }
                Set.Links.Add(k);
                Changed("links");
                return k;
            }

            public void LinkChanged() { Changed("links"); }
            public void RemoveLink(ZoneLink k) { Set.Links.Remove(k); Changed("links"); }

            public void Save(string path)
            {
                path = path ?? Path;
                if (File.Exists(path) && !File.Exists(path + ".BKP")) File.Copy(path, path + ".BKP");
                Set.Save(path);
                Path = path;
                Dirty = false;
                AskForName = false;
            }

            public void Open(string path)
            {
                Set = ZoneSet.Load(path, Session.Profile.LevelSlots);
                Path = path;
                Dirty = false;
                undo.Clear();
                CurrentZone = First();
            }

            public void NewFile() { Set = New(); Dirty = true; undo.Clear(); CurrentZone = 2; }

            public void Adopt(ZoneSet set, string path) { Set = set; Path = path; AskForName = true; Dirty = true; undo.Clear(); CurrentZone = First(); }

            public bool LoadExample() { return false; }
        }

        // ---- the tools

        IRandomizerZoning RzZ() { return RzZoning(); }

        static string RzZoneLabel(IRandomizerZoning z, int id)
        {
            var zone = z.Set.GetZone(id);
            return z.Set.Label(id) + (zone != null && zone.Name.Length > 0 ? " \"" + zone.Name + "\"" : "");
        }

        Zone RzZoneOf(IRandomizerZoning z, int id)
        {
            var zone = z.Set.GetZone(id);
            if (zone == null || id <= ZoneSet.Unassigned) throw new McpEditorException("There is no zone " + id + " (zones_list shows them).");
            return zone;
        }

        /// <summary>The zoning: its file, unsaved changes, zones, links, start zone, the squares ending the game, the levels' squares in no zone.</summary>
        public string ZonesInfo()
        {
            var z = RzZ();
            var s = z.Set;
            var b = new StringBuilder();
            b.Append("Zoning ").Append(File.Exists(z.Path) ? z.Path : z.Path + " (not saved yet)").Append(z.Dirty ? ", unsaved changes" : "").Append(IsAttached && rzOwnZoning != z ? ", the editor's (its Zones window)" : "").Append('\n');
            b.Append("Source: ").Append(s.Source).Append(AutoZones.IsAutomatic(s) ? " (automatic zones)" : "").Append(s.PerLevel ? "; zones numbered a level (L<level>:<n>)" : "").Append('\n');
            b.Append(s.Zones.Count(x => x.Id > ZoneSet.Unassigned)).Append(" zones, ").Append(s.Links.Count).Append(" links, ").Append(s.Placements.Count).Append(" grants; ").Append(z.HasZones ? "painted" : "nothing painted yet (the solver then makes automatic zones)").Append('\n');
            b.Append("Start zone ").Append(RzZoneLabel(z, s.StartZone)).Append(s.GoalZone > 0 ? "; goal zone " + RzZoneLabel(z, s.GoalZone) : "").Append("; split multi-level zones by level for the solver: ").Append(z.SplitByLevel ? "on" : "off").Append('\n');
            try
            {
                var ends = EndGame.Find(Session, RzLevels);
                b.Append("Squares ending the game: ").Append(ends.Count == 0 ? "none" : string.Join("; ", ends.Select(e => e + " zone " + s.ZoneAt(e.Level, e.X, e.Y)))).Append('\n');
            }
            catch (Exception) { }
            var levels = new List<string>();
            for (int l = 1; l <= s.Levels; l++)
            {
                if (s.Tiles[l - 1] == null) continue;
                int un;
                s.CountsOnLevel(l).TryGetValue(ZoneSet.Unassigned, out un);
                levels.Add("level " + l + ": " + un);
            }
            b.Append("Open squares in no zone: ").Append(levels.Count == 0 ? "no level has a zone map yet" : string.Join(", ", levels)).Append('\n');
            return b.ToString();
        }

        /// <summary>Every zone: id, name, tiles, levels, colour, status, start/goal/ends the game.</summary>
        public string ZonesList()
        {
            var z = RzZ();
            var s = z.Set;
            var counts = new Dictionary<int, int>();
            for (int l = 1; l <= s.Levels; l++) foreach (var kv in s.CountsOnLevel(l)) { int c; counts.TryGetValue(kv.Key, out c); counts[kv.Key] = c + kv.Value; }
            var ends = new HashSet<int>();
            try { foreach (var e in EndGame.Find(Session, RzLevels)) ends.Add(s.ZoneAt(e.Level, e.X, e.Y)); } catch (Exception) { }
            var b = new StringBuilder();
            foreach (var zone in s.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                int n; counts.TryGetValue(zone.Id, out n);
                b.AppendFormat("{0} \"{1}\": {2} tiles, levels {3}, colour {4}{5}{6}{7}{8}\n", s.Label(zone.Id), zone.Name, n, string.Join(",", s.LevelsOf(zone.Id)), ZoneSet.ColorString(s.ColorOf(zone.Id)),
                    zone.Status.Length > 0 ? ", " + zone.Status : "", zone.Id == s.StartZone ? ", start" : "", ends.Contains(zone.Id) ? ", ends the game" : "", zone.Notes.Length > 0 ? "; notes: " + zone.Notes : "");
            }
            return b.Length == 0 ? "The zoning has no zones yet." : b.ToString();
        }

        /// <summary>One zone: its fields, links, grants and the key items in it.</summary>
        public string ZoneGet(int id)
        {
            var z = RzZ();
            var zone = RzZoneOf(z, id);
            var s = z.Set;
            var b = new StringBuilder();
            b.AppendFormat("Zone {0} \"{1}\": {2} tiles on levels {3}; colour {4}; status {5}; levels text \"{6}\"{7}\n", s.Label(id), zone.Name, s.TileCount(id), string.Join(",", s.LevelsOf(id)), ZoneSet.ColorString(s.ColorOf(id)), zone.Status, zone.Levels, id == s.StartZone ? "; the start zone" : "");
            if (zone.Notes.Length > 0) b.Append("Notes: ").Append(zone.Notes).Append('\n');
            var links = s.Links.Where(k => k.From == id || k.To == id).ToList();
            b.Append("Links (").Append(links.Count).Append("):\n");
            foreach (var k in links) b.Append("  ").Append(RzLinkText(z, k)).Append('\n');
            var grants = s.Placements.Where(p => p.Zone == id).ToList();
            b.Append("Grants (").Append(grants.Count).Append("):\n");
            for (int i = 0; i < grants.Count; i++) b.Append("  [").Append(i).Append("] ").Append(grants[i].Grants).Append(grants[i].Requires.Length > 0 ? " if " + grants[i].Requires : "").Append(grants[i].Notes.Length > 0 ? " (" + grants[i].Notes + ")" : "").Append('\n');
            List<KeyItem> items;
            try { items = z.Items.Where(i => i.Zone == id).OrderBy(i => i.Level).ThenBy(i => i.Kind).ThenBy(i => i.Index).ToList(); }
            catch (Exception e) { items = new List<KeyItem>(); b.Append("(could not read the key items: ").Append(e.Message).Append(")\n"); }
            b.Append("Key items in it (").Append(items.Count).Append("):\n");
            foreach (var i in items) b.Append("  ").Append(i).Append('\n');
            return b.ToString();
        }

        static string RzLinkText(IRandomizerZoning z, ZoneLink k)
        {
            return string.Format("link {0}: {1} {2} {3}, {4}, requires {5}{6}{7}{8}{9}", k.Id, RzZoneLabel(z, k.From), k.TwoWay ? "<->" : "->", RzZoneLabel(z, k.To), k.Kind, k.Requires,
                k.Once ? ", once" : "", k.Confidence != "certain" ? ", " + k.Confidence : "", k.Level > 0 ? string.Format(", at L{0} ({1}, {2})", k.Level, k.X, k.Y) : "", k.Notes.Length > 0 ? "; " + k.Notes : "");
        }

        /// <summary>Every link of the zoning (or of one zone).</summary>
        public string ZoneLinks(int? zone)
        {
            var z = RzZ();
            var l = z.Set.Links.Where(k => zone == null || k.From == zone || k.To == zone).ToList();
            if (l.Count == 0) return "No links" + (zone != null ? " of zone " + zone : "") + ".";
            return string.Join("\n", l.Select(k => RzLinkText(z, k)));
        }

        /// <summary>A level's zone map as text: rows north (y 63) to south, each tile its zone number (two hex digits; ## rock, .. open in no zone).</summary>
        public string ZoneMap(int level)
        {
            var z = RzZ();
            CheckLevelNumber(Session, level);
            var s = z.Set;
            if (s.Tiles[level - 1] == null) return "Level " + level + " has no zone map yet (painting or filling starts one from its tiles).";
            var b = new StringBuilder("Level " + level + " zones (hex zone numbers" + (s.PerLevel ? " of this level" : "") + "; ## rock, .. in no zone), north at the top, x 0 at the left:\n");
            for (int y = 63; y >= 0; y--)
            {
                for (int x = 0; x < 64; x++)
                {
                    int id = s.ZoneAt(level, x, y);
                    b.Append(id == ZoneSet.Solid ? "##" : id == ZoneSet.Unassigned ? ".." : (s.Number(id) & 255).ToString("X2"));
                    if (x < 63) b.Append(' ');
                }
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Adds a zone (UW2: a zone of the level given); it becomes the one painted.</summary>
        public string ZoneAdd(string name, int level)
        {
            var z = RzZ();
            if (z.Set.PerLevel) CheckLevelNumber(Session, level);
            var zone = z.AddZone(name, level);
            return "Zone " + z.Set.Label(zone.Id) + " \"" + zone.Name + "\" added.";
        }

        /// <summary>Changes a zone's name, colour (#RRGGBB, empty: the default), notes, status or levels text.</summary>
        public string ZoneSetField(int id, string field, string value)
        {
            var z = RzZ();
            var zone = RzZoneOf(z, id);
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "name": z.Rename(id, value); break;
                case "colour":
                case "color": z.SetColor(id, value); break;
                case "notes": z.SetNotes(id, value ?? ""); break;
                case "status": zone.Status = (value ?? "").Trim(); z.LinkChanged(); break;
                case "levels": zone.Levels = (value ?? "").Trim(); z.LinkChanged(); break;
                default: throw new McpEditorException("The field is name, colour, notes, status or levels.");
            }
            return "Zone " + z.Set.Label(id) + " " + field.Trim().ToLowerInvariant() + " set.";
        }

        /// <summary>Removes a zone: its tiles become unassigned, its links and grants go.</summary>
        public string ZoneRemove(int id)
        {
            var z = RzZ();
            var zone = RzZoneOf(z, id);
            int tiles = z.Set.TileCount(id), links = z.Set.Links.Count(k => k.From == id || k.To == id);
            z.RemoveZone(id);
            return string.Format("Zone {0} \"{1}\" removed: {2} tiles unassigned, {3} links and its grants gone (zones_undo brings back the tiles, not the links).", z.Set.Label(id), zone.Name, tiles, links);
        }

        /// <summary>Paints a rectangle of a level's tiles as a zone (rock stays rock); one step of zones_undo.</summary>
        public string ZonePaint(int level, int x1, int y1, int x2, int y2, int zone)
        {
            var z = RzZ();
            CheckLevelNumber(Session, level);
            if (!ReadModel(level).Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            foreach (int v in new[] { x1, y1, x2, y2 }) if (v < 0 || v > 63) throw new ArgumentOutOfRangeException(null, "Corners are 0-63.");
            if (zone <= ZoneSet.Unassigned || zone > (z.Set.PerLevel ? 0xFFFF : ZoneSet.MaxZone)) throw new ArgumentOutOfRangeException(null, "A zone is 2-255" + (z.Set.PerLevel ? " (UW2: the id zones_list shows, L<level>:<n> is level*256+n)" : "") + ".");
            if (!z.Set.OnLevel(level, zone)) throw new McpEditorException("Zone " + z.Set.Label(zone) + " belongs to another level.");
            int keep = z.CurrentZone, n = 0;
            bool first = true;
            z.CurrentZone = zone;
            try
            {
                for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                    for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
                    {
                        if (z.PaintTile(level, x, y, first)) n++;
                        first = false;
                    }
            }
            finally { z.CurrentZone = keep; }
            return n + " tile(s) of level " + level + " painted as zone " + RzZoneLabel(z, zone) + " (rock and tiles already in it stay).";
        }

        /// <summary>Fills the area reachable from a square that shares its zone (no rock, no door tiles, no climb above the limit) with a zone.</summary>
        public string ZoneFill(int level, int x, int y, int zone)
        {
            var z = RzZ();
            CheckLevelNumber(Session, level);
            if (!ReadModel(level).Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            if (x < 0 || x > 63 || y < 0 || y > 63) throw new ArgumentOutOfRangeException(null, "x and y are 0-63.");
            if (zone <= ZoneSet.Unassigned) throw new ArgumentOutOfRangeException(null, "A zone is 2-255.");
            if (!z.Set.OnLevel(level, zone)) throw new McpEditorException("Zone " + z.Set.Label(zone) + " belongs to another level.");
            int n = z.FillArea(level, x, y, zone);
            return n == 0 ? "Nothing to fill there (rock, a door tile, or already that zone)." : n + " tile(s) filled as zone " + RzZoneLabel(z, zone) + ".";
        }

        /// <summary>Takes back the last painting, fill or zone removal of the zoning.</summary>
        public string ZonesUndo()
        {
            var z = RzZ();
            if (!z.CanUndo) throw new McpEditorException("The zoning has no painting to undo.");
            z.Undo();
            return "The last painting of the zoning is undone.";
        }

        /// <summary>A new link between two zones; its requirement is taken from the doors between them.</summary>
        public string ZoneLinkAdd(int from, int to, bool twoWay)
        {
            var z = RzZ();
            RzZoneOf(z, from); RzZoneOf(z, to);
            var k = z.Connect(from, to, twoWay);
            return "Added " + RzLinkText(z, k) + ".";
        }

        /// <summary>Changes a link: from, to, kind, requires, twoWay, notes, confidence, once, level, x, y.</summary>
        public string ZoneLinkSet(int id, string field, string value)
        {
            var z = RzZ();
            var k = z.Set.Links.FirstOrDefault(l => l.Id == id);
            if (k == null) throw new McpEditorException("There is no link " + id + " (zone_links shows them).");
            string v = (value ?? "").Trim();
            Func<int> num = () => { int n; if (!int.TryParse(v, out n)) throw new ArgumentException("\"" + v + "\" is not a whole number."); return n; };
            Func<bool> yes = () => { bool bv; if (bool.TryParse(v, out bv)) return bv; if (v == "1") return true; if (v == "0") return false; throw new ArgumentException("\"" + v + "\" is not true or false."); };
            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "from": { int n = num(); RzZoneOf(z, n); k.From = n; break; }
                case "to": { int n = num(); RzZoneOf(z, n); k.To = n; break; }
                case "kind":
                    if (!ZoneLink.Kinds.Contains(v)) throw new ArgumentException("The kind is one of " + string.Join(", ", ZoneLink.Kinds) + ".");
                    k.Kind = v; break;
                case "requires": k.Requires = v.Length == 0 ? "free" : v; break;
                case "twoway": k.TwoWay = yes(); break;
                case "notes": k.Notes = value ?? ""; break;
                case "confidence":
                    if (v != "certain" && v != "todo") throw new ArgumentException("Confidence is certain or todo.");
                    k.Confidence = v; break;
                case "once": k.Once = yes(); break;
                case "level": { int n = num(); if (n != 0) CheckLevelNumber(Session, n); k.Level = n; break; }
                case "x": { int n = num(); if (n < -1 || n > 63) throw new ArgumentOutOfRangeException(null, "x is 0-63 (-1 none)."); k.X = n; break; }
                case "y": { int n = num(); if (n < -1 || n > 63) throw new ArgumentOutOfRangeException(null, "y is 0-63 (-1 none)."); k.Y = n; break; }
                default: throw new McpEditorException("The field is from, to, kind, requires, twoWay, notes, confidence, once, level, x or y.");
            }
            z.LinkChanged();
            return "Now " + RzLinkText(z, k) + ".";
        }

        public string ZoneLinkRemove(int id)
        {
            var z = RzZ();
            var k = z.Set.Links.FirstOrDefault(l => l.Id == id);
            if (k == null) throw new McpEditorException("There is no link " + id + ".");
            z.RemoveLink(k);
            return "Link " + id + " removed.";
        }

        /// <summary>Adds a grant to a zone (one token: key:5, item:medallion, flag:gazer_dead, talisman:cup...), optionally only when requires holds.</summary>
        public string ZoneGrantAdd(int zone, string grants, string requires, string notes)
        {
            var z = RzZ();
            RzZoneOf(z, zone);
            if (string.IsNullOrWhiteSpace(grants)) throw new McpEditorException("Give the token the zone grants (key:5, item:medallion, flag:gazer_dead...).");
            z.Set.Placements.Add(new Placement { Zone = zone, Grants = grants.Trim(), Requires = (requires ?? "").Trim(), Notes = notes ?? "" });
            z.LinkChanged();
            return "Zone " + z.Set.Label(zone) + " grants " + grants.Trim() + (string.IsNullOrWhiteSpace(requires) ? "" : " if " + requires.Trim()) + ".";
        }

        /// <summary>Removes a zone's grant by its number in zone_get's list.</summary>
        public string ZoneGrantRemove(int zone, int number)
        {
            var z = RzZ();
            RzZoneOf(z, zone);
            var l = z.Set.Placements.Where(p => p.Zone == zone).ToList();
            if (number < 0 || number >= l.Count) throw new ArgumentOutOfRangeException(null, "Zone " + zone + " has grants 0-" + (l.Count - 1) + ".");
            z.Set.Placements.Remove(l[number]);
            z.LinkChanged();
            return "Removed the grant " + l[number].Grants + " of zone " + z.Set.Label(zone) + ".";
        }

        /// <summary>The Zones window's "Add them to the zone's grants": the grants the key items lying in the zone suggest.</summary>
        public string ZoneGrantsSuggest(int zone)
        {
            var z = RzZ();
            RzZoneOf(z, zone);
            var items = z.Items.Where(i => i.Zone == zone).ToList();
            var add = KeyItems.SuggestPlacements(z.Set, items);
            z.Set.Placements.AddRange(add);
            z.LinkChanged();
            return add.Count + " grant(s) added to zone " + z.Set.Label(zone) + (add.Count > 0 ? ": " + string.Join(", ", add.Select(p => p.Grants)) : "") + ".";
        }

        /// <summary>The key items of the levels (optionally of one zone), with their zone.</summary>
        public string ZoneKeyItems(int? zone)
        {
            var z = RzZ();
            var items = z.Items.Where(i => zone == null || i.Zone == zone).OrderBy(i => i.Level).ThenBy(i => i.Index).ToList();
            if (items.Count == 0) return "No key items" + (zone != null ? " in zone " + zone : "") + ".";
            return items.Count + " key item(s):\n" + string.Join("\n", items.Take(600).Select(i => i + " (zone " + z.Set.Label(i.Zone) + (i.Marked ? ", marked" : "") + ")"));
        }

        /// <summary>Marks an object as a key item (true), not one (false), or takes the mark away (null).</summary>
        public string ZoneMarkItem(int level, int index, bool? important)
        {
            var z = RzZ();
            var m = ReadModel(level);
            if (index < 1 || index >= m.Objects.Length || m.Objects[index].Free) throw new McpEditorException("Level " + level + " has no object #" + index + ".");
            z.Mark(level, index, important);
            return string.Format("Level {0} #{1} {2}.", level, index, important == true ? "is a key item now" : important == false ? "is left out of the key items" : "is unmarked");
        }

        /// <summary>The doors between zones, and what each needs.</summary>
        public string ZoneDoorsList(int? level)
        {
            var z = RzZ();
            var d = z.Doors.Where(x => level == null || x.Level == level).ToList();
            if (d.Count == 0) return "No doors between zones" + (level != null ? " on level " + level : "") + ".";
            return d.Count + " door(s) between zones:\n" + string.Join("\n", d.Take(600).Select(x => x + (string.IsNullOrEmpty(x.Notes) ? "" : " (" + x.Notes + ")")));
        }

        /// <summary>The zoning's start zone (the solver's start is the game's start square; this names its zone).</summary>
        public string ZonesSetStart(int zone)
        {
            var z = RzZ();
            RzZoneOf(z, zone);
            z.Set.StartZone = zone;
            z.LinkChanged();
            return "The start zone is " + RzZoneLabel(z, zone) + ".";
        }

        /// <summary>Split multi-level zones by level for the solver (each level its own zone, joined by free two-way links).</summary>
        public string ZonesSplit(bool on)
        {
            var z = RzZ();
            z.SplitByLevel = on;
            return "Split by level for the solver: " + (on ? "on" : "off") + ".";
        }

        /// <summary>Mark as goal: a square that ends the game (a move trigger and hack trap 0x3F) on a level (a level edit: undo takes it away).</summary>
        public string ZoneMarkGoal(int level, int x, int y)
        {
            var e = Editable(level);
            var r = TrapRecipes.EndOfGame(e, x, y);
            return r.Summary + " Undo takes it away; save writes the level.";
        }

        /// <summary>The zoning's file: new (empty), open a file, save (to its file or another), example (UW1: the zoning shipped with the editor).</summary>
        public string ZonesFile(string action, string path, bool discard)
        {
            var z = RzZ();
            string a = (action ?? "").Trim().ToLowerInvariant();
            if ((a == "new" || a == "open" || a == "example") && z.Dirty && !discard) throw new McpEditorException("The zoning has unsaved changes: save first, or give discard.");
            switch (a)
            {
                case "new": z.NewFile(); return "A new zoning (not saved yet: " + z.Path + ").";
                case "open":
                    {
                        if (string.IsNullOrWhiteSpace(path)) throw new McpEditorException("Give the zone file to open.");
                        string p = Path.GetFullPath(path.Trim().Trim('"'));
                        if (!File.Exists(p)) throw new McpEditorException("There is no file " + p + ".");
                        z.Open(p);
                        return "Opened " + p + ": " + z.Set.Zones.Count(x => x.Id > ZoneSet.Unassigned) + " zones, " + z.Set.Links.Count + " links.";
                    }
                case "save":
                    {
                        string p = string.IsNullOrWhiteSpace(path) ? z.Path : Path.GetFullPath(path.Trim().Trim('"'));
                        z.Save(p);
                        return "Saved the zoning to " + p + " (an old file kept once as .BKP).";
                    }
                case "example":
                    if (Session.IsUw2) throw new McpEditorException("The example zoning is for UW1.");
                    if (!z.LoadExample()) throw new McpEditorException("The example zoning ships inside the editor's program: load it in the editor's window (or open a zone file).");
                    return "The UW1 example zoning is loaded (not saved yet).";
            }
            throw new McpEditorException("The action is new, open, save or example.");
        }

        /// <summary>A level's zones as a picture file (the Zones window's export), or painted from one (import).</summary>
        public string ZonePicture(int level, string action, string path, bool overwrite)
        {
            var z = RzZ();
            CheckLevelNumber(Session, level);
            if (string.IsNullOrWhiteSpace(path)) throw new McpEditorException("Give the picture file.");
            string p = Path.GetFullPath(path.Trim().Trim('"'));
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "export":
                    {
                        if (z.Set.Tiles[level - 1] == null) throw new McpEditorException("Level " + level + " has no zone map yet.");
                        if (File.Exists(p) && !overwrite) throw new McpEditorException(p + " exists: give another name, or overwrite.");
                        Directory.CreateDirectory(Path.GetDirectoryName(p));
                        File.WriteAllBytes(p, z.Set.ExportPicture(level));
                        return "Wrote level " + level + "'s zones to " + p + ".";
                    }
                case "import":
                    {
                        if (!File.Exists(p)) throw new McpEditorException("There is no file " + p + ".");
                        z.EnsureLevel(level);
                        string said = z.Set.ImportPicture(level, File.ReadAllBytes(p));
                        z.LinkChanged();
                        return said ?? ("Painted level " + level + " from " + p + ".");
                    }
            }
            throw new McpEditorException("The action is export or import.");
        }
    }
}
