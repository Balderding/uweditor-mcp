using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The game's data tables (not a level's): OBJECTS.DAT and COMOBJ.DAT (ObjectTables), TERRAIN.DAT,
    /// SKILLS.DAT, CMB.DAT, SHADES.DAT and UW2's DL.DAT (the game tables), and STRINGS.PAK.
    ///
    /// Each is held as the editor's own windows hold it: in memory until save, written with SafeFile
    /// (NAME.EXT.ORIG once, NAME.EXT.BKP each time). Attached to the app, a table its window has open
    /// (Object tables, Game tables, the strings everything shares) is the one changed, so the window
    /// shows the change and saving either side writes it; else this server keeps its own copy. A copy
    /// without changes is read again when its file changed on disk; one with changes is not written
    /// over a file changed on disk since.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>Attached: the app's object tables while its Object tables window is open, else null.</summary>
        public Func<ObjectTables> HostObjectTables { get; set; }
        /// <summary>Attached: the app's game tables while its Game tables window is open, else null.</summary>
        public Func<IList<DataTable>> HostGameTables { get; set; }
        /// <summary>Attached: the app's STRINGS.PAK editor (shared by its strings, keys and conversation windows), or null.</summary>
        public Func<StringsEditor> HostStrings { get; set; }
        /// <summary>Attached: after a table changed (so a window without change events redraws).</summary>
        public Action HostTablesEdited { get; set; }
        /// <summary>Attached: after table files were written, with their names (so the views read them again).</summary>
        public Action<IList<string>> HostTablesSaved { get; set; }

        GameSession ownFor;
        ObjectTables ownObjects;
        byte[] ownObjectsDisk, ownComObjDisk;
        readonly Dictionary<Type, DataTable> ownData = new Dictionary<Type, DataTable>();
        StringsEditor ownStrings;
        byte[] ownStringsDisk;

        /// <summary>The own copies belong to one game: another game open drops them.</summary>
        void TblFresh()
        {
            Require();
            if (ownFor == Session) return;
            ownFor = Session;
            ownObjects = null; ownData.Clear(); ownStrings = null;
        }

        static bool TblDiffers(string path, byte[] read)
        {
            if (path == null || read == null) return false;
            try { return !File.ReadAllBytes(path).SequenceEqual(read); }
            catch (IOException) { return false; }
        }

        static string TblBusy(string files, string window)
        {
            return "The editor's " + window + " window is open now, without your unsaved changes to " + files + ": close it first, or table_revert them.";
        }

        ObjectTables TblObjects()
        {
            TblFresh();
            var h = HostObjectTables?.Invoke();
            if (h != null && h.Session == Session)
            {
                if (ownObjects != null && ownObjects.HasChanges) throw new McpEditorException(TblBusy("OBJECTS.DAT/COMOBJ.DAT", "Object tables"));
                ownObjects = null;
                return h;
            }
            if (ownObjects == null || (!ownObjects.HasChanges && (TblDiffers(ownObjects.ObjectsPath, ownObjectsDisk) || TblDiffers(ownObjects.ComObjPath, ownComObjDisk))))
            {
                var t = ObjectTables.Load(Session);
                ownObjectsDisk = File.ReadAllBytes(t.ObjectsPath);
                ownComObjDisk = File.ReadAllBytes(t.ComObjPath);
                ownObjects = t;
            }
            return ownObjects;
        }

        T TblData<T>(Func<GameSession, T> load) where T : DataTable
        {
            TblFresh();
            DataTable own;
            ownData.TryGetValue(typeof(T), out own);
            var hosted = HostGameTables?.Invoke();
            var h = hosted == null ? null : hosted.OfType<T>().FirstOrDefault();
            T t;
            if (h != null && h.Session == Session)
            {
                if (own != null && own.HasChanges) throw new McpEditorException(TblBusy(own.FileName, "Game tables"));
                ownData.Remove(typeof(T));
                t = h;
            }
            else
            {
                if (own == null || (!own.HasChanges && TblDiffers(own.Path, own.OriginalBytes))) ownData[typeof(T)] = own = load(Session);
                t = (T)own;
            }
            if (!t.Exists) throw new McpEditorException("There is no " + t.FileName + " in " + Session.DataFolder + (t is MinimumLightTable && !Session.IsUw2 ? " (only Underworld II has one)." : "."));
            return t;
        }

        StringsEditor TblStrings()
        {
            TblFresh();
            var h = HostStrings?.Invoke();
            if (h != null && h.Session == Session)
            {
                if (ownStrings != null && ownStrings.HasChanges) throw new McpEditorException("The editor has the strings open now, without your unsaved changes to STRINGS.PAK.");
                ownStrings = null;
                return h;
            }
            if (ownStrings == null || (!ownStrings.HasChanges && TblDiffers(ownStrings.Pak.Path, ownStringsDisk)))
            {
                var s = StringsEditor.Open(Session);
                ownStringsDisk = File.ReadAllBytes(s.Pak.Path);
                ownStrings = s;
            }
            return ownStrings;
        }

        // ---- saving (save calls these)

        IEnumerable<DataTable> TblOwnData() { return ownFor == Session ? ownData.Values.ToList() : new List<DataTable>(); }

        /// <summary>Refuses (writing nothing) when one of the own changed copies' files was changed on disk since it was read.</summary>
        void CheckTablesOnDisk()
        {
            if (ownFor != Session) return;
            var stale = new List<string>();
            if (ownObjects != null && ownObjects.HasChanges)
            {
                if (TblDiffers(ownObjects.ObjectsPath, ownObjectsDisk)) stale.Add(ObjectTables.ObjectsFile);
                if (TblDiffers(ownObjects.ComObjPath, ownComObjDisk)) stale.Add(ObjectTables.ComObjFile);
            }
            foreach (var d in TblOwnData()) if (d.HasChanges && TblDiffers(d.Path, d.OriginalBytes)) stale.Add(d.FileName);
            if (ownStrings != null && ownStrings.HasChanges && TblDiffers(ownStrings.Pak.Path, ownStringsDisk)) stale.Add("STRINGS.PAK");
            if (stale.Count > 0)
                throw new McpEditorException(string.Join(", ", stale) + " changed on disk since your changes began (saved from the editor?); writing would lose that. Nothing was written: table_revert drops your changes, then make them again.");
        }

        /// <summary>The tables with unsaved changes, own and the host's.</summary>
        List<object> TblChanged()
        {
            var l = new List<object>();
            if (Session == null) return l;
            var ho = HostObjectTables?.Invoke();
            if (ho != null && ho.Session == Session && ho.HasChanges) l.Add(ho);
            if (ownFor == Session && ownObjects != null && ownObjects.HasChanges) l.Add(ownObjects);
            var hd = HostGameTables?.Invoke();
            if (hd != null) foreach (var d in hd) if (d != null && d.Session == Session && d.Exists && d.HasChanges) l.Add(d);
            foreach (var d in TblOwnData()) if (d.Exists && d.HasChanges) l.Add(d);
            var hs = HostStrings?.Invoke();
            if (hs != null && hs.Session == Session && hs.HasChanges) l.Add(hs);
            if (ownFor == Session && ownStrings != null && ownStrings.HasChanges) l.Add(ownStrings);
            return l;
        }

        /// <summary>Writes every changed table (own copies and the open windows' tables); answers what was written, or null.</summary>
        string SaveTables()
        {
            var written = new List<string>();
            foreach (var t in TblChanged())
            {
                if (t is ObjectTables o)
                {
                    written.AddRange(o.Save());
                    if (o == ownObjects) { ownObjectsDisk = File.ReadAllBytes(o.ObjectsPath); ownComObjDisk = File.ReadAllBytes(o.ComObjPath); }
                }
                else if (t is DataTable d)
                {
                    d.Save();
                    if (d is TerrainTable) Automap.TerrainChanged();
                    written.Add(d.FileName);
                }
                else if (t is StringsEditor s)
                {
                    s.Save();
                    if (s == ownStrings) ownStringsDisk = File.ReadAllBytes(s.Pak.Path);
                    written.Add("STRINGS.PAK");
                }
            }
            if (written.Count == 0) return null;
            HostTablesSaved?.Invoke(written);
            return "Saved " + string.Join(", ", written.Distinct()) + " (the old " + (written.Count == 1 ? "file" : "files") + " kept as .BKP, the first as .ORIG).";
        }

        // ---- the tables

        /// <summary>One table as the tools see it: rows, named fields, values as text.</summary>
        abstract class TblTab
        {
            protected readonly McpEditor ed;
            protected TblTab(McpEditor ed) { this.ed = ed; }
            public abstract string Name { get; }
            public abstract string File { get; }
            public virtual int First { get { return 0; } }
            public abstract int Count { get; }
            /// <summary>How rows are named, for tables.</summary>
            public virtual string RowsHelp { get { return "rows " + First + "-" + (First + Count - 1); } }
            public abstract string[] Fields { get; }
            public abstract string Label(int row);
            public virtual string Help(int field) { return null; }
            public abstract string Value(int row, int field);
            public abstract bool Changed(int row, int field);
            /// <summary>Sets a field from text; answers a note, or null. Throws when refused (nothing changed).</summary>
            public abstract string Set(int row, int field, string value);
            public abstract void Revert();

            public virtual int Row(string text)
            {
                int n;
                if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    throw new ArgumentException(Name + " rows are numbers: " + RowsHelp + ".");
                return CheckRow(n);
            }

            protected int CheckRow(int n)
            {
                if (n < First || n >= First + Count) throw new ArgumentOutOfRangeException(null, Name + " has " + RowsHelp + ".");
                return n;
            }
        }

        static string TblItem(GameSession s, int item) { return Hex(item) + " " + s.Names.Get(item); }

        static int TblNumber(string field, string text, int lo, int hi)
        {
            int v;
            if (!ObjectTables.TryParse(text, out v)) throw new ArgumentException(field + ": \"" + (text ?? "").Trim() + "\" is not a number.");
            if (v < lo || v > hi) throw new ArgumentOutOfRangeException(null, field + " is " + lo + "-" + hi + ".");
            return v;
        }

        static bool TblFlag(string field, string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
            }
            throw new ArgumentException(field + " is 1 or 0 (true or false).");
        }

        /// <summary>An OBJECTS.DAT or COMOBJ.DAT table: a row is an item.</summary>
        sealed class TblObjTab : TblTab
        {
            readonly ObjectTable t;
            public TblObjTab(McpEditor ed, ObjectTable t) : base(ed) { this.t = t; }
            public ObjectTable Table { get { return t; } }
            ObjectTables T { get { return ed.TblObjects(); } }
            public override string Name { get { return t.Name; } }
            public override string File { get { return t.FileName; } }
            public override int Count { get { return t.Count; } }
            public override string RowsHelp { get { return string.Format("rows 0-{0}, or the item: {1}-{2} or its name", t.Count - 1, Hex(t.FirstItem), Hex(t.FirstItem + t.Count - 1)); } }
            public override string[] Fields { get { return t.Schema.Fields.Select(f => f.Name).ToArray(); } }
            public override string Label(int row) { return "row " + row + ", " + TblItem(ed.Session, t.ItemOf(row)); }
            public override string Help(int field) { return ObjectTables.Explain(t, t.Schema.Fields[field]); }

            public override int Row(string text)
            {
                string s = (text ?? "").Trim();
                int n;
                if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return CheckRow(n);
                var m = ed.Matches(s);
                var inside = m.Where(i => i >= t.FirstItem && i < t.FirstItem + t.Count).ToList();
                if (inside.Count == 1) return inside[0] - t.FirstItem;
                if (inside.Count == 0)
                    throw new ArgumentException((m.Count == 1 ? TblItem(ed.Session, m[0]) + " is" : "No item called " + s + " is") + " not in " + t.Name + " (" + RowsHelp + ").");
                throw new ArgumentException(s + " is " + inside.Count + " items of " + t.Name + "; give the number: " + string.Join(", ", inside.Take(20).Select(i => TblItem(ed.Session, i))) + ".");
            }

            public override string Value(int row, int field)
            {
                var tables = T;
                var f = t.Schema.Fields[field];
                int v = tables.Get(t, row, f);
                string meaning = tables.Meaning(t, f, v);
                return v.ToString(CultureInfo.InvariantCulture) + (f.Bits >= 8 && v > 9 ? " (0x" + v.ToString("X") + ")" : "") + (meaning != null ? ", " + meaning : "");
            }

            public override bool Changed(int row, int field) { return T.IsChanged(t, row, t.Schema.Fields[field]); }

            public override string Set(int row, int field, string value)
            {
                var tables = T;
                var f = t.Schema.Fields[field];
                int v;
                if (!ObjectTables.TryParse(value, out v)) throw new ArgumentException(f.Name + ": \"" + (value ?? "").Trim() + "\" is not a number (decimal, or hexadecimal with 0x).");
                string err = tables.Set(t, row, f, v);
                if (err != null) throw new ArgumentException(err);
                return tables.Warning(t, f, v);
            }

            public override void Revert() { var tables = T; for (int r = 0; r < t.Count; r++) if (tables.IsRowChanged(t, r)) tables.RevertRow(t, r); }
        }

        /// <summary>TERRAIN.DAT: a word for each texture.</summary>
        sealed class TblTerrainTab : TblTab
        {
            public TblTerrainTab(McpEditor ed) : base(ed) { }
            TerrainTable T { get { return ed.TblData(TerrainTable.Load); } }
            bool Uw2 { get { return ed.Session.IsUw2; } }
            public override string Name { get { return "Terrain"; } }
            public override string File { get { return "TERRAIN.DAT"; } }
            public override int Count { get { return T.Count; } }
            public override string RowsHelp { get { return "rows 0-" + (Count - 1) + (Uw2 ? " (T64.TR's textures)" : " (0-255 the walls of W64.TR, 256 on the floors of F32.TR; or \"wall N\", \"floor N\")"); } }
            public override string[] Fields { get { return Uw2 ? new[] { "Value", "Class", "Sub", "Kind" } : new[] { "Value" }; } }
            bool Floor(int row) { return !Uw2 && row >= 0x100; }
            public override string Label(int row) { return Uw2 ? "texture " + row : Floor(row) ? "floor texture " + (row - 0x100) : "wall texture " + row; }

            public override string Help(int field)
            {
                if (!Uw2) return "Value: the texture's type word. Walls: 0 normal, 2 ankh, 3 stairs up, 4 stairs down, 5 pipe, 6 grating, 7 drain, 8 chained princess, 9 window, 0xA tapestry, 0xB textured door. Floors: 0 normal, 0x10 water, 0x20 lava. 0-0xFFFF.";
                switch (field)
                {
                    case 0: return "Value: the whole word, 0-0xFFFF.";
                    case 1: return "Class: bits 6-7, 0 normal, 1 water, 2 lava, 3 ice.";
                    case 2: return "Sub: bits 3-5, 0-7: water's current (1 south, 2 north, 3 west, 4 east) or ice's grip (7 grips, 0-6 slide).";
                    default: return "Kind: bits 0-2, 0-7: 3 stairs up, 4 stairs down, 5 a shaft.";
                }
            }

            public override int Row(string text)
            {
                string s = (text ?? "").Trim().ToLowerInvariant();
                int n;
                foreach (var w in new[] { "wall", "floor" })
                    if (s.StartsWith(w, StringComparison.Ordinal) && int.TryParse(s.Substring(w.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                        return CheckRow(T.EntryOf(n, w == "floor"));
                return base.Row(text);
            }

            public override string Value(int row, int field)
            {
                int v = T.Get(row);
                switch (field)
                {
                    case 1: return TerrainTable.Uw2Class(v) + ", " + TerrainTable.Uw2Classes[TerrainTable.Uw2Class(v)];
                    case 2: return TerrainTable.Uw2Sub(v) + ", " + TerrainTable.Uw2SubName(TerrainTable.Uw2Class(v), TerrainTable.Uw2Sub(v));
                    case 3: return TerrainTable.Uw2Kind(v) + ", " + TerrainTable.Uw2KindName(TerrainTable.Uw2Kind(v));
                }
                return "0x" + v.ToString("X") + ", " + TerrainTable.Describe(ed.Session.Kind, Floor(row), v);
            }

            public override bool Changed(int row, int field) { return T.IsChanged(row); }

            public override string Set(int row, int field, string value)
            {
                var t = T;
                int v = t.Get(row);
                switch (field)
                {
                    case 0: v = TblNumber("Value", value, 0, 0xFFFF); break;
                    case 1: v = TerrainTable.Uw2Compose(v, TblNumber("Class", value, 0, 3), TerrainTable.Uw2Sub(v), TerrainTable.Uw2Kind(v)); break;
                    case 2: v = TerrainTable.Uw2Compose(v, TerrainTable.Uw2Class(v), TblNumber("Sub", value, 0, 7), TerrainTable.Uw2Kind(v)); break;
                    default: v = TerrainTable.Uw2Compose(v, TerrainTable.Uw2Class(v), TerrainTable.Uw2Sub(v), TblNumber("Kind", value, 0, 7)); break;
                }
                t.Set(row, v);
                return null;
            }

            public override void Revert() { T.Revert(); }
        }

        /// <summary>SKILLS.DAT: each class's attributes, bonus points and five skill entries.</summary>
        sealed class TblSkillsTab : TblTab
        {
            public TblSkillsTab(McpEditor ed) : base(ed) { }
            SkillsTable T { get { return ed.TblData(SkillsTable.Load); } }
            static readonly string[] fields = { "Strength", "Dexterity", "Intelligence", "Bonus points", "Entry 1", "Entry 2", "Entry 3", "Entry 4", "Entry 5" };
            public override string Name { get { return "Skills"; } }
            public override string File { get { return "SKILLS.DAT"; } }
            public override int Count { get { return UltimateEditor.Uw1.SkillsDat.Classes; } }
            public override string RowsHelp { get { return "rows 0-7, the classes (" + string.Join(", ", Enumerable.Range(0, Count).Select(SkillsTable.ClassName)) + ")"; } }
            public override string[] Fields { get { return fields; } }
            public override string Label(int row) { return "class " + row + ", " + SkillsTable.ClassName(row); }

            public override string Help(int field)
            {
                if (field < 3) return fields[field] + ": the class's starting attribute, 0-255 (bonus points never take one above 30).";
                if (field == 3) return "Bonus points: shared out over the attributes at character creation, 0-255.";
                return fields[field] + ": the skills the entry offers: none gives nothing, one is given, several make a menu to pick one. Set as skill numbers (0-19) or names, comma-separated; none to empty it.";
            }

            public override int Row(string text)
            {
                string s = (text ?? "").Trim();
                for (int c = 0; c < Count; c++) if (string.Equals(SkillsTable.ClassName(c), s, StringComparison.OrdinalIgnoreCase)) return c;
                return base.Row(text);
            }

            public override string Value(int row, int field)
            {
                var t = T;
                if (field < 4) return t.Attribute(row, field).ToString(CultureInfo.InvariantCulture);
                var c = t.Choice(row, field - 4);
                return (c.Count == 0 ? "none" : string.Join(", ", c)) + ": " + t.Explain(row, field - 4);
            }

            public override bool Changed(int row, int field) { return field < 4 ? T.AttributeChanged(row, field) : T.ChoiceChanged(row, field - 4); }

            public override string Set(int row, int field, string value)
            {
                var t = T;
                if (field < 4) { t.SetAttribute(row, field, TblNumber(fields[field], value, 0, 255)); }
                else
                {
                    var skills = new List<int>();
                    string s = (value ?? "").Trim();
                    if (s.Length > 0 && !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
                        foreach (var part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string p = part.Trim();
                            int k;
                            if (!ObjectTables.TryParse(p, out k))
                            {
                                k = Enumerable.Range(0, UltimateEditor.Uw1.SkillsDat.Skills).FirstOrDefault(i => string.Equals(t.SkillName(i), p, StringComparison.OrdinalIgnoreCase), -1);
                                if (k < 0) throw new ArgumentException("No skill is called " + p + "; the skills are " + string.Join(", ", Enumerable.Range(0, UltimateEditor.Uw1.SkillsDat.Skills).Select(i => i + " " + t.SkillName(i))) + ".");
                            }
                            if (k < 0 || k >= UltimateEditor.Uw1.SkillsDat.Skills) throw new ArgumentOutOfRangeException(null, "Skills are 0-19.");
                            if (!skills.Contains(k)) skills.Add(k);
                        }
                    t.SetChoice(row, field - 4, skills);
                }
                var warn = t.Check().Where(w => w.StartsWith(SkillsTable.ClassName(row) + ":", StringComparison.Ordinal)).ToList();
                return warn.Count == 0 ? null : string.Join(" ", warn);
            }

            public override void Revert() { T.Revert(); }
        }

        /// <summary>CMB.DAT: ten rules, two sources and an output.</summary>
        sealed class TblCombinationTab : TblTab
        {
            public TblCombinationTab(McpEditor ed) : base(ed) { }
            CombinationTable T { get { return ed.TblData(CombinationTable.Load); } }
            static readonly string[] fields = { "First", "First used up", "Second", "Second used up", "Output" };
            public override string Name { get { return "Combinations"; } }
            public override string File { get { return "CMB.DAT"; } }
            public override int First { get { return 1; } }
            public override int Count { get { return UltimateEditor.Uw1.CmbDat.Rules; } }
            public override string RowsHelp { get { return "rows 1-10, the rules (the first naming both items is used)"; } }
            public override string[] Fields { get { return fields; } }
            public override string Label(int row) { return "rule " + row + (T.IsEmpty(row - 1) ? ", empty" : T.Active(row - 1) ? "" : ", not used by the game"); }
            static int Which(int field) { return field < 2 ? 0 : field < 4 ? 1 : 2; }

            public override string Help(int field)
            {
                return field == 1 || field == 3 ? fields[field] + ": 1 the source is used up by combining, 0 kept (UW1 takes a rule only with a source used up)."
                    : fields[field] + ": an item, by number or name.";
            }

            public override string Value(int row, int field)
            {
                var t = T;
                int w = Which(field);
                if (field == 1 || field == 3) return t.UsedUp(row - 1, w) ? "1, used up" : "0, kept";
                return TblItem(ed.Session, t.Item(row - 1, w));
            }

            public override bool Changed(int row, int field) { return T.Changed(row - 1, Which(field)); }

            public override string Set(int row, int field, string value)
            {
                var t = T;
                int w = Which(field);
                if (field == 1 || field == 3) t.SetUsedUp(row - 1, w, TblFlag(fields[field], value));
                else
                {
                    int item = ed.ItemOf(value);
                    if (t.IsEmpty(row - 1)) t.MakeRule(row - 1, w == 0 ? item : 0, w == 1 ? item : 0, w == 2 ? item : 0);
                    else t.SetItem(row - 1, w, item);
                }
                var warn = t.Check().Where(x => x.StartsWith("Rule " + row + " ", StringComparison.Ordinal) || x.StartsWith("Rule " + row + ":", StringComparison.Ordinal)).ToList();
                return warn.Count == 0 ? null : string.Join(" ", warn);
            }

            public override void Revert() { T.Revert(); }
        }

        /// <summary>SHADES.DAT: six values for each of the eight light levels.</summary>
        sealed class TblShadesTab : TblTab
        {
            public TblShadesTab(McpEditor ed) : base(ed) { }
            ShadesTable T { get { return ed.TblData(ShadesTable.Load); } }
            public override string Name { get { return "Shades"; } }
            public override string File { get { return "SHADES.DAT"; } }
            public override int Count { get { return 8; } }
            public override string RowsHelp { get { return "rows 0-7, the light levels"; } }
            public override string[] Fields { get { return UltimateEditor.Uw1.ShadesDat.Columns; } }

            public override string Label(int row)
            {
                var n = UltimateEditor.Uw1.ShadesDat.LevelNames;
                return "light level " + row + (row < n.Length ? ", " + n[row] : "");
            }

            public override string Help(int field)
            {
                var h = UltimateEditor.Uw1.ShadesDat.ColumnHelp;
                return Fields[field] + ": " + (field < h.Length ? h[field] + " " : "") + ShadesTable.Min[field] + "-" + ShadesTable.Max[field] + ".";
            }

            public override string Value(int row, int field) { return T.Get(row, field).ToString(CultureInfo.InvariantCulture); }
            public override bool Changed(int row, int field) { return T.IsChanged(row, field); }
            public override string Set(int row, int field, string value) { T.Set(row, field, TblNumber(Fields[field], value, ShadesTable.Min[field], ShadesTable.Max[field])); return null; }
            public override void Revert() { T.Revert(); }
        }

        /// <summary>UW2's DL.DAT: each level's minimum light.</summary>
        sealed class TblMinimumLightTab : TblTab
        {
            public TblMinimumLightTab(McpEditor ed) : base(ed) { }
            MinimumLightTable T { get { return ed.TblData(MinimumLightTable.Load); } }
            static readonly string[] fields = { "Minimum", "On unflagged" };
            public override string Name { get { return "Minimum light"; } }
            public override string File { get { return "DL.DAT"; } }
            public override int First { get { return 1; } }
            public override int Count { get { return ed.Session.IsUw2 ? T.Count : 0; } }
            public override string RowsHelp { get { return ed.Session.IsUw2 ? "rows 1-" + Count + ", the levels" : "no rows (only Underworld II has DL.DAT)"; } }
            public override string[] Fields { get { return fields; } }
            public override string Label(int row) { return "level " + row + ", " + T.Explain(row); }

            public override string Help(int field)
            {
                return field == 0 ? "Minimum: the level's minimum light, 0-9 (0 none)."
                    : "On unflagged: 1 the minimum applies on squares without the light flag, 0 on squares with it.";
            }

            public override string Value(int row, int field) { return field == 0 ? T.Minimum(row).ToString(CultureInfo.InvariantCulture) : T.OnUnflagged(row) ? "1" : "0"; }
            public override bool Changed(int row, int field) { return T.IsChanged(row); }

            public override string Set(int row, int field, string value)
            {
                var t = T;
                if (field == 0) t.Set(row, TblNumber("Minimum", value, 0, 9), t.OnUnflagged(row));
                else t.Set(row, t.Minimum(row), TblFlag("On unflagged", value));
                return null;
            }

            public override void Revert() { T.Revert(); }
        }

        List<TblTab> TblTabs()
        {
            Require();
            var l = new List<TblTab>();
            foreach (var t in ObjectTables.Layout) l.Add(new TblObjTab(this, t));
            l.Add(new TblTerrainTab(this));
            l.Add(new TblSkillsTab(this));
            l.Add(new TblCombinationTab(this));
            l.Add(new TblShadesTab(this));
            if (Session.IsUw2) l.Add(new TblMinimumLightTab(this));
            return l;
        }

        static string TblPlain(string s) { return new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()); }

        TblTab TblTabOf(string name)
        {
            var all = TblTabs();
            string p = TblPlain(name);
            if (p.Length == 0) throw new ArgumentException("Give a table: " + string.Join(", ", all.Select(t => t.Name)) + ".");
            var m = all.Where(t => TblPlain(t.Name) == p || (TblPlain(t.File) == p && all.Count(u => u.File == t.File) == 1) || TblPlain(Path.GetFileNameWithoutExtension(t.File)) == p && all.Count(u => u.File == t.File) == 1).ToList();
            if (m.Count == 0) m = all.Where(t => TblPlain(t.Name).StartsWith(p, StringComparison.Ordinal)).ToList();
            if (m.Count == 1) return m[0];
            throw new ArgumentException((m.Count == 0 ? "No table is called " + name : name + " is " + m.Count + " tables") + "; the tables are " + string.Join(", ", (m.Count == 0 ? all : m).Select(t => t.Name)) + ".");
        }

        /// <summary>A field by name: exact (any case, spaces and signs ignored), else the one whose name holds the text.</summary>
        static int TblFieldOf(TblTab t, string field)
        {
            var names = t.Fields;
            string p = TblPlain(field);
            if (p.Length > 0)
            {
                for (int i = 0; i < names.Length; i++) if (TblPlain(names[i]) == p) return i;
                var m = Enumerable.Range(0, names.Length).Where(i => TblPlain(names[i]).Contains(p)).ToList();
                if (m.Count == 1) return m[0];
                if (m.Count > 1) throw new ArgumentException(field + " is " + m.Count + " fields of " + t.Name + ": " + string.Join(", ", m.Select(i => names[i])) + ".");
            }
            throw new ArgumentException(t.Name + " has no field " + field + "; its fields are " + string.Join(", ", names) + ".");
        }

        /// <summary>The tables there are, with their rows and fields; with a table, each of its fields explained.</summary>
        public string Tables(string table)
        {
            var b = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(table))
            {
                var t = TblTabOf(table);
                b.Append(t.Name).Append(" (").Append(t.File).Append("): ").Append(t.RowsHelp).Append(".\n");
                if (t is TblObjTab o) b.Append(o.Table.Summary).Append('\n');
                for (int i = 0; i < t.Fields.Length; i++) b.Append("- ").Append(t.Help(i) ?? t.Fields[i]).Append('\n');
                return b.ToString();
            }
            foreach (var t in TblTabs())
                b.Append(t.Name).Append(" (").Append(t.File).Append("): ").Append(t.RowsHelp).Append(". Fields: ").Append(string.Join(", ", t.Fields)).Append(".\n");
            b.Append("Strings (STRINGS.PAK): string_get and string_set; item names are block 4, the item's number the index.\n");
            return b.ToString();
        }

        string TblRowText(TblTab t, int row)
        {
            var b = new StringBuilder();
            b.Append(t.Name).Append(", ").Append(t.Label(row)).Append(" (").Append(t.File).Append("):\n");
            for (int i = 0; i < t.Fields.Length; i++)
            {
                b.Append(t.Fields[i]).Append(" = ").Append(t.Value(row, i));
                if (t.Changed(row, i)) b.Append("  [changed, not saved]");
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Every field of one row with its value.</summary>
        public string TableGet(string table, string row)
        {
            var t = TblTabOf(table);
            return TblRowText(t, t.Row(row));
        }

        /// <summary>Sets one field of one row (in memory until save).</summary>
        public string TableSet(string table, string row, string field, string value)
        {
            var t = TblTabOf(table);
            int r = t.Row(row);
            int f = TblFieldOf(t, field);
            string before = t.Value(r, f);
            string note = t.Set(r, f, value);
            HostTablesEdited?.Invoke();
            return t.Name + ", " + t.Label(r) + ": " + t.Fields[f] + " " + before + " -> " + t.Value(r, f) + ". Not saved until save." + (note != null ? " Note: " + note : "");
        }

        /// <summary>Drops the unsaved changes of one table.</summary>
        public string TableRevert(string table)
        {
            var t = TblTabOf(table);
            t.Revert();
            HostTablesEdited?.Invoke();
            return t.Name + " is back as in " + t.File + (t is TblObjTab ? "" : " (the whole file)") + ".";
        }

        /// <summary>The creatures' table row of a creature, by name or item number (0x40-0x7F, 64-127).</summary>
        int TblCreatureRow(string creature)
        {
            var t = ObjectTables.Table("Creatures");
            string s = (creature ?? "").Trim();
            int n;
            if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n)) s = "0x" + n.ToString("X");
            var m = Matches(s).Where(i => i >= t.FirstItem && i < t.FirstItem + t.Count).ToList();
            if (m.Count == 1) return m[0] - t.FirstItem;
            if (m.Count == 0) throw new ArgumentException("No creature (items 0x040-0x07F) is " + creature + ".");
            throw new ArgumentException(creature + " is " + m.Count + " creatures; give the number: " + string.Join(", ", m.Take(20).Select(i => TblItem(Session, i))) + ".");
        }

        public string CreatureGet(string creature)
        {
            var t = TblTabOf("Creatures");
            return TblRowText(t, TblCreatureRow(creature));
        }

        public string CreatureSet(string creature, string field, string value)
        {
            int r = TblCreatureRow(creature);
            return TableSet("Creatures", r.ToString(CultureInfo.InvariantCulture), field, value);
        }

        // ---- STRINGS.PAK

        static int TblBlock(string block)
        {
            int n;
            if (!ObjectTables.TryParse(block, out n) || n < 0 || n > 0xFFFF) throw new ArgumentException("A block is a number: 4 or 0x0004.");
            return n;
        }

        /// <summary>The longest answer string_get gives, in characters.</summary>
        public const int MaxStringsText = 30000;

        /// <summary>The blocks (no block given), a block's strings, or one string.</summary>
        public string StringGet(string block, int? index)
        {
            var st = TblStrings();
            var b = new StringBuilder();
            if (string.IsNullOrWhiteSpace(block))
            {
                foreach (var info in st.Blocks()) b.Append(info.Label).Append('\n');
                return b.ToString();
            }
            int bl = TblBlock(block);
            if (!st.Pak.Has(bl)) throw new ArgumentException("STRINGS.PAK has no block " + bl + " (0x" + bl.ToString("X4") + ").");
            var l = st.Get(bl);
            if (index.HasValue)
            {
                if (index.Value < 0 || index.Value >= l.Count) throw new ArgumentOutOfRangeException(null, "Block " + bl + " has strings 0-" + (l.Count - 1) + ".");
                return l[index.Value];
            }
            b.Append("Block ").Append(bl).Append(" (0x").Append(bl.ToString("X4")).Append("), ").Append(StringsEditor.Describe(Session, bl)).Append(", ").Append(l.Count).Append(" strings:\n");
            for (int i = 0; i < l.Count; i++)
            {
                string line = i + ": " + (l[i] ?? "").Replace("\n", "\\n") + "\n";
                if (b.Length + line.Length > MaxStringsText) { b.Append("... strings ").Append(i).Append('-').Append(l.Count - 1).Append(" not shown: give the index.\n"); break; }
                b.Append(line);
            }
            return b.ToString();
        }

        /// <summary>Changes one string, or adds one at the end (index = the block's count).</summary>
        public string StringSet(string block, int index, string text)
        {
            var st = TblStrings();
            int bl = TblBlock(block);
            if (!st.Pak.Has(bl)) throw new ArgumentException("STRINGS.PAK has no block " + bl + " (0x" + bl.ToString("X4") + ").");
            int count = st.Get(bl).Count;
            if (index < 0 || index > count) throw new ArgumentOutOfRangeException(null, "Block " + bl + " has strings 0-" + (count - 1) + "; " + count + " adds one at the end.");
            if ((text ?? "").Length > UwEditor.Core.Conversations.StringsPakFile.MaxStringLength) throw new ArgumentException("A string is at most " + UwEditor.Core.Conversations.StringsPakFile.MaxStringLength + " characters.");
            string before = index < count ? st.Get(bl, index) : null;
            try { st.Set(bl, index, text ?? ""); }
            catch (FormatException e) { throw new ArgumentException(e.Message); }
            HostTablesEdited?.Invoke();
            return "Block " + bl + " string " + index + ": " + (before == null ? "added" : "\"" + before + "\" ->") + " \"" + st.Get(bl, index) + "\". Not saved until save.";
        }
    }
}
