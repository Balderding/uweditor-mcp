using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>One line of an object's links for the properties panel: an object to go to, or a square.</summary>
    public sealed class LinkEntry
    {
        public string Text { get; set; }
        public int Level { get; set; }
        /// <summary>The object to go to, or −1 for a square.</summary>
        public int Object { get; set; } = -1;
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public override string ToString() { return Text; }
    }

    public static class TriggerLinks
    {
        /// <summary>
        /// What leads to an object and where it acts, for the properties panel: the triggers
        /// whose chains run it and the objects whose link names it, the triggers whose target
        /// square is its square, and the squares it acts on itself.
        /// </summary>
        public static List<LinkEntry> Of(LevelModel m, ObjectInfo o)
        {
            var list = new List<LinkEntry>();
            if (m == null || o == null || o.Free) return list;
            var seen = new HashSet<int>();
            foreach (var x in Triggers.LinkedFrom(m, o))
                if ((o.IsTrapOrTrigger || x.IsTrapOrTrigger) && seen.Add(x.Index))
                    list.Add(new LinkEntry { Text = "Linked from " + x + Place(x), Level = m.Level, Object = x.Index });
            if (Triggers.IsTrap(o.Item))
                foreach (var t in Triggers.TriggersReaching(m, o))
                    if (seen.Add(t.Index)) list.Add(new LinkEntry { Text = "Run by " + t + Place(t), Level = m.Level, Object = t.Index });
            if (o.IsTrapOrTrigger && o.Parent > 0 && !m.Objects[o.Parent].Free && seen.Add(o.Parent))
                list.Add(new LinkEntry { Text = "Held by " + m.Objects[o.Parent] + Place(m.Objects[o.Parent]), Level = m.Level, Object = o.Parent });
            if (o.TileX >= 0)
                foreach (var t in Triggers.TriggersOf(m))
                    if (t.Index != o.Index && t.Quality == o.TileX && t.Owner == o.TileY && (t.TileX != o.TileX || t.TileY != o.TileY) && seen.Add(t.Index))
                        list.Add(new LinkEntry { Text = "Its square is the target of " + t + Place(t), Level = m.Level, Object = t.Index });
            if (o.IsTrapOrTrigger)
                foreach (var t in Triggers.Targets(m, o))
                    list.Add(new LinkEntry { Text = string.Format("Acts on ({0}, {1}){2}: {3}", t.X, t.Y, t.Level != m.Level ? " of level " + t.Level : "", t.What), Level = t.Level, X = t.X, Y = t.Y });
            return list;
        }

        static string Place(ObjectInfo x) { return x.TileX >= 0 ? string.Format(" at ({0}, {1})", x.TileX, x.TileY) : ""; }
    }

    public sealed partial class LevelEditor
    {
        /// <summary>
        /// Links one object to another that stays where it lies, as the shipped levels do: the
        /// link of a trigger or trap is set to the object (its is-quantity bit set, so the link
        /// is no list), and a trigger's or delete trap's target square becomes the object's
        /// square. A lever, switch, button or chain links through the use trigger it holds (one
        /// is made inside it when it has none). When the trigger or trap already ran something
        /// and the new object is a trap with no link of its own, the old one comes after it, so
        /// the chain grows instead of being cut. One undo step. Returns the trigger or trap whose
        /// link was set.
        /// </summary>
        public int LinkObjects(int from, int to)
        {
            ListsMustPass();
            var m = Model;
            if (from <= 1 || to <= 1 || from >= m.Objects.Length || to >= m.Objects.Length || m.Objects[from].Free || m.Objects[to].Free) throw new InvalidOperationException("Both objects must be in the level.");
            if (from == to) throw new InvalidOperationException("An object cannot link to itself.");
            var f = m.Objects[from];
            var t = m.Objects[to];
            if (!f.IsTrapOrTrigger && !Triggers.IsControl(f.Item)) throw new InvalidOperationException(f.Name + " links to nothing: a trigger, a trap, or a lever, switch, button or chain does.");
            if (t.TileX < 0) throw new InvalidOperationException("#" + to + " is not in the level.");
            for (int up = t.Parent, g = 0; up >= 0 && g < 1024; up = m.Objects[up].Parent, g++)
                if (up == from) throw new InvalidOperationException("#" + to + " already lies inside #" + from + ": it runs it.");
            int result = -1;
            Batch("Link #" + from + " " + f.Name + " to #" + to + " " + t.Name, () =>
            {
                var S = Uw1Schemas.StaticObject;
                int src = from;
                if (Triggers.IsControl(f.Item))
                {
                    // the use trigger it holds, or a new one inside it
                    var use = f.Children.Select(c => Model.Objects[c]).FirstOrDefault(c => Triggers.IsTrigger(c.Item));
                    if (use == null && f.IsQuantity) SetObjectField(from, S["Is quantity"], 0);      // its link becomes the list that holds the trigger
                    src = use != null ? use.Index : AddObject(new ObjectSpec { Item = TrapRecipes.UseTrigger, Quality = t.TileX, Owner = t.TileY, IsQuantity = true, Link = 0, Z = f.Z }, f.TileX, f.TileY, from);
                }
                var s = Model.Objects[src];
                if (!s.IsQuantity && s.Children.Count > 0)
                    throw new InvalidOperationException("#" + src + " " + s.Name + " holds " + s.Children.Count + " object(s) inside it, which it runs: take them out first (drag one onto a tile), then link it.");
                int old = s.IsQuantity || s.IsTrapOrTrigger ? s.Link : 0;
                if (!s.IsQuantity) SetObjectField(src, S["Is quantity"], 1);
                SetObjectField(src, S["Link"], to);
                if (Triggers.IsTrigger(s.Item) || Triggers.IsDeleteTrap(s.Item))
                {
                    SetObjectField(src, S["Quality"], t.TileX);
                    SetObjectField(src, S["Owner"], t.TileY);
                }
                // what it ran before comes after the new trap, when that trap leads nowhere yet
                var nt = Model.Objects[to];
                if (old > 0 && old != to && old < Model.Objects.Length && !Model.Objects[old].Free && Triggers.IsTrap(nt.Item) && Triggers.IsTrigger(s.Item)
                    && nt.Children.Count == 0 && (nt.IsQuantity ? nt.Link == 0 : nt.Link == 0))
                {
                    if (!nt.IsQuantity) SetObjectField(to, S["Is quantity"], 1);
                    SetObjectField(to, S["Link"], old);
                }
                result = src;
            });
            return result;
        }

        /// <summary>Takes a trigger's or trap's link off (the object it named stays where it lies). One undo step.</summary>
        public void Unlink(int from)
        {
            var f = Model.Objects[from];
            if (!f.IsTrapOrTrigger || f.Free) throw new InvalidOperationException("Only a trigger's or a trap's link is taken off.");
            if (!f.IsQuantity && f.Children.Count > 0) throw new InvalidOperationException("#" + from + " holds what it runs inside it: drag those objects out onto a tile instead.");
            if (f.Link == 0) return;
            Batch("Unlink #" + from + " " + f.Name, () =>
            {
                var S = Uw1Schemas.StaticObject;
                if (!Model.Objects[from].IsQuantity) SetObjectField(from, S["Is quantity"], 1);
                SetObjectField(from, S["Link"], 0);
            });
        }
    }
}
