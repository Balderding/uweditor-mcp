using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Every field of one object, as the editor's properties panel shows it (PropertyRows.ForObject:
    /// the record's fields, a creature's mobile fields, the kind's own names such as a lock's number
    /// or a trigger's target), set by name; putting objects inside others; locks on doors and chests.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>A name as compared: no case, spaces, hyphens, underscores or dots.</summary>
        static string Key(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name ?? "") if (char.IsLetterOrDigit(c) || c == '+') b.Append(char.ToLowerInvariant(c));
            return b.ToString();
        }

        /// <summary>Other names a field is known by (the game's source, UW-Formats).</summary>
        static readonly Dictionary<string, string> FieldAliases = new Dictionary<string, string>
        {
            { "item", "itemid" }, { "id", "itemid" }, { "gtarg", "goaltarget" }, { "target", "goaltarget" },
            { "oldgoal", "savedgoal" }, { "hitpoints", "hp" }, { "isquant", "isquantity" },
            { "quantity", "isquantity" }, { "special", "link" }, { "xhome", "homex" }, { "yhome", "homey" },
            { "npcwhoami", "whoami" }, { "conversation", "whoami" }, { "flag12", "enchanted" }, { "flag13", "tenacious" },
            { "flag14", "invisible" }, { "flag15", "isquantity" },
        };

        /// <summary>An object in use on a level, or a refusal.</summary>
        static ObjectInfo ObjectAt(LevelModel m, int index)
        {
            if (index < 1 || index >= LevelBlock.Objects) throw new ArgumentOutOfRangeException(null, "Objects are #1-#" + (LevelBlock.Objects - 1) + ".");
            var o = m.Objects[index];
            if (o == null || o.Free) throw new ArgumentException("#" + index + " is a free slot, not an object.");
            return o;
        }

        /// <summary>Every field of one object by name with its value; what holds it and what it holds.</summary>
        public string ObjectGet(int level, int index)
        {
            var m = ReadModel(level);
            var o = ObjectAt(m, index);
            var b = new StringBuilder();
            b.Append('#').Append(o.Index).Append(' ').Append(Hex(o.Item)).Append(' ').Append(o.Name).Append(" on level ").Append(level).Append('\n');
            b.Append("(name = value can be set with object_set; name: value is shown only)\n");
            foreach (var r in PropertyRows.ForObject(m, o))
            {
                if (r.IsHeader) { b.Append("-- ").Append(r.Name).Append(" --\n"); continue; }
                if (r.Name == "Bytes") { b.Append("Bytes: ").Append(r.Value).Append('\n'); continue; }
                b.Append(r.Name).Append(r.Field != null ? " = " : ": ").Append(r.Value);
                if (r.Field != null)
                {
                    b.Append(" (0-").Append(r.Field.Max).Append(')');
                    if (!string.IsNullOrEmpty(r.ValueNote)) b.Append(' ').Append(r.ValueNote);
                    else if (r.Choices != null) { var c = r.Choices.FirstOrDefault(x => x.Value.ToString() == r.Value); if (c != null) b.Append(' ').Append(Label(c)); }
                }
                if (!string.IsNullOrEmpty(r.Certainty)) b.Append(" [").Append(r.Certainty).Append(']');
                b.Append('\n');
            }
            var targets = Triggers.Targets(m, o);
            if (targets.Count > 0) b.Append("Acts on: ").Append(string.Join("; ", targets.Select(t => t.ToString()))).Append('\n');
            if (o.Children.Count > 0)
            {
                b.Append(o.IsCritter ? "Carries:\n" : o.IsTrapOrTrigger ? "Runs:\n" : "Holds:\n");
                foreach (int c in o.Children) Line(b, m, m.Objects[c], 1);
            }
            else if (!o.IsQuantity) b.Append(o.IsCritter ? "Carries nothing.\n" : "Holds nothing.\n");
            return b.ToString();
        }

        static string Label(Choice c) { int sp = c.Label.IndexOf(' '); return sp < 0 ? c.Label : c.Label.Substring(sp + 1); }

        /// <summary>The panel's rows for an object that can be typed into.</summary>
        static List<PropertyRow> SettableRows(LevelModel m, ObjectInfo o)
        {
            return PropertyRows.ForObject(m, o).Where(r => !r.IsHeader && r.Field != null && !LevelEditor.IsListField(r.Field.Name)).ToList();
        }

        /// <summary>A field of the object by its name (or a known other name); refused naming the fields it has.</summary>
        static PropertyRow RowOf(LevelModel m, ObjectInfo o, string field)
        {
            var rows = SettableRows(m, o);
            string k = Key(field), alias;
            if (k.Length == 0) throw new ArgumentException("Give a field's name. #" + o.Index + "'s fields: " + string.Join(", ", rows.Select(r => r.Name).Distinct()) + ".");
            var hit = rows.FirstOrDefault(r => Key(r.Name) == k);
            if (hit == null && FieldAliases.TryGetValue(k, out alias)) hit = rows.FirstOrDefault(r => Key(r.Name) == alias);
            if (hit == null)
            {
                var part = rows.Where(r => Key(r.Name).StartsWith(k, StringComparison.Ordinal)).GroupBy(r => r.Name).Select(g => g.First()).ToList();
                if (part.Count == 1) hit = part[0];
            }
            if (hit == null)
            {
                string why = (k == "locked" || k == "lock" || k == "locknumber") && !o.IsQuantity || (k == "locked" && DoorSpikes.IsClosedDoor(o.Item))
                    ? " A door's or chest's lock is an object inside it: use lock_door." : "";
                throw new ArgumentException("#" + o.Index + " " + o.Name + " has no field " + field + "." + why + " Its fields: " + string.Join(", ", rows.Select(r => r.Name).Distinct()) + ".");
            }
            return hit;
        }

        /// <summary>A value: a number (0x1F or 31), true/false/yes/no/on/off for a flag, or a choice's name (attitude friendly).</summary>
        static int ValueOf(PropertyRow row, string text)
        {
            string t = (text ?? "").Trim();
            int v;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            string k = Key(t);
            if (row.Field.Bits == 1)
            {
                if (k == "true" || k == "yes" || k == "on" || k == "set") return 1;
                if (k == "false" || k == "no" || k == "off" || k == "clear") return 0;
            }
            if (row.Choices != null && k.Length > 0)
            {
                var exact = row.Choices.Where(c => Key(Label(c)) == k).ToList();
                var part = exact.Count > 0 ? exact : row.Choices.Where(c => Key(Label(c)).StartsWith(k, StringComparison.Ordinal)).ToList();
                if (part.Count == 1) return part[0].Value;
                if (part.Count > 1) throw new ArgumentException(row.Name + " " + text + " is several: " + string.Join(", ", part.Take(12).Select(c => c.Label)) + ".");
            }
            throw new ArgumentException("'" + text + "' is not a value of " + row.Name + ": give a number" + (row.Field.Bits == 1 ? " (0 or 1, or true/false)" : "")
                + (row.Choices != null ? " or one of " + string.Join(", ", row.Choices.Take(16).Select(c => c.Label)) + (row.Choices.Count > 16 ? ", ..." : "") : "") + ".");
        }

        /// <summary>"hp=40, attitude=friendly" (or ; or new lines between) as name, value pairs.</summary>
        static List<KeyValuePair<string, string>> Pairs(string fields)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var part in (fields ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Trim().Length == 0) continue;
                int eq = part.IndexOf('=');
                if (eq < 0) eq = part.IndexOf(':');
                if (eq <= 0) throw new ArgumentException("'" + part.Trim() + "' is not name=value (as in hp=40, attitude=friendly).");
                l.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            return l;
        }

        /// <summary>
        /// Sets fields of an object by the names the editor shows (one field, or several as
        /// "hp=40, attitude=friendly"): all or nothing, one undo step.
        /// </summary>
        public string ObjectSet(int level, int index, string field, string value, string fields)
        {
            var e = Editable(level);
            Index(e, index);
            var pairs = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrWhiteSpace(field))
            {
                if (value == null) throw new ArgumentException("Give the value for " + field + ".");
                pairs.Add(new KeyValuePair<string, string>(field, value));
            }
            pairs.AddRange(Pairs(fields));
            if (pairs.Count == 0)
                throw new ArgumentException("Give field and value, or fields as name=value pairs. #" + index + "'s fields: " + string.Join(", ", SettableRows(e.Model, e.Model.Objects[index]).Select(r => r.Name).Distinct()) + ".");
            // every name and value checked before anything changes
            foreach (var p in pairs) ValueOf(RowOf(e.Model, e.Model.Objects[index], p.Key), p.Value);
            var said = new List<string>();
            e.Batch(pairs.Count == 1 ? "Set " + pairs[0].Key + " of #" + index : "Set " + pairs.Count + " fields of #" + index, () =>
            {
                foreach (var p in pairs)
                {
                    // the rows again each time: a change (Is quantity) can change which fields it has
                    var o = e.Model.Objects[index];
                    var row = RowOf(e.Model, o, p.Key);
                    int v = ValueOf(row, p.Value);
                    if (v < row.Field.Min || v > row.Field.Max) throw new ArgumentOutOfRangeException(null, row.Name + " is " + row.Field.Min + "-" + row.Field.Max + ".");
                    int was = row.Field.Get(o.Record);
                    if (DoorSpikes.IsField(row.Field)) DoorSpikes.Set(e, index, row.Field, v);
                    else e.SetObjectField(index, row.Field, v);
                    int now = row.Field.Get(e.Model.Objects[index].Record);
                    said.Add(row.Name + " " + (was == now ? "stays " + now : was + " -> " + now));
                }
            });
            var after = e.Model.Objects[index];
            return "#" + index + " " + after.Name + ": " + string.Join(", ", said) + ".";
        }

        /// <summary>A door or container whose link is a quantity of nothing gets a list (as a lock is added), so things go inside.</summary>
        static void MakeList(LevelEditor e, int container)
        {
            var c = e.Model.Objects[container];
            if (!c.IsQuantity) return;
            bool holder = (ItemCategories.Of(c.Item, e.Session.Kind) & (ItemCategory.Door | ItemCategory.Container)) != 0;
            if (!holder || (c.Link & 0x3FF) != 0)
                throw new InvalidOperationException("#" + container + " " + c.Name + " is not a container: its link is a quantity or special value (" + c.Link + ").");
            e.SetObjectField(container, Uw1Schemas.StaticObject["Is quantity"], 0);
        }

        /// <summary>Puts an object (from a tile or another container) inside a container, a creature's inventory or a trigger's chain.</summary>
        public string ObjectPutInside(int level, int index, int container)
        {
            var e = Editable(level);
            Index(e, index);
            var c = ObjectAt(e.Model, container);
            string name = e.Model.Objects[index].Name;
            e.Batch("Put #" + index + " " + name + " in #" + container, () =>
            {
                MakeList(e, container);
                if (!e.CanPutInside(index, container)) throw new InvalidOperationException(c.Name + " cannot hold " + name + " (#" + index + " is the container itself, holds it, or is a creature going into a bag).");
                e.PutInside(index, container);
            });
            return "Put #" + index + " " + name + " in #" + container + " " + c.Name + ".";
        }

        /// <summary>Adds an item inside a container, a creature's inventory or a trigger's chain, with the editor's defaults for it.</summary>
        public string ObjectAddInside(int level, int container, string item, int? quality, int? owner, int? quantity)
        {
            var e = Editable(level);
            var c = ObjectAt(e.Model, container);
            int it = ItemOf(item);
            var spec = ObjectSpec.Default(it, Session.Kind);
            if (spec.IsMobile && !c.IsCritter && !c.IsTrapOrTrigger) throw new InvalidOperationException("A creature goes on a tile, in a creature's inventory or in a trap's chain, not in " + c.Name + ".");
            if (quality.HasValue) spec.Quality = Range(quality.Value, 0, 63, "quality");
            if (owner.HasValue) spec.Owner = Range(owner.Value, 0, 63, "owner");
            if (quantity.HasValue)
            {
                if (!spec.IsQuantity || spec.Link >= 0x200) throw new ArgumentException(Session.Names.Get(it) + " is no stack: it takes no quantity.");
                spec.Link = Range(quantity.Value, 1, 511, "quantity");
            }
            int made = -1;
            e.Batch("Add " + Session.Names.Get(it) + " in #" + container, () =>
            {
                MakeList(e, container);
                made = e.AddObject(spec, Math.Max(0, c.TileX), Math.Max(0, c.TileY), container);
            });
            return "Added #" + made + " " + Hex(it) + " " + Session.Names.Get(it) + " in #" + container + " " + c.Name + " on level " + level + ".";
        }

        /// <summary>
        /// A lock on a door or a chest: the lock object (0x10F) inside it (made, or the one it has
        /// changed), its number (link &amp; 0x1FF), locked (flag 9) or not, its pick difficulty (z).
        /// A key opens it when the key's owner equals the number (obj/USEITEMS.C checkLock).
        /// </summary>
        public string LockDoor(int level, int doorIndex, int lockNumber, bool locked, int? difficulty)
        {
            var e = Editable(level);
            Index(e, doorIndex);
            if (lockNumber < 0 || lockNumber > 0x1FF) throw new ArgumentOutOfRangeException(null, "A lock's number is 0-511 (keys reach 0-63).");
            if (difficulty.HasValue) Range(difficulty.Value, 0, 15, "difficulty (15 cannot be picked)");
            var d = e.Model.Objects[doorIndex];
            var old = e.LockOf(doorIndex);
            int lk;
            if (old == null) lk = e.AddLock(doorIndex, lockNumber, locked, difficulty ?? 0);
            else
            {
                lk = old.Index;
                var S = Uw1Schemas.StaticObject;
                e.Batch("Lock " + lockNumber + " on #" + doorIndex + " " + d.Name, () =>
                {
                    if (!e.Model.Objects[lk].IsQuantity) throw new InvalidOperationException("The lock #" + lk + " holds a list, not a number: delete it and lock again.");
                    e.SetObjectField(lk, S["Link"], 0x200 + lockNumber);
                    e.SetObjectField(lk, S["Flag 9"], locked ? 1 : 0);
                    if (difficulty.HasValue) e.SetObjectField(lk, S["Z"], difficulty.Value);
                });
            }
            var m = e.Model;
            var keys = m.Objects.Where(o => o != null && !o.Free && o.TileX >= 0 && KeyIndex.IsKey(o.Item, Session.Kind) && o.Owner == lockNumber).Select(o => o.ToString()).ToList();
            var b = new StringBuilder();
            b.Append("#").Append(doorIndex).Append(' ').Append(d.Name).Append(old == null ? " has a new lock #" : " has its lock #").Append(lk)
                .Append(": number ").Append(lockNumber).Append(locked ? ", locked" : ", unlocked").Append(", pick difficulty ").Append(m.Objects[lk].Z).Append(". ");
            if (lockNumber > 63) b.Append("No key opens it (a key's owner is 0-63); it can only be picked or broken.");
            else
            {
                b.Append("A key whose owner is ").Append(lockNumber).Append(" opens it");
                b.Append(keys.Count > 0 ? "; on this level: " + string.Join(", ", keys.Take(10)) + "." : "; no such key is on this level (add a key with owner " + lockNumber + ").");
            }
            return b.ToString();
        }
    }
}
