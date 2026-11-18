using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: triggers and traps, links, targets, terrain preview, keys (McpEditor.EditingTriggers.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "trigger_chains"), Description("Tools > Triggers and traps: a level's triggers with their target squares and the traps their chains run, the wandering-monster spawners, the traps no trigger reaches, and UW2's rune traps.")]
        public CallToolResult TriggerChains([Description("Level number, from 1.")] int level) { return Answer(() => ed.TriggerChains(level)); }

        [McpServerTool(Name = "object_links"), Description("An object's links as the properties panel shows them: what a trap or trigger does, its chain (next, previous), what links to it or runs it, and the squares it acts on.")]
        public CallToolResult ObjectLinks([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index)
        {
            return Answer(() => ed.ObjectLinks(level, index));
        }

        [McpServerTool(Name = "object_link"), Description("Links a trigger, trap, or a lever/switch/button/chain (through the use trigger it holds, made when it has none) to another object that stays where it lies, as the shipped levels do; a trigger's target square becomes that object's square, and what it ran before follows the new trap. One undo step.")]
        public CallToolResult ObjectLink([Description("Level number, from 1.")] int level, [Description("The trigger, trap or lever (#).")] int from, [Description("The object it runs (#).")] int to)
        {
            return Answer(() => ed.ObjectLink(level, from, to));
        }

        [McpServerTool(Name = "object_unlink"), Description("Takes a trigger's or trap's link off (the object it named stays where it lies). One undo step.")]
        public CallToolResult ObjectUnlink([Description("Level number, from 1.")] int level, [Description("The trigger or trap (#).")] int from)
        {
            return Answer(() => ed.ObjectUnlink(level, from));
        }

        [McpServerTool(Name = "trap_target_set"), Description("Edit > Choose the selected trap's target: the square a trigger, teleport trap or delete trap acts on (its quality and owner); a teleport trap may lead to another level. One undo step.")]
        public CallToolResult TrapTargetSet([Description("Level number, from 1.")] int level, [Description("The trigger or trap (#).")] int index,
            [Description("0-63.")] int x, [Description("0-63.")] int y, [Description("A teleport's destination level (default this one).")] int? toLevel = null)
        {
            return Answer(() => ed.TrapTargetSet(level, index, x, y, toLevel));
        }

        [McpServerTool(Name = "teleport_target"), Description("Edit > Go to the teleport's target: the level and square a teleport trap (or a trigger running one) sends the player to.")]
        public CallToolResult TeleportTarget([Description("Level number, from 1.")] int level, [Description("The teleport trap or its trigger (#).")] int index)
        {
            return Answer(() => ed.TeleportTarget(level, index));
        }

        [McpServerTool(Name = "terrain_changes"), Description("Tools > Preview changed terrain: a level's change-terrain traps (UW2 also height oscillators), numbered, with what runs each and the squares it changes.")]
        public CallToolResult TerrainChanges([Description("Level number, from 1.")] int level) { return Answer(() => ed.TerrainChanges(level)); }

        [McpServerTool(Name = "terrain_preview"), Description("The 2D map of a level as it would look after its change-terrain traps ran (all, or numbers from terrain_changes), and the squares that change. The level itself is not changed.")]
        public CallToolResult TerrainPreview([Description("Level number, from 1.")] int level, [Description("\"all\" or numbers, e.g. \"1, 3\".")] string which = "all",
            [Description("Pixels a tile, 4-32 (default 8).")] int cell = 8)
        {
            string said = null;
            return EdPicture(() => said, () => ed.TerrainPreviewPng(level, which, cell, out said));
        }

        [McpServerTool(Name = "keys_list"), Description("Tools > Keys and locks: every lock number with its keys and locks across the levels (where each lies, locked or not) and the keys' description; give number for one.")]
        public CallToolResult KeysList([Description("One lock number (leave out for all).")] int? number = null) { return Answer(() => ed.KeysList(number)); }

        [McpServerTool(Name = "key_text_set"), Description("The description of the keys of one lock number (\"a key to the ...\": STRINGS.PAK block 5, string 0x64 + the number). Not written until save.")]
        public CallToolResult KeyTextSet([Description("The lock number.")] int number, [Description("The description.")] string text)
        {
            return Answer(() => ed.KeyTextSet(number, text));
        }
    }
}
