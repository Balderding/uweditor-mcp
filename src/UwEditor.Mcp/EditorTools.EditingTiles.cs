using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: the tile clipboard, prefabs and decoration (McpEditor.EditingTiles.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "tiles_copy"), Description("Edit > Copy tiles with what is on them: a rectangle's tiles (type, height, textures, flags) and the objects there with what they hold, their links and target squares; kept for tiles_paste, tiles_turn, tiles_mirror and prefab_save.")]
        public CallToolResult TilesCopy([Description("Level number, from 1.")] int level, [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("The other corner, 0-63.")] int x2, [Description("The other corner, 0-63.")] int y2)
        {
            return Answer(() => ed.TilesCopy(level, x1, y1, x2, y2));
        }

        [McpServerTool(Name = "tiles_turn"), Description("Edit > Turn the copied tiles: clockwise by quarters, seen from above with north up (slopes, diagonals, objects' places and headings turn with them).")]
        public CallToolResult TilesTurn([Description("Quarters clockwise, 1-3 (default 1).")] int quarters = 1) { return Answer(() => ed.TilesTurn(quarters)); }

        [McpServerTool(Name = "tiles_mirror"), Description("Edit > Mirror the copied tiles: east and west change places.")]
        public CallToolResult TilesMirror() { return Answer(() => ed.TilesMirror()); }

        [McpServerTool(Name = "tiles_clip"), Description("What tiles_copy (or prefab_load) holds now.")]
        public CallToolResult TilesClip() { return Answer(() => ed.TilesClip()); }

        [McpServerTool(Name = "tiles_paste"), Description("Edit > Paste tiles: the copied tiles with their south-west corner on (x, y). Each copied tile replaces the tile there and what lies on it; textures by number where the level's list has them; links and target squares inside the copy point at the new copies. One undo step.")]
        public CallToolResult TilesPaste([Description("Level number, from 1.")] int level, [Description("The south-west corner, 0-63.")] int x, [Description("The south-west corner, 0-63.")] int y)
        {
            return Answer(() => ed.TilesPaste(level, x, y));
        }

        [McpServerTool(Name = "prefab_list"), Description("Edit > Prefabs: the prefabs (saved tile copies, shared with the editor's window) for the open game, with their sizes.")]
        public CallToolResult PrefabList() { return Answer(() => ed.PrefabList()); }

        [McpServerTool(Name = "prefab_save"), Description("Saves the copied tiles as a prefab under a name (one of the same name is replaced); give a level and a rectangle to copy those tiles first.")]
        public CallToolResult PrefabSave([Description("The prefab's name.")] string name,
            [Description("Level to copy from (leave out to save the copied tiles).")] int? level = null,
            [Description("A corner.")] int? x1 = null, [Description("A corner.")] int? y1 = null, [Description("The other corner.")] int? x2 = null, [Description("The other corner.")] int? y2 = null)
        {
            return Answer(() => ed.PrefabSave(name, level, x1, y1, x2, y2));
        }

        [McpServerTool(Name = "prefab_load"), Description("Loads a prefab as the copied tiles (to turn, mirror and tiles_paste).")]
        public CallToolResult PrefabLoad([Description("The prefab's name.")] string name) { return Answer(() => ed.PrefabLoad(name)); }

        [McpServerTool(Name = "prefab_place"), Description("Places a prefab with its south-west corner on (x, y), mirrored and turned first if asked (as tiles_paste; the copied tiles stay as they were). One undo step.")]
        public CallToolResult PrefabPlace([Description("Level number, from 1.")] int level, [Description("The prefab's name.")] string name,
            [Description("The south-west corner, 0-63.")] int x, [Description("The south-west corner, 0-63.")] int y,
            [Description("Quarters clockwise, 0-3.")] int quarters = 0, [Description("Mirror east to west first.")] bool mirror = false)
        {
            return Answer(() => ed.PrefabPlace(level, name, x, y, quarters, mirror));
        }

        [McpServerTool(Name = "decoration_kinds"), Description("The decoration kinds (debris, plants, moss, bones, stalactites) with the game's items for each, and which hang from the ceiling.")]
        public CallToolResult DecorationKinds() { return Answer(() => ed.DecorationKinds()); }

        [McpServerTool(Name = "decorate"), Description("Edit > Decorate the selected tiles: scatters small scenery over the open tiles of a rectangle at random fine places and headings (hanging things from the ceiling), from a seed. One undo step: undo and roll again with another seed or density.")]
        public CallToolResult Decorate([Description("Level number, from 1.")] int level, [Description("A corner.")] int x1, [Description("A corner.")] int y1,
            [Description("The other corner.")] int x2, [Description("The other corner.")] int y2,
            [Description("Kinds, comma separated: debris, plants, moss, bones, stalactites (default all).")] string kinds = null,
            [Description("Objects a hundred tiles, 1-400 (default 15).")] int perHundred = 15,
            [Description("The roll's seed (the same seed gives the same scatter).")] int seed = 1,
            [Description("Leave water and lava bare (default true).")] bool avoidLiquid = true,
            [Description("Leave tiles that hold something bare.")] bool avoidOccupied = false)
        {
            return Answer(() => ed.Decorate(level, x1, y1, x2, y2, kinds, perHundred, seed, avoidLiquid, avoidOccupied));
        }
    }
}
