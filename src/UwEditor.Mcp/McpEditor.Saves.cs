using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Saved games (File ▸ Saved games, Export as a saved game): the game folder's SAVE1-SAVE4,
    /// opening one (or DATA), renaming, copying, making a new one, exporting the open levels as one.
    /// The host hooks (HostSv...) hand over what the app holds; without them the server keeps its own.
    /// </summary>
    public sealed partial class McpEditor
    {
        // ---- host hooks (set by the app, src/UwEditor.App/McpWindow.Saves.cs)

        /// <summary>The app's player sheet of the open saved game (one copy of PLAYER.DAT for the whole editor), or null when there is none.</summary>
        public Func<PlayerSheet> HostSvPlayer { get; set; }
        /// <summary>Called after a tool changed or saved the player (the app's player window, markers and unsaved state follow).</summary>
        public Action HostSvPlayerEdited { get; set; }
        /// <summary>The global variables the app's Global variables window holds, or null when it is closed.</summary>
        public Func<GlobalVariables> HostSvGlobals { get; set; }
        /// <summary>The app's conversations (with their unsaved edits), for the conversations' own globals.</summary>
        public Func<ConversationLibrary> HostSvConversations { get; set; }
        /// <summary>Opens a folder (a game folder or SAVEn) in the app: (folder, discard) → what was opened; throws when refused.</summary>
        public Func<string, bool, string> HostSvOpen { get; set; }
        /// <summary>Every kind of unsaved work the app holds (empty: nothing unsaved).</summary>
        public Func<List<string>> HostSvUnsaved { get; set; }
        /// <summary>The app's checkpoint store of the open game.</summary>
        public Func<Checkpoints> HostSvCheckpoints { get; set; }
        /// <summary>The app's checkpoint of all its unsaved work now (label) → the checkpoint, or null when nothing is unsaved.</summary>
        public Func<string, CheckpointInfo> HostSvWriteCheckpoint { get; set; }
        /// <summary>The app restores a checkpoint as unsaved edits → what it restored.</summary>
        public Func<CheckpointInfo, string> HostSvRestoreCheckpoint { get; set; }
        /// <summary>The app's checkpoint settings: (minutes or null, copies or null) → the settings now.</summary>
        public Func<int?, int?, string> HostSvCheckpointSettings { get; set; }
        /// <summary>The app's File ▸ Restore all without its question: every unsaved change dropped (a checkpoint kept first) → what it did.</summary>
        public Func<string> HostSvRestoreAll { get; set; }
        /// <summary>Called after repairs were made as level edits (the views and the run marker follow).</summary>
        public Action HostSvRepaired { get; set; }

        /// <summary>Where the server keeps checkpoints without the app (the app's: the settings folder's checkpoints).</summary>
        public string CheckpointBase { get; set; }

        // ---- the server's own player and globals (without the app)

        PlayerSheet svSheet;
        GameSession svSheetOf;

        /// <summary>The open saved game's player, read once and kept with its unsaved edits.</summary>
        PlayerSheet SvSheet()
        {
            Require();
            var s = Session;
            if (HostSvPlayer != null)
            {
                var h = HostSvPlayer();
                if (h != null) return h;
            }
            if (!s.IsSave) throw new McpEditorException("The player is kept in a saved game's PLAYER.DAT, and DATA is open (its PLAYER.DAT is a template the game never reads). Open a saved game with save_open, or make one with save_create.");
            if (svSheet != null && svSheetOf == s) return svSheet;
            string path = GameProfile.Find(s.SaveFolder, "PLAYER.DAT");
            if (path == null) throw new McpEditorException(Path.GetFileName(s.SaveFolder) + " has no PLAYER.DAT.");
            try { svSheet = PlayerSheet.Load(path, s.Kind, s.DataFolder); }
            catch (InvalidDataException e) { throw new McpEditorException("PLAYER.DAT could not be read: " + e.Message); }
            svSheetOf = s;
            return svSheet;
        }

        /// <summary>The player's sheet if one is open and readable now, else null (no refusal).</summary>
        PlayerSheet SvSheetOrNull()
        {
            try { return Session != null && (Session.IsSave || HostSvPlayer != null) ? SvSheet() : null; }
            catch (McpEditorException) { return null; }
        }

        /// <summary>After a change to the player: the session's player point and the host follow.</summary>
        void SvPlayerEdited(PlayerSheet sheet)
        {
            Session.PlayerMoved(sheet.Dat);
            HostSvPlayerEdited?.Invoke();
        }

        /// <summary>The unsaved work: the app's every kind when attached, else the levels, the player and the globals here.</summary>
        List<string> SvUnsaved()
        {
            if (HostSvUnsaved != null)
            {
                var h = new List<string>(HostSvUnsaved() ?? new List<string>());
                if (svGlobals != null && svGlobalsOf == Session && svGlobals.Dirty && !h.Contains("global variables")) h.Add("global variables (changed by the assistant; global_save writes them)");
                return h;
            }
            var l = new List<string>();
            if (Edits != null && Edits.HasChanges) l.Add("levels " + string.Join(", ", Edits.DirtyLevels));
            if (svSheet != null && svSheetOf == Session && svSheet.Dirty) l.Add("the player (PLAYER.DAT)");
            if (svGlobals != null && svGlobalsOf == Session && svGlobals.Dirty) l.Add("global variables");
            return l;
        }

        // ---- the slots

        static string SvNorm(string p) { return Path.GetFullPath(p).TrimEnd('\\', '/'); }

        bool SvIsOpen(string folder)
        {
            var s = Session;
            return s != null && s.IsSave && string.Equals(SvNorm(s.SaveFolder), SvNorm(folder), StringComparison.OrdinalIgnoreCase);
        }

        List<SaveSlot> SvSlots() { Require(); return SaveSlots.List(Session.GameFolder); }

        SaveSlot SvSlot(int n)
        {
            if (n < 1 || n > SaveSlots.Count) throw new ArgumentOutOfRangeException(null, "Saved game slots are 1-" + SaveSlots.Count + ".");
            return SvSlots().First(x => x.Number == n);
        }

        /// <summary>SAVE1-SAVE4 with their descriptions as the game's menu shows them, and which is open.</summary>
        public string SaveList()
        {
            Require();
            var s = Session;
            var b = new StringBuilder();
            b.Append("Open: ").Append(s.IsSave ? Path.GetFileName(s.SaveFolder) + " (" + s.SaveFolder + ")" : "DATA (what a new game starts with)").Append('\n');
            foreach (var slot in SvSlots())
            {
                b.Append(slot.Name).Append(": ");
                if (!slot.Exists) { b.Append("empty (no folder)\n"); continue; }
                b.Append(slot.Used ? "\"" + slot.Description + "\"" : SaveSlots.NotUsed);
                var parts = new List<string>();
                parts.Add(slot.HasLevels ? "LEV.ARK" : "no LEV.ARK");
                parts.Add(GameProfile.Find(slot.Folder, "PLAYER.DAT") != null ? "PLAYER.DAT" : "no PLAYER.DAT");
                if (GameProfile.Find(slot.Folder, "BGLOBALS.DAT") != null) parts.Add("BGLOBALS.DAT");
                if (slot.Written != DateTime.MinValue) parts.Add("written " + slot.Written.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                if (Directory.Exists(SaveSlots.BackupOf(slot.Folder))) parts.Add("an older copy in " + slot.Name + SaveSlots.BackupSuffix);
                b.Append(" (").Append(string.Join(", ", parts)).Append(')');
                if (SvIsOpen(slot.Folder)) b.Append(" [open]");
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Opens saved game n (1-4) or DATA (0) in place of the open game.</summary>
        public string SaveOpen(int slot, bool discard)
        {
            Require();
            if (slot < 0 || slot > SaveSlots.Count) throw new ArgumentOutOfRangeException(null, "Give 0 for DATA (a new game's levels) or a saved game slot 1-" + SaveSlots.Count + ".");
            string folder;
            if (slot == 0) folder = Session.GameFolder;
            else
            {
                var sl = SvSlot(slot);
                if (!sl.HasLevels) throw new McpEditorException(sl.Name + (sl.Exists ? " has no LEV.ARK." : " is empty."));
                if (GameProfile.Find(sl.Folder, "PLAYER.DAT") == null) throw new McpEditorException(sl.Name + " has no PLAYER.DAT.");
                folder = sl.Folder;
            }
            return SvOpenFolder(folder, discard);
        }

        /// <summary>Opens a folder of the same game (the game folder or a saved game), refusing over unsaved work unless discard.</summary>
        string SvOpenFolder(string folder, bool discard)
        {
            if (HostSvOpen != null) return HostSvOpen(folder, discard);
            if (IsAttached) throw new McpEditorException("The editor did not hand this server a way to open games: open it in the editor (File ▸ Saved games).");
            var unsaved = SvUnsaved();
            if (!discard && unsaved.Count > 0)
                throw new McpEditorException("Unsaved: " + string.Join("; ", unsaved) + ". Save first, or give discard true to drop them.");
            if (only != null)
            {
                string f = SvNorm(folder);
                bool inside = string.Equals(f, only, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(Path.GetDirectoryName(f), only, StringComparison.OrdinalIgnoreCase) && GameSession.IsSaveFolderName(Path.GetFileName(f)));
                if (!inside) throw new McpEditorException("This server opens only " + only + " and its saved games.");
            }
            var s = GameSession.Open(folder);
            session = s;
            edits = new LevelEdits(s);
            svSheet = null; svSheetOf = null;
            svGlobals = null; svGlobalsOf = null;
            return SvOpened(s);
        }

        static string SvOpened(GameSession s)
        {
            var b = new StringBuilder();
            b.Append("Opened ").Append(s.IsSave ? Path.GetFileName(s.SaveFolder) + (string.IsNullOrEmpty(s.SaveDescription) ? "" : " (\"" + s.SaveDescription + "\")") : "DATA").Append(" of ").Append(s.Kind).Append(" at ").Append(s.GameFolder).Append(".\n");
            if (s.PlayerPoint != null) b.Append("The player stands on level ").Append(s.PlayerPoint.Level).Append(", tile (").Append((int)s.PlayerPoint.X).Append(", ").Append((int)s.PlayerPoint.Y).Append(").\n");
            foreach (var p in s.ReadProblems) b.Append("Problem: ").Append(p).Append('\n');
            foreach (var slot in s.LevelSlots())
                b.Append("level ").Append(slot.Level).Append(": ").Append(slot.Damage != null ? "damaged (" + slot.Damage + ")" : slot.Present ? "in use" : "empty").Append('\n');
            return b.ToString();
        }

        /// <summary>Writes slot n's description (DESC, the old one kept as DESC.BKP).</summary>
        public string SaveRename(int slot, string description)
        {
            var s = SvSlot(slot);
            if (!s.Exists) throw new McpEditorException(s.Name + " does not exist: copy or create a saved game there first.");
            var bytes = SaveSlots.DescBytes(description);
            SaveSlots.Rename(s, description);
            return s.Name + " is now \"" + Encoding.ASCII.GetString(bytes) + "\"" + ((description ?? "").Length > SaveSlots.DescMax ? " (cut to the game's 30 characters)." : ".");
        }

        /// <summary>Copies a saved game onto another slot (a replaced one kept as SAVEn.BKP); a slot that exists needs confirm.</summary>
        public string SaveCopy(int from, int to, bool confirm)
        {
            var f = SvSlot(from);
            var t = SvSlot(to);
            if (from == to) throw new ArgumentException("A slot cannot be copied onto itself.");
            if (!f.Exists) throw new McpEditorException(f.Name + " does not exist.");
            if (SvIsOpen(t.Folder)) throw new McpEditorException(t.Name + " is open: open DATA or another slot before replacing it.");
            if (t.Exists && !confirm)
                return "Not copied: " + t.Name + (t.Used ? " (\"" + t.Description + "\")" : "") + " holds a saved game. Give confirm true to replace it (the old one is kept as " + t.Name + SaveSlots.BackupSuffix + ", replacing an older one).";
            string note = SvIsOpen(f.Folder) && SvUnsaved().Count > 0 ? " The copy is of " + f.Name + " as it is on disk: the unsaved work is not in it." : "";
            string bkp = SaveSlots.Copy(f, t);
            return f.Name + " copied to " + t.Name + (bkp != null ? "; the old " + t.Name + " is kept as " + Path.GetFileName(bkp) + "." : ".") + note;
        }

        /// <summary>
        /// A new saved game in slot n: the levels open now (with their unsaved edits), the editor's
        /// default character standing on the new-game start square, BGLOBALS.DAT as a new game has it.
        /// </summary>
        public string SaveCreate(int slot, string description, bool confirm)
        {
            Require();
            var s = Session;
            var t = SvSlot(slot);
            if (SvIsOpen(t.Folder)) throw new McpEditorException(t.Name + " is open: open DATA or another slot before replacing it.");
            if (t.Exists && !confirm)
                return "Not made: " + t.Name + (t.Used ? " (\"" + t.Description + "\")" : "") + " holds a saved game. Give confirm true to replace it (the old one is kept as " + t.Name + SaveSlots.BackupSuffix + ").";
            if (DefaultPlayer.Bytes(s.Kind) == null) throw new McpEditorException("The editor has no default player for this game.");
            var lm = ReadModel(1);
            if (!lm.Present) throw new McpEditorException("Level 1 is empty: a new game starts there.");
            int x = (int)s.NewGameStart.X, y = (int)s.NewGameStart.Y;
            string target = t.Folder.TrimEnd('/', '\\');
            string tmp = target + "." + Environment.ProcessId + ".new";
            DefaultPlayer.WriteSave(s, tmp, Edits.ToBytes(), string.IsNullOrWhiteSpace(description) ? "Editor" : description);
            try
            {
                string pp = Path.Combine(tmp, "PLAYER.DAT");
                var pd = PlayerDat.Parse(File.ReadAllBytes(pp), s.Kind);
                var tile = lm.Tiles[x, y];
                pd.SetPosition(1, x, y, tile.Height, tile.Type >= TileType.SlopeUpN, 0);
                File.WriteAllBytes(pp, pd.ToBytes());
            }
            catch { Directory.Delete(tmp, true); throw; }
            string backup = null;
            if (Directory.Exists(target))
            {
                backup = SaveSlots.BackupOf(target);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(target, backup);
            }
            Directory.Move(tmp, target);
            return string.Format("Made {0}: the levels open now{1}, the editor's default character on level 1 at the new-game start ({2}, {3}).{4} save_open {5} opens it.",
                t.Name, Edits.HasChanges ? " with their unsaved edits" : "", x, y, backup != null ? " The old " + t.Name + " is kept as " + Path.GetFileName(backup) + "." : "", slot);
        }

        /// <summary>
        /// File ▸ Export as a saved game: the open levels (with their unsaved edits) and the player
        /// (as edited) written as a saved game in a folder: a slot of the game (1-4), of Underworld
        /// Exhumed ("exhumed 1-4"), or a path. A folder holding files is replaced only with confirm.
        /// </summary>
        public string SaveExport(string where, string description, bool confirm)
        {
            Require();
            var s = Session;
            if (string.IsNullOrWhiteSpace(where)) throw new ArgumentException("Say where: a slot 1-4, exhumed 1-4, or a folder.");
            string w = where.Trim().Trim('"');
            string folder;
            int n;
            if (int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            {
                if (n < 1 || n > 4) throw new ArgumentOutOfRangeException(null, "Slots are 1-4.");
                folder = Path.Combine(s.GameFolder, "SAVE" + n);
            }
            else if (w.StartsWith("exhumed", StringComparison.OrdinalIgnoreCase))
            {
                string rest = w.Substring(7).Trim();
                if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1 || n > 4) throw new ArgumentException("Underworld Exhumed's slots are exhumed 1-4.");
                folder = Path.Combine(ExhumedCheck.PortHome(s.Kind), "SAVE" + n);
            }
            else
            {
                folder = SvNorm(w);
                if (only != null)
                {
                    string inside = only + Path.DirectorySeparatorChar;
                    if (!folder.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
                        throw new McpEditorException("This server writes only inside " + only + " (or give a slot number, or exhumed 1-4).");
                }
            }
            if (ExhumedCheck.SaveSource(s) == null) throw new McpEditorException("A saved game holds the player as well as the levels, and this game has no saved player to start from: make one with save_create, or save once in the game.");
            if (SvIsOpen(folder)) throw new McpEditorException("That is the saved game open now, and save writes to it. Export to another folder.");
            bool holds = Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
            if (holds && !confirm) return "Not exported: " + folder + " holds a saved game (or files). Give confirm true to replace its files.";
            var sheet = SvSheetOrNull();
            byte[] player = sheet != null && sheet.Dirty ? sheet.ToBytes() : null;
            string desc = string.IsNullOrWhiteSpace(description) ? "Editor " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : description;
            string done = ExhumedCheck.WriteSave(s, Edits, player, folder, desc);
            return "Saved game written to " + done + ", from " + ExhumedCheck.SaveSource(s) + (Edits.HasChanges ? ", with the unsaved level edits" : "") + (player != null ? " and the edited player" : "") + ".";
        }
    }
}
