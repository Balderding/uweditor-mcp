using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>A string of STRINGS.PAK an object's text is: its block and number, and the text.</summary>
    public sealed class TextSlot
    {
        public int Block, Index;
        public string Text;
        public override string ToString() { return "block " + Block + " string " + Index; }
    }

    /// <summary>
    /// Texts that follow their objects: a book, scroll, gravestone, writing or text trap gets a
    /// string of its own, a copy takes a copy of the text, and a text no object uses any more is
    /// freed. The rules of which string an object reads are ObjectTexts.TextOf's: a book or
    /// scroll block 3 string link − 0x200; a gravestone (0x165) block 8 strings 1-63 and a
    /// writing (0x166) block 8 strings 64-351, both link − 0x200; a text trap (0x190) block 9
    /// string quality &lt;&lt; 5 | owner. A string is free for a new text when no object of any
    /// level reads it and it is empty (or lies past the end of its block).
    /// </summary>
    public static class ObjectTextEdits
    {
        /// <summary>Whether an item carries a text, and the strings it may use.</summary>
        public static bool Range(GameKind kind, int item, out int block, out int lo, out int hi, out string what)
        {
            block = lo = hi = 0; what = null;
            var cat = ItemCategories.Of(item, kind);
            if ((cat & (ItemCategory.Book | ItemCategory.Scroll)) != 0) { block = 3; lo = 1; hi = 0x1FF; what = "book or scroll"; return true; }
            if (item == 0x165) { block = 8; lo = 1; hi = 63; what = "gravestone"; return true; }
            if (item == 0x166) { block = 8; lo = 64; hi = 351; what = "writing"; return true; }
            if (item == 0x190) { block = 9; lo = 1; hi = 63 << 5 | 31; what = "text trap"; return true; }
            return false;
        }

        /// <summary>The string an object reads now (whether or not the block holds it), by ObjectTexts.TextOf's rules.</summary>
        public static bool Slot(GameKind kind, ObjectInfo o, out int block, out int index)
        {
            int lo, hi; string what;
            index = -1;
            if (o == null || o.Free || !Range(kind, o.Item, out block, out lo, out hi, out what)) { block = 0; return false; }
            if (o.Item == 0x190) { index = (o.Quality & 0x3F) << 5 | (kind == GameKind.UW2 ? o.Owner & 0x1F : o.Owner & 0x3F); return true; }
            if (!o.IsQuantity || o.Link < 0x200 || (block == 3 && o.Enchanted)) return false;
            index = o.Link - 0x200;
            return block == 3 || (index >= lo && index <= hi);
        }

        /// <summary>The strings of a block that objects read, on every level as edited.</summary>
        public static HashSet<int> Used(LevelEdits edits, int block)
        {
            var used = new HashSet<int>();
            var s = edits.Session;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                foreach (var o in edits.Model(slot.Level).Objects)
                {
                    int b, i;
                    if (!o.Free && o.TileX >= 0 && Slot(s.Kind, o, out b, out i) && b == block) used.Add(i);
                }
            }
            return used;
        }

        /// <summary>How many objects of all levels read this string.</summary>
        public static int Users(LevelEdits edits, int block, int index)
        {
            int n = 0;
            var s = edits.Session;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                foreach (var o in edits.Model(slot.Level).Objects)
                {
                    int b, i;
                    if (!o.Free && o.TileX >= 0 && Slot(s.Kind, o, out b, out i) && b == block && i == index) n++;
                }
            }
            return n;
        }

        /// <summary>The text an object reads, from the strings as edited (not yet saved ones too), or null.</summary>
        public static TextSlot TextOf(GameKind kind, ObjectInfo o, StringsEditor st)
        {
            int b, i;
            if (!Slot(kind, o, out b, out i)) return null;
            return new TextSlot { Block = b, Index = i, Text = st.Get(b, i) };
        }

        /// <summary>Puts a text in a free string of the block within lo-hi (the block grows with empty strings up to it) and returns its number; −1 when none is free.</summary>
        static int Store(StringsEditor st, HashSet<int> used, int block, int lo, int hi, string text)
        {
            int count = st.Get(block).Count;
            for (int i = lo; i <= hi; i++)
            {
                if (used.Contains(i)) continue;
                if (i < count)
                {
                    if (!string.IsNullOrEmpty(st.Get(block, i))) continue;
                    st.Set(block, i, text);
                }
                else
                {
                    while (count < i) { st.Add(block, ""); count++; }
                    st.Add(block, text);
                }
                used.Add(i);
                return i;
            }
            return -1;
        }

        static void Point(LevelEditor e, GameKind kind, int index, int block, int at)
        {
            var S = Uw1Schemas.StaticObject;
            var o = e.Model.Objects[index];
            if (o.Item == 0x190)
            {
                e.SetObjectField(index, S["Quality"], at >> 5);
                e.SetObjectField(index, S["Owner"], at & 0x1F);
                return;
            }
            if (!o.IsQuantity) e.SetObjectField(index, S["Is quantity"], 1);
            e.SetObjectField(index, S["Link"], 0x200 + at);
        }

        /// <summary>
        /// Gives an object a string of its own holding the text, and points it there: one undo
        /// step of its level. Throws when the object carries no text, is an enchanted scroll (its
        /// link is its spell), holds objects, or its kind has no free string left.
        /// </summary>
        public static TextSlot GiveOwnText(LevelEdits edits, int level, int index, StringsEditor st, string text)
        {
            var e = edits.For(level);
            var kind = edits.Session.Kind;
            var o = e.Model.Objects[index];
            int block, lo, hi; string what;
            if (o.Free || !Range(kind, o.Item, out block, out lo, out hi, out what)) throw new InvalidOperationException(o.Name + " carries no text: books, scrolls, gravestones, writings and text traps do.");
            if (block == 3 && o.Enchanted) throw new InvalidOperationException("An enchanted scroll's link is its spell, not a text.");
            if (o.Item != 0x190 && !o.IsQuantity && o.Children.Count > 0) throw new InvalidOperationException("Empty it first: its link holds " + o.Children.Count + " objects.");
            st.ToGame(text);                                             // refuses letters the file cannot write, before anything changes
            int at = Store(st, Used(edits, block), block, lo, hi, text);
            if (at < 0) throw new InvalidOperationException(string.Format("No free text left for a {0}: block {1} strings {2}-{3} are all in use. Delete one that uses a text, or clear a string no object reads.", what, block, lo, hi));
            try { e.Batch("Text of #" + index + " " + o.Name, () => Point(e, kind, index, block, at)); }
            catch { st.Set(block, at, ""); throw; }
            return new TextSlot { Block = block, Index = at, Text = text };
        }

        /// <summary>The objects under these (themselves and what they hold) that read a text.</summary>
        public static List<int> WithTexts(LevelModel m, IEnumerable<int> roots)
        {
            var list = new List<int>();
            var seen = new HashSet<int>();
            Action<int, int> walk = null;
            walk = (i, d) =>
            {
                if (i <= 0 || i >= m.Objects.Length || !seen.Add(i) || d > 64) return;
                int b, k;
                if (Slot(m.Session.Kind, m.Objects[i], out b, out k)) list.Add(i);
                foreach (int c in m.Objects[i].Children) walk(c, d + 1);
            };
            foreach (int r in roots) walk(r, 0);
            return list;
        }

        /// <summary>
        /// After a paste: each copy that reads a text some other object also reads gets a string
        /// of its own with the same text. Returns the new strings; shared counts the copies left
        /// reading the original's text because no string was free.
        /// </summary>
        public static List<TextSlot> GiveCopiesOwnTexts(LevelEdits edits, int level, IEnumerable<int> roots, StringsEditor st, out int shared)
        {
            shared = 0;
            var made = new List<TextSlot>();
            var e = edits.For(level);
            var kind = edits.Session.Kind;
            foreach (int i in WithTexts(e.Model, roots))
            {
                var t = TextOf(kind, e.Model.Objects[i], st);
                if (t == null || string.IsNullOrEmpty(t.Text) || Users(edits, t.Block, t.Index) < 2) continue;
                try { made.Add(GiveOwnText(edits, level, i, st, t.Text)); }
                catch (InvalidOperationException) { shared++; }
            }
            return made;
        }

        /// <summary>The strings these objects (and what they hold) read, each once, with their texts: taken before a delete.</summary>
        public static List<TextSlot> TextsUnder(LevelModel m, IEnumerable<int> roots, StringsEditor st)
        {
            var list = new List<TextSlot>();
            foreach (int i in WithTexts(m, roots))
            {
                var t = TextOf(m.Session.Kind, m.Objects[i], st);
                if (t != null && !string.IsNullOrEmpty(t.Text) && !list.Exists(x => x.Block == t.Block && x.Index == t.Index)) list.Add(t);
            }
            return list;
        }

        /// <summary>After a delete: the strings among these that no object of any level reads any more are emptied. Returns them (with the text they held).</summary>
        public static List<TextSlot> FreeUnused(LevelEdits edits, StringsEditor st, IEnumerable<TextSlot> slots)
        {
            var freed = new List<TextSlot>();
            foreach (var g in slots.GroupBy(t => t.Block))
            {
                var used = Used(edits, g.Key);
                foreach (var t in g)
                    if (!used.Contains(t.Index) && !string.IsNullOrEmpty(st.Get(t.Block, t.Index))) { st.Set(t.Block, t.Index, ""); freed.Add(t); }
            }
            return freed;
        }
    }
}
