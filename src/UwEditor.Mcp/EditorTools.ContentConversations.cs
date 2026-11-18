using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Conversations (Tools ▸ Conversations, Simulator, Graph).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "conversation_list"), Description("Every conversation of CNV.ARK: slot, the name it talks under, its strings block, code size, whether changed; and the empty slots. Generic conversations (every creature of a kind) are slots 256 + the creature's class.")]
        public CallToolResult ConversationList([Description("Also list the creatures in the levels that talk through each (reads every level).")] bool users = false)
        {
            return Answer(() => ed.ConversationList(users));
        }

        [McpServerTool(Name = "conversation_get"), Description("One conversation as the editor shows it: view dialogue (the Dialogue tab's source, which conversation_set takes back changed), flat (every instruction's line, read only), script (the Script tab's assembly, for conversation_script_set) or strings (its numbered strings).")]
        public CallToolResult ConversationGet([Description("The slot (conversation_list).")] int slot,
            [Description("dialogue, flat, script or strings (default dialogue).")] string view = "dialogue")
        {
            return Answer(() => ed.ConversationGet(slot, view));
        }

        [McpServerTool(Name = "conversation_users"), Description("The creatures in the levels (as saved) that talk through a slot: level, object index, name and tile. conversation_simulate's talker numbers them.")]
        public CallToolResult ConversationUsers([Description("The slot.")] int slot) { return Answer(() => ed.ConversationUsers(slot)); }

        [McpServerTool(Name = "conversation_check"), Description("Compiles a conversation's source (the dialogue view's language) without applying it: its errors and warnings with line and column, or what would change.")]
        public CallToolResult ConversationCheck([Description("The slot.")] int slot, [Description("The whole source, as conversation_get's dialogue view gives it, changed.")] string source)
        {
            return Answer(() => ed.ConversationCheck(slot, source));
        }

        [McpServerTool(Name = "conversation_set"), Description("Compiles a conversation's source and applies it, as the editor's Apply (Ctrl+Enter): functions whose text did not change keep their code, new strings go at the end of its block. Refused with the errors when it does not compile. Not written until save; conversation_undo takes it back.")]
        public CallToolResult ConversationSet([Description("The slot.")] int slot, [Description("The whole source (conversation_get's dialogue view, changed).")] string source)
        {
            return Answer(() => ed.ConversationSet(slot, source));
        }

        [McpServerTool(Name = "conversation_script_set"), Description("Assembles a conversation's assembly text (conversation_get's script view, changed) into it, as the Script tab's Apply. Not written until save; conversation_undo takes it back.")]
        public CallToolResult ConversationScriptSet([Description("The slot.")] int slot, [Description("The whole assembly text, its .slot line included.")] string script)
        {
            return Answer(() => ed.ConversationScriptSet(slot, script));
        }

        [McpServerTool(Name = "conversation_string_set"), Description("Changes one string of a conversation's block (or adds one at the index after the last), accents written as letters. Not written until save.")]
        public CallToolResult ConversationStringSet([Description("The slot.")] int slot, [Description("The string's index in the block (conversation_get view strings).")] int index,
            [Description("The new text.")] string text)
        {
            return Answer(() => ed.ConversationStringSet(slot, index, text));
        }

        [McpServerTool(Name = "conversation_add"), Description("Gives an empty slot a new conversation (a greeting, one answer and a goodbye, to be changed with conversation_set), and the name it talks under. A creature talks through it when its conversation number (whoami) is the slot. Not written until save.")]
        public CallToolResult ConversationAdd([Description("The slot, 1-255 (leave out for the first empty one).")] int? slot = null,
            [Description("The name the slot talks under (STRINGS.PAK block 7).")] string name = null,
            [Description("The first thing it says.")] string greeting = null, [Description("The player's one answer.")] string answer = null,
            [Description("What it says to that, before the conversation ends.")] string farewell = null)
        {
            return Answer(() => ed.ConversationAdd(slot, name, greeting, answer, farewell));
        }

        [McpServerTool(Name = "conversation_delete"), Description("Takes a slot's conversation out of CNV.ARK (its strings stay); creatures with that conversation number then have nothing to say. Not written until save; conversation_undo takes it back.")]
        public CallToolResult ConversationDelete([Description("The slot.")] int slot) { return Answer(() => ed.ConversationDelete(slot)); }

        [McpServerTool(Name = "conversation_revert"), Description("Puts a conversation and its strings back as last saved, or as shipped (the .ORIG copies the editor kept when it first wrote the files). conversation_undo takes it back.")]
        public CallToolResult ConversationRevert([Description("The slot.")] int slot, [Description("saved or original (default saved).")] string to = "saved")
        {
            return Answer(() => ed.ConversationRevert(slot, to));
        }

        [McpServerTool(Name = "conversation_undo"), Description("Takes back the last change made through these tools to a conversation (set, script, string, add, delete, revert), one step at a time.")]
        public CallToolResult ConversationUndo([Description("The slot.")] int slot) { return Answer(() => ed.ConversationUndo(slot)); }

        [McpServerTool(Name = "conversation_simulate"), Description("Runs a conversation in the editor's simulator (no game, nothing played): what the NPC says and the player answers, feeding the menu choices given in order, until the end, an error, or a menu or typed question with nothing left to feed (its options are listed). Run again with more choices to go on. The world (player, talker's inventory, quest flags) is read from the game as the Simulator window does.")]
        public CallToolResult ConversationSimulate([Description("The slot.")] int slot,
            [Description("Menu choices to pick in order, from 1, comma-separated (\"2,1,3\").")] string choices = null,
            [Description("Answers to typed questions (babl_ask) in order, separated by |.")] string answers = null,
            [Description("The talker: 0 default values, n the n-th creature of conversation_users; leave out for the first one.")] int talker = -1,
            [Description("The player's name (default from the game, or Avatar).")] string playerName = null,
            [Description("The player is female.")] bool? female = null,
            [Description("The random seed.")] int? seed = null,
            [Description("Every random draw gives this value.")] int? fixedRandom = null,
            [Description("The player accepts the NPC's offers (default true).")] bool? offersAccepted = null,
            [Description("The player gives in to demands (default true).")] bool? demandsGivenIn = null,
            [Description("Simulate this source instead of the applied conversation (compiled, not applied).")] string source = null,
            [Description("The most instructions to run, 1000-5000000 (default 500000).")] int maxSteps = 500000,
            [Description("Also list the built-in calls, writes and notes the run made.")] bool log = false)
        {
            return Answer(() => ed.ConversationSimulate(slot, choices, answers, talker, playerName, female, seed, fixedRandom, offersAccepted, demandsGivenIn, source, maxSteps, log));
        }

        [McpServerTool(Name = "conversation_graph"), Description("A conversation's graph as text (Tools ▸ Conversations ▸ Graph): its nodes (says, menus, code; entries and ends marked) with their lines, and each node's edges to others (next, jump, branch with its condition, choice with its text, call).")]
        public CallToolResult ConversationGraph([Description("The slot.")] int slot, [Description("Show each node's lines (default true).")] bool lines = true)
        {
            return Answer(() => ed.ConversationGraph(slot, lines));
        }
    }
}
