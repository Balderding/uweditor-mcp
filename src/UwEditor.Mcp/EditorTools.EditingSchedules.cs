using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: UW2's schedules (McpEditor.EditingSchedules.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "schedules"), Description("Tools > Schedules (UW2's SCD.ARK, the save's or DATA's): the 16 blocks (0 by the time of day, the others by the X clocks) with their row counts, or one block's rows, each described.")]
        public CallToolResult Schedules([Description("A block 0-15 (leave out for the list).")] int? block = null) { return Answer(() => ed.SchedulesList(block)); }

        [McpServerTool(Name = "schedule_row"), Description("One schedule row's fields (time, level, once, event, then the event's parameters) with values, ranges and meanings.")]
        public CallToolResult ScheduleRow([Description("Block 0-15.")] int block, [Description("Row, from 0.")] int row) { return Answer(() => ed.ScheduleRow(block, row)); }

        [McpServerTool(Name = "schedule_set"), Description("Sets a field of a schedule row by name (as schedule_row lists them); a new time moves the row to its place. Not written until schedules_save.")]
        public CallToolResult ScheduleSet([Description("Block 0-15.")] int block, [Description("Row, from 0.")] int row, [Description("The field's name.")] string field, [Description("The value.")] int value)
        {
            return Answer(() => ed.ScheduleSet(block, row, field, value));
        }

        [McpServerTool(Name = "schedule_row_add"), Description("Adds a schedule row after a row (-1: at the start): a copy of it, or an empty one (event 0, any level) with its time.")]
        public CallToolResult ScheduleRowAdd([Description("Block 0-15.")] int block, [Description("The row it follows (-1: the start).")] int after, [Description("A copy of that row.")] bool copy = false)
        {
            return Answer(() => ed.ScheduleRowAdd(block, after, copy));
        }

        [McpServerTool(Name = "schedule_row_delete"), Description("Deletes a schedule row.")]
        public CallToolResult ScheduleRowDelete([Description("Block 0-15.")] int block, [Description("Row, from 0.")] int row) { return Answer(() => ed.ScheduleRowDelete(block, row)); }

        [McpServerTool(Name = "schedule_row_move"), Description("Swaps a schedule row with the one above or below it when both have the same time (rows run in time order).")]
        public CallToolResult ScheduleRowMove([Description("Block 0-15.")] int block, [Description("Row, from 0.")] int row, [Description("True up, false down.")] bool up)
        {
            return Answer(() => ed.ScheduleRowMove(block, row, up));
        }

        [McpServerTool(Name = "schedules_save"), Description("Writes the changed schedule blocks to SCD.ARK (the old file kept as SCD.ARK.BKP). Refuses while a block is out of time order.")]
        public CallToolResult SchedulesSave() { return Answer(() => ed.SchedulesSave()); }

        [McpServerTool(Name = "schedules_revert"), Description("Drops the unsaved schedule changes: SCD.ARK as it is on disk.")]
        public CallToolResult SchedulesRevert() { return Answer(() => ed.SchedulesRevert()); }
    }
}
