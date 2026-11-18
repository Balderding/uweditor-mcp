using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The tools for the player, its inventory and the global variables (McpEditor.SavesPlayer.cs, McpEditor.SavesGlobals.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "player_get"), Description("The open saved game's character: name, class, sex, hand, body, attributes, vitality, mana, hunger, fatigue, level, experience, skill points, UW1's story bits, every skill, the position, what the game derives, and a count of warnings.")]
        public CallToolResult PlayerGet() { return Answer(() => ed.PlayerGet()); }

        [McpServerTool(Name = "player_set"), Description("Sets one field of the character or one skill (by name: strength, max mana, experience, class, female, garamon buried, attack, swimming...). Kept until player_save or save; the answer adds what the game would notice about the value.")]
        public CallToolResult PlayerSet([Description("The field or skill, as player_get names it.")] string field, [Description("The value: a number, true/false, a class name, a name.")] string value)
        {
            return Answer(() => ed.PlayerSet(field, value));
        }

        [McpServerTool(Name = "player_check"), Description("Everything about the character the game would notice (UW2 calls attributes over 64 cheating), values play cannot produce, and notes (maxima the game would recompute, inventory problems).")]
        public CallToolResult PlayerCheck() { return Answer(() => ed.PlayerCheck()); }

        [McpServerTool(Name = "player_recompute"), Description("Sets max vitality, max mana and carrying capacity as the game computes them from the attributes, level and mana skill.")]
        public CallToolResult PlayerRecompute() { return Answer(() => ed.PlayerRecompute()); }

        [McpServerTool(Name = "player_move"), Description("Edit > Move the player here: puts the player on an open tile of any level, at its floor; the heading kept unless given. Kept until player_save or save.")]
        public CallToolResult PlayerMove([Description("Level number, from 1.")] int level, [Description("1-62.")] int x, [Description("1-62.")] int y,
            [Description("Where in the tile, 0 to below 1 (default 0.5).")] double? fineX = null, [Description("0 to below 1 (default 0.5).")] double? fineY = null,
            [Description("0-7: 0 north, 2 east, 4 south, 6 west.")] int? heading = null)
        {
            return Answer(() => ed.PlayerMove(level, x, y, fineX, fineY, heading));
        }

        [McpServerTool(Name = "player_save"), Description("Writes the player to PLAYER.DAT (the old file kept as PLAYER.DAT.BKP), and the conversations' globals changed here to BGLOBALS.DAT.")]
        public CallToolResult PlayerSave() { return Answer(() => ed.PlayerSave()); }

        [McpServerTool(Name = "player_default"), Description("The editor's built-in default character (used by play tests and save_create when there is no saved game): its fields, skills and inventory count. It is not editable.")]
        public CallToolResult PlayerDefault() { return Answer(() => ed.PlayerDefault()); }

        // ---- the inventory

        [McpServerTool(Name = "inventory_list"), Description("The player's inventory, each object numbered (the numbers the other inventory tools take): its slot (head, body, gloves, legs, feet, shoulders, hands, rings, pack 1-8), item, quality, owner, quantity or special value, containers' contents indented.")]
        public CallToolResult InventoryList() { return Answer(() => ed.InventoryList()); }

        [McpServerTool(Name = "inventory_add"), Description("Puts a new object into the inventory: in a slot (equipping it: head, body, gloves, legs, feet, shoulder1/2, hand1/2, ring1/2, pack1-8 or 0-18), into a container (into: its number), or the first free pack slot. Kept until player_save or save.")]
        public CallToolResult InventoryAdd([Description("Item number (0x0B0 or 176) or name.")] string item,
            [Description("A slot name or 0-18; leave out for the first free pack slot.")] string slot = null,
            [Description("A container's number from inventory_list.")] int? into = null,
            [Description("0-63 (default 40).")] int? quality = null, [Description("0-63.")] int? owner = null,
            [Description("A quantity 0-511 (default 1).")] int? quantity = null,
            [Description("A special value 0-511 instead of a quantity (a spell, a text; stored as 512 + it).")] int? special = null,
            [Description("The enchanted bit.")] bool? enchanted = null)
        {
            return Answer(() => ed.InventoryAdd(item, slot, into, quality, owner, quantity, special, enchanted));
        }

        [McpServerTool(Name = "inventory_set"), Description("Sets a field of an inventory object: item, quality, owner, quantity, special, link, flags, enchanted, invisible, is quantity.")]
        public CallToolResult InventorySet([Description("The object's number from inventory_list.")] int number, [Description("The field.")] string field, [Description("The value.")] string value)
        {
            return Answer(() => ed.InventorySet(number, field, value));
        }

        [McpServerTool(Name = "inventory_move"), Description("Moves an inventory object (with what it holds) to a slot, equipping it (an object there goes to the moved one's old slot or the pack), or into a container.")]
        public CallToolResult InventoryMove([Description("The object's number from inventory_list.")] int number,
            [Description("A slot name or 0-18.")] string slot = null, [Description("Or a container's number.")] int? into = null)
        {
            return Answer(() => ed.InventoryMove(number, slot, into));
        }

        [McpServerTool(Name = "inventory_remove"), Description("Takes an object, and whatever is inside it, out of the inventory.")]
        public CallToolResult InventoryRemove([Description("The object's number from inventory_list.")] int number) { return Answer(() => ed.InventoryRemove(number)); }

        // ---- global variables

        [McpServerTool(Name = "global_list"), Description("Tools > Global variables: the game state conversations reach, with values in the open saved game: quest flags and bytes, game variables (traps), X clocks (UW2), skills, imported talker/player variables, and each conversation's own (private). Filter by kind, a part of the name, a conversation slot, used or set ones.")]
        public CallToolResult GlobalList([Description("quest, variable, xclock, skill, private or imported; leave out for all.")] string kind = null,
            [Description("A part of the name or description.")] string filter = null, [Description("Only those of this conversation slot.")] int? slot = null,
            [Description("Only those some conversation uses.")] bool onlyUsed = false, [Description("Only those not 0.")] bool onlySet = false)
        {
            return Answer(() => ed.GlobalList(kind, filter, slot, onlyUsed, onlySet));
        }

        [McpServerTool(Name = "global_get"), Description("One global: where it is kept, its value and range, and every conversation that reads or sets it.")]
        public CallToolResult GlobalGet([Description("quest, variable, xclock, skill, private or imported.")] string kind, [Description("Its number (a private one: its address).")] int number = 0,
            [Description("For private: the conversation slot.")] int? slot = null, [Description("For imported: its name.")] string name = null)
        {
            return Answer(() => ed.GlobalGet(kind, number, slot, name));
        }

        [McpServerTool(Name = "global_set"), Description("Changes a global in the open saved game (quest flag 0-1, quest byte 0-255, game variable, X clock, skill, a conversation's own). Kept until global_save.")]
        public CallToolResult GlobalSet([Description("quest, variable, xclock, skill or private.")] string kind, [Description("Its number (private: its address).")] int number,
            [Description("The new value.")] int value, [Description("For private: the conversation slot.")] int? slot = null)
        {
            return Answer(() => ed.GlobalSet(kind, number, value, slot, null));
        }

        [McpServerTool(Name = "global_save"), Description("Writes the changed globals: PLAYER.DAT and BGLOBALS.DAT (each old file kept as .BKP).")]
        public CallToolResult GlobalSave() { return Answer(() => ed.GlobalSave()); }
    }
}
