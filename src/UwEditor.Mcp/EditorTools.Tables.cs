using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The game's data tables (OBJECTS.DAT, COMOBJ.DAT, TERRAIN.DAT, SKILLS.DAT, CMB.DAT, SHADES.DAT, DL.DAT) and STRINGS.PAK.</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "tables"), Description("The game's data tables (not a level's): the item tables of OBJECTS.DAT (Weapons, Ranged, Armour, Creatures, Containers, Lights, Food, Triggers, Animations) and COMOBJ.DAT (Common properties), Terrain, Skills, Combinations, Shades and (UW2) Minimum light, each with its rows and field names. Give a table for each of its fields explained with its range.")]
        public CallToolResult Tables([Description("A table's name, to explain its fields; leave out for the list.")] string table = null)
        {
            return Answer(() => ed.Tables(table));
        }

        [McpServerTool(Name = "table_get"), Description("Every field of one row of a data table with its value (and its meaning, where it has one). Changed fields not yet saved are marked.")]
        public CallToolResult TableGet([Description("The table's name (see tables).")] string table,
            [Description("The row: a decimal row number; in an item table also the item's number in hexadecimal (0x045) or its name.")] string row)
        {
            return Answer(() => ed.TableGet(table, row));
        }

        [McpServerTool(Name = "table_set"), Description("Sets one field of one row of a data table, range-checked; a wrong field is refused, listing the table's fields. These files apply to the whole game, saved games too. Not written until save.")]
        public CallToolResult TableSet([Description("The table's name (see tables).")] string table,
            [Description("The row: a decimal row number; in an item table also the item's number in hexadecimal (0x045) or its name.")] string row,
            [Description("The field's name (any case; a unique part of it will do).")] string field,
            [Description("The value: decimal or hexadecimal with 0x; an item by number or name for a combination's items; skill numbers or names, comma-separated, for a skills entry; 1/0 for a flag.")] string value)
        {
            return Answer(() => ed.TableSet(table, row, field, value));
        }

        [McpServerTool(Name = "table_revert"), Description("Drops the unsaved changes of one data table (for the game tables the whole file's).")]
        public CallToolResult TableRevert([Description("The table's name (see tables).")] string table) { return Answer(() => ed.TableRevert(table)); }

        [McpServerTool(Name = "creature_get"), Description("A kind of creature's stats from OBJECTS.DAT (hit points, attributes, race, attacks, defence, speed, senses, loot, experience...), every field by name with its value.")]
        public CallToolResult CreatureGet([Description("The creature: its item number (0x45 or 69, 0x40-0x7F) or name.")] string creature)
        {
            return Answer(() => ed.CreatureGet(creature));
        }

        [McpServerTool(Name = "creature_set"), Description("Sets one stat of a kind of creature in OBJECTS.DAT (every creature of that kind, in every level and saved game), range-checked. Not written until save.")]
        public CallToolResult CreatureSet([Description("The creature: its item number (0x45 or 69, 0x40-0x7F) or name.")] string creature,
            [Description("The field's name as creature_get shows it (any case; a unique part will do: hp, intelligence, race).")] string field,
            [Description("The value, decimal or hexadecimal with 0x.")] string value)
        {
            return Answer(() => ed.CreatureSet(creature, field, value));
        }

        [McpServerTool(Name = "string_get"), Description("STRINGS.PAK: without a block, the blocks there are; with a block, its strings (index: text); with an index too, that one string. Item names are block 4 (the index is the item's number).")]
        public CallToolResult StringGet([Description("The block, decimal or 0x hex; leave out for the list of blocks.")] string block = null,
            [Description("The string's index in the block; leave out for all of them.")] int? index = null)
        {
            return Answer(() => ed.StringGet(block, index));
        }

        [McpServerTool(Name = "string_set"), Description("Changes one string of STRINGS.PAK (or adds one, at the index after the last). Not written until save.")]
        public CallToolResult StringSet([Description("The block, decimal or 0x hex.")] string block, [Description("The string's index.")] int index,
            [Description("The new text.")] string text)
        {
            return Answer(() => ed.StringSet(block, index, text));
        }
    }
}
