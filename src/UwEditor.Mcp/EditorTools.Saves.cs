using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The tools for saved games, checkpoints, the saved game check, backups and GOG's image (McpEditor.Saves*.cs).</summary>
    public sealed partial class EditorTools
    {
        // ---- saved games

        [McpServerTool(Name = "save_list"), Description("The game's saved games SAVE1-SAVE4: description (as the game's menu shows it), files, when written, which one is open.")]
        public CallToolResult SaveList() { return Answer(() => ed.SaveList()); }

        [McpServerTool(Name = "save_open"), Description("Opens saved game 1-4 (its levels, player and globals) in place of the open game, or 0 for DATA (a new game's levels). Refuses while there is unsaved work, unless discard is true.")]
        public CallToolResult SaveOpen([Description("1-4, or 0 for DATA.")] int slot, [Description("True drops the unsaved work.")] bool discard = false)
        {
            return Answer(() => ed.SaveOpen(slot, discard));
        }

        [McpServerTool(Name = "save_rename"), Description("Changes a saved game's description (DESC: printable ASCII, up to 30 characters; the old one kept as DESC.BKP).")]
        public CallToolResult SaveRename([Description("1-4.")] int slot, [Description("The new description.")] string description)
        {
            return Answer(() => ed.SaveRename(slot, description));
        }

        [McpServerTool(Name = "save_copy"), Description("Copies a saved game onto another slot, as the game copies a whole folder. A slot holding a save is replaced only with confirm true (the old one kept as SAVEn.BKP).")]
        public CallToolResult SaveCopy([Description("The slot copied, 1-4.")] int from, [Description("The slot written, 1-4.")] int to, [Description("True replaces a saved game in the target slot.")] bool confirm = false)
        {
            return Answer(() => ed.SaveCopy(from, to, confirm));
        }

        [McpServerTool(Name = "save_create"), Description("Makes a new saved game in a slot: the levels open now (with their unsaved edits), the editor's default character on the new-game start square of level 1, the conversations' globals as a new game has them. A slot holding a save is replaced only with confirm true (kept as SAVEn.BKP).")]
        public CallToolResult SaveCreate([Description("1-4.")] int slot, [Description("The description (up to 30 characters).")] string description = "Editor",
            [Description("True replaces a saved game in the slot.")] bool confirm = false)
        {
            return Answer(() => ed.SaveCreate(slot, description, confirm));
        }

        [McpServerTool(Name = "save_export"), Description("File > Export as a saved game: the open levels (with their unsaved edits) and the player (as edited) written as a saved game: in a slot of this game (1-4), of Underworld Exhumed (exhumed 1-4), or a folder. A folder holding files is replaced only with confirm true.")]
        public CallToolResult SaveExport([Description("1-4, exhumed 1-4, or a folder path.")] string where, [Description("The description (default: Editor and the time).")] string description = null,
            [Description("True replaces the files of a saved game there.")] bool confirm = false)
        {
            return Answer(() => ed.SaveExport(where, description, confirm));
        }

        [McpServerTool(Name = "save_check"), Description("File > Check and repair the saved game: finds what makes the original game crash or complain (map notes read from another block, notes with no end, automap blocks of the wrong size, objects lost from the object lists), numbered, each with its repair.")]
        public CallToolResult SaveCheck() { return Answer(() => ed.SaveCheckRun()); }

        [McpServerTool(Name = "save_repair"), Description("Repairs the findings of save_check (by number; leave out for all) as unsaved level edits; save writes them.")]
        public CallToolResult SaveRepair([Description("Finding numbers from save_check; leave out for all.")] int[] numbers = null)
        {
            return Answer(() => ed.SaveRepair(numbers));
        }

        // ---- checkpoints, restore

        [McpServerTool(Name = "checkpoint_list"), Description("The checkpoints (copies of unsaved work the editor keeps every few minutes) of the open game, newest first, numbered from 1.")]
        public CallToolResult CheckpointList() { return Answer(() => ed.CheckpointList()); }

        [McpServerTool(Name = "checkpoint_make"), Description("Keeps a checkpoint of the unsaved work now (changed levels, the player, automaps; in the editor's window every kind it keeps). Nothing in the game folder is written.")]
        public CallToolResult CheckpointMake([Description("A label for it.")] string label = null) { return Answer(() => ed.CheckpointMake(label)); }

        [McpServerTool(Name = "checkpoint_restore"), Description("Loads a checkpoint back as unsaved, undoable edits (nothing in the game folder is written). Replacing unsaved player edits (no undo) needs confirm true.")]
        public CallToolResult CheckpointRestore([Description("Its number in checkpoint_list (1 the newest).")] int number, [Description("True replaces unsaved player edits.")] bool confirm = false)
        {
            return Answer(() => ed.CheckpointRestore(number, confirm));
        }

        [McpServerTool(Name = "checkpoint_delete"), Description("Deletes one checkpoint (the editor's copy, not a game file). Needs confirm true.")]
        public CallToolResult CheckpointDelete([Description("Its number in checkpoint_list.")] int number, [Description("True deletes it.")] bool confirm = false)
        {
            return Answer(() => ed.CheckpointDelete(number, confirm));
        }

        [McpServerTool(Name = "checkpoint_settings"), Description("How often the editor keeps a checkpoint (minutes, 0 none) and how many it keeps; give a value to change it.")]
        public CallToolResult CheckpointSettings([Description("0-240.")] int? minutes = null, [Description("1-1000.")] int? copies = null)
        {
            return Answer(() => ed.CheckpointSettings(minutes, copies));
        }

        [McpServerTool(Name = "restore_all"), Description("File > Restore all: drops every unsaved change and reads the game again from its files; a checkpoint keeps the dropped work first. Without confirm true it only says what is unsaved.")]
        public CallToolResult RestoreAll([Description("True drops the unsaved work.")] bool confirm = false) { return Answer(() => ed.RestoreAll(confirm)); }

        [McpServerTool(Name = "backup_list"), Description("The game folder's files that have the copies the editor keeps: NAME.EXT.ORIG (the file before the editor first wrote it) and NAME.EXT.BKP (the version before the last save).")]
        public CallToolResult BackupList() { return Answer(() => ed.BackupList()); }

        [McpServerTool(Name = "backup_restore"), Description("Puts one file back from its .ORIG or .BKP copy (the file now kept as .BKP), then reads the game again. Refused while there is unsaved work; overwrites the file, so it needs confirm true (without it, says what it would do).")]
        public CallToolResult BackupRestore([Description("The file as backup_list names it (DATA\\LEV.ARK, SAVE1\\PLAYER.DAT).")] string file,
            [Description("orig or bkp.")] string from = "bkp", [Description("True overwrites the file.")] bool confirm = false)
        {
            return Answer(() => ed.BackupRestore(file, from, confirm));
        }

        [McpServerTool(Name = "backup_restore_all"), Description("Puts every file the editor changed back to its .ORIG copy (the game as first found), then reads the game again. Refused while there is unsaved work; needs confirm true (without it, lists the files).")]
        public CallToolResult BackupRestoreAll([Description("True overwrites the files.")] bool confirm = false) { return Answer(() => ed.BackupRestoreAll(confirm)); }

        // ---- GOG

        [McpServerTool(Name = "gog_info"), Description("What GOG's game.gog (the CD image GOG's installs play) holds: the games, its SHA-256, whether the editor knows the release.")]
        public CallToolResult GogInfo([Description("game.gog, or the GOG install folder.")] string image) { return Answer(() => ed.GogInfo(image)); }

        [McpServerTool(Name = "gog_extract"), Description("File > Open GOG's game.gog: copies both games out of the image into an empty folder outside the GOG install (UW and UW2 in it); the install is left as it is. open picks the game to open afterwards.")]
        public CallToolResult GogExtract([Description("game.gog, or the GOG install folder.")] string image, [Description("An empty folder for the copies.")] string folder,
            [Description("True copies from an image the editor does not know.")] bool unknownRelease = false, [Description("uw1, uw2 or none (default).")] string open = null)
        {
            return Answer(() => ed.GogExtract(image, folder, unknownRelease, open));
        }
    }
}
