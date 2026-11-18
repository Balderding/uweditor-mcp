using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Underworld.Mods;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Mods;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Mods (Tools ▸ Mods, format 2, Underworld.Mods): the saved changes of the game folder (against
    /// the .ORIG copies the editor kept, or a folder of originals) packed into a .uwmod or a .zip,
    /// a .uwmod checked against the game, and applied to it (then the game is opened again).
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>Unsaved work of any kind here (levels, tables, content), by name.</summary>
        List<string> CtUnsaved()
        {
            var l = new List<string>();
            if (Edits != null && Edits.HasChanges) l.Add("levels " + string.Join(", ", Edits.DirtyLevels));
            if (TblChanged().Count > 0) l.Add("data tables or strings");
            l.AddRange(CtChangedParts());
            return l;
        }

        FolderChanges CtCollect(string reference)
        {
            CtFresh();
            string r = string.IsNullOrWhiteSpace(reference) ? null : Path.GetFullPath(reference.Trim().Trim('"'));
            if (r != null && !Directory.Exists(r)) throw new McpEditorException("There is no folder " + r + " (an untouched copy of the game).");
            return EditorMods.Collect(Session.GameFolder, Session.Kind, r);
        }

        string CtUnsavedNote()
        {
            var u = CtUnsaved();
            return u.Count == 0 ? "" : " Unsaved here: " + string.Join("; ", u) + " (only saved files are compared).";
        }

        public string ModChanges(string reference)
        {
            var c = CtCollect(reference);
            var b = new StringBuilder();
            b.Append(Session.GameFolder).Append(string.IsNullOrWhiteSpace(reference) ? " against the .ORIG copies the editor kept" : " against " + reference).Append(":\n");
            if (c.Changed.Count == 0) b.Append("No changed files.\n");
            foreach (var f in c.Changed.OrderBy(x => x.Path, StringComparer.Ordinal))
                b.Append("  ").Append(f.Path).Append(f.Original == null ? "  (new, " + f.Edited.Length + " bytes)" : "  (" + f.Original.Length + " -> " + f.Edited.Length + " bytes)").Append('\n');
            if (c.Problems.Count > 0) b.Append("Problems that stop them being a mod:\n").Append(string.Join("\n", c.Problems.Select(p => "  " + p))).Append('\n');
            b.Append(CtUnsavedNote());
            return b.ToString();
        }

        public string ModMake(string path, string id, string name, string version, string description, string authors, string reference)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No .uwmod path given.");
            string p = Path.GetFullPath(path.Trim().Trim('"'));
            if (!p.EndsWith(ModPackage.Extension, StringComparison.OrdinalIgnoreCase)) p += ModPackage.Extension;
            var c = CtCollect(reference);
            if (c.Problems.Count > 0) throw new McpEditorException("These problems stop the changes from becoming a mod (a mod may only change the game's own files and add sound files the game opens by number): " + string.Join("; ", c.Problems));
            if (c.Changed.Count == 0) throw new McpEditorException("No changes found: save your edits first (only saved files are compared)." + CtUnsavedNote());
            var auth = (authors ?? "").Split(',').Select(a => a.Trim()).Where(a => a.Length > 0);
            ModPackage m;
            try { m = ModMaker.Make(c, (id ?? "my.mod").Trim(), (name ?? "My mod").Trim(), (version ?? "1.0.0").Trim(), description ?? "", auth, EditorMods.MadeWith); }
            catch (ModException e) { throw new McpEditorException(e.Message); }
            var checks = ModChecks.Run(Session, CtLevels());
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            m.Write(p);
            return "Written: " + p + "\n" + m.Summary + string.Format(Ci, "{0:N0} bytes of changes in all, nothing of the original files.\n", m.Files.Sum(f => (long)f.Delta.Length)) + checks.Report() + CtUnsavedNote();
        }

        public string ModZip(string path, string reference)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No .zip path given.");
            string p = Path.GetFullPath(path.Trim().Trim('"'));
            if (!p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) p += ".zip";
            var c = CtCollect(reference);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            var files = EditorMods.WriteZip(c, p, Session.IsUw2 ? "Ultima Underworld II" : "Ultima Underworld");
            return "Written: " + p + " with " + string.Join(", ", files) + ". " + EditorMods.ZipWarning + CtUnsavedNote();
        }

        ModPackage CtLoadMod(string path)
        {
            byte[] b = CtInput(path, ".uwmod");
            try { return ModPackage.FromBytes(b); }
            catch (ModException e) { throw new McpEditorException("Not a mod this editor reads: " + e.Message); }
        }

        public string ModCheck(string path)
        {
            CtFresh();
            var m = CtLoadMod(path);
            var b = new StringBuilder(m.Summary);
            b.Append("Against ").Append(Session.GameFolder).Append(":\n");
            if (m.Game != Session.Kind) b.Append("  It is for ").Append(m.GameName).Append(", not the game open here.\n");
            var checks = EditorMods.Check(m, Session.GameFolder);
            foreach (var c in checks)
                b.Append("  ").Append(c.File.Path).Append(": ").Append(c.State == FileState.Original ? "the original: the mod applies" : c.State == FileState.Applied ? "already applied" : c.State == FileState.Missing ? "missing" : c.File.Added ? "a file of that name is already there" : "not the file the mod was made from").Append('\n');
            bool fits = m.Game == Session.Kind && checks.All(c => c.State == FileState.Original || c.State == FileState.Applied);
            b.Append(fits ? (checks.All(c => c.State == FileState.Applied) ? "Already applied." : "It applies (mod_apply).") : "It does not apply to this game.");
            return b.ToString();
        }

        public string ModApply(string path)
        {
            CtFresh();
            var m = CtLoadMod(path);
            var unsaved = CtUnsaved();
            if (IsAttached && HostUnsaved != null && HostUnsaved()) unsaved.Add("the editor's own unsaved work");
            if (unsaved.Count > 0) throw new McpEditorException("Applying a mod opens the game again, and these unsaved changes would be lost: " + string.Join("; ", unsaved) + ". Save (or undo/revert) them first.");
            if (IsAttached && HostReopen == null) throw new McpEditorException("The editor cannot open the game again from here: apply the mod in Tools ▸ Mods.");
            List<string> written;
            try { written = EditorMods.Apply(m, Session.GameFolder, Session.Kind); }
            catch (ModException e) { throw new McpEditorException(e.Message); }
            if (written.Count == 0) return "Nothing to write: " + m.Name + " is already applied.";
            if (IsAttached) HostReopen();
            else
            {
                var old = session;
                var s = old.IsSave ? GameSession.Open(old.SaveFolder, old.GameFolder) : GameSession.Open(old.GameFolder);
                session = s;
                edits = new LevelEdits(s);
            }
            return "Applied " + m.Name + " " + m.Version + ": " + string.Join(", ", written) + " (each original kept as .ORIG). The game was opened again.";
        }
    }
}
