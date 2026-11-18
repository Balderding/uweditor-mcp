using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: history, floors, the object clipboard, locks and walls (McpEditor.Editing.cs).</summary>
    public sealed partial class EditorTools
    {
        /// <summary>A picture with a line of text after it (or the refusal alone).</summary>
        CallToolResult EdPicture(System.Func<string> said, System.Func<byte[]> png, [System.Runtime.CompilerServices.CallerMemberName] string method = null)
        {
            byte[] bytes = null;
            var r = Answer(() => { bytes = png(); return said(); }, method);
            if (bytes == null) return r;
            return new CallToolResult { Content = new List<ContentBlock> { ImageContentBlock.FromBytes(bytes, "image/png"), new TextContentBlock { Text = Text(r) ?? "" } } };
        }

        [McpServerTool(Name = "redo"), Description("Edit > Redo: takes the next undone change of a level again, step by step (a change made on several levels together is redone on all of them).")]
        public CallToolResult Redo([Description("Level number, from 1.")] int level, [Description("How many steps, 1-50 (default 1).")] int steps = 1)
        {
            return Answer(() => ed.Redo(level, steps));
        }

        [McpServerTool(Name = "undo_history"), Description("Edit > Undo history: a level's changes that undo can take back (the next first) and those redo can take again, by their labels.")]
        public CallToolResult UndoHistory([Description("Level number, from 1.")] int level) { return Answer(() => ed.UndoHistory(level)); }

        [McpServerTool(Name = "floor_raise"), Description("Raises (steps above 0) or lowers (below 0) the floor of a tile or a rectangle by floor steps, kept to 0-15; the objects lying there move with it, traps and triggers keep their height (Ctrl+wheel in the editor). One undo step.")]
        public CallToolResult FloorRaise([Description("Level number, from 1.")] int level, [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("Floor steps, -15 to 15, not 0.")] int steps,
            [Description("The other corner, 0-63 (leave out for one tile).")] int? x2 = null, [Description("The other corner, 0-63.")] int? y2 = null)
        {
            return Answer(() => ed.FloorRaise(level, x1, y1, x2, y2, steps));
        }

        [McpServerTool(Name = "objects_to_floor"), Description("Puts the objects lying on a tile or rectangle down on its floor (by their fine position on a slope); things placed at a height on purpose (0x160 on: bridges, pillars, wall writing, switches, traps) stay. One undo step.")]
        public CallToolResult ObjectsToFloor([Description("Level number, from 1.")] int level, [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("The other corner (leave out for one tile).")] int? x2 = null, [Description("The other corner.")] int? y2 = null)
        {
            return Answer(() => ed.DropToFloor(level, x1, y1, x2, y2));
        }

        [McpServerTool(Name = "objects_copy"), Description("Edit > Copy (or Cut): objects with everything inside them (contents, a trigger's traps), kept with where they lay relative to the first, for objects_paste on any level of this game. Cut also deletes them (one undo step).")]
        public CallToolResult ObjectsCopy([Description("Level number, from 1.")] int level, [Description("The objects' indexes, e.g. \"12, 34\".")] string indexes,
            [Description("True deletes them after copying (Edit > Cut).")] bool cut = false)
        {
            return Answer(() => ed.ObjectsCopy(level, indexes, cut));
        }

        [McpServerTool(Name = "objects_paste"), Description("Edit > Paste: the copied objects on a tile (the others on the tiles they had relative to the first, at the same height above the floor; any on rock are left out), or inside a container, creature or trigger. Copies of books and scrolls get texts of their own. One undo step.")]
        public CallToolResult ObjectsPaste([Description("Level number, from 1.")] int level,
            [Description("The tile, 0-63 (or give container).")] int? x = null, [Description("The tile, 0-63.")] int? y = null,
            [Description("A container's index (#) to paste into, instead of a tile.")] int? container = null)
        {
            return Answer(() => ed.ObjectsPaste(level, x, y, container));
        }

        [McpServerTool(Name = "objects_duplicate"), Description("Edit > Duplicate: copies of objects (with what they hold) on the same tiles, or in the same container. One undo step; the copies' indexes are answered.")]
        public CallToolResult ObjectsDuplicate([Description("Level number, from 1.")] int level, [Description("The objects' indexes, e.g. \"12, 34\".")] string indexes)
        {
            return Answer(() => ed.ObjectsDuplicate(level, indexes));
        }

        [McpServerTool(Name = "objects_delete"), Description("Deletes several objects with everything inside them as one undo step; the book, scroll and writing texts no other object reads are emptied (and come back on undo).")]
        public CallToolResult ObjectsDelete([Description("Level number, from 1.")] int level, [Description("The objects' indexes, e.g. \"12, 34\".")] string indexes)
        {
            return Answer(() => ed.ObjectsDelete(level, indexes));
        }

        [McpServerTool(Name = "objects_clip"), Description("What objects_copy holds now: the level copied from, the objects and where they lie relative to the first.")]
        public CallToolResult ObjectsClip() { return Answer(() => ed.ObjectsClip()); }

        [McpServerTool(Name = "object_take_out"), Description("Takes an object out of a container, a creature's inventory or a trigger's chain onto a tile of the level (traps keep their settings); one lying on a tile is moved. One undo step.")]
        public CallToolResult ObjectTakeOut([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("0-63.")] int x, [Description("0-63.")] int y, [Description("0-7 (default the middle).")] int? fineX = null, [Description("0-7.")] int? fineY = null)
        {
            return Answer(() => ed.ObjectTakeOut(level, index, x, y, fineX, fineY));
        }

        [McpServerTool(Name = "lock_remove"), Description("Takes the lock off a door or container (the right-click menu's Remove its lock). One undo step.")]
        public CallToolResult LockRemove([Description("Level number, from 1.")] int level, [Description("The door's or container's index (#).")] int holder)
        {
            return Answer(() => ed.LockRemove(level, holder));
        }

        [McpServerTool(Name = "lock_copy"), Description("Gives a door or container a copy of another one's lock (number, locked, difficulty, flags); the lock it had goes. One undo step.")]
        public CallToolResult LockCopy([Description("Level number, from 1.")] int level, [Description("The door or container whose lock is copied (#).")] int from,
            [Description("The door or container that gets it (#).")] int to)
        {
            return Answer(() => ed.LockCopy(level, from, to));
        }

        [McpServerTool(Name = "snap_to_wall"), Description("Puts a lever, button, writing or picture (0x160-0x17F) against the nearest wall of its tile, facing into the wall as the shipped ones do. One undo step.")]
        public CallToolResult SnapToWall([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index)
        {
            return Answer(() => ed.SnapToWall(level, index));
        }
    }
}
