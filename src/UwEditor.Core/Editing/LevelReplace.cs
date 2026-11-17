using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>What Replace does to each object found: it becomes another item, or one of its fields is set.</summary>
    public sealed class ReplaceAction
    {
        /// <summary>The item the objects become (their other fields kept), or −1 to set a field instead.</summary>
        public int NewItem = -1;
        /// <summary>The field to set (a name of LevelReplace.Fields) when NewItem is −1.</summary>
        public string Field;
        public int Value;

        public string Describe(ObjectNames names)
        {
            return NewItem >= 0 ? "become " + names.Get(NewItem) + " (0x" + NewItem.ToString("X") + ")" : "get " + Field + " " + Value;
        }
    }

    /// <summary>What a replace did.</summary>
    public sealed class ReplaceResult
    {
        public int Changed, Same, Refused;
        public readonly SortedSet<int> Levels = new SortedSet<int>();
        /// <summary>Why the first refused object was refused.</summary>
        public string Why;

        public string Describe()
        {
            var s = string.Format("{0} object{1} changed on {2} level{3}", Changed, Changed == 1 ? "" : "s", Levels.Count, Levels.Count == 1 ? "" : "s");
            if (Same > 0) s += ", " + Same + " already so";
            if (Refused > 0) s += "; " + Refused + " left as " + (Refused == 1 ? "it was" : "they were") + (Why != null ? ": " + Why : "");
            return s + ".";
        }
    }

    /// <summary>
    /// Find and replace across levels: every object of a search's results becomes another item,
    /// or gets one of its fields set, as one undo step over all the levels touched (Edit ▸ Undo
    /// on any of them takes the whole replace back). Each object goes through the same checks
    /// as a field typed in the panel (a creature stays a creature, an item an item, a link
    /// that holds objects is not overwritten), and one that is refused is left as it was and
    /// counted.
    /// </summary>
    public static class LevelReplace
    {
        /// <summary>The fields Replace can set: every field of an object's record that is typed in the panel.</summary>
        public static List<string> Fields
        {
            get { return Uw1Schemas.StaticObject.Fields.Where(f => !LevelEditor.IsListField(f.Name) && f.Name != "Item id").Select(f => f.Name).ToList(); }
        }

        public static ReplaceResult Apply(LevelEdits edits, IEnumerable<SearchHit> hits, ReplaceAction a)
        {
            var s = edits.Session;
            var S = Uw1Schemas.StaticObject;
            FieldDef field;
            if (a.NewItem >= 0)
            {
                if (a.NewItem > 0x1FF) throw new ArgumentOutOfRangeException("NewItem", "Item ids are 0-511 (0x1FF).");
                field = S["Item id"];
            }
            else
            {
                if (string.IsNullOrEmpty(a.Field) || !Fields.Contains(a.Field)) throw new ArgumentException("Choose the field to set: " + string.Join(", ", Fields) + ".");
                field = S[a.Field];
            }
            int value = a.NewItem >= 0 ? a.NewItem : a.Value;
            if (value < field.Min || value > field.Max) throw new ArgumentOutOfRangeException("Value", field.Name + " is " + field.Min + "-" + field.Max + ".");
            var res = new ReplaceResult();
            var byLevel = hits.GroupBy(h => h.Level).OrderBy(g => g.Key).ToList();
            edits.Together(() =>
            {
                foreach (var g in byLevel)
                {
                    var e = edits.For(g.Key);
                    var list = g.Select(h => h.Index).Distinct().OrderBy(i => i).ToList();
                    e.BeginBatch((a.NewItem >= 0 ? "Replace with " + s.Names.Get(a.NewItem) : "Set " + field.Name + " to " + value) + " on " + list.Count + " object" + (list.Count == 1 ? "" : "s"));
                    try
                    {
                        foreach (int i in list)
                        {
                            var o = e.Model.Objects[i];
                            if (o.Free || o.TileX < 0) { res.Refused++; res.Why = res.Why ?? "#" + i + " of level " + g.Key + " is no longer there"; continue; }
                            if (field.Get(o.Record) == value) { res.Same++; continue; }
                            try { e.SetObjectField(i, field, value); res.Changed++; res.Levels.Add(g.Key); }
                            catch (Exception ex) { res.Refused++; res.Why = res.Why ?? ex.Message; }
                        }
                    }
                    finally { e.EndBatch(); }
                }
            });
            return res;
        }
    }
}
