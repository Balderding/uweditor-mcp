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
using Underworld.Mods;

namespace UwEditor.Mcp
{
    /// <summary>
    /// File ▸ Checkpoints, Check and repair the saved game, Restore all, the .ORIG/.BKP copies the
    /// editor keeps of every file it writes, and Open GOG's game.gog.
    /// </summary>
    public sealed partial class McpEditor
    {
        Checkpoints svStore;
        GameSession svStoreOf;
        int svCopies = Checkpoints.DefaultCopies;

        /// <summary>The checkpoint folder the app uses: the settings folder's (UWEDITOR_SETTINGS_DIR, else %APPDATA%\UwEditorAvalonia).</summary>
        static string SvDefaultCheckpointBase()
        {
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SETTINGS_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UwEditorAvalonia");
            return Path.Combine(dir, "checkpoints");
        }

        Checkpoints SvStore()
        {
            Require();
            if (HostSvCheckpoints != null)
            {
                var c = HostSvCheckpoints();
                if (c == null) throw new McpEditorException("The editor has no checkpoints for the open game.");
                return c;
            }
            if (svStore == null || svStoreOf != Session)
            {
                svStore = new Checkpoints(CheckpointBase ?? SvDefaultCheckpointBase(), Session);
                svStoreOf = Session;
            }
            return svStore;
        }

        CheckpointInfo SvCheckpoint(int number)
        {
            var list = SvStore().List();
            if (list.Count == 0) throw new McpEditorException("There are no checkpoints of this game.");
            if (number < 1 || number > list.Count) throw new ArgumentOutOfRangeException(null, "Checkpoints are 1-" + list.Count + " (1 the newest; checkpoint_list).");
            return list[number - 1];
        }

        /// <summary>The checkpoints of the open game, newest first, numbered from 1.</summary>
        public string CheckpointList()
        {
            var store = SvStore();
            var list = store.List();
            if (list.Count == 0) return "There are no checkpoints of this game (in " + store.Root + ").";
            var b = new StringBuilder();
            for (int i = 0; i < list.Count; i++) b.Append(i + 1).Append(". ").Append(list[i].Summary).Append('\n');
            b.Append("Kept in ").Append(store.Root).Append('\n');
            return b.ToString();
        }

        /// <summary>The unsaved work a checkpoint made here keeps besides the levels: the player and the edited automaps.</summary>
        Dictionary<string, byte[]> SvCheckpointFiles()
        {
            var files = new Dictionary<string, byte[]>();
            if (svSheet != null && svSheetOf == Session && svSheet.Dirty) files["PLAYER.DAT"] = svSheet.ToBytes();
            foreach (var a in Edits.Automaps.Where(a => a.Dirty))
            {
                files[string.Format("automap-{0:D2}.bin", a.Level)] = (byte[])a.Map.Clone();
                files[string.Format("notes-{0:D2}.bin", a.Level)] = Automap.WriteNotes(a.Notes);
            }
            return files;
        }

        /// <summary>A checkpoint of the unsaved work now (the levels, the player, the automaps; in the editor every kind it keeps).</summary>
        public string CheckpointMake(string label)
        {
            var store = SvStore();
            string l = string.IsNullOrWhiteSpace(label) ? "by the assistant" : label.Trim();
            CheckpointInfo c = HostSvWriteCheckpoint != null ? HostSvWriteCheckpoint(l) : store.Write(Edits, SvCheckpointFiles(), l, svCopies);
            return c == null ? "Nothing unsaved, so no checkpoint was written." : "Checkpoint kept: " + c.Summary + ".";
        }

        /// <summary>Loads a checkpoint back as unsaved edits (each level an undo step). Replacing the unsaved player needs confirm.</summary>
        public string CheckpointRestore(int number, bool confirm)
        {
            var c = SvCheckpoint(number);
            if (HostSvRestoreCheckpoint != null)
            {
                if (!confirm && c.Files.Contains("PLAYER.DAT") && SvUnsaved().Any(u => u.Contains("PLAYER.DAT")))
                    return "Not restored: the unsaved player edits would be replaced by the checkpoint's, with no undo. Give confirm true to restore.";
                return HostSvRestoreCheckpoint(c);
            }
            var store = SvStore();
            if (!confirm && c.Files.Contains("PLAYER.DAT") && svSheet != null && svSheetOf == Session && svSheet.Dirty)
                return "Not restored: the unsaved player edits would be replaced by the checkpoint's, with no undo. Give confirm true to restore.";
            var said = new List<string>();
            var levels = store.RestoreLevels(c, Edits);
            if (levels.Count > 0) said.Add("level" + (levels.Count == 1 ? " " : "s ") + string.Join(", ", levels));
            var pd = store.ReadFile(c, "PLAYER.DAT");
            if (pd != null && Session.IsSave)
            {
                string path = GameProfile.Find(Session.SaveFolder, "PLAYER.DAT");
                svSheet = PlayerSheet.FromBytes(pd, Session.Kind, Session.DataFolder, path);
                svSheetOf = Session;
                SvPlayerEdited(svSheet);
                said.Add("the player");
            }
            int maps = 0;
            foreach (var f in c.Files.Where(f => f.StartsWith("automap-", StringComparison.Ordinal)))
            {
                int lv;
                if (!int.TryParse(f.Substring(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out lv)) continue;
                var map = store.ReadFile(c, f);
                var notes = store.ReadFile(c, string.Format("notes-{0:D2}.bin", lv));
                var a = Edits.AutomapFor(lv);
                if (map == null || (map.SequenceEqual(a.Map) && (notes ?? new byte[0]).SequenceEqual(Automap.WriteNotes(a.Notes)))) continue;
                a.Replace(map, notes == null ? a.Notes : Automap.ReadNotes(notes), "Checkpoint");
                Edits.AutomapEdited(a);
                maps++;
            }
            if (maps > 0) said.Add(maps + " automap(s)");
            var skipped = c.Files.Where(f => f == "STRINGS.PAK" || f == "CNV.ARK" || f.EndsWith(".txt", StringComparison.Ordinal)).ToList();
            string skip = skipped.Count == 0 ? "" : " Not restored here (the editor's window restores them): " + string.Join(", ", skipped) + ".";
            return (said.Count == 0 ? "The checkpoint matches what is already here." : "Restored " + string.Join(", ", said) + " from " + c.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " as unsaved edits (undo takes a level back).") + skip;
        }

        /// <summary>Deletes one checkpoint (the editor's own copy; the game's files are not touched). Needs confirm.</summary>
        public string CheckpointDelete(int number, bool confirm)
        {
            var c = SvCheckpoint(number);
            if (!confirm) return "Not deleted: give confirm true to delete checkpoint " + number + " (" + c.Summary + ").";
            SvStore().Delete(c);
            return "Deleted checkpoint " + c.Summary + ".";
        }

        /// <summary>How often the editor keeps a checkpoint and how many it keeps; either changed when given.</summary>
        public string CheckpointSettings(int? minutes, int? copies)
        {
            Require();
            if (minutes.HasValue && (minutes < 0 || minutes > 240)) throw new ArgumentOutOfRangeException(null, "Minutes are 0-240 (0: none kept by time).");
            if (copies.HasValue && (copies < 1 || copies > Checkpoints.MaxCopies)) throw new ArgumentOutOfRangeException(null, "Copies are 1-" + Checkpoints.MaxCopies + ".");
            if (HostSvCheckpointSettings != null) return HostSvCheckpointSettings(minutes, copies);
            if (minutes.HasValue) throw new McpEditorException("This server keeps checkpoints only when asked (checkpoint_make): the timer is the editor window's.");
            if (copies.HasValue) { svCopies = copies.Value; int gone = SvStore().Prune(svCopies); return "Keeping the newest " + svCopies + " checkpoints" + (gone > 0 ? "; " + gone + " older one(s) deleted." : "."); }
            return "Checkpoints only when asked (checkpoint_make); the newest " + svCopies + " are kept.";
        }

        // ---- check and repair the saved game

        /// <summary>What makes the original game crash or complain with the open levels (notes, automaps, object lists), numbered.</summary>
        public string SaveCheckRun()
        {
            Require();
            var found = SaveCheck.Find(Session, Edits);
            if (found.Count == 0) return "Nothing wrong found.";
            var b = new StringBuilder();
            b.Append(found.Count(f => f.Problem)).Append(" problem(s), ").Append(found.Count(f => !f.Problem)).Append(" warning(s):\n");
            for (int i = 0; i < found.Count; i++)
            {
                var f = found[i];
                b.Append(i + 1).Append(". ").Append(f.Problem ? "Problem" : "Warning").Append(", level ").Append(f.Level).Append(' ').Append(f.Kind).Append(": ").Append(f.Text).Append('\n');
                b.Append("   repair: ").Append(f.CanFix ? f.FixText : "none automatic").Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Repairs findings (numbers as save_check lists them; none: all) as unsaved level edits.</summary>
        public string SaveRepair(int[] numbers)
        {
            Require();
            var found = SaveCheck.Find(Session, Edits);
            if (found.Count == 0) return "Nothing wrong found, so nothing to repair.";
            var which = new List<SaveFinding>();
            if (numbers == null || numbers.Length == 0) which.AddRange(found);
            else
                foreach (int n in numbers)
                {
                    if (n < 1 || n > found.Count) throw new ArgumentOutOfRangeException(null, "Findings are 1-" + found.Count + " (save_check).");
                    which.Add(found[n - 1]);
                }
            int done = 0;
            foreach (var f in which.Distinct()) if (SaveCheck.Repair(f, Edits)) done++;
            HostSvRepaired?.Invoke();
            int left = SaveCheck.Find(Session, Edits).Count;
            return (done == 0 ? "Nothing repaired." : "Repaired " + done + " as unsaved edits: save writes them.") + (left > 0 ? " " + left + " finding(s) left (save_check)." : " Nothing wrong is left.");
        }

        // ---- Restore all: drop the unsaved work

        /// <summary>File ▸ Restore all: drops every unsaved change and reads the game again (a checkpoint kept first). Needs confirm.</summary>
        public string RestoreAll(bool confirm)
        {
            Require();
            var parts = SvUnsaved();
            if (parts.Count == 0) return "Nothing unsaved: the files and the editor agree.";
            if (!confirm) return "Not restored. Unsaved: " + string.Join("; ", parts) + ". Give confirm true to drop it all (a checkpoint keeps it first; checkpoint_restore brings it back).";
            if (HostSvRestoreAll != null) return HostSvRestoreAll();
            var c = SvStore().Write(Edits, SvCheckpointFiles(), "before Restore all", svCopies);
            string folder = Session.SaveFolder ?? Session.GameFolder;
            SvReopen(folder);
            return "Restored: every unsaved change was dropped and the files read again" + (c != null ? " (checkpoint " + c.Summary + " keeps them)." : ".");
        }

        /// <summary>Reads the open game again from its files (nothing unsaved is asked about: the caller has).</summary>
        void SvReopen(string folder)
        {
            if (HostSvOpen != null) { HostSvOpen(folder, true); return; }
            var s = GameSession.Open(folder);
            session = s;
            edits = new LevelEdits(s);
            svSheet = null; svSheetOf = null;
            svGlobals = null; svGlobalsOf = null;
        }

        // ---- the .ORIG and .BKP copies

        sealed class SvBackup
        {
            public string File, Rel;
            public bool Orig, Bkp, OrigEmpty, SameAsOrig;
        }

        /// <summary>Every file of the game folder with a copy the editor kept: NAME.EXT.ORIG (as first found) and NAME.EXT.BKP (the version before the last save).</summary>
        List<SvBackup> SvBackups()
        {
            Require();
            string root = Session.GameFolder;
            var map = new Dictionary<string, SvBackup>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string up = f.ToUpperInvariant();
                bool orig = up.EndsWith(".ORIG"), bkp = up.EndsWith(".BKP");
                if (!orig && !bkp) continue;
                string file = f.Substring(0, f.Length - (orig ? 5 : 4));
                SvBackup b;
                if (!map.TryGetValue(file, out b)) map[file] = b = new SvBackup { File = file, Rel = Path.GetRelativePath(root, file) };
                if (orig) { b.Orig = true; b.OrigEmpty = new FileInfo(f).Length == 0; }
                else b.Bkp = true;
            }
            foreach (var b in map.Values)
                if (b.Orig && File.Exists(b.File) && !b.OrigEmpty)
                    b.SameAsOrig = new FileInfo(b.File).Length == new FileInfo(b.File + ".ORIG").Length && File.ReadAllBytes(b.File).AsSpan().SequenceEqual(File.ReadAllBytes(b.File + ".ORIG"));
            return map.Values.OrderBy(b => b.Rel, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The files a restore could put back: each with its .ORIG and .BKP copies.</summary>
        public string BackupList()
        {
            var list = SvBackups();
            if (list.Count == 0) return "No file of " + Session.GameFolder + " has an .ORIG or .BKP copy: the editor has written nothing here.";
            var b = new StringBuilder();
            b.Append("In ").Append(Session.GameFolder).Append(" (.ORIG: the file before the editor first wrote it; .BKP: the version before the last save):\n");
            foreach (var x in list)
            {
                b.Append(x.Rel).Append(": ");
                var parts = new List<string>();
                if (x.Orig) parts.Add(x.OrigEmpty ? ".ORIG empty (the editor added this file)" : x.SameAsOrig ? ".ORIG (same as now)" : ".ORIG (differs from now)");
                if (x.Bkp) parts.Add(".BKP");
                if (!File.Exists(x.File)) parts.Add("the file itself is missing");
                b.Append(string.Join(", ", parts)).Append('\n');
            }
            return b.ToString();
        }

        string SvBackupPath(SvBackup x, string from)
        {
            string k = SvKey(from);
            if (k == "orig" || k == "original") { if (!x.Orig) throw new McpEditorException(x.Rel + " has no .ORIG copy."); return x.File + ".ORIG"; }
            if (k == "bkp" || k == "backup") { if (!x.Bkp) throw new McpEditorException(x.Rel + " has no .BKP copy."); return x.File + ".BKP"; }
            throw new ArgumentException("from is orig (the file as first found) or bkp (the version before the last save).");
        }

        /// <summary>
        /// Puts a file back from its .ORIG or .BKP copy (the file now kept as .BKP), then reads the
        /// game again. Refused over unsaved work; without confirm, says what it would do.
        /// </summary>
        public string BackupRestore(string file, string from, bool confirm)
        {
            var list = SvBackups();
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Give the file, as backup_list names it (DATA\\LEV.ARK, say).");
            string want = file.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            var x = list.FirstOrDefault(b => string.Equals(b.Rel, want, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(b => string.Equals(Path.GetFileName(b.Rel), want, StringComparison.OrdinalIgnoreCase) && list.Count(c => string.Equals(Path.GetFileName(c.Rel), want, StringComparison.OrdinalIgnoreCase)) == 1);
            if (x == null) throw new McpEditorException("No copy of " + file + " (backup_list lists the files that have one).");
            string src = SvBackupPath(x, from ?? "bkp");
            if (src.EndsWith(".ORIG", StringComparison.OrdinalIgnoreCase) && x.OrigEmpty)
                throw new McpEditorException(x.Rel + " was added by the editor (its .ORIG is empty): there is no earlier version to put back.");
            string what = x.Rel + " from " + Path.GetFileName(src);
            if (!confirm) return "Not restored: this would overwrite " + x.Rel + " with " + Path.GetFileName(src) + " (the file now kept as " + Path.GetFileName(x.File) + ".BKP) and read the game again. Give confirm true to do it.";
            SvRefuseUnsaved("restoring a file");
            SafeFile.Write(x.File, File.ReadAllBytes(src));
            SvReopen(Session.SaveFolder ?? Session.GameFolder);
            return "Restored " + what + " (the file it replaced is kept as " + Path.GetFileName(x.File) + ".BKP); the game was read again.";
        }

        /// <summary>Every file the editor changed back to its .ORIG copy (the game as first found), then the game read again. Needs confirm.</summary>
        public string BackupRestoreAll(bool confirm)
        {
            var list = SvBackups().Where(b => b.Orig && !b.OrigEmpty && !b.SameAsOrig).ToList();
            if (list.Count == 0) return "Every file is as first found (or has no .ORIG copy): nothing to restore.";
            if (!confirm) return "Not restored: this would put back " + list.Count + " file(s) from their .ORIG copies: " + string.Join(", ", list.Select(b => b.Rel)) + " (each file now kept as .BKP). Files the editor added are left. Give confirm true to do it.";
            SvRefuseUnsaved("restoring files");
            foreach (var x in list) SafeFile.Write(x.File, File.ReadAllBytes(x.File + ".ORIG"));
            SvReopen(Session.SaveFolder ?? Session.GameFolder);
            return "Restored " + list.Count + " file(s) from .ORIG: " + string.Join(", ", list.Select(b => b.Rel)) + " (each replaced file kept as .BKP); the game was read again.";
        }

        void SvRefuseUnsaved(string doing)
        {
            var parts = SvUnsaved();
            if (parts.Count > 0) throw new McpEditorException("Unsaved: " + string.Join("; ", parts) + ". Save or drop them (restore_all) before " + doing + ".");
        }

        // ---- GOG's game.gog

        /// <summary>What a GOG image holds: the games, its hash and whether the editor knows that release.</summary>
        public string GogInfo(string image)
        {
            string path = SvGogPath(image);
            string sha = GogImage.Sha256(path);
            var games = GogImage.Games(path);
            string release = GogImage.Release(sha);
            return path + ": " + (games.Count == 0 ? "neither UW\\UW.EXE nor UW2\\UW2.EXE: not GOG's image of the games" : string.Join(" and ", games.Select(g => g == GameKind.UW2 ? "Ultima Underworld II (UW2)" : "Ultima Underworld (UW)")))
                + ". SHA-256 " + sha + (release != null ? " (" + release + ")." : ", a release the editor was not checked with.");
        }

        static string SvGogPath(string image)
        {
            if (string.IsNullOrWhiteSpace(image)) throw new ArgumentException("Give the path of game.gog.");
            string p = Path.GetFullPath(image.Trim().Trim('"'));
            if (Directory.Exists(p)) p = GogImage.Find(p) ?? throw new McpEditorException("No game.gog in " + p + ".");
            if (!File.Exists(p)) throw new McpEditorException(p + " does not exist.");
            return p;
        }

        /// <summary>
        /// Open GOG's game.gog: copies both games out of the image into an empty folder outside the
        /// GOG install (UW and UW2 in it), leaving the install as it is; open picks the game to open then.
        /// </summary>
        public string GogExtract(string image, string folder, bool unknownRelease, string open)
        {
            string path = SvGogPath(image);
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Give the folder to copy the games into (they go into UW and UW2 in it).");
            string dest = SvNorm(folder.Trim().Trim('"'));
            string install = Path.GetDirectoryName(path);
            if ((dest + Path.DirectorySeparatorChar).StartsWith(SvNorm(install) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new McpEditorException("Choose a folder outside " + install + ": the GOG install is left exactly as it is.");
            var games = GogImage.Games(path);
            if (games.Count == 0) throw new McpEditorException(path + " holds neither UW\\UW.EXE nor UW2\\UW2.EXE.");
            string sha = GogImage.Sha256(path);
            if (GogImage.Release(sha) == null && !unknownRelease)
                return "Not copied: " + Path.GetFileName(path) + " is not the GOG release the editor was checked with (SHA-256 " + sha + "). Give unknownRelease true to copy the games out anyway.";
            var taken = games.Select(g => Path.Combine(dest, GogImage.FolderOf(g))).Where(d => Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any()).ToList();
            if (taken.Count > 0) throw new McpEditorException(string.Join(", ", taken) + " is not empty: choose an empty folder, so no game is overwritten.");
            GameKind? which = null;
            if (!string.IsNullOrWhiteSpace(open))
            {
                string k = SvKey(open);
                if (k == "uw" || k == "uw1" || k == "1") which = GameKind.UW1;
                else if (k == "uw2" || k == "2") which = GameKind.UW2;
                else if (k != "none" && k != "no") throw new ArgumentException("open is uw1, uw2 or none.");
                if (which.HasValue && !games.Contains(which.Value)) throw new McpEditorException("The image has no " + open + ".");
            }
            bool existed = Directory.Exists(dest);
            var made = new List<string>();
            try
            {
                foreach (var g in games)
                {
                    string to = Path.Combine(dest, GogImage.FolderOf(g));
                    GogImage.Extract(path, g, to, install);
                    made.Add(to);
                }
                GogImage.WriteInstallNote(dest, install, games.ToDictionary(g => g, g => GogImage.SetupInstall(install, g)));
            }
            catch
            {
                foreach (var d in made) try { Directory.Delete(d, true); } catch (Exception) { }
                if (!existed) try { Directory.Delete(dest, true); } catch (Exception) { }
                throw;
            }
            string said = "Copied " + string.Join(" and ", games.Select(g => Path.Combine(dest, GogImage.FolderOf(g)))) + " out of " + path + " (the GOG install is unchanged; GOG's launcher always plays game.gog, so play a changed copy through a mod).";
            var bare = games.Where(g => GogImage.SetupInstall(install, g) == null).ToList();
            if (bare.Count > 0) said += " No GOG sound setup (" + string.Join(", ", bare.Select(GogImage.HardDiskFolderOf)) + ") was found beside the install, so " + string.Join(" and ", bare.Select(GogImage.FolderOf)) + " keep the disc's UW.CFG without sound.";
            if (which.HasValue)
            {
                string game = Path.Combine(dest, GogImage.FolderOf(which.Value));
                if (HostSvOpen != null) said += " " + HostSvOpen(game, false);
                else if (only != null) said += " Not opened: this server opens only " + only + ".";
                else
                {
                    var unsaved = SvUnsaved();
                    if (unsaved.Count > 0) said += " Not opened: unsaved " + string.Join("; ", unsaved) + " (open it with open, discard true).";
                    else said += " " + Open(game);
                }
            }
            return said;
        }
    }
}
