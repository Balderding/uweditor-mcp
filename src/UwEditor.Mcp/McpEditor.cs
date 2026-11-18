using System;
using System.IO;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>A plain answer for the MCP client (a refusal, a missing game), not a program fault.</summary>
    public sealed class McpEditorException : Exception
    {
        public McpEditorException(string message) : base(message) { }
    }

    /// <summary>
    /// What the MCP tools do, without the protocol: one open game folder and its level edits.
    /// Each method answers in plain text. With an only-folder, Open refuses every other folder.
    /// </summary>
    public sealed partial class McpEditor
    {
        readonly string only;
        GameSession session;
        LevelEdits edits;
        // attached to a game someone else holds (the app): where it is, and the thread its calls run on
        Func<GameSession> hostSession;
        Func<LevelEdits> hostEdits;
        Func<Func<string>, string> runner;
        Func<string> hostSave;

        public GameSession Session { get { return hostSession != null ? hostSession() : session; } }
        public LevelEdits Edits { get { return hostEdits != null ? hostEdits() : edits; } }
        public bool IsAttached { get { return hostSession != null; } }

        public McpEditor(string onlyFolder)
        {
            only = string.IsNullOrWhiteSpace(onlyFolder) ? null : Normal(onlyFolder);
        }

        /// <summary>
        /// Works on the game a host holds (the app: whichever game it has open at the time of each
        /// call), each call run through runner (the app's UI thread). Open is refused.
        /// </summary>
        /// <param name="save">The host's own save (the app's, as Ctrl+S), answering what it did; null: the levels are saved here.</param>
        public static McpEditor Attached(Func<GameSession> session, Func<LevelEdits> edits, Func<Func<string>, string> runner, Func<string> save = null)
        {
            if (session == null || edits == null || runner == null) throw new ArgumentNullException();
            return new McpEditor(null) { hostSession = session, hostEdits = edits, runner = runner, hostSave = save };
        }

        /// <summary>Runs one tool call: through the host's runner when attached, else here.</summary>
        public string Run(Func<string> call) { return runner != null ? runner(call) : call(); }

        static string Normal(string folder)
        {
            return Path.GetFullPath(folder.Trim().Trim('"')).TrimEnd('\\', '/');
        }

        /// <summary>Opens a game folder (the one holding DATA); answers the game and its level slots.</summary>
        public string Open(string folder, bool discard = false)
        {
            if (IsAttached) throw new McpEditorException("Open games in the app; this server works on the game open there.");
            if (string.IsNullOrWhiteSpace(folder)) throw new McpEditorException("No folder given.");
            if (only != null && !string.Equals(Normal(folder), only, StringComparison.OrdinalIgnoreCase))
                throw new McpEditorException("This server opens only " + only + ".");
            if (!discard && Edits != null && Edits.HasChanges)
                throw new McpEditorException("Unsaved changes to level " + string.Join(", ", Edits.DirtyLevels) + ": save first, or open with discard to drop them.");
            var s = GameSession.Open(folder);
            session = s;
            edits = new LevelEdits(s);
            var b = new StringBuilder();
            b.Append(s.Kind).Append(" at ").Append(s.GameFolder).Append('\n');
            foreach (var slot in s.LevelSlots())
            {
                b.Append("level ").Append(slot.Level).Append(": ");
                b.Append(slot.Damage != null ? "damaged (" + slot.Damage + ")" : slot.Present ? "in use" : "empty");
                b.Append('\n');
            }
            return b.ToString();
        }

        void Require()
        {
            if (Session == null || Edits == null) throw new McpEditorException(IsAttached ? "No game is open in the app." : "Open a game folder first.");
        }

        LevelEditor Level(int level)
        {
            Require();
            CheckLevelNumber(Session, level);
            return Edits.For(level);
        }

        static void CheckLevelNumber(GameSession s, int level)
        {
            if (level < 1 || level > s.Profile.LevelSlots)
                throw new ArgumentOutOfRangeException(null, "Levels are 1-" + s.Profile.LevelSlots + ".");
        }

        /// <summary>A level to read: its editor's model when it has one, else as saved (no editor made for reading).</summary>
        LevelModel ReadModel(int level)
        {
            Require();
            CheckLevelNumber(Session, level);
            return Edits.Model(level);
        }

        /// <summary>Takes back the last steps of a level's changes.</summary>
        public string Undo(int level, int steps)
        {
            Require();
            CheckLevelNumber(Session, level);
            if (steps < 1 || steps > 50) throw new ArgumentOutOfRangeException(null, "Undo 1-50 steps.");
            var e = Edits.Existing(level);
            if (e == null || !e.CanUndo) throw new McpEditorException("Level " + level + " has nothing to undo.");
            var labels = new System.Collections.Generic.List<string>();
            for (int i = 0; i < steps && e.CanUndo; i++)
            {
                string label = e.UndoLabel;
                string refused;
                Edits.Undo(level, out refused);
                if (refused != null) throw new McpEditorException(refused);
                labels.Add(label);
            }
            return "Undid on level " + level + ": " + string.Join("; ", labels) + ".";
        }

        /// <summary>A level's editor for a change: refuses an empty or damaged slot (it has no level to change).</summary>
        LevelEditor Editable(int level)
        {
            var e = Level(level);
            if (!e.Model.Present)
            {
                var slot = Session.LevelSlots().Find(x => x.Level == level);
                throw new McpEditorException("Level " + level + " is " + (slot != null && slot.Damage != null ? "damaged (" + slot.Damage + ")" : "an empty slot") + ": there is nothing to change.");
            }
            return e;
        }
    }
}
