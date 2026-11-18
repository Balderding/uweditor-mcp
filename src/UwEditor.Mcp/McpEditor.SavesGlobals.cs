using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Conversations;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Tools ▸ Global variables: quest flags and quest bytes, game variables, UW2's X clocks, the
    /// skills, the talker's and player's variables and each conversation's own (BGLOBALS.DAT),
    /// with their values in the open saved game and which conversations read and set them.
    /// </summary>
    public sealed partial class McpEditor
    {
        GlobalVariables svGlobals;
        GameSession svGlobalsOf;
        object svGlobalsSheet;

        /// <summary>The globals: the app's window's when it is open, else read here once (with the shared player sheet).</summary>
        GlobalVariables SvGlobals()
        {
            Require();
            var host = HostSvGlobals?.Invoke();
            if (host != null) return host;
            var sheet = SvSheetOrNull();
            if (svGlobals != null && svGlobalsOf == Session && ReferenceEquals(svGlobalsSheet, sheet)) return svGlobals;
            if (svGlobals != null && svGlobalsOf == Session && svGlobals.Dirty && svGlobals.Private != null)
                throw new McpEditorException("The player was read again while conversation globals were unsaved: global_save them first.");
            var s = Session;
            ConversationLibrary lib = HostSvConversations?.Invoke();
            if (lib == null)
            {
                try { lib = ConversationLibrary.Open(s); } catch (FileNotFoundException) { lib = null; }
            }
            var uses = lib == null ? null : GlobalUsage.Collect(lib.Archive, null, s.Kind);
            svGlobals = GlobalVariables.Load(s, lib?.Archive, uses, lib == null ? (Func<int, string>)null : lib.Name, sheet);
            svGlobalsOf = s;
            svGlobalsSheet = sheet;
            return svGlobals;
        }

        static GlobalKind SvKind(string kind)
        {
            switch (SvKey(kind))
            {
                case "quest": case "quests": case "questflag": case "questbyte": return GlobalKind.Quest;
                case "var": case "variable": case "gamevariable": case "gamevar": case "trap": return GlobalKind.GameVariable;
                case "xclock": case "clock": return GlobalKind.XClock;
                case "skill": case "skills": return GlobalKind.Skill;
                case "private": case "conversation": case "bglobals": return GlobalKind.Private;
                case "imported": case "talker": case "player": case "talkerplayer": return GlobalKind.Imported;
            }
            throw new ArgumentException("No kind " + kind + ": quest, variable, xclock (UW2), skill, private (a conversation's own; give its slot), imported.");
        }

        static string SvGlobalLine(GlobalVariable v)
        {
            var b = new StringBuilder();
            b.Append(v.KindName).Append(' ');
            if (v.Kind == GlobalKind.Private) b.Append("slot ").Append(v.Slot).Append(" #");
            b.Append(v.Kind == GlobalKind.Imported ? v.Name : v.Label);
            b.Append(" = ").Append(v.Value.HasValue ? v.Value.Value.ToString(CultureInfo.InvariantCulture) : "-");
            if (v.Kind != GlobalKind.Imported) b.Append(" (").Append(v.Min).Append('-').Append(v.Max).Append(v.Editable ? "" : ", read only").Append(')');
            if (v.Uses.Count > 0) b.Append(" used by ").Append(v.Uses.Select(u => u.Slot).Distinct().Count()).Append(" conversation(s)");
            return b.ToString();
        }

        /// <summary>The globals of a kind (or all), filtered by a part of the name or a conversation slot; at most MaxListed lines.</summary>
        public string GlobalList(string kind, string filter, int? slot, bool onlyUsed, bool onlySet)
        {
            var g = SvGlobals();
            IEnumerable<GlobalVariable> all = g.All;
            if (!string.IsNullOrWhiteSpace(kind)) { var k = SvKind(kind); all = all.Where(v => v.Kind == k); }
            if (slot.HasValue) all = all.Where(v => v.Slot == slot.Value || v.Uses.Any(u => u.Slot == slot.Value));
            if (!string.IsNullOrWhiteSpace(filter)) all = all.Where(v => (v.Name ?? "").IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || (v.Description ?? "").IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            if (onlyUsed) all = all.Where(v => v.Uses.Count > 0);
            if (onlySet) all = all.Where(v => v.Value.HasValue && v.Value.Value != 0);
            var list = all.ToList();
            var b = new StringBuilder();
            var s = Session;
            b.Append(!s.IsSave ? "DATA is open: a new game starts with these at 0 (read only). Open a saved game to see and change its values.\n"
                : g.Player == null ? "The saved game has no readable PLAYER.DAT.\n" : "");
            foreach (var v in list.Take(MaxListed)) b.Append(SvGlobalLine(v)).Append('\n');
            if (list.Count > MaxListed) b.Append("... and ").Append(list.Count - MaxListed).Append(" more: narrow it by kind, filter or slot.\n");
            if (list.Count == 0) b.Append("No global matches.\n");
            return b.ToString();
        }

        GlobalVariable SvGlobal(GlobalVariables g, string kind, int number, int? slot, string name)
        {
            var k = SvKind(kind);
            GlobalVariable v;
            if (k == GlobalKind.Imported)
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the imported variable's name.");
                v = g.All.FirstOrDefault(x => x.Kind == k && string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (k == GlobalKind.Private && !slot.HasValue) throw new ArgumentException("A conversation's own global needs its slot.");
                v = g.Find(k, number, slot ?? -1);
            }
            if (v == null) throw new McpEditorException("There is no " + GlobalVariables.KindName(k) + " " + (k == GlobalKind.Imported ? name : number.ToString(CultureInfo.InvariantCulture)) + (k == GlobalKind.Private ? " in conversation " + slot : "") + " in this game (global_list lists them).");
            return v;
        }

        /// <summary>One global: what it is, where it is kept, its value and the conversations reading and setting it.</summary>
        public string GlobalGet(string kind, int number, int? slot, string name)
        {
            var g = SvGlobals();
            var v = SvGlobal(g, kind, number, slot, name);
            var b = new StringBuilder();
            b.Append(SvGlobalLine(v)).Append('\n').Append(v.Description).Append('\n');
            foreach (var u in v.Uses.OrderBy(u => u.Slot).ThenBy(u => u.Address).Take(60))
            {
                string what = u.Access == GlobalAccess.Read ? "reads" : u.Access == GlobalAccess.Write ? "sets" : "reads and sets";
                string val = u.Access != GlobalAccess.Read ? (u.Value.HasValue ? " to " + u.Value : " to a computed value") : "";
                b.Append("conversation ").Append(u.Slot).Append(": ").Append(what).Append(val).Append(" (").Append(u.Via).Append(", at ").Append(u.Address.ToString("X4")).Append(")\n");
            }
            if (v.Uses.Count > 60) b.Append("... and ").Append(v.Uses.Count - 60).Append(" more uses\n");
            if (v.Uses.Count == 0 && v.Kind != GlobalKind.Imported) b.Append("No conversation uses it by a constant number (traps and the game's own code may).\n");
            return b.ToString();
        }

        /// <summary>Changes a global in the open saved game (kept until global_save or player_save).</summary>
        public string GlobalSet(string kind, int number, int value, int? slot, string name)
        {
            var g = SvGlobals();
            var v = SvGlobal(g, kind, number, slot, name);
            int? before = v.Value;
            string err = g.Set(v, value);
            if (err != null) throw new McpEditorException(err);
            if (v.Kind != GlobalKind.Private)
            {
                var sheet = SvSheetOrNull();
                if (sheet != null) { sheet.Dirty = true; SvPlayerEdited(sheet); }
            }
            return SvGlobalLine(v).Split(" (")[0] + " (was " + (before.HasValue ? before.Value.ToString(CultureInfo.InvariantCulture) : "-") + "; not saved: global_save writes it" + (v.Kind == GlobalKind.Private ? " to BGLOBALS.DAT" : " to PLAYER.DAT") + ").";
        }

        /// <summary>Writes the changed globals: PLAYER.DAT and BGLOBALS.DAT (each old file kept as .BKP).</summary>
        public string GlobalSave()
        {
            var g = SvGlobals();
            if (!g.CanEdit) throw new McpEditorException("DATA holds a new game's values: open a saved game to change them.");
            var sheet = SvSheetOrNull();
            var done = g.Save();
            if (sheet != null && sheet.Dirty) { sheet.Save(); done.Add(sheet.Path); }
            if (sheet != null) SvPlayerEdited(sheet);
            done = done.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return done.Count == 0 ? "Nothing to save." : "Saved " + string.Join(" and ", done.Select(Path.GetFileName)) + " (the old files kept as .BKP).";
        }
    }
}
