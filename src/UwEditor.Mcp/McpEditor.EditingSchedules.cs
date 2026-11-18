using System;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>Tools ▸ Schedules (UW2's SCD.ARK): the 16 schedules, their rows and fields; written by schedules_save.</summary>
    public sealed partial class McpEditor
    {
        /// <summary>Attached: the schedules of the app's Schedules window while it is open, else null.</summary>
        public Func<Schedules> HostSchedules { get; set; }
        /// <summary>Attached: after a schedule changed here (the window shows it again).</summary>
        public Action HostSchedulesEdited { get; set; }

        Schedules edSchedules;

        Schedules EdSchedules()
        {
            Require();
            var h = HostSchedules?.Invoke();
            if (h != null && h.Session == Session)
            {
                if (edSchedules != null && edSchedules.Session == Session && edSchedules.HasChanges) throw new McpEditorException("The editor's Schedules window is open now, without your unsaved schedule changes: schedules_save or schedules_revert them first.");
                edSchedules = null;
                return h;
            }
            if (!Session.IsUw2) throw new McpEditorException("Schedules (SCD.ARK) belong to Ultima Underworld II; Ultima Underworld I has none.");
            if (edSchedules == null || edSchedules.Session != Session) edSchedules = Schedules.Load(Session);
            return edSchedules;
        }

        static void EdBlock(Schedules sc, int block)
        {
            if (block < 0 || block >= sc.Blocks.Count) throw new ArgumentOutOfRangeException(null, "Blocks are 0-" + (sc.Blocks.Count - 1) + ".");
        }

        static void EdRow(Schedules sc, int block, int row)
        {
            EdBlock(sc, block);
            int n = sc.Blocks[block].Rows.Count;
            if (row < 0 || row >= n) throw new ArgumentOutOfRangeException(null, n == 0 ? "Block " + block + " has no rows." : "Block " + block + " has rows 0-" + (n - 1) + ".");
        }

        void EdSchedulesEdited() { HostSchedulesEdited?.Invoke(); }

        /// <summary>The blocks (no block given) or a block's rows, each described.</summary>
        public string SchedulesList(int? block)
        {
            var sc = EdSchedules();
            var b = new StringBuilder();
            if (!block.HasValue)
            {
                b.Append(sc.Where).Append("/SCD.ARK").Append(sc.HasChanges ? " (unsaved changes)" : "").Append(":\n");
                for (int i = 0; i < sc.Blocks.Count; i++) b.Append("block ").Append(i).Append(" (").Append(ScdArk.ClockName(i)).Append("): ").Append(sc.Blocks[i].Rows.Count).Append(" rows\n");
                foreach (var p in sc.Problems()) b.Append("Problem: ").Append(p).Append('\n');
                return b.ToString();
            }
            EdBlock(sc, block.Value);
            var rows = sc.Blocks[block.Value].Rows;
            b.Append("Block ").Append(block.Value).Append(" (").Append(ScdArk.ClockName(block.Value)).Append("), ").Append(rows.Count).Append(" rows:\n");
            for (int i = 0; i < rows.Count; i++) b.Append(i).Append(": ").Append(sc.Describe(rows[i])).Append('\n');
            return b.ToString();
        }

        /// <summary>A row's fields with their values and meanings.</summary>
        public string ScheduleRow(int block, int row)
        {
            var sc = EdSchedules();
            EdRow(sc, block, row);
            var r = sc.Blocks[block].Rows[row];
            var b = new StringBuilder("Block " + block + " row " + row + ": " + sc.Describe(r) + "\n");
            foreach (var f in sc.Fields(block, r))
            {
                string meaning = sc.Meaning(r, f);
                b.Append(f.Name).Append(" = ").Append(f.Get(r)).Append(" (").Append(f.Min).Append('-').Append(f.Max).Append(')').Append(string.IsNullOrWhiteSpace(meaning) ? "" : ": " + meaning).Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Sets a field of a row by name; a new time moves the row to its place. Not written until schedules_save.</summary>
        public string ScheduleSet(int block, int row, string field, int value)
        {
            var sc = EdSchedules();
            EdRow(sc, block, row);
            var r = sc.Blocks[block].Rows[row];
            var fields = sc.Fields(block, r);
            var f = fields.FirstOrDefault(x => string.Equals(x.Name, (field ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (f == null) throw new ArgumentException("This row's fields are " + string.Join(", ", fields.Select(x => x.Name)) + ".");
            if (value < f.Min || value > f.Max) throw new ArgumentOutOfRangeException(null, f.Name + " is " + f.Min + "-" + f.Max + ".");
            int at = sc.SetField(block, row, f, value);
            EdSchedulesEdited();
            return "Block " + block + ": " + f.Name + " of row " + row + " set to " + value + (at != row ? "; the row moved to " + at + " by its time" : "") + ". Now: " + sc.Describe(sc.Blocks[block].Rows[at]) + ". Not written until schedules_save.";
        }

        /// <summary>Adds a row after a row (-1: at the start): a copy of it, or an empty one with its time.</summary>
        public string ScheduleRowAdd(int block, int after, bool copy)
        {
            var sc = EdSchedules();
            EdBlock(sc, block);
            if (after < -1 || after >= sc.Blocks[block].Rows.Count) throw new ArgumentOutOfRangeException(null, "After row -1 (the start) to " + (sc.Blocks[block].Rows.Count - 1) + ".");
            if (copy && after < 0) throw new ArgumentException("Copy needs a row to copy (after).");
            int at = sc.AddRow(block, after, copy);
            EdSchedulesEdited();
            return "Block " + block + ": row " + at + " added" + (copy ? " (a copy of row " + after + ")" : "") + ": " + sc.Describe(sc.Blocks[block].Rows[at]) + ".";
        }

        public string ScheduleRowDelete(int block, int row)
        {
            var sc = EdSchedules();
            EdRow(sc, block, row);
            var r = sc.Blocks[block].Rows[row];
            string was = sc.Describe(r);
            sc.DeleteRow(block, row);
            EdSchedulesEdited();
            return "Block " + block + ": row " + row + " (" + was + ") deleted" + (r.Event == 10 ? "; the disabled rows after it no longer belong to a test" : "") + ".";
        }

        /// <summary>Swaps a row with its neighbour of the same time (rows run in time order).</summary>
        public string ScheduleRowMove(int block, int row, bool up)
        {
            var sc = EdSchedules();
            EdRow(sc, block, row);
            if (!sc.MoveRow(block, row, up ? -1 : 1)) throw new ArgumentException("Rows run in order of time: only rows of the same time swap. Change the time to move a row past others.");
            EdSchedulesEdited();
            return "Block " + block + ": row " + row + " is row " + (row + (up ? -1 : 1)) + " now.";
        }

        /// <summary>Writes the changed schedule blocks (SCD.ARK.BKP keeps the old file).</summary>
        public string SchedulesSave()
        {
            var sc = EdSchedules();
            if (!sc.HasChanges) return "The schedules have no changes.";
            var written = sc.Save();
            EdSchedulesEdited();
            return "Wrote block" + (written.Count == 1 ? " " : "s ") + string.Join(", ", written) + " of " + sc.Path + ".";
        }

        /// <summary>Drops the unsaved schedule changes.</summary>
        public string SchedulesRevert()
        {
            var sc = EdSchedules();
            sc.Revert();
            EdSchedulesEdited();
            return "The schedules are as " + sc.Where + "/SCD.ARK has them on disk.";
        }
    }
}
