using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Level ▸ Find in all levels (and its replace), World check, Fixes, Item cleaner, the checks
    /// before a mod; Tools ▸ Item slots; an object's own text; the Try Jump tool and the
    /// collision boxes.
    /// </summary>
    public sealed partial class McpEditor
    {
        // ---- find and replace

        static ItemCategory EdCategories(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ItemCategory.All;
            ItemCategory c = ItemCategory.None;
            foreach (var p in text.Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                ItemCategory one;
                if (int.TryParse(p, out _) || !Enum.TryParse(p.Trim(), true, out one))
                    throw new ArgumentException("Categories are " + string.Join(", ", Enum.GetNames(typeof(ItemCategory)).Where(n => n != "None" && n != "All")) + ".");
                c |= one;
            }
            return c;
        }

        ObjectQuery EdQuery(string text, string categories, int? level, bool? enchanted, bool? invisible, bool? inContainer, bool? owned, int qualityMin, int qualityMax)
        {
            Require();
            if (level.HasValue) CheckLevelNumber(Session, level.Value);
            Range(qualityMin, 0, 63, "lowest quality");
            Range(qualityMax, 0, 63, "highest quality");
            return new ObjectQuery
            {
                Text = (text ?? "").Trim(), Categories = EdCategories(categories), OnlyLevel = level ?? 0,
                Enchanted = enchanted, Invisible = invisible, InContainer = inContainer, Owned = owned, QualityMin = qualityMin, QualityMax = qualityMax,
            };
        }

        /// <summary>Level ▸ Find in all levels: objects by name (also an NPC's, a quest item's, an enchantment's or a text), 0x item, #index, category and flags, as edited.</summary>
        public string ObjectsFind(string text, string categories, int? level, bool? enchanted, bool? invisible, bool? inContainer, bool? owned, int qualityMin, int qualityMax, int max)
        {
            var q = EdQuery(text, categories, level, enchanted, invisible, inContainer, owned, qualityMin, qualityMax);
            if (max < 1 || max > 2000) throw new ArgumentOutOfRangeException(null, "Show 1-2000 objects.");
            var hits = LevelSearch.Run(Session, q, max + 1, l => Edits.Existing(l)?.Model);
            if (hits.Count == 0) return "Nothing found.";
            var b = new StringBuilder();
            foreach (var h in hits.Take(max))
            {
                b.Append("L").Append(h.Level).Append(" #").Append(h.Index).Append(' ').Append(Hex(h.Item)).Append(' ').Append(h.Name).Append(" at (").Append(h.TileX).Append(", ").Append(h.TileY).Append(')');
                if (!string.IsNullOrEmpty(h.Inside)) b.Append(" in ").Append(h.Inside);
                if (!string.IsNullOrEmpty(h.Matched)) b.Append(" [").Append(h.Matched).Append(']');
                b.Append('\n');
            }
            if (hits.Count > max) b.Append("... more: give max (up to 2000) or narrow the search.\n");
            return b.ToString();
        }

        /// <summary>Find's replace: every object found becomes another item, or gets one field set; one undo step over all the levels touched.</summary>
        public string ObjectsReplace(string text, string categories, int? level, bool? enchanted, bool? invisible, bool? inContainer, bool? owned, int qualityMin, int qualityMax, string newItem, string field, int? value)
        {
            var q = EdQuery(text, categories, level, enchanted, invisible, inContainer, owned, qualityMin, qualityMax);
            if (q.IsEmpty) throw new ArgumentException("Narrow the search first (text, categories, a level or a filter): a replace of every object of the game is refused.");
            Edits.RefuseDamagedLevels("Replace");
            var a = new ReplaceAction();
            if (!string.IsNullOrWhiteSpace(newItem))
            {
                if (field != null || value.HasValue) throw new ArgumentException("Give newItem, or field and value, not both.");
                a.NewItem = ItemOf(newItem);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(field) || !value.HasValue) throw new ArgumentException("Give newItem, or field and value (fields: " + string.Join(", ", LevelReplace.Fields) + ").");
                a.Field = LevelReplace.Fields.FirstOrDefault(f => string.Equals(f, field.Trim(), StringComparison.OrdinalIgnoreCase));
                if (a.Field == null) throw new ArgumentException("Fields are " + string.Join(", ", LevelReplace.Fields) + ".");
                a.Value = value.Value;
            }
            var hits = LevelSearch.Run(Session, q, 2000, l => Edits.Existing(l)?.Model);
            if (hits.Count == 0) return "Nothing found, nothing changed.";
            var r = LevelReplace.Apply(Edits, hits, a);
            return hits.Count + " found, to " + a.Describe(Session.Names) + ": " + r.Describe() + (r.Changed > 0 ? " Undo on any of those levels takes the whole replace back." : "");
        }

        // ---- world check, fixes, mod checks

        /// <summary>Level ▸ World check: locks no key opens, keys that open nothing, levels near the object limits, damaged levels.</summary>
        public string WorldCheckRun()
        {
            Require();
            var found = WorldCheck.Run(Session, l => Edits.Model(l));
            if (found.Count == 0) return "The world check found nothing.";
            var b = new StringBuilder();
            foreach (var f in found) b.Append(f.Severity == 2 ? "PROBLEM " : f.Severity == 1 ? "warning " : "note ").Append(f.Text).Append('\n');
            return b.ToString();
        }

        List<int> EdLevels(string levels)
        {
            Require();
            var present = Session.LevelSlots().Where(s => s.Present).Select(s => s.Level).ToList();
            if (string.IsNullOrWhiteSpace(levels) || levels.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return present;
            var list = EdIndexes(levels);
            foreach (int l in list) { CheckLevelNumber(Session, l); if (!present.Contains(l) && Edits.Existing(l) == null) throw new ArgumentException("Level " + l + " is an empty or damaged slot."); }
            return list;
        }

        byte[] EdDataOf(int level)
        {
            var e = Edits.Existing(level);
            if (e != null) return e.Data;
            var m = Session.LoadLevel(level);
            return m.Present ? m.Block.Data : null;
        }

        static string EdFindingLine(int n, LevelFinding f)
        {
            return n + ". [" + f.Check.Id + "] " + (f.Severity == FixSeverity.ListError ? "LIST ERROR " : f.Severity == FixSeverity.Problem ? "problem " : "warning ") + f.Text
                + (f.CanFix ? " Fix: " + f.FixText : " (no automatic fix)");
        }

        /// <summary>Level ▸ Fixes: what the checks find in each level, numbered a level, with what each fix does.</summary>
        public string FixesFind(string levels, string check)
        {
            var list = EdLevels(levels);
            var ctx = FixContext.Build(Session, l => Edits.Model(l));
            var b = new StringBuilder();
            int total = 0;
            foreach (int l in list)
            {
                var found = LevelFixes.Find(Session, l, EdDataOf(l), ctx);
                if (!string.IsNullOrWhiteSpace(check)) found = found.Where(f => string.Equals(f.Check.Id, check.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (found.Count == 0) continue;
                b.Append("Level ").Append(l).Append(": ").Append(found.Count).Append(" finding").Append(found.Count == 1 ? "" : "s").Append('\n');
                for (int i = 0; i < found.Count && i < 200; i++) b.Append("  ").Append(EdFindingLine(i + 1, found[i])).Append('\n');
                if (found.Count > 200) b.Append("  ... ").Append(found.Count - 200).Append(" more.\n");
                total += found.Count;
            }
            if (total == 0) return "The checks found nothing" + (string.IsNullOrWhiteSpace(check) ? "" : " of [" + check.Trim() + "]") + " in " + (list.Count == 1 ? "level " + list[0] : list.Count + " levels") + ".";
            return b.ToString();
        }

        /// <summary>The Fixes window's checks: id, title, kind and what each looks for.</summary>
        public string FixesChecks()
        {
            var b = new StringBuilder();
            foreach (var c in LevelFixes.Checks) b.Append('[').Append(c.Id).Append("] ").Append(c.Title).Append(" (").Append(c.Severity).Append("): ").Append(c.Description).Append('\n');
            return b.ToString();
        }

        /// <summary>
        /// Applies fixes on a level, one undo step: "all" (Fix all; warnings too when asked), a check's id
        /// (every finding of it), or finding numbers from fixes_find.
        /// </summary>
        public string FixesApply(int level, string what, bool warnings)
        {
            var e = Editable(level);
            var ctx = FixContext.Build(Session, l => Edits.Model(l));
            string w = (what ?? "all").Trim();
            int n;
            if (w.Length == 0 || w.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                n = LevelFixes.FixAll(e, warnings, ctx);
                return n == 0 ? "Nothing to fix on level " + level + (warnings ? "." : " (warnings left out: give warnings to fix those too).") : "Fixed " + n + " finding" + (n == 1 ? "" : "s") + " on level " + level + ", one undo step. " + Check(level);
            }
            var found = LevelFixes.Find(e, ctx);
            List<LevelFinding> chosen;
            var byId = LevelFixes.Checks.FirstOrDefault(c => string.Equals(c.Id, w, StringComparison.OrdinalIgnoreCase));
            if (byId != null) chosen = found.Where(f => f.Check == byId).ToList();
            else chosen = EdIndexes(w).Select(i => { if (i < 1 || i > found.Count) throw new ArgumentOutOfRangeException(null, "Level " + level + " has findings 1-" + found.Count + " now (fixes_find)."); return found[i - 1]; }).ToList();
            var none = chosen.Where(f => !f.CanFix).ToList();
            if (chosen.Count == 0) return "Nothing of that to fix on level " + level + ".";
            n = LevelFixes.FixMany(e, chosen, "Fix: " + (byId != null ? byId.Title.ToLowerInvariant() : chosen.Count + " findings"));
            return "Fixed " + n + " of " + chosen.Count + " finding" + (chosen.Count == 1 ? "" : "s") + " on level " + level + (none.Count > 0 ? " (" + none.Count + " have no automatic fix)" : "") + ", one undo step. " + Check(level);
        }

        /// <summary>The checks run before a mod is made: the game's list check, the Fixes' problems and the world check, on the levels as saved (or as edited).</summary>
        public string ModChecksRun(bool edited)
        {
            Require();
            var r = edited ? ModChecks.Run(Session, l => Edits.Model(l)) : ModChecks.Run(Session);
            return (edited ? "The levels as edited (a mod holds only what is saved):\n" : "The game as saved:\n") + r.Report(80);
        }

        // ---- item cleaner

        static CleanKind EdCleanKinds(string kinds)
        {
            if (string.IsNullOrWhiteSpace(kinds)) return CleanKind.Debris | CleanKind.Bones;
            CleanKind k = CleanKind.None;
            foreach (var p in kinds.Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                CleanKind one;
                if (int.TryParse(p, out _) || !Enum.TryParse(p.Trim(), true, out one) || one == CleanKind.None)
                    throw new ArgumentException("Kinds are " + string.Join(", ", ItemCleaner.Choices.Select(c => c.Key.ToString())) + ".");
                k |= one;
            }
            return k;
        }

        /// <summary>Level ▸ Item cleaner: what it would remove (apply false) or removes (one undo step a level). Named, quest and guarded items are kept.</summary>
        public string ItemClean(string levels, string kinds, bool insideContainers, bool creatureInventories, int x1, int y1, int x2, int y2, string name, bool apply)
        {
            var list = EdLevels(levels);
            CheckTile(x1, y1); CheckTile(x2, y2);
            var opt = new CleanOptions
            {
                Kinds = EdCleanKinds(kinds), InsideContainers = insideContainers, CreatureInventories = creatureInventories,
                X0 = Math.Min(x1, x2), X1 = Math.Max(x1, x2), Y0 = Math.Min(y1, y2), Y1 = Math.Max(y1, y2), Name = name ?? "",
            };
            var plan = ItemCleaner.Preview(Session, l => Edits.Model(l), list, opt);
            var b = new StringBuilder();
            if (apply)
            {
                int n = plan.Remove.Count == 0 ? 0 : ItemCleaner.Apply(Edits, plan);
                b.Append("Removed ").Append(n).Append(" object").Append(n == 1 ? "" : "s").Append(n > 0 ? " (one undo step a level: " + string.Join(", ", plan.Remove.Select(c => c.Level).Distinct()) + ")" : "").Append(".\n");
            }
            else b.Append("Would remove ").Append(plan.Remove.Count).Append(" object").Append(plan.Remove.Count == 1 ? "" : "s").Append(" (give apply to remove them):\n");
            foreach (var kv in plan.ByName.OrderByDescending(k => k.Value)) b.Append("  ").Append(kv.Value).Append(" x ").Append(kv.Key).Append('\n');
            if (plan.Kept.Count > 0) b.Append("Kept: ").Append(string.Join("; ", plan.Kept.Select(k => k.Value + " " + k.Key))).Append('\n');
            if (!apply) foreach (var c in plan.Remove.Take(60)) b.Append("  ").Append(c).Append('\n');
            if (!apply && plan.Remove.Count > 60) b.Append("  ... ").Append(plan.Remove.Count - 60).Append(" more\n");
            return b.ToString();
        }

        // ---- item slots

        ItemUses.ConversationIndex EdConversations()
        {
            try { return ItemUses.IndexConversations(ConversationLibrary.Open(Session)); }
            catch (Exception) { return null; }
        }

        ObjectTables EdObjectTables()
        {
            try { return TblObjects(); } catch (Exception) { return null; }
        }

        CombinationTable EdCombinations()
        {
            try { return CombinationTable.Load(Session); } catch (Exception) { return null; }
        }

        /// <summary>Tools ▸ Item slots: everything that uses an item id (objects, conversations, the game's code, loot, combinations, quest items).</summary>
        public string ItemUsesOf(string item)
        {
            Require();
            int it = ItemOf(item);
            var conv = EdConversations();
            var r = ItemUses.Find(Session, it, l => Edits.Model(l), conv, EdObjectTables(), EdCombinations());
            var b = new StringBuilder();
            b.Append(Hex(it)).Append(' ').Append(r.Name).Append(": ").Append(r.Summary).Append(". Class: ").Append(ItemUses.ClassOf(it, Session.IsUw2)).Append(".\n");
            if (conv == null) b.Append("(The conversations could not be read: they are left out.)\n");
            foreach (var u in r.Uses.Take(150)) b.Append("  ").Append(u.Kind).Append(": ").Append(EdFlat(u.Text)).Append('\n');
            if (r.Uses.Count > 150) b.Append("  ... ").Append(r.Uses.Count - 150).Append(" more\n");
            return b.ToString();
        }

        /// <summary>The item slots nothing uses (free to turn into a new item), or every slot with its uses in short.</summary>
        public string ItemSlots(bool freeOnly)
        {
            Require();
            var conv = EdConversations();
            var all = ItemUses.Census(Session, l => Edits.Model(l), conv, EdObjectTables(), EdCombinations());
            var b = new StringBuilder();
            b.Append(all.Count).Append(" item ids, ").Append(all.Count(r => r.Free)).Append(" free").Append(conv == null ? " (conversations could not be read and are left out)" : "").Append(".\n");
            foreach (var r in all.Where(r => !freeOnly || r.Free)) b.Append(Hex(r.Item)).Append(' ').Append(r.Name).Append(": ").Append(r.Summary).Append('\n');
            return b.ToString();
        }

        // ---- an object's own text

        /// <summary>The text a book, scroll, gravestone, writing or text trap reads, and how many objects share that string.</summary>
        public string ObjectText(int level, int index)
        {
            var m = ReadModel(level);
            if (index < 2 || index >= m.Objects.Length || m.Objects[index].Free) throw new ArgumentException("#" + index + " is not an object of level " + level + ".");
            var o = m.Objects[index];
            int block, lo, hi; string what;
            if (!ObjectTextEdits.Range(Session.Kind, o.Item, out block, out lo, out hi, out what)) return o + " carries no text: books, scrolls, gravestones, writings and text traps do.";
            var st = TblStrings();
            var t = ObjectTextEdits.TextOf(Session.Kind, o, st);
            if (t == null) return o + " (a " + what + ") points at no text of block " + block + (o.Enchanted && block == 3 ? " (an enchanted scroll: its link is its spell)" : "") + ".";
            int users = ObjectTextEdits.Users(Edits, t.Block, t.Index);
            return o + " reads block " + t.Block + " string " + t.Index + (users > 1 ? " (shared by " + users + " objects)" : "") + ": \"" + (t.Text ?? "") + "\"";
        }

        /// <summary>
        /// Sets what an object reads: its string is changed when no other object reads it, else the object
        /// gets a free string of its own (one undo step for the pointer; the string itself is written by save).
        /// </summary>
        public string ObjectTextSet(int level, int index, string text)
        {
            var e = Editable(level);
            Index(e, index);
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Give the text.");
            var st = TblStrings();
            var o = e.Model.Objects[index];
            var t = ObjectTextEdits.TextOf(Session.Kind, o, st);
            if (t != null && ObjectTextEdits.Users(Edits, t.Block, t.Index) <= 1 && t.Index < st.Get(t.Block).Count)
            {
                try { st.Set(t.Block, t.Index, text); } catch (FormatException ex) { throw new ArgumentException(ex.Message); }
                HostTablesEdited?.Invoke();
                return o + " reads block " + t.Block + " string " + t.Index + " (its own): \"" + st.Get(t.Block, t.Index) + "\". Not written until save.";
            }
            TextSlot made;
            try { made = ObjectTextEdits.GiveOwnText(Edits, level, index, st, text); }
            catch (FormatException ex) { throw new ArgumentException(ex.Message); }
            HostTablesEdited?.Invoke();
            return o + " now reads its own string: block " + made.Block + " string " + made.Index + ": \"" + made.Text + "\". The pointer is an undo step; the string is written by save.";
        }

        // ---- the Try Jump tool and the collision boxes

        /// <summary>The Try Jump tool: whether the player can jump from one square to another (plain, and with the Jump spell).</summary>
        public string TryJump(int level, int fromX, int fromY, int toX, int toY)
        {
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            CheckTile(fromX, fromY); CheckTile(toX, toY);
            var boxes = JumpTrace.Obstacles(m, Hitboxes.ReadComObj(Session));
            Func<int, string> name = i => Session.Names.Get(i);
            var plain = JumpTrace.Try(m, fromX, fromY, toX, toY, null, boxes, null, null, name, false);
            var spell = JumpTrace.Try(m, fromX, fromY, toX, toY, null, boxes, null, null, name, true);
            var b = new StringBuilder();
            b.Append("Jump from (").Append(fromX).Append(", ").Append(fromY).Append(") to (").Append(toX).Append(", ").Append(toY).Append(") on level ").Append(level).Append(":\n");
            b.Append("Plain: ").Append(plain.Lands ? "lands. " : "does not land. ").Append(EdFlat(plain.Text)).Append('\n');
            b.Append("With the Jump spell: ").Append(spell.Lands ? "lands. " : "does not land. ").Append(EdFlat(spell.Text)).Append('\n');
            var from = JumpTrace.Surfaces(m, boxes, fromX, fromY, name);
            var to = JumpTrace.Surfaces(m, boxes, toX, toY, name);
            if (from.Count > 1) b.Append("Surfaces at the start: ").Append(string.Join("; ", from)).Append(" (the highest is used)\n");
            if (to.Count > 1) b.Append("Surfaces at the landing: ").Append(string.Join("; ", to)).Append('\n');
            return b.ToString();
        }

        /// <summary>View ▸ Collision edges: the boxes that overlap (a creature that starts inside another box may never move).</summary>
        public string HitboxOverlaps(int level)
        {
            var m = ReadModel(level);
            if (!m.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            var com = Hitboxes.ReadComObj(Session);
            if (com == null) throw new McpEditorException("There is no COMOBJ.DAT to read the boxes from.");
            var boxes = Hitboxes.Of(m, com);
            var over = Hitboxes.Overlaps(boxes);
            if (over.Count == 0) return "Level " + level + ": " + boxes.Count + " collision boxes, none overlapping.";
            var b = new StringBuilder("Level " + level + ": " + boxes.Count + " collision boxes, " + over.Count + " overlaps:\n");
            foreach (var o in over.Take(200))
                b.Append("  ").Append(o).Append(": ").Append(Session.Names.Get(o.A.Item)).Append(o.A.Creature ? " (creature)" : "").Append(" and ").Append(Session.Names.Get(o.B.Item)).Append(o.B.Creature ? " (creature)" : "").Append('\n');
            if (over.Count > 200) b.Append("  ... ").Append(over.Count - 200).Append(" more\n");
            return b.ToString();
        }
    }
}
