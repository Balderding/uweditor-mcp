using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Tools ▸ Player and inventory, and Edit ▸ Move the player here: the open saved game's
    /// character, field by field, its position, the game's own derived values and checks, and
    /// the inventory (slots, containers, each object's fields). Kept until player_save (or the
    /// editor's save).
    /// </summary>
    public sealed partial class McpEditor
    {
        sealed class SvField
        {
            public string Name, Help;
            public Func<PlayerSheet, string> Get;
            public Action<PlayerSheet, string> Set;
            public bool Uw1Only;
        }

        static string SvKey(string s) { return new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); }

        static int SvInt(string v)
        {
            string t = (v ?? "").Trim();
            int n;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return n;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            throw new ArgumentException("'" + v + "' is not a number.");
        }

        static long SvLong(string v)
        {
            long n;
            if (long.TryParse((v ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return SvInt(v);
        }

        static bool SvBool(string v)
        {
            switch (SvKey(v))
            {
                case "true": case "yes": case "on": case "1": return true;
                case "false": case "no": case "off": case "0": return false;
            }
            throw new ArgumentException("'" + v + "' is not true or false.");
        }

        static string SvB(bool b) { return b ? "true" : "false"; }

        static SvField SvByte(string name, string help, Func<PlayerSheet, int> get, Action<PlayerSheet, int> set, bool uw1 = false)
        {
            return new SvField { Name = name, Help = help, Get = p => get(p).ToString(CultureInfo.InvariantCulture), Set = (p, v) => set(p, SvInt(v)), Uw1Only = uw1 };
        }

        static SvField SvFlag(string name, string help, Func<PlayerSheet, bool> get, Action<PlayerSheet, bool> set, bool uw1 = true)
        {
            return new SvField { Name = name, Help = help, Get = p => SvB(get(p)), Set = (p, v) => set(p, SvBool(v)), Uw1Only = uw1 };
        }

        static readonly SvField[] SvFields =
        {
            new SvField { Name = "name", Help = "1-29 letters (the first is the record's cipher key)", Get = p => p.Name, Set = (p, v) => { if (string.IsNullOrEmpty(v)) throw new ArgumentException("The name cannot be empty."); p.Name = v; } },
            new SvField { Name = "class", Help = "0-7 or a name: " + string.Join(", ", PlayerSheet.ClassNames), Get = p => p.Class + " " + PlayerSheet.ClassNames[p.Class],
                Set = (p, v) => { int i = Array.FindIndex(PlayerSheet.ClassNames, c => SvKey(c) == SvKey(v)); p.Class = i >= 0 ? i : SvInt(v); } },
            SvFlag("female", "the sex: true female, false male", p => p.Female, (p, v) => p.Female = v, false),
            SvFlag("hand bit", "the hand bit (UW1: set for right-handed; UW2: set for left-handed)", p => p.HandBit, (p, v) => p.HandBit = v, false),
            SvByte("body", "the portrait's body 0-4 (HEADS.GR has 5 for each sex; 0-7 can be stored)", p => p.Body, (p, v) => p.Body = v),
            SvByte("strength", "0-255 (character creation stops at 30)", p => p.Strength, (p, v) => p.Strength = v),
            SvByte("dexterity", "0-255", p => p.Dexterity, (p, v) => p.Dexterity = v),
            SvByte("intelligence", "0-255", p => p.Intelligence, (p, v) => p.Intelligence = v),
            SvByte("vitality", "current hit points 0-255 (kept in the record and the player object)", p => p.Vitality, (p, v) => p.Vitality = v),
            SvByte("max vitality", "0-255 (the game computes 30 + level x strength / 5)", p => p.MaxVitality, (p, v) => p.MaxVitality = v),
            SvByte("mana", "0-255", p => p.Mana, (p, v) => p.Mana = v),
            SvByte("max mana", "0-255 (the game computes (mana skill + 1) x intelligence / 8)", p => p.MaxMana, (p, v) => p.MaxMana = v),
            SvByte("hunger", "0-255", p => p.Hunger, (p, v) => p.Hunger = v),
            SvByte("fatigue", "0-255", p => p.Fatigue, (p, v) => p.Fatigue = v),
            SvByte("level", "experience level 1-16", p => p.Level, (p, v) => p.Level = v),
            new SvField { Name = "experience", Help = "experience points as the game shows them (stored in tenths)", Get = p => p.Experience.ToString(CultureInfo.InvariantCulture), Set = (p, v) => { long n = SvLong(v); if (n < 0 || n > 429496729) throw new ArgumentOutOfRangeException(null, "0-429496729"); p.Experience = n; } },
            new SvField { Name = "experience tenths", Help = "experience as stored, in tenths", Get = p => p.ExperienceTenths.ToString(CultureInfo.InvariantCulture), Set = (p, v) => { long n = SvLong(v); if (n < 0 || n > uint.MaxValue) throw new ArgumentOutOfRangeException(null, "0-4294967295"); p.ExperienceTenths = n; } },
            SvByte("skill points", "unspent skill points 0-255", p => p.SkillPoints, (p, v) => p.SkillPoints = v),
            SvByte("skill points earned", "skill points the experience has given so far 0-255", p => p.SkillPointsEarned, (p, v) => p.SkillPointsEarned = v),
            SvByte("max weight", "carrying capacity 0-65535 (the game computes it from strength)", p => p.MaxWeight, (p, v) => p.MaxWeight = v),
            new SvField { Name = "weight", Help = "the weight carried (read only: the game works it out)", Get = p => p.Weight.ToString(CultureInfo.InvariantCulture) },
            SvFlag("talismans can burn", "UW1: talismans thrown into the lava are destroyed (set when Garamon is buried)", p => p.TalismansCanBurn, (p, v) => p.TalismansCanBurn = v),
            SvFlag("garamon buried", "UW1: Garamon's bones are buried", p => p.GaramonBuried, (p, v) => p.GaramonBuried = v),
            SvByte("talismans left", "UW1: talismans still to destroy (8 at the start, 255 once the last is gone)", p => p.TalismansLeft, (p, v) => p.TalismansLeft = v, true),
            SvFlag("orb destroyed", "UW1: Tybal's orb on level 7 is destroyed", p => p.OrbDestroyed, (p, v) => p.OrbDestroyed = v),
            SvFlag("key of truth given", "UW1: the shrine gave the Key of Truth", p => p.KeyOfTruthGiven, (p, v) => p.KeyOfTruthGiven = v),
            SvFlag("cup found", "UW1: the Cup of Wonder was found", p => p.CupFound, (p, v) => p.CupFound = v),
            SvFlag("armageddon cast", "UW1: Armageddon was cast (every level arrived on is cleared)", p => p.ArmageddonCast, (p, v) => p.ArmageddonCast = v),
            SvFlag("maze spell", "UW1: the maze navigation spell is on", p => p.MazeSpell, (p, v) => p.MazeSpell = v),
            SvByte("moonstone level", "UW1: the level the moonstone lies on, 0-15 (0 while carried)", p => p.MoonstoneLevel, (p, v) => p.MoonstoneLevel = v, true),
            SvByte("silver tree level", "UW1: the level the silver tree grows on, 0-15 (0 none)", p => p.SilverTreeLevel, (p, v) => p.SilverTreeLevel = v, true),
            SvByte("dreams", "UW1: the dreams seen, a bit each (0-65535)", p => p.Dreams, (p, v) => p.Dreams = v, true),
        };

        SvField SvFindField(string name, out int skill)
        {
            skill = Array.FindIndex(PlayerDat.SkillNames, n => SvKey(n) == SvKey(name));
            if (skill >= 0) return null;
            var f = SvFields.FirstOrDefault(x => SvKey(x.Name) == SvKey(name));
            if (f == null) throw new ArgumentException("No player field " + name + ". Fields: " + string.Join(", ", SvFields.Select(x => x.Name)) + "; skills: " + string.Join(", ", PlayerDat.SkillNames) + ".");
            return f;
        }

        /// <summary>Everything about the player: the fields, the skills, where it stands, what the game derives, and the checks.</summary>
        public string PlayerGet()
        {
            var p = SvSheet();
            bool uw2 = p.Kind == GameKind.UW2;
            var b = new StringBuilder();
            b.Append("PLAYER.DAT: ").Append(p.Path).Append(p.Dirty ? " (unsaved changes)" : "").Append('\n');
            foreach (var f in SvFields)
            {
                if (f.Uw1Only && uw2) continue;
                b.Append(f.Name).Append(": ").Append(f.Get(p)).Append(f.Set == null ? " (read only)" : "").Append('\n');
            }
            b.Append("skills:");
            for (int i = 0; i < 20; i++) b.Append(i == 0 ? " " : ", ").Append(PlayerDat.SkillNames[i].ToLowerInvariant()).Append(' ').Append(p.Skill(i));
            b.Append('\n');
            b.Append(SvPosition(p)).Append('\n');
            b.Append(string.Format(CultureInfo.InvariantCulture, "As the game computes them: max vitality {0}, max mana {1}, max weight {2}; the experience has earned level {3} and {4} skill points.\n",
                p.ExpectedMaxVitality, p.ExpectedMaxMana, p.ExpectedMaxWeight, p.ExpectedLevel, p.ExpectedPointsEarned));
            var w = p.Check();
            b.Append(w.Count == 0 ? "Nothing the game would notice." : w.Count + " warning(s): player_check lists them.").Append('\n');
            b.Append("Inventory: ").Append(p.Inventory.All().Count()).Append(" object(s) (inventory_list).\n");
            return b.ToString();
        }

        static string SvPosition(PlayerSheet p)
        {
            var d = p.Dat;
            return string.Format(CultureInfo.InvariantCulture, "position: level {0}, tile ({1}, {2}), fine x {3} y {4} (1/256 of a tile), z {5} (0x40 a floor step), heading {6} (0 north, 2 east), facing {7}",
                d.SavedLevel, d.TileX, d.TileY, d.SavedX & 0xFF, d.SavedY & 0xFF, d.SavedZ, d.Heading, d.SavedFacing);
        }

        /// <summary>Sets one player field or skill; refused values change nothing.</summary>
        public string PlayerSet(string field, string value)
        {
            var p = SvSheet();
            int skill;
            var f = SvFindField(field, out skill);
            string before;
            if (f == null)
            {
                int v = SvInt(value);
                if (v < 0 || v > 255) throw new ArgumentOutOfRangeException(null, "A skill is 0-255 (play stops at 30).");
                before = p.Skill(skill).ToString(CultureInfo.InvariantCulture);
                p.SetSkill(skill, v);
                SvPlayerEdited(p);
                return PlayerDat.SkillNames[skill] + ": " + before + " -> " + v + SvWarnNote(p, PlayerDat.SkillNames[skill], "Skills") + " (not saved).";
            }
            if (f.Set == null) throw new McpEditorException(f.Name + " is read only: " + f.Help + ".");
            if (f.Uw1Only && p.Kind == GameKind.UW2) throw new McpEditorException(f.Name + " is Ultima Underworld 1's: Underworld II keeps other things there.");
            before = f.Get(p);
            try { f.Set(p, value); }
            catch (ArgumentOutOfRangeException e) { throw new ArgumentOutOfRangeException(null, f.Name + ": " + f.Help + " (" + e.Message.Split('\n')[0].Split(" (Parameter")[0] + ")."); }
            SvPlayerEdited(p);
            return f.Name + ": " + before + " -> " + f.Get(p) + SvWarnNote(p, f.Name, null) + " (not saved).";
        }

        /// <summary>The checks' words about a field just set, if any.</summary>
        static string SvWarnNote(PlayerSheet p, string field, string group)
        {
            var w = p.Check().Where(x => SvKey(x.Field) == SvKey(field) || (group != null && x.Field == group && x.Message.StartsWith(field, StringComparison.OrdinalIgnoreCase))).ToList();
            return w.Count == 0 ? "" : ". " + string.Join(" ", w.Select(x => x.Level + ": " + x.Message));
        }

        /// <summary>Everything the game would notice about the player, or could not have produced.</summary>
        public string PlayerCheck()
        {
            var p = SvSheet();
            var w = p.Check();
            if (w.Count == 0) return "Nothing the game would notice.";
            var b = new StringBuilder();
            foreach (var x in w.OrderBy(x => x.Level)) b.Append(x.Level).Append(" (").Append(x.Field).Append("): ").Append(x.Message).Append('\n');
            return b.ToString();
        }

        /// <summary>Sets max vitality, max mana and carrying capacity as the game computes them.</summary>
        public string PlayerRecompute()
        {
            var p = SvSheet();
            string before = string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", p.MaxVitality, p.MaxMana, p.MaxWeight);
            p.Recompute();
            SvPlayerEdited(p);
            return string.Format(CultureInfo.InvariantCulture, "Max vitality / mana / weight: {0} -> {1}/{2}/{3} (not saved).", before, p.MaxVitality, p.MaxMana, p.MaxWeight);
        }

        /// <summary>Edit ▸ Move the player here: onto an open tile of any level, at its floor; the heading kept unless given.</summary>
        public string PlayerMove(int level, int x, int y, double? fineX, double? fineY, int? heading)
        {
            var p = SvSheet();
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            if (x < 1 || x > 62 || y < 1 || y > 62) throw new ArgumentOutOfRangeException(null, "The player stands on tiles 1-62.");
            if (m.Tiles[x, y].IsSolid) throw new McpEditorException(string.Format("({0}, {1}) is rock, so there is nowhere to stand.", x, y));
            double fx = fineX ?? 0.5, fy = fineY ?? 0.5;
            if (fx < 0 || fx >= 1 || fy < 0 || fy >= 1) throw new ArgumentOutOfRangeException(null, "Fine positions are 0 to below 1 (0.5 the middle).");
            if (heading.HasValue && (heading < 0 || heading > 7)) throw new ArgumentOutOfRangeException(null, "Headings are 0-7 (0 north, 2 east, 4 south, 6 west).");
            double floor = m.FloorAt(x, y, fx, fy);
            int facing = heading.HasValue ? heading.Value << 13 : p.Dat.SavedFacing;
            p.Dat.SetPosition(level, (int)((x + fx) * 256), (int)((y + fy) * 256), (int)Math.Round(floor * 256), facing);
            p.Dirty = true;
            SvPlayerEdited(p);
            return string.Format("Player moved to ({0}, {1}) on level {2} (not saved). {3}", x, y, level, SvPosition(p));
        }

        /// <summary>Writes PLAYER.DAT (the old one kept as PLAYER.DAT.BKP) and the conversations' globals changed here (BGLOBALS.DAT).</summary>
        public string PlayerSave()
        {
            var p = SvSheet();
            var said = new List<string>();
            if (p.Dirty)
            {
                p.Save();
                said.Add(Path.GetFileName(p.Path));
            }
            var g = svGlobals != null && svGlobalsOf == Session ? svGlobals : null;
            if (g != null && g.Dirty) foreach (var f in g.Save()) if (!said.Contains(Path.GetFileName(f))) said.Add(Path.GetFileName(f));
            SvPlayerEdited(p);
            if (said.Count == 0) return "Nothing to save: the player is as on disk.";
            return "Saved " + string.Join(" and ", said) + " in " + Path.GetDirectoryName(p.Path) + " (the old files kept as .BKP).";
        }

        /// <summary>The editor's default character (the one play tests and save_create use when there is no saved game).</summary>
        public string PlayerDefault()
        {
            Require();
            var bytes = DefaultPlayer.Bytes(Session.Kind);
            if (bytes == null) return "The editor has no default player for " + Session.Kind + ".";
            var p = PlayerSheet.FromBytes(bytes, Session.Kind, Session.DataFolder);
            var b = new StringBuilder();
            b.Append("The editor's default ").Append(Session.Kind).Append(" character (built into the editor, not editable; save_create makes a saved game with it):\n");
            foreach (var f in SvFields) if (!(f.Uw1Only && p.Kind == GameKind.UW2)) b.Append(f.Name).Append(": ").Append(f.Get(p)).Append('\n');
            b.Append("skills:");
            for (int i = 0; i < 20; i++) b.Append(i == 0 ? " " : ", ").Append(PlayerDat.SkillNames[i].ToLowerInvariant()).Append(' ').Append(p.Skill(i));
            b.Append("\ninventory: ").Append(p.Inventory.All().Count()).Append(" object(s)\n");
            return b.ToString();
        }

        // ---- the inventory

        /// <summary>The objects numbered as inventory_list shows them: depth first, from 1.</summary>
        static List<InventoryItem> SvItems(PlayerSheet p) { return p.Inventory.All().ToList(); }

        InventoryItem SvItem(PlayerSheet p, int number)
        {
            var all = SvItems(p);
            if (number < 1 || number > all.Count) throw new ArgumentOutOfRangeException(null, all.Count == 0 ? "The inventory is empty." : "Inventory objects are 1-" + all.Count + " (inventory_list numbers them).");
            return all[number - 1];
        }

        static readonly string[] SvSlotKeys =
        {
            "head", "body", "gloves", "legs", "feet", "shoulder1", "shoulder2", "hand1", "hand2", "ring1", "ring2",
            "pack1", "pack2", "pack3", "pack4", "pack5", "pack6", "pack7", "pack8",
        };

        static int SvSlotOf(string slot)
        {
            string k = SvKey(slot);
            int n;
            if (int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            {
                if (n < 0 || n >= PlayerInventory.Slots) throw new ArgumentOutOfRangeException(null, "Slots are 0-18.");
                return n;
            }
            if (k == "torso" || k == "chest") return 1;
            if (k == "hands" || k == "handsgloves") return 2;
            if (k == "helmet") return 0;
            if (k == "boots") return 4;
            int i = Array.IndexOf(SvSlotKeys, k);
            if (i < 0) throw new ArgumentException("No slot " + slot + ". Slots: " + string.Join(", ", SvSlotKeys.Select((s, j) => j + " " + s)) + ".");
            return i;
        }

        string SvItemLine(InventoryItem it, int number)
        {
            var b = new StringBuilder();
            int depth = 0;
            for (var q = it.Parent; q != null; q = q.Parent) depth++;
            b.Append(new string(' ', depth * 2)).Append(number).Append(". ");
            if (it.Parent == null) b.Append('[').Append(it.Slot >= 0 ? PlayerInventory.SlotNames[it.Slot] : "no slot").Append("] ");
            b.Append(Hex(it.ItemId)).Append(' ').Append(Session.Names.Get(it.ItemId));
            b.Append(" quality ").Append(it.Quality).Append(" owner ").Append(it.Owner);
            if (it.IsQuantity) b.Append(it.Link < 0x200 ? " quantity " + it.Link : " special " + (it.Link - 0x200) + " (link 0x" + it.Link.ToString("X3") + ")");
            else b.Append(" container of ").Append(it.Contents.Count);
            if (it.Flags != 0) b.Append(" flags ").Append(it.Flags);
            if (it.Enchanted) b.Append(" enchanted");
            if (it.Invisible) b.Append(" invisible");
            return b.ToString();
        }

        /// <summary>The inventory: each object numbered, in its slot or indented in its container.</summary>
        public string InventoryList()
        {
            var p = SvSheet();
            var all = SvItems(p);
            if (all.Count == 0) return "The inventory is empty.";
            var b = new StringBuilder();
            for (int i = 0; i < all.Count; i++) b.Append(SvItemLine(all[i], i + 1)).Append('\n');
            var free = Enumerable.Range(0, PlayerInventory.Slots).Where(s => p.Inventory.InSlot(s) == null).Select(s => PlayerInventory.SlotNames[s]).ToList();
            b.Append("Empty slots: ").Append(free.Count == 0 ? "none" : string.Join(", ", free)).Append('\n');
            foreach (var x in p.Inventory.Problems()) b.Append("Note: ").Append(x).Append('\n');
            return b.ToString();
        }

        /// <summary>
        /// Puts a new object in the inventory: in a slot (an equip slot or the pack), in a container
        /// object, or (neither) in the first free pack slot. Containers get an empty list, anything
        /// else a quantity of 1, quality 40 (what the game's own saves have).
        /// </summary>
        public string InventoryAdd(string item, string slot, int? into, int? quality, int? owner, int? quantity, int? special, bool? enchanted)
        {
            var p = SvSheet();
            int id = ItemOf(item);
            if (slot != null && into.HasValue) throw new ArgumentException("Give a slot or a container (into), not both.");
            if (quantity.HasValue && special.HasValue) throw new ArgumentException("Give a quantity or a special value, not both.");
            bool isContainer = (ItemCategories.Of(id, Session.Kind) & ItemCategory.Container) != 0;
            if (isContainer && (quantity.HasValue || special.HasValue)) throw new ArgumentException(Session.Names.Get(id) + " is a container: its link holds its contents, not a quantity.");
            if (quantity.HasValue && (quantity < 0 || quantity >= 0x200)) throw new ArgumentOutOfRangeException(null, "A quantity is 0-511.");
            if (special.HasValue && (special < 0 || special > 0x1FF)) throw new ArgumentOutOfRangeException(null, "A special value is 0-511 (it is stored as 512 + the value).");
            if (quality.HasValue && (quality < 0 || quality > 63)) throw new ArgumentOutOfRangeException(null, "Quality is 0-63.");
            if (owner.HasValue && (owner < 0 || owner > 63)) throw new ArgumentOutOfRangeException(null, "Owner is 0-63.");
            var inv = p.Inventory;
            InventoryItem it;
            if (into.HasValue)
            {
                var c = SvItem(p, into.Value);
                if (c.IsQuantity) throw new McpEditorException("Object " + into + " (" + Session.Names.Get(c.ItemId) + ") is not a container: its link is a quantity or special value.");
                it = inv.Add(id, c, isContainer);
            }
            else if (slot != null)
            {
                int s = SvSlotOf(slot);
                var there = inv.InSlot(s);
                if (there != null) throw new McpEditorException("The " + PlayerInventory.SlotNames[s] + " slot holds " + Hex(there.ItemId) + " " + Session.Names.Get(there.ItemId) + ": move or remove it first.");
                it = new InventoryItem();
                it.ItemId = id;
                it.Quality = 0x28;
                if (!isContainer) { it.IsQuantity = true; it.Link = 1; }
                it.Slot = s;
                inv.Top.Add(it);
                inv.Changed = true;
            }
            else it = inv.Add(id, null, isContainer);
            if (quality.HasValue) it.Quality = quality.Value;
            if (owner.HasValue) it.Owner = owner.Value;
            if (quantity.HasValue) it.Link = quantity.Value;
            if (special.HasValue) it.Link = 0x200 + special.Value;
            if (enchanted.HasValue) it.Enchanted = enchanted.Value;
            inv.Changed = true;
            SvPlayerEdited(p);
            int number = SvItems(p).IndexOf(it) + 1;
            return "Added (not saved): " + SvItemLine(it, number).TrimStart();
        }

        /// <summary>Sets a field of an inventory object: item, quality, owner, quantity, special, link, flags, enchanted, invisible, is quantity.</summary>
        public string InventorySet(int number, string field, string value)
        {
            var p = SvSheet();
            var it = SvItem(p, number);
            string before = SvItemLine(it, number).TrimStart();
            switch (SvKey(field))
            {
                case "item": case "itemid": case "id": it.ItemId = ItemOf(value); break;
                case "quality": it.Quality = SvRange(SvInt(value), 0, 63, "Quality"); break;
                case "owner": it.Owner = SvRange(SvInt(value), 0, 63, "Owner"); break;
                case "quantity":
                    {
                        int q = SvRange(SvInt(value), 0, 0x1FF, "A quantity");
                        if (!it.IsQuantity) it.IsQuantity = true;
                        it.Link = q;
                        break;
                    }
                case "special":
                    {
                        int q = SvRange(SvInt(value), 0, 0x1FF, "A special value");
                        if (!it.IsQuantity) it.IsQuantity = true;
                        it.Link = 0x200 + q;
                        break;
                    }
                case "link":
                    if (!it.IsQuantity) throw new McpEditorException("A container's link is its contents (written on saving): set is quantity true first, or give quantity or special.");
                    it.Link = SvRange(SvInt(value), 0, 0x3FF, "The link");
                    break;
                case "flags": it.Flags = SvRange(SvInt(value), 0, 7, "Flags"); break;
                case "enchanted": it.Enchanted = SvBool(value); break;
                case "invisible": it.Invisible = SvBool(value); break;
                case "isquantity": it.IsQuantity = SvBool(value); break;
                default: throw new ArgumentException("No field " + field + ". Fields: item, quality, owner, quantity, special, link, flags, enchanted, invisible, is quantity.");
            }
            p.Inventory.Changed = true;
            SvPlayerEdited(p);
            return "Changed (not saved): " + before + "\n-> " + SvItemLine(it, number).TrimStart();
        }

        static int SvRange(int v, int min, int max, string what)
        {
            if (v < min || v > max) throw new ArgumentOutOfRangeException(null, what + " is " + min + "-" + max + ".");
            return v;
        }

        /// <summary>Moves an object (with what it holds) to a slot (equipping it; what was there goes to its old slot or the pack) or into a container.</summary>
        public string InventoryMove(int number, string slot, int? into)
        {
            var p = SvSheet();
            var it = SvItem(p, number);
            if ((slot == null) == !into.HasValue) throw new ArgumentException("Give a slot or a container (into).");
            if (into.HasValue)
            {
                var c = SvItem(p, into.Value);
                p.Inventory.MoveInto(it, c);
            }
            else
            {
                int s = SvSlotOf(slot);
                p.Inventory.MoveToSlot(it, s);
            }
            SvPlayerEdited(p);
            int n = SvItems(p).IndexOf(it) + 1;
            return "Moved (not saved): " + SvItemLine(it, n).TrimStart() + (into.HasValue ? " (now inside object " + (SvItems(p).IndexOf(it.Parent) + 1) + ")" : "") + ". Numbers may have changed: inventory_list.";
        }

        /// <summary>Takes an object, and whatever is inside it, out of the inventory.</summary>
        public string InventoryRemove(int number)
        {
            var p = SvSheet();
            var it = SvItem(p, number);
            int inside = it.All().Count() - 1;
            string line = SvItemLine(it, number).TrimStart();
            p.Inventory.Remove(it);
            SvPlayerEdited(p);
            return "Removed (not saved): " + line + (inside > 0 ? " and the " + inside + " object(s) inside it" : "") + ". Numbers after it moved up.";
        }
    }
}
