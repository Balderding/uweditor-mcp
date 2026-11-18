using System;
using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The MCP tools: each one calls McpEditor and answers its text. A refusal (a bad value, no game, a
    /// level the game could not load) is an error result starting "Refused:", so a client can tell it
    /// from a change made; anything unexpected is an error result naming what failed.
    /// </summary>
    [McpServerToolType]
    public sealed class EditorTools
    {
        readonly McpEditor ed;

        public EditorTools(McpEditor editor) { ed = editor; }

        /// <summary>After each call: the tool's name and its answer (for a window showing what was done).</summary>
        public event Action<string, string> Called;

        /// <summary>Checked before each call: a refusal to answer instead (the host stopped), or null.</summary>
        public Func<string> Gate { get; set; }

        CallToolResult Answer(Func<string> f, [System.Runtime.CompilerServices.CallerMemberName] string method = null)
        {
            string answer;
            bool error = false;
            // the SDK may run calls at the same time; the editor's levels are one at a time. Every failure
            // is caught inside the runner, so none crosses the host's thread (the editor's UI thread).
            lock (ed)
            {
                try
                {
                    answer = ed.Run(() =>
                    {
                        string refused = Gate?.Invoke();
                        if (refused != null) { error = true; return refused; }
                        try { return f(); }
                        catch (Exception e) when (e is McpEditorException || e is ArgumentException || e is InvalidOperationException || e is System.IO.IOException || e is UnauthorizedAccessException)
                        {
                            error = true;
                            return "Refused: " + e.Message;
                        }
                        catch (Exception e)
                        {
                            error = true;
                            return "Failed: " + e.GetType().Name + ": " + e.Message;
                        }
                    });
                }
                catch (Exception e)
                {
                    error = true;
                    answer = "Failed: " + e.GetType().Name + ": " + e.Message;
                }
            }
            Called?.Invoke(ToolName(method), answer);
            return Result(answer, error);
        }

        static CallToolResult Result(string text, bool error)
        {
            return new CallToolResult { Content = new List<ContentBlock> { new TextContentBlock { Text = text ?? "" } }, IsError = error ? true : (bool?)null };
        }

        /// <summary>The text of a tool's answer (its first text block).</summary>
        public static string Text(CallToolResult r)
        {
            foreach (var c in r.Content) if (c is TextContentBlock t) return t.Text;
            return null;
        }

        /// <summary>The tool's name from its method's (LevelMap: level_map).</summary>
        public static string ToolName(string method)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in method ?? "")
            {
                if (char.IsUpper(c) && b.Length > 0) b.Append('_');
                b.Append(char.ToLowerInvariant(c));
            }
            return b.ToString();
        }

        [McpServerTool(Name = "open"), Description("Opens an Ultima Underworld game folder (the one holding DATA) and lists its level slots. Refuses while the open game has unsaved changes, unless discard is true.")]
        public CallToolResult Open([Description("The game folder.")] string folder,
            [Description("True drops unsaved changes of the game open now; without it, open refuses while there are any.")] bool discard = false)
        {
            return Answer(() => ed.Open(folder, discard));
        }

        [McpServerTool(Name = "level_map"), Description("A level as text: a 64x64 grid of tile types (# solid, . open, 1-4 diagonals, n s e w slopes), then one of floor heights (0-f). North (y 63) is the top row, x 0 the left.")]
        public CallToolResult LevelMap([Description("Level number, from 1.")] int level) { return Answer(() => ed.LevelMap(level)); }

        [McpServerTool(Name = "tile_get"), Description("One tile's fields: type, floor height, light, floor and wall texture, no magic, door, and its object count.")]
        public CallToolResult TileGet([Description("Level number, from 1.")] int level, [Description("0-63, west to east.")] int x, [Description("0-63, south to north.")] int y)
        {
            return Answer(() => ed.TileGet(level, x, y));
        }

        [McpServerTool(Name = "tile_set"), Description("Sets one field of a tile: Type (0 solid, 1 open, 2-5 diagonal open SE SW NE NW, 6-9 slope rising N S E W), Floor height (0-15, objects on it move with it), Light, Floor texture, No magic, Door, Wall texture. Not saved until save.")]
        public CallToolResult TileSet([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("The field's name.")] string field, [Description("The new value.")] int value)
        {
            return Answer(() => ed.TileSet(level, x, y, field, value));
        }

        [McpServerTool(Name = "fill"), Description("Sets a rectangle of tiles at once (corners in any order), one undo step; when one value is wrong nothing changes. Leave out what should stay as it is. Not saved until save.")]
        public CallToolResult Fill([Description("Level number, from 1.")] int level,
            [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("The other corner, 0-63.")] int x2, [Description("The other corner, 0-63.")] int y2,
            [Description("Tile type: a map character (# . 1-4 n s e w), solid or open.")] string type = null,
            [Description("Floor height 0-15.")] int? height = null,
            [Description("Index into the level's floor textures.")] int? floorTexture = null,
            [Description("Index into the level's wall textures.")] int? wallTexture = null)
        {
            return Answer(() => ed.Fill(level, x1, y1, x2, y2, type, height, floorTexture, wallTexture));
        }

        [McpServerTool(Name = "item_find"), Description("Items by number (0x1A2 or 418) or by a part of their name: number and name, one a line.")]
        public CallToolResult ItemFind([Description("A number or a part of a name.")] string text) { return Answer(() => ed.ItemFind(text)); }

        [McpServerTool(Name = "object_list"), Description("The objects on a tile (give x and y), or on every tile of the level (the first 300): index, item, name, tile, fine position, z, quality, owner, link; contents indented under their container.")]
        public CallToolResult ObjectList([Description("Level number, from 1.")] int level, [Description("0-63, or leave out for the whole level.")] int? x = null, [Description("0-63, or leave out.")] int? y = null)
        {
            return Answer(() => ed.ObjectList(level, x, y));
        }

        [McpServerTool(Name = "object_add"), Description("Adds an item on an open tile with the editor's defaults for it (a creature, a container, a lock get theirs). The item by number or exact name; several matching are listed and nothing is added. Not saved until save.")]
        public CallToolResult ObjectAdd([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("Item number (0x0B0 or 176) or name.")] string item,
            [Description("0-63.")] int? quality = null, [Description("0-63.")] int? owner = null,
            [Description("Height 0-127 (8 a floor step); leave out for on the floor.")] int? z = null,
            [Description("0-7 (45 degrees a step).")] int? heading = null,
            [Description("Fine position in the tile, 0-7 west to east.")] int? fineX = null,
            [Description("Fine position in the tile, 0-7 south to north.")] int? fineY = null)
        {
            return Answer(() => ed.ObjectAdd(level, x, y, item, quality, owner, z, heading, fineX, fineY));
        }

        [McpServerTool(Name = "object_move"), Description("Moves an object lying on a tile to another open tile, keeping its height above the floor.")]
        public CallToolResult ObjectMove([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("0-63.")] int x, [Description("0-63.")] int y, [Description("0-7.")] int? fineX = null, [Description("0-7.")] int? fineY = null)
        {
            return Answer(() => ed.ObjectMove(level, index, x, y, fineX, fineY));
        }

        [McpServerTool(Name = "object_delete"), Description("Deletes an object and everything inside it.")]
        public CallToolResult ObjectDelete([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index)
        {
            return Answer(() => ed.ObjectDelete(level, index));
        }

        [McpServerTool(Name = "recipe_teleport"), Description("Stepping on a square sends the player to a square of this or another level (a move trigger and a teleport trap); with back, a second pair leads back. One undo step a level.")]
        public CallToolResult RecipeTeleport([Description("Level of the square stepped on.")] int level, [Description("0-63.")] int fromX, [Description("0-63.")] int fromY,
            [Description("Level arrived at.")] int toLevel, [Description("0-63.")] int toX, [Description("0-63.")] int toY,
            [Description("Also a pair leading back.")] bool back = false,
            [Description("With back: the square stepped on to go back (default: beside the arrival).")] int? backFromX = null, [Description("With back.")] int? backFromY = null,
            [Description("With back: the square arrived at (default: beside the start).")] int? backToX = null, [Description("With back.")] int? backToY = null)
        {
            return Answer(() => ed.RecipeTeleport(level, fromX, fromY, toLevel, toX, toY, back, backFromX, backFromY, backToX, backToY));
        }

        [McpServerTool(Name = "recipe_lever_door"), Description("A new lever on a wall of a tile that toggles a door (its use trigger runs a door trap on the door's square). One undo step.")]
        public CallToolResult RecipeLeverDoor([Description("Level number, from 1.")] int level, [Description("The door's index (#).")] int doorIndex,
            [Description("The lever's tile, 0-63.")] int wallX, [Description("The lever's tile, 0-63.")] int wallY,
            [Description("Which wall of the tile: auto, north, east, south or west.")] string side = "auto")
        {
            return Answer(() => ed.RecipeLeverDoor(level, doorIndex, wallX, wallY, side));
        }

        [McpServerTool(Name = "recipe_plate"), Description("Stepping on a square runs a trap: damage, or toggling a door. One undo step.")]
        public CallToolResult RecipePlate([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("damage or door.")] string kind, [Description("Damage 1-63 (default 5).")] int? damage = null,
            [Description("For door: the door's index (#).")] int? doorIndex = null)
        {
            return Answer(() => ed.RecipePlate(level, x, y, kind, damage, doorIndex));
        }

        [McpServerTool(Name = "recipe_spawner"), Description("Wandering monsters: a spawner on a square that the game runs by itself every few game minutes, making the creature with the given chance.")]
        public CallToolResult RecipeSpawner([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y,
            [Description("The creature, by number (0x40-0x7F) or name.")] string creature, [Description("Chance a run, percent.")] double chance = 30)
        {
            return Answer(() => ed.RecipeSpawner(level, x, y, creature, chance));
        }

        [McpServerTool(Name = "picture"), Description("The editor's 2D map of a level as a picture (floor textures, walls, objects), the whole level or a rectangle of tiles; north at the top.")]
        public CallToolResult Picture([Description("Level number, from 1.")] int level,
            [Description("A corner, 0-63 (leave all four out for the whole level).")] int? x1 = null, [Description("A corner, 0-63.")] int? y1 = null,
            [Description("The other corner, 0-63.")] int? x2 = null, [Description("The other corner, 0-63.")] int? y2 = null,
            [Description("Pixels a tile, 4-32 (default 8).")] int cell = 8)
        {
            byte[] png = null;
            var r = Answer(() => { png = ed.PicturePng(level, x1, y1, x2, y2, cell); return "A picture of level " + level + "."; });
            if (png == null) return r;
            return new CallToolResult { Content = new List<ContentBlock> { ImageContentBlock.FromBytes(png, "image/png") } };
        }

        [McpServerTool(Name = "undo"), Description("Takes back the last changes made to a level (by you or in the editor), one step at a time.")]
        public CallToolResult Undo([Description("Level number, from 1.")] int level, [Description("How many steps, 1-50 (default 1).")] int steps = 1)
        {
            return Answer(() => ed.Undo(level, steps));
        }

        [McpServerTool(Name = "check"), Description("Runs the game's own checks on a level as it is now (its object lists).")]
        public CallToolResult Check([Description("Level number, from 1.")] int level) { return Answer(() => ed.Check(level)); }

        [McpServerTool(Name = "save"), Description("Writes the changed levels to LEV.ARK (the old file kept as LEV.ARK.BKP); in the editor's window it is the editor's own save (Ctrl+S), which also writes its other unsaved work. Refuses, writing nothing, when a changed level fails the game's checks.")]
        public CallToolResult Save() { return Answer(() => ed.Save()); }
    }
}
