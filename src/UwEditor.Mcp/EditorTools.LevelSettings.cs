using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The tools for level-wide and game-wide settings: texture slots, clearing and making levels, the start square, tile flags, the automap.</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "level_textures"), Description("A level's texture slots (UW1: wall slots 0-47, floor slots 0-9 with 9 the ceiling, door slots 0-5; UW2: 64 slots, the first 16 also floors): each slot's global texture number, its terrain from TERRAIN.DAT (water, lava, ...) and how many open tiles use it. A tile's Wall texture / Floor texture is a slot here.")]
        public CallToolResult LevelTextures([Description("Level number, from 1.")] int level,
            [Description("wall, floor or door; leave out for all.")] string kind = null)
        {
            return Answer(() => ed.LevelTextures(level, kind));
        }

        [McpServerTool(Name = "level_texture_set"), Description("Points one of a level's texture slots at another global texture (as Level > Level textures): every tile using the slot shows it. One undo step; written with the level by save.")]
        public CallToolResult LevelTextureSet([Description("Level number, from 1.")] int level,
            [Description("wall, floor or door (UW2: wall or door).")] string kind,
            [Description("The slot: UW1 wall 0-47, floor 0-9 (9 the ceiling), door 0-5.")] int slot,
            [Description("The global texture number (textures_find lists them).")] int texture)
        {
            return Answer(() => ed.LevelTextureSet(level, kind, slot, texture));
        }

        [McpServerTool(Name = "textures_find"), Description("The game's wall, floor or door textures with their terrain class from TERRAIN.DAT; give terrain (e.g. lava, water, stairs) to list only those. UW1 floors 23-25 are lava, 16, 17 and 32-34 water.")]
        public CallToolResult TexturesFind([Description("wall, floor or door.")] string kind,
            [Description("A word of the terrain class to look for (lava, water, ice, stairs, door, ...); leave out for all.")] string terrain = null)
        {
            return Answer(() => ed.TexturesFind(kind, terrain));
        }

        [McpServerTool(Name = "level_clear"), Description("Level > Clear the level: removes every object (creatures, items, traps, triggers; UW2 also animations and timers), the free lists as in a new level; with tilesToo every tile also becomes solid rock (textures kept). One undo step; written by save.")]
        public CallToolResult LevelClear([Description("Level number, from 1.")] int level,
            [Description("Also make every tile solid rock.")] bool tilesToo = false)
        {
            return Answer(() => ed.LevelClear(level, tilesToo));
        }

        [McpServerTool(Name = "level_make"), Description("Level > Make this slot a level: an empty level slot (the game cannot enter it) becomes a level with no objects, a texture map copied from its world, and a 5x5 open room at (30, 30)-(34, 34) or all rock. One undo step; written by save.")]
        public CallToolResult LevelMake([Description("Level number of the empty slot, from 1.")] int level,
            [Description("True (default): a small open room in the middle; false: all rock.")] bool room = true)
        {
            return Answer(() => ed.LevelMake(level, room));
        }

        [McpServerTool(Name = "start_get"), Description("Where a new game starts (always level 1; the square is stored in the game's executable) and, in a saved game, where the player is.")]
        public CallToolResult StartGet() { return Answer(() => ed.StartGet()); }

        [McpServerTool(Name = "start_set"), Description("Moves the new-game start square on level 1 to an open tile. Patches UW.EXE / UW2.EXE at once (the old file kept as .BKP, the first as .ORIG): not an undo step and not part of save. Refuses when there is no executable beside DATA.")]
        public CallToolResult StartSet([Description("0-63, west to east.")] int x, [Description("0-63, south to north.")] int y)
        {
            return Answer(() => ed.StartSet(x, y));
        }

        [McpServerTool(Name = "fill_fields"), Description("Sets the tile flags fill leaves out on a rectangle of tiles (corners in any order): Light (0-3, bit 0 the light flag), No magic, Door (keeps an open door open). One undo step; leave out what should stay. Not saved until save.")]
        public CallToolResult FillFields([Description("Level number, from 1.")] int level,
            [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("The other corner, 0-63.")] int x2, [Description("The other corner, 0-63.")] int y2,
            [Description("The Light field, 0-3 (bit 0 the light flag).")] int? light = null,
            [Description("Magic does not work on the tile.")] bool? noMagic = null,
            [Description("The door bit: an open door on the tile is never closed by the game.")] bool? door = null,
            [Description("Only the open tiles of the rectangle (solid rock left alone).")] bool openOnly = false)
        {
            return Answer(() => ed.FillFields(level, x1, y1, x2, y2, light, noMagic, door, openOnly));
        }

        [McpServerTool(Name = "light_flags_all"), Description("Sets the light flag on every open square of a level, or clears it everywhere (UW2 applies DL.DAT's minimum light by it). One undo step.")]
        public CallToolResult LightFlagsAll([Description("Level number, from 1.")] int level, [Description("True on, false off.")] bool on)
        {
            return Answer(() => ed.LightFlagsAll(level, on));
        }

        [McpServerTool(Name = "automap_notes"), Description("A level's automap (the player's map): how many tiles are marked seen, and its notes, numbered, with their tiles.")]
        public CallToolResult AutomapNotes([Description("Level number, from 1.")] int level) { return Answer(() => ed.AutomapNotes(level)); }

        [McpServerTool(Name = "automap_all"), Description("Marks every open tile of a level's automap seen (as the game writes them) or clears it. Undone with automap_undo (the automap has its own undo); written by save.")]
        public CallToolResult AutomapAll([Description("Level number, from 1.")] int level, [Description("True all seen, false none.")] bool seen = true)
        {
            return Answer(() => ed.AutomapAll(level, seen));
        }

        [McpServerTool(Name = "automap_note_add"), Description("Adds a note to a level's automap on a tile. The text is kept as the game can draw it: capitals, at most 49 characters. Written by save.")]
        public CallToolResult AutomapNoteAdd([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("The note's text.")] string text)
        {
            return Answer(() => ed.AutomapNoteAdd(level, x, y, text));
        }

        [McpServerTool(Name = "automap_note_delete"), Description("Deletes a note of a level's automap by its number (from automap_notes). Written by save.")]
        public CallToolResult AutomapNoteDelete([Description("Level number, from 1.")] int level, [Description("The note's number.")] int index)
        {
            return Answer(() => ed.AutomapNoteDelete(level, index));
        }

        [McpServerTool(Name = "automap_undo"), Description("Takes back the last change to a level's automap or its notes (the automap's own undo, apart from the level's).")]
        public CallToolResult AutomapUndo([Description("Level number, from 1.")] int level) { return Answer(() => ed.AutomapUndo(level)); }
    }
}
