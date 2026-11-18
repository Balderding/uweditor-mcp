using System.Linq;

namespace UwEditor.Mcp
{
    /// <summary>check and save.</summary>
    public sealed partial class McpEditor
    {
        /// <summary>The game's own checks on a level as it is now (its object lists).</summary>
        public string Check(int level)
        {
            bool ok;
            string why = Level(level).Check(out ok);
            return ok ? "Level " + level + " passes the game's checks." : "Level " + level + " fails the game's checks: " + why;
        }

        /// <summary>
        /// Writes the changed levels (the editor's safe write: the old LEV.ARK kept as LEV.ARK.BKP).
        /// The editor refuses, writing nothing, when a changed level now fails the game's checks.
        /// </summary>
        public string Save()
        {
            Require();
            if (hostSave != null) return hostSave();
            var levels = Edits.Save();
            if (levels.Count == 0) return "Nothing to save.";
            return (levels.Count == 1 ? "Saved level " : "Saved levels ") + string.Join(", ", levels) + ".";
        }
    }
}
