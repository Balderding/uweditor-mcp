using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: every trap recipe with all its settings (McpEditor.EditingRecipes.cs).</summary>
    public sealed partial class EditorTools
    {
        const string EdTrapHelp = "The trap: damage (damage 1-63), poison (damage = strength 1-15), message (text: a new string of block 9, or textIndex), variable (variable, operation 0 add 1 subtract 2 set 3 and 4 or 5 xor 6 shift, value; UW1 questBit for quest bits 0-31), create (item, optionally atX/atY), door (door, doorAction open/close/toggle, unlock), delete (deleteIndex, an object lying on a tile).";

        [McpServerTool(Name = "recipe_list"), Description("Edit > Trap recipes: the recipes the editor makes, with what each does and the tools that make them.")]
        public CallToolResult RecipeList() { return Answer(() => ed.RecipeList()); }

        [McpServerTool(Name = "lever_items"), Description("The switches (levers, buttons, pull chains) a new lever of recipe_lever can be.")]
        public CallToolResult LeverItems() { return Answer(() => ed.LeverItems()); }

        [McpServerTool(Name = "recipe_plate_trap"), Description("Pressure plate recipe with every setting: stepping on a square runs any trap. " + EdTrapHelp + " A move trigger works in both games; pressure is UW2's weight plate. One undo step.")]
        public CallToolResult RecipePlateTrap([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("damage, poison, message, variable, create, door or delete.")] string trap,
            [Description("UW2: a pressure plate instead of a move trigger.")] bool pressure = false,
            [Description("Works every time (default); false: once.")] bool reusable = true,
            [Description("Creatures set it off too.")] bool creatures = false,
            [Description("Damage 1-63, or poison strength 1-15.")] int? damage = null,
            [Description("Message: the new text.")] string text = null, [Description("Message: a string of block 9 already there.")] int? textIndex = null,
            [Description("Variable: its number.")] int? variable = null, [Description("UW1: variable is a quest bit.")] bool questBit = false,
            [Description("Variable: operation 0-7 (default 2 set).")] int? operation = null, [Description("Variable: the value (default 1).")] int? value = null,
            [Description("Create: the item, by number or name.")] string item = null, [Description("Create: where it appears (default the plate).")] int? atX = null, [Description("Create.")] int? atY = null,
            [Description("Door: the door's index (#).")] int? door = null, [Description("Door: open, close or toggle (default).")] string doorAction = "toggle",
            [Description("Door: also unlock it.")] bool unlock = false,
            [Description("Delete: the index (#) of the object removed.")] int? deleteIndex = null)
        {
            return Answer(() => ed.RecipePlateTrap(level, x, y, trap, pressure, reusable, creatures, damage, text, textIndex, variable, questBit, operation, value, item, atX, atY, door, doorAction, unlock, deleteIndex));
        }

        [McpServerTool(Name = "recipe_object_trigger"), Description("Object trigger recipe: picking up, using or looking at an object runs a trap (the trigger goes inside the object). " + EdTrapHelp + " One undo step.")]
        public CallToolResult RecipeObjectTrigger([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("pickup, use or look.")] string when, [Description("damage, poison, message, variable, create, door or delete.")] string trap,
            [Description("Works every time (default); false: once.")] bool reusable = true,
            [Description("Creatures set it off too.")] bool creatures = false,
            [Description("Damage 1-63, or poison strength 1-15.")] int? damage = null,
            [Description("Message: the new text.")] string text = null, [Description("Message: a string of block 9 already there.")] int? textIndex = null,
            [Description("Variable: its number.")] int? variable = null, [Description("UW1: variable is a quest bit.")] bool questBit = false,
            [Description("Variable: operation 0-7 (default 2 set).")] int? operation = null, [Description("Variable: the value (default 1).")] int? value = null,
            [Description("Create: the item, by number or name.")] string item = null, [Description("Create: where it appears (default the object's square).")] int? atX = null, [Description("Create.")] int? atY = null,
            [Description("Door: the door's index (#).")] int? door = null, [Description("Door: open, close or toggle (default).")] string doorAction = "toggle",
            [Description("Door: also unlock it.")] bool unlock = false,
            [Description("Delete: the index (#) of the object removed.")] int? deleteIndex = null)
        {
            return Answer(() => ed.RecipeObjectTrigger(level, index, when, trap, reusable, creatures, damage, text, textIndex, variable, questBit, operation, value, item, atX, atY, door, doorAction, unlock, deleteIndex));
        }

        [McpServerTool(Name = "recipe_lever"), Description("Lever opens a door recipe with every setting: a lever already in the level (lever), or a new one on a wall of a tile (wallX, wallY, side, leverItem from lever_items); it opens, closes or toggles the door, optionally unlocking it. One undo step.")]
        public CallToolResult RecipeLever([Description("Level number, from 1.")] int level, [Description("The door's index (#).")] int door,
            [Description("A lever, switch, button or pull chain in the level (#), instead of a new one.")] int? lever = null,
            [Description("A new lever's tile, 0-63.")] int? wallX = null, [Description("A new lever's tile.")] int? wallY = null,
            [Description("A new lever's wall: auto, north, east, south or west.")] string side = "auto",
            [Description("A new lever's item (0x170-0x177 by number or name; default 0x175).")] string leverItem = null,
            [Description("open, close or toggle (default).")] string action = "toggle", [Description("Also unlock the door.")] bool unlock = false,
            [Description("Works every time (default); false: once.")] bool reusable = true, [Description("Creatures set it off too.")] bool creatures = false)
        {
            return Answer(() => ed.RecipeLever(level, door, lever, wallX, wallY, side, leverItem, action, unlock, reusable, creatures));
        }

        [McpServerTool(Name = "recipe_end_of_game"), Description("UW1: stepping on a square ends the game (a move trigger and a hack trap of quality 0x3F, as level 9's). One undo step.")]
        public CallToolResult RecipeEndOfGame([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y)
        {
            return Answer(() => ed.RecipeEndOfGame(level, x, y));
        }
    }
}
