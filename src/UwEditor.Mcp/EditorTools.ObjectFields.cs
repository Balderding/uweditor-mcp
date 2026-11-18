using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The tools for one object's fields, its contents and its lock (McpEditor.ObjectFields.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "object_get"), Description("Every field of one object as the editor's properties panel shows it: the record (item id, flags, z, heading, fine x/y, quality, owner, link), for a creature all its mobile fields (HP, goal, goal target, attitude, home x/y, whoami, talked to ...), the kind's own names (a lock's number, a trigger's target, a door's spike), what it is inside and what it holds. 'name = value' fields can be set with object_set.")]
        public CallToolResult ObjectGet([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index)
        {
            return Answer(() => ed.ObjectGet(level, index));
        }

        [McpServerTool(Name = "object_set"), Description("Sets fields of an object by the names object_get shows (case and spaces do not matter; gtarg, item and a few other short names work): one field with field and value, or several with fields as \"hp=40, attitude=friendly\". A value is a number (31 or 0x1F), true/false for a flag, or a choice's name (attitude friendly, goal attack). Range-checked, all or nothing, one undo step. Not saved until save.")]
        public CallToolResult ObjectSet([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("A field's name, as object_get shows it.")] string field = null,
            [Description("Its new value.")] string value = null,
            [Description("Several at once: name=value pairs separated by commas.")] string fields = null)
        {
            return Answer(() => ed.ObjectSet(level, index, field, value, fields));
        }

        [McpServerTool(Name = "object_put_inside"), Description("Puts an object (from a tile or another container) inside a container, a door, a creature's inventory or a trigger's chain, at the head of its list. One undo step.")]
        public CallToolResult ObjectPutInside([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("The container's index (#).")] int container)
        {
            return Answer(() => ed.ObjectPutInside(level, index, container));
        }

        [McpServerTool(Name = "object_add_inside"), Description("Adds an item inside a container, a creature's inventory or a trigger's chain, with the editor's defaults for it. The item by number or exact name. One undo step. Not saved until save.")]
        public CallToolResult ObjectAddInside([Description("Level number, from 1.")] int level, [Description("The container's index (#).")] int container,
            [Description("Item number (0x0B0 or 176) or name.")] string item,
            [Description("0-63.")] int? quality = null, [Description("0-63 (a key: the lock number it opens).")] int? owner = null,
            [Description("For a stack: how many, 1-511.")] int? quantity = null)
        {
            return Answer(() => ed.ObjectAddInside(level, container, item, quality, owner, quantity));
        }

        [McpServerTool(Name = "lock_door"), Description("Locks (or unlocks) a door or a chest: a lock object (0x10F) inside it with this number, or its lock changed. A key whose owner equals the number opens it (numbers above 63 have no key). Answers which keys on the level fit. One undo step.")]
        public CallToolResult LockDoor([Description("Level number, from 1.")] int level, [Description("The door's or chest's index (#).")] int doorIndex,
            [Description("The lock number, 0-511 (keys reach 0-63).")] int lockNumber,
            [Description("Locked (default) or not.")] bool locked = true,
            [Description("Pick difficulty 0-15 (skill check against 3 x this; 15 cannot be picked). Leave out: 0 for a new lock, unchanged for an old one.")] int? difficulty = null)
        {
            return Answer(() => ed.LockDoor(level, doorIndex, lockNumber, locked, difficulty));
        }
    }
}
