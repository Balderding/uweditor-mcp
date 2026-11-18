using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Conversations (Tools ▸ Conversations): CNV.ARK's scripts as the editor shows them (the
    /// Dialogue tab's source, the Script tab's assembly, the strings), edits compiled and applied
    /// as the editor applies them, the simulator run headless and the graph as text.
    /// Attached, the app's ConversationLibrary is the one changed; standing alone this server
    /// keeps its own, sharing STRINGS.PAK with string_set's copy so both write one file.
    /// </summary>
    public sealed partial class McpEditor
    {
        ConversationLibrary ctLib;
        byte[] ctLibCnvDisk, ctLibPakDisk;
        readonly Dictionary<int, List<ConversationState>> ctUndo = new Dictionary<int, List<ConversationState>>();
        ConversationLibrary ctUndoLib;

        /// <summary>The library to read and change, or null when there is none open yet (for save).</summary>
        ConversationLibrary CtLibIfAny()
        {
            if (Session == null) return null;
            var h = HostConversations?.Invoke();
            if (h != null && h.Session == Session) return h;
            return ctFor == Session ? ctLib : null;
        }

        ConversationLibrary CtLib()
        {
            CtFresh();
            var h = HostConversations?.Invoke();
            if (h != null && h.Session == Session)
            {
                if (ctLib != null && ctLib.HasChanges) throw new McpEditorException("The editor has the conversations open now, without your unsaved conversation changes.");
                ctLib = null;
                return CtUndoFor(h);
            }
            if (ctLib == null || (!ctLib.HasChanges && (CtDiffers(ctLib.Archive.Path, ctLibCnvDisk) || CtDiffers(ctLib.Strings.Path, ctLibPakDisk))))
            {
                TblFresh();
                if (ownStrings != null && ownStrings.HasChanges && (ctLib == null || ownStrings.Pak != ctLib.Strings))
                    throw new McpEditorException("STRINGS.PAK has string_set changes not saved yet, and conversations keep their strings there too: save (or table_revert Strings) first.");
                ConversationLibrary lib;
                try { lib = ConversationLibrary.Open(Session); }
                catch (FileNotFoundException e) { throw new McpEditorException(e.Message); }
                ctLibCnvDisk = CtRead(lib.Archive.Path);
                ctLibPakDisk = CtRead(lib.Strings.Path);
                ctLib = lib;
            }
            // one STRINGS.PAK in memory: string_set's copy is the conversations' (as the app shares them)
            TblFresh();
            if (HostStrings?.Invoke() == null && (ownStrings == null || ownStrings.Pak != ctLib.Strings))
            {
                if (ownStrings != null && ownStrings.HasChanges)
                {
                    if (ctLib.Strings.HasChanges) throw new McpEditorException("STRINGS.PAK has unsaved changes both from string_set and from conversation edits: save one first.");
                    ctLib = null;
                    return CtLib();
                }
                ownStrings = StringsEditor.Open(Session, ctLib.Strings);
                ownStringsDisk = ctLibPakDisk;
            }
            return CtUndoFor(ctLib);
        }

        ConversationLibrary CtUndoFor(ConversationLibrary lib)
        {
            if (ctUndoLib != lib) { ctUndo.Clear(); ctUndoLib = lib; }
            return lib;
        }

        Conversation CtConv(ConversationLibrary lib, int slot)
        {
            if (slot < 0 || slot >= lib.Archive.Count) throw new ArgumentOutOfRangeException(null, "Conversation slots are 0-" + (lib.Archive.Count - 1) + " (generic ones from 256).");
            if (!lib.Archive.Has(slot)) throw new McpEditorException("Slot " + slot + " has no conversation (conversation_list shows the slots in use).");
            try { return lib.Archive.Read(slot); }
            catch (Exception e) when (!(e is McpEditorException)) { throw new McpEditorException("Conversation " + slot + " cannot be read: " + e.Message); }
        }

        /// <summary>Runs a change of a slot as one step conversation_undo takes back; a change that throws is put back.</summary>
        string CtChange(ConversationLibrary lib, int slot, Func<string> change)
        {
            string busy = HostConversationBusy?.Invoke(slot);
            if (busy != null) throw new McpEditorException(busy);
            var before = lib.Snapshot(slot);
            var namesBefore = lib.Names.UserSlot(slot);
            string said;
            try { said = change(); }
            catch (Exception)
            {
                try { lib.Restore(before); lib.Names.SetUserSlot(slot, namesBefore); } catch (Exception) { }
                throw;
            }
            List<ConversationState> list;
            if (!ctUndo.TryGetValue(slot, out list)) ctUndo[slot] = list = new List<ConversationState>();
            list.Add(before);
            if (list.Count > 50) list.RemoveAt(0);
            HostConversationChanged?.Invoke(slot);
            return said;
        }

        string CtSlotName(ConversationLibrary lib, int slot) { return lib.Name(slot) ?? "no name"; }

        // ---- reading

        public string ConversationList(bool users)
        {
            var lib = CtLib();
            Dictionary<int, List<ConversationUser>> who = users ? lib.FindUsers() : null;
            var b = new StringBuilder();
            int n = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                Conversation c;
                try { c = lib.Archive.Read(slot); } catch (Exception e) { b.Append(slot).Append(": cannot be read (").Append(e.Message).Append(")\n"); continue; }
                if (c == null) continue;
                n++;
                b.AppendFormat(Ci, "{0}: {1}  (strings block 0x{2:X4}, {3} words of code{4})", slot, CtSlotName(lib, slot), c.StringBlock, c.Code.Length, lib.Archive.IsChanged(slot) ? ", changed, not saved" : "");
                if (who != null)
                {
                    List<ConversationUser> u;
                    b.Append(who.TryGetValue(slot, out u) ? "  talkers: " + string.Join("; ", u.Take(6)) + (u.Count > 6 ? "; and " + (u.Count - 6) + " more" : "") : "  (no creature in the levels talks through it)");
                }
                b.Append('\n');
            }
            var empty = lib.EmptySlots();
            b.Insert(0, n + " conversations in " + lib.Archive.Path + "; empty slots a creature can use: " + (empty.Count == 0 ? "none" : empty.Count + " (" + string.Join(", ", empty.Take(12)) + (empty.Count > 12 ? "..." : "") + ")") + ".\n");
            return b.ToString();
        }

        public string ConversationGet(int slot, string view)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            string v = (view ?? "dialogue").Trim().ToLowerInvariant();
            string head = string.Format(Ci, "Conversation {0}: {1}. String block 0x{2:X4}, {3} words of code, {4} variables, {5} functions imported{6}.\n",
                slot, CtSlotName(lib, slot), c.StringBlock, c.Code.Length, c.Variables, c.Imports.Count(i => i.IsFunction), lib.Archive.IsChanged(slot) ? " (changed, not saved)" : "");
            switch (v)
            {
                case "dialogue":
                case "source":
                    return head + "The Dialogue tab's source (conversation_set takes it back, changed):\n\n" + lib.Decompile(c).Text;
                case "flat":
                    return head + "The flat view (every instruction's line, with labels; read only):\n\n" + Decompiler.Decompile(c, lib.ShownStringsOf(c).ToArray(), lib.Kind, false, lib.Names.Resolve(c)).Text;
                case "script":
                case "assembly":
                    {
                        var d = lib.Decompile(c);
                        return head + "The Script tab's assembly (conversation_script_set takes it back, changed):\n\n" +
                            Disassembler.Disassemble(c, a => { string n; return d.Notes.TryGetValue(a, out n) ? n : null; }, "Conversation " + slot + " — " + CtSlotName(lib, slot), d.Names);
                    }
                case "strings":
                    {
                        var s = lib.ShownStringsOf(c);
                        var b = new StringBuilder(head);
                        b.Append(s.Count).Append(" strings (conversation_string_set changes one):\n");
                        for (int i = 0; i < s.Count; i++) b.Append(i).Append(": ").Append(Decompiler.Quote(s[i])).Append('\n');
                        return b.ToString();
                    }
                default:
                    throw new ArgumentException("The view is dialogue (the source), flat, script (the assembly) or strings.");
            }
        }

        public string ConversationUsers(int slot)
        {
            var lib = CtLib();
            var map = lib.FindUsers();
            List<ConversationUser> u;
            if (!map.TryGetValue(slot, out u) || u.Count == 0) return "No creature in the levels (as saved) talks through slot " + slot + ".";
            return u.Count + " creature(s) talk through slot " + slot + " (" + CtSlotName(lib, slot) + "):\n" + string.Join("\n", u.Select((x, i) => (i + 1) + ". " + x));
        }

        // ---- changing

        static string CtErrors(CompileResult r)
        {
            var b = new StringBuilder();
            foreach (var e in r.Errors.Take(30)) b.Append("\n  error: ").Append(e);
            if (r.Errors.Count > 30) b.Append("\n  and ").Append(r.Errors.Count - 30).Append(" more errors");
            foreach (var w in r.Warnings.Take(20)) b.Append("\n  warning: ").Append(w);
            return b.ToString();
        }

        static string CtCompiled(CompileResult r)
        {
            string s = string.Format(Ci, "{0} function{1} compiled ({2}), {3} unchanged{4}", r.CompiledFunctions.Count, r.CompiledFunctions.Count == 1 ? "" : "s",
                r.CompiledFunctions.Count == 0 ? "none" : string.Join(", ", r.CompiledFunctions), r.CopiedFunctions.Count,
                r.AddedStrings.Count > 0 ? "; new strings " + string.Join(", ", r.AddedStrings) : "");
            if (r.UnusedStrings.Count > 0) s += "; strings no longer used (kept in the block): " + string.Join(", ", r.UnusedStrings.Select(x => "#" + x.Id));
            if (r.Warnings.Count > 0) s += ". Warnings:" + string.Join("", r.Warnings.Select(w => "\n  " + w));
            return s;
        }

        public string ConversationCheck(int slot, string source)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            if (source == null) throw new ArgumentException("No source given.");
            var r = lib.Compile(c, source);
            if (!r.Ok) return "The source does not compile: " + r.Errors.Count + " error(s)." + CtErrors(r);
            return "The source compiles: " + CtCompiled(r) + ". conversation_set applies it.";
        }

        public string ConversationSet(int slot, string source)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("No source given (conversation_get gives the conversation's source to change).");
            var r = lib.Compile(c, source);
            if (!r.Ok) throw new McpEditorException("Not applied, the source does not compile: " + r.Errors.Count + " error(s)." + CtErrors(r));
            return CtChange(lib, slot, () =>
            {
                lib.Apply(r);
                lib.Names.Adopt(lib.Archive.Read(slot), r.Placed);
                return "Conversation " + slot + " applied: " + CtCompiled(r) + "; " + lib.Archive.Read(slot).Code.Length + " words of code. Not saved until save; conversation_undo takes it back.";
            });
        }

        public string ConversationScriptSet(int slot, string script)
        {
            var lib = CtLib();
            CtConv(lib, slot);
            if (string.IsNullOrWhiteSpace(script)) throw new ArgumentException("No assembly given (conversation_get with view script gives it).");
            Conversation c;
            try
            {
                c = Assembler.Assemble(script);
                if (c.Slot != slot) throw new AssemblyError(1, ".slot says " + c.Slot + ", but this is conversation " + slot + ".");
            }
            catch (AssemblyError e) { throw new McpEditorException("Not applied: " + e.Message); }
            return CtChange(lib, slot, () =>
            {
                lib.Archive.Write(slot, c);
                return "Conversation " + slot + " assembled: " + c.Code.Length + " words of code. Not saved until save; conversation_undo takes it back.";
            });
        }

        public string ConversationStringSet(int slot, int index, string text)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            var s = lib.ShownStringsOf(c);
            if (index < 0 || index > s.Count) throw new ArgumentOutOfRangeException(null, "The block has strings 0-" + (s.Count - 1) + "; " + s.Count + " adds one at the end.");
            if (text == null) throw new ArgumentException("No text given.");
            bool added = index == s.Count;
            return CtChange(lib, slot, () =>
            {
                if (added) s.Add(text); else s[index] = text;
                try { lib.SetShownStrings(c, s); }
                catch (InvalidDataException e) { throw new McpEditorException(e.Message); }
                return string.Format(Ci, "String {0} of conversation {1} (block 0x{2:X4}) is now {3}{4}. Not saved until save.", index, slot, c.StringBlock, Decompiler.Quote(text), added ? " (added)" : "");
            });
        }

        public string ConversationAdd(int? slot, string name, string greeting, string answer, string farewell)
        {
            var lib = CtLib();
            int s;
            if (slot.HasValue) s = slot.Value;
            else
            {
                var empty = lib.EmptySlots();
                if (empty.Count == 0) throw new McpEditorException("CNV.ARK has no empty slot a creature can name (1-255).");
                s = empty[0];
            }
            if (lib.Archive.Has(s)) throw new McpEditorException("Slot " + s + " already has a conversation.");
            return CtChange(lib, s, () =>
            {
                Conversation c;
                try { c = lib.AddConversation(s, name, greeting ?? "Greetings.", answer ?? "Farewell.", farewell ?? "Fare thee well."); }
                catch (ArgumentOutOfRangeException e) { throw new McpEditorException(e.Message.Split('\n')[0].Replace(" (Parameter 'slot')", "")); }
                return string.Format(Ci, "Conversation {0} added{1}: a greeting, one answer and a goodbye (strings in block 0x{2:X4}). A creature talks through it when its conversation number (whoami) is {0}. Change it with conversation_set; not saved until save.",
                    s, string.IsNullOrWhiteSpace(name) ? "" : " as " + name.Trim(), c.StringBlock);
            });
        }

        public string ConversationDelete(int slot)
        {
            var lib = CtLib();
            CtConv(lib, slot);
            int talkers = lib.FindUsers().TryGetValue(slot, out var u) ? u.Count : 0;
            return CtChange(lib, slot, () =>
            {
                lib.DeleteConversation(slot);
                return "Conversation " + slot + " deleted (its strings stay in their block)." + (talkers > 0 ? " " + talkers + " creature(s) talk through it and will have nothing to say." : "") + " Not saved until save; conversation_undo takes it back.";
            });
        }

        public string ConversationRevert(int slot, string to)
        {
            var lib = CtLib();
            string t = (to ?? "saved").Trim().ToLowerInvariant();
            if (t != "saved" && t != "original") throw new ArgumentException("Revert to saved (as last saved) or original (as shipped: the .ORIG copies the editor kept).");
            if (t == "saved" && !lib.Archive.Has(slot) && !lib.Archive.IsChanged(slot)) throw new McpEditorException("Slot " + slot + " has no conversation.");
            return CtChange(lib, slot, () =>
            {
                if (t == "original")
                {
                    if (!lib.RevertToShipped(slot)) throw new McpEditorException("There is no shipped copy of conversation " + slot + ".");
                    return "Conversation " + slot + " is back as shipped (not saved; conversation_undo takes it back).";
                }
                var now = lib.Archive.Has(slot) ? lib.Archive.Read(slot) : null;
                if (now != null) lib.Strings.Revert(now.StringBlock);
                lib.Archive.Revert(slot);
                var c = lib.Archive.Has(slot) ? lib.Archive.Read(slot) : null;
                if (c != null) lib.Strings.Revert(c.StringBlock);
                return "Conversation " + slot + " is back as last saved (conversation_undo takes it back).";
            });
        }

        public string ConversationUndo(int slot)
        {
            var lib = CtLib();
            List<ConversationState> list;
            if (!ctUndo.TryGetValue(slot, out list) || list.Count == 0) throw new McpEditorException("Nothing of conversation " + slot + " to undo (only changes made here are undone; the editor's window has its own Undo apply).");
            string busy = HostConversationBusy?.Invoke(slot);
            if (busy != null) throw new McpEditorException(busy);
            var st = list[list.Count - 1];
            lib.Restore(st);
            list.RemoveAt(list.Count - 1);
            HostConversationChanged?.Invoke(slot);
            return "Undid the last change of conversation " + slot + " (" + list.Count + " more to undo).";
        }

        // ---- the simulator

        public string ConversationSimulate(int slot, string choices, string answers, int talker, string playerName, bool? female,
            int? seed, int? fixedRandom, bool? offersAccepted, bool? demandsGivenIn, string source, int maxSteps, bool log)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            if (maxSteps < 1000 || maxSteps > 5000000) throw new ArgumentOutOfRangeException(null, "maxSteps is 1000-5000000.");
            var strings = lib.ShownStringsOf(c);
            string what = "the conversation as applied";
            if (!string.IsNullOrWhiteSpace(source))
            {
                var r = lib.Compile(c, source);
                if (!r.Ok) throw new McpEditorException("Not simulated, the source does not compile: " + r.Errors.Count + " error(s)." + CtErrors(r));
                c = r.Conversation;
                strings = r.Strings;
                what = "the source given (not applied)";
            }
            var picks = new Queue<int>();
            foreach (var p in (choices ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)) picks.Enqueue(CtInt(p, "A choice"));
            var says = new Queue<string>((answers ?? "").Split(new[] { '|' }, StringSplitOptions.None).Where(a => answers != null && answers.Length > 0));
            var users = lib.FindUsers().TryGetValue(slot, out var ul) ? ul : new List<ConversationUser>();
            ConversationUser who;
            if (talker < 0) who = users.FirstOrDefault();
            else if (talker == 0) who = null;
            else if (talker <= users.Count) who = users[talker - 1];
            else throw new ArgumentOutOfRangeException(null, users.Count == 0 ? "No creature talks through slot " + slot + ": talker is 0 (default values)." : "talker is 0 (default values) or 1-" + users.Count + " (conversation_users lists them).");
            var world = SimWorld.FromGame(Session, slot, who);
            if (!string.IsNullOrWhiteSpace(playerName)) world.PlayerName = playerName.Trim();
            if (female.HasValue) world.PlayerFemale = female.Value;
            if (seed.HasValue) world.RandomSeed = seed.Value;
            if (fixedRandom.HasValue) world.FixedRandom = fixedRandom.Value;
            if (offersAccepted.HasValue) world.OffersAccepted = offersAccepted.Value;
            if (demandsGivenIn.HasValue) world.DemandsGivenIn = demandsGivenIn.Value;
            var sim = new ConversationSimulator(c, strings, world, (blk, i) => Session.Strings.Get(blk, i), SimWorld.SavedGlobals(Session, slot));
            sim.Names = lib.Names.Resolve(c);
            sim.LogInstructions = false;
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Simulating conversation {0} ({1}), {2}, talker: {3}.\n", slot, CtSlotName(lib, slot), what, world.TalkerDescription);
            int shown = 0;
            Action flush = () =>
            {
                for (; shown < sim.Output.Count; shown++)
                {
                    var o = sim.Output[shown];
                    string k = o.Kind == SimOutputKind.Say ? "NPC" : o.Kind == SimOutputKind.Respond ? "Player" : o.Kind.ToString();
                    b.Append(k).Append(": ").Append(o.Text).Append('\n');
                }
            };
            while (sim.Steps < maxSteps)
            {
                sim.Run((int)Math.Min(200000, maxSteps - sim.Steps));
                flush();
                if (sim.State == SimState.WaitingMenu)
                {
                    if (picks.Count == 0) break;
                    int n = picks.Dequeue();
                    string err = sim.Choose(n);
                    if (err != null) { b.Append("(choice ").Append(n).Append(" refused: ").Append(err).Append(")\n"); break; }
                }
                else if (sim.State == SimState.WaitingAsk)
                {
                    if (says.Count == 0) break;
                    string a = says.Dequeue();
                    b.Append("(typed: ").Append(a).Append(")\n");
                    sim.Answer(a);
                }
                else break;
            }
            flush();
            switch (sim.State)
            {
                case SimState.WaitingMenu:
                    b.Append("\nWAITING FOR A CHOICE (add it to choices and simulate again):\n");
                    for (int i = 0; i < sim.Menu.Texts.Count; i++) b.Append("  ").Append(i + 1).Append(". ").Append(sim.Menu.Texts[i]).Append('\n');
                    break;
                case SimState.WaitingAsk: b.Append("\nWAITING FOR TYPED TEXT (add it to answers, separated by |, and simulate again).\n"); break;
                case SimState.Ended: b.Append("\nTHE CONVERSATION ENDED.\n"); break;
                case SimState.Error: b.Append("\nSTOPPED ON AN ERROR: ").Append(sim.Error).Append('\n'); break;
                default: b.Append("\nSTOPPED after ").Append(sim.Steps).Append(" instructions (maxSteps).\n"); break;
            }
            if (picks.Count > 0) b.Append("(").Append(picks.Count).Append(" choice(s) not used.)\n");
            foreach (var e in sim.EndReport) b.Append(e).Append('\n');
            if (log)
            {
                b.Append("\nLOG (calls, writes, notes; the last 200):\n");
                foreach (var l in sim.Log.Where(x => x.Kind != SimLogKind.Instruction).Reverse().Take(200).Reverse()) b.Append(l).Append('\n');
            }
            return b.ToString();
        }

        // ---- the graph

        public string ConversationGraph(int slot, bool lines)
        {
            var lib = CtLib();
            var c = CtConv(lib, slot);
            var d = Decompiler.Decompile(c, lib.ShownStringsOf(c).ToArray(), lib.Kind, false, lib.Names.Resolve(c));
            var g = UwEditor.Core.Conversations.ConversationGraph.Build(c, d);
            var b = new StringBuilder();
            var nodes = g.Visible.ToList();
            b.AppendFormat(Ci, "Graph of conversation {0} ({1}): {2} nodes, {3} edges. Nodes (kind, address; entry = a function's start, END = the conversation can end there):\n", slot, CtSlotName(lib, slot), nodes.Count, g.Edges.Count(e => !e.From.Dummy && !e.To.Dummy));
            foreach (var n in nodes)
            {
                b.AppendFormat(Ci, "[{0}] {1} ({2}, 0x{3:X4}{4}{5})\n", n.Id, n.Title, n.Kind, n.Address, n.Entry ? ", entry" : "", n.Ends ? ", END" : "");
                if (lines) foreach (var l in n.Lines) b.Append("      ").Append(new string(' ', 2 * Math.Max(0, l.Indent - 1))).Append(l.Text).Append('\n');
                foreach (var e in n.Out)
                {
                    var to = e.To;
                    while (to.Dummy && to.Out.Count > 0) to = to.Out[0].To;
                    b.AppendFormat(Ci, "    -> [{0}] {1}  {2}{3}{4}\n", to.Id, to.Title, e.Kind, e.Choice != null ? " choice " + Decompiler.Quote(e.Choice) : "", e.Label != null ? " [" + e.Label + "]" : "");
                }
            }
            return b.ToString();
        }
    }
}
