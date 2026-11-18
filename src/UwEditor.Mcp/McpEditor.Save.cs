using System.Linq;

namespace UwEditor.Mcp
{
    /// <summary>check and save.</summary>
    public sealed partial class McpEditor
    {
        /// <summary>The game's own checks on a level as it is now (its object lists).</summary>
        public string Check(int level)
        {
            var m = ReadModel(level);
            if (!m.Present) return "Level " + level + " is an empty slot.";
            return m.CheckOk ? "Level " + level + " passes the game's checks." : "Level " + level + " fails the game's checks: " + m.CheckReport;
        }

        /// <summary>
        /// Writes the changed levels (the editor's safe write: the old LEV.ARK kept as LEV.ARK.BKP).
        /// The editor refuses, writing nothing, when a changed level now fails the game's checks.
        /// </summary>
        public string Save()
        {
            Require();
            CheckTablesOnDisk();   // the data tables (McpEditor.Tables.cs): refused before anything is written
            CheckContentOnDisk();  // conversations, pictures, sound... (McpEditor.Content.cs): the same
            if (hostSave != null)
            {
                // the editor's own save (Ctrl+S) tells the user, not the assistant, why it did not save:
                // the reason it will refuse for is checked here first, as LevelEdits.Save does
                var dirty = Edits.DirtyLevels.ToList();
                if (dirty.Count == 0) return Saved(null, CtJoin(SaveTables(), SaveContent()));
                foreach (int l in dirty)
                {
                    var e = Edits.Existing(l);
                    if (e == null) continue;
                    bool ok;
                    string why = e.Check(out ok);
                    if (!ok) throw new McpEditorException("Level " + l + " was not saved: its object lists now fail the game's check (" + why + "). Undo the last changes there. Nothing was written.");
                }
                string said = hostSave();
                return Saved(said, CtJoin(SaveTables(), SaveContent()));
            }
            var levels = Edits.Save();
            return Saved(levels.Count == 0 ? null : (levels.Count == 1 ? "Saved level " : "Saved levels ") + string.Join(", ", levels) + ".", CtJoin(SaveTables(), SaveContent()));
        }

        /// <summary>What save wrote: the levels' answer and the tables', or that there was nothing.</summary>
        static string Saved(string levels, string tables)
        {
            if (levels == null && tables == null) return "Nothing to save.";
            return levels == null ? tables : tables == null ? levels : levels + " " + tables;
        }
    }
}
