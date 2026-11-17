using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// Typed editors for the properties panel: for the kind of object selected, the fields of its
    /// record under the names of what they are there (a teleport's destination, a door trap's
    /// action, a lock's number), with a list to pick from where the values are a list. Each is
    /// the same bits as a row of the record below it, so typing in either changes both. The
    /// meanings are the game's (event/TRIGGER.C for UW1's traps, as Uw1Traps.Roles gives them;
    /// obj/USEITEMS.C for locks and keys); for UW2's traps only what is the same in both games
    /// is named (target squares, the teleport's destination, the delete trap's square).
    /// </summary>
    public static class TypedFields
    {
        static FieldDef F(string name, int offset, int bit, int bits, string description, string source)
        {
            return new FieldDef(name, offset, bit, bits, description, source);
        }

        const string Trig = "event/TRIGGER.C", Use = "obj/USEITEMS.C";

        // the record's own fields under other names: the same offsets as Uw1Schemas.StaticObject
        static FieldDef Quality(string n, string d, string s = Trig) { return F(n, 4, 0, 6, d, s); }
        static FieldDef Owner(string n, string d, string s = Trig) { return F(n, 6, 0, 6, d, s); }
        static FieldDef Z(string n, string d, string s = Trig) { return F(n, 2, 0, 7, d, s); }
        static FieldDef Heading(string n, string d, string s = Trig) { return F(n, 2, 7, 3, d, s); }
        static FieldDef FineX(string n, string d, string s = Trig) { return F(n, 2, 13, 3, d, s); }
        static FieldDef FineY(string n, string d, string s = Trig) { return F(n, 2, 10, 3, d, s); }
        static FieldDef Flag(string n, int bit, string d, string s = Trig) { return F(n, 0, bit, 1, d, s); }

        static List<Choice> Choices(params object[] valueLabel)
        {
            var l = new List<Choice>();
            for (int i = 0; i + 1 < valueLabel.Length; i += 2) l.Add(new Choice { Value = (int)valueLabel[i], Label = valueLabel[i] + " " + valueLabel[i + 1] });
            return l;
        }

        /// <summary>One typed field: the bits it is, and the values to pick from (or null).</summary>
        public sealed class Typed
        {
            public FieldDef Field;
            public List<Choice> Choices;
            public Typed(FieldDef f, List<Choice> c = null) { Field = f; Choices = c; }
        }

        /// <summary>The typed fields of an object, by its kind; empty when its record's own names say it all.</summary>
        public static List<Typed> For(GameKind kind, ObjectInfo o)
        {
            var l = new List<Typed>();
            if (o == null || o.Free) return l;
            int item = o.Item;
            bool uw1 = kind == GameKind.UW1;
            if (Triggers.IsTrigger(item))
            {
                l.Add(new Typed(Quality("Target X", "The square its chain runs on (0-63), not where the trigger lies.")));
                l.Add(new Typed(Owner("Target Y", "The square its chain runs on (0-63).")));
                if (uw1)
                {
                    l.Add(new Typed(Flag("Player sets it off", 11, "Flag 11: the player may set it off.")));
                    l.Add(new Typed(Flag("Creatures set it off", 12, "Enchanted: creatures may set it off. With it and without the player's flag, thrown objects do not.")));
                    l.Add(new Typed(Flag("Reusable", 10, "Flag 10: without it the trap chain is deleted after it runs.")));
                    if (item == 0x1A3) l.Add(new Typed(Z("Search difficulty", "A look trigger's Z is no height: above 0 the player needs a Search check against it, a chance of (15 + Search − Z) / 31.")));
                }
                return l;
            }
            if (item == 0x10F)
            {
                l.Add(new Typed(Flag("Locked", 9, "Flag 9: set while locked. Its key, or picking it, unlocks it.", Use)));
                if (o.IsQuantity) l.Add(new Typed(F("Lock number", 6, 6, 9, "Keys whose owner field is this number open it (link & 0x1FF).", Use + " checkLock")));
                l.Add(new Typed(Z("Pick difficulty", "A skill check against this × 3: 14 needs a skill of 31, 15 cannot be picked.", Use)));
                l.Add(new Typed(Flag("Stays when unlocked", 10, "Flag 10: the lock stays on the door after it is unlocked, so the key can lock it again.", Use)));
                return l;
            }
            if (KeyIndex.IsKey(item, kind))
            {
                l.Add(new Typed(Owner("Opens lock number", "A key opens the lock whose number equals this.", Use + " checkLock")));
                return l;
            }
            if (Triggers.IsTeleport(item))
            {
                l.Add(new Typed(Quality("Destination X", "The square the player is sent to. 0 is ignored in UW1.")));
                l.Add(new Typed(Owner("Destination Y", "The square the player is sent to.")));
                l.Add(new Typed(Z("Destination level", "The level the player is sent to; 0 is this level.")));
                return l;
            }
            if (Triggers.IsDeleteTrap(item))
            {
                l.Add(new Typed(Quality("Square X", "The square its linked object is deleted from; the object must lie directly on that square.")));
                l.Add(new Typed(Owner("Square Y", "The square its linked object is deleted from.")));
                return l;
            }
            if (item == 0x188)
            {
                l.Add(new Typed(Quality("What it does", "What happens to the door on the target square; a moving door is reversed instead."),
                    Choices(0, "only swaps the lock", 1, "opens the door", 2, "closes the door", 3, "toggles the door")));
                return l;
            }
            if (uw1 && Uw1Traps.IsTrap(item))
            {
                switch (item)
                {
                    case 0x180:
                        l.Add(new Typed(Quality("Damage", "Damage dealt to whoever set the chain off; with Poisons, the poison's strength (1-15 is useful).")));
                        l.Add(new Typed(Owner("Poisons", "0 damages. Anything else poisons the player at strength Damage instead of damaging him."), Choices(0, "damages", 1, "poisons the player")));
                        break;
                    case 0x183:
                        l.Add(new Typed(Quality("Special trap", "Which special trap this is; other numbers do nothing in UW1."),
                            Uw1Traps.HackTraps.Select(h => new Choice { Value = h.Key, Label = h.Key + " " + h.Value }).ToList()));
                        l.Add(new Typed(Owner("Its number", "By the special trap: the race for a crime (5), the mode 0-4 of the bullfrog puzzle (24), the strength of an earthquake (60-62).")));
                        break;
                    case 0x185:
                        l.Add(new Typed(F("New floor texture", 4, 1, 5, "The floor texture slot the tiles get; 11 or more keeps theirs (quality >> 1).", Trig)));
                        l.Add(new Typed(Owner("New wall texture", "The wall texture slot the tiles get; 48 or more keeps theirs.")));
                        l.Add(new Typed(F("New floor height", 2, 3, 4, "The floor height the tiles get; above 13 keeps theirs (Z >> 3).", Trig)));
                        l.Add(new Typed(FineX("Tiles east", "It changes the tiles from the target square to this many further east.")));
                        l.Add(new Typed(FineY("Tiles north", "It changes the tiles from the target square to this many further north.")));
                        break;
                    case 0x186:
                        l.Add(new Typed(Quality("Spell class", "The spell's class: heal (4), missile (5), area (6), special (13) and cutscene (14) work from a trap.")));
                        l.Add(new Typed(Owner("Spell effect", "The effect within the class.")));
                        break;
                    case 0x187:
                        l.Add(new Typed(Quality("Chance not to make", "Of 63: it makes its linked object when a roll of 0-62 is at least this. 0 always makes it, 63 never. (" + Math.Round(Triggers.SpawnChance(o.Quality)) + "% now.)")));
                        break;
                    case 0x18C:
                        l.Add(new Typed(Z("Least stack size", "Above 0: when the item asked for is a plain stack it must hold at least this many.")));
                        break;
                    case 0x18D:
                        l.Add(new Typed(Z("Variable", "1-63: the game variable written. 0: a quest bit instead.")));
                        l.Add(new Typed(Heading("Operation", "What is done to the variable with the trap's value; the result is kept to 6 bits. For a quest bit: 5 toggles, 1 clears, anything else sets."),
                            Choices(0, "add", 1, "subtract", 2, "set", 3, "and", 4, "or", 5, "xor", 6, "shift left", 7, "nothing")));
                        break;
                    case 0x18E:
                        l.Add(new Typed(Z("First variable", "The first game variable checked.")));
                        l.Add(new Typed(Heading("More variables", "It checks this many more after the first.")));
                        l.Add(new Typed(FineX("Adds them up", "0 packs 3 bits of each variable into the number compared; anything else adds them up."), Choices(0, "packs 3 bits of each", 1, "adds them up")));
                        break;
                }
                return l;
            }
            // a plain item whose link is a count or a property: the +512 that tells them apart, and the number
            if (o.IsQuantity && !o.IsTrapOrTrigger && !o.IsCritter && item < 0x180)
            {
                l.Add(new Typed(F("Special (+512)", 6, 15, 1, "The link's top bit: clear, the link is how many there are; set, it is a property (an enchantment, a text, a lock's number).", "include/object.h")));
                l.Add(new Typed(F(o.Link >= 0x200 ? "Property" : "How many", 6, 6, 9, o.Link >= 0x200 ? "The link less 512: the enchantment, text or number." : "How many there are in the stack (1-511).", "include/object.h")));
            }
            return l;
        }

        /// <summary>The typed fields as rows for the panel (each editable like a row of the record).</summary>
        public static List<PropertyRow> Rows(GameKind kind, ObjectInfo o)
        {
            var rows = new List<PropertyRow>();
            foreach (var t in For(kind, o))
            {
                var f = t.Field;
                if (o.Record.Length < f.Offset + f.ByteSpan) continue;
                int v = f.Get(o.Record);
                var row = new PropertyRow
                {
                    Field = f, Target = "object", TargetIndex = o.Index, Name = f.Name, Value = v.ToString(), Hex = "", Certainty = "",
                    Tip = f.Description + "\nSource: " + f.Source + string.Format("\nThe same bits as the record below: offset 0x{0:X2}, bit {1}, {2} bit{3}.", f.Offset, f.Bit, f.Bits, f.Bits == 1 ? "" : "s"),
                };
                if (t.Choices != null)
                {
                    row.Choices = t.Choices;
                    var c = t.Choices.FirstOrDefault(x => x.Value == v);
                    if (c != null) { row.ValueNote = c.Label.Substring(c.Label.IndexOf(' ') + 1); row.Tip = "Now: " + c.Label + "\n" + row.Tip; }
                }
                rows.Add(row);
            }
            return rows;
        }
    }
}
