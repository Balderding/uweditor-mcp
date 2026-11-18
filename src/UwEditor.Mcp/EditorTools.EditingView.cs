using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: the app's selection and view, and the 3D picture (McpEditor.EditingView.cs).</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "editor_selection"), Description("In the editor's window: the level shown, the selected object and tile, the tiles chosen together and what the editor has copied, so you can act on what the user points at.")]
        public CallToolResult EditorSelection() { return Answer(() => ed.EditorSelectionGet()); }

        [McpServerTool(Name = "editor_show"), Description("In the editor's window: shows a level with a square (or an object) selected and the view on it (View > Go to a square). This moves the user's view: use it only when the user asks to be shown something.")]
        public CallToolResult EditorShow([Description("Level number, from 1.")] int level, [Description("0-63.")] int? x = null, [Description("0-63.")] int? y = null,
            [Description("An object to select (#).")] int? index = null)
        {
            return Answer(() => ed.EditorShow(level, x, y, index));
        }

        [McpServerTool(Name = "view_picture"), Description("The level in 3D as the editor's view draws it (textured walls, floors and ceiling, the objects as models and pictures): from a square, first person at eye height looking along a heading; with distance, from that far away looking at the square; with fromEditor, as the editor's 3D view looks now. Works without the window.")]
        public CallToolResult ViewPicture([Description("Level number, from 1 (ignored with fromEditor).")] int level = 1,
            [Description("The square, 0-63.")] int? x = null, [Description("The square, 0-63.")] int? y = null,
            [Description("Which way it looks, degrees clockwise from north (0 north, 90 east).")] double heading = 0,
            [Description("Degrees down (negative: up), -80 to 89.")] double pitch = 0,
            [Description("First person: the eye's height above the floor in floor steps (default 2.5).")] double? eyeHeight = null,
            [Description("Look at the square from this many tiles away (0.5-120) instead of standing on it.")] double? distance = null,
            [Description("Field of view in degrees, 20-120 (default 60).")] double fov = 60,
            [Description("Pixels wide, 64-1600.")] int width = 800, [Description("Pixels high, 64-1200.")] int height = 500,
            [Description("Draw the ceiling (default true).")] bool ceiling = true, [Description("Draw the objects (default true).")] bool objects = true,
            [Description("Draw invisible objects too.")] bool invisible = false, [Description("Draw traps and triggers too.")] bool traps = false,
            [Description("Doors, furniture and levers as 3D models (default true).")] bool models = true,
            [Description("As the editor's 3D view looks now (its camera and what it shows).")] bool fromEditor = false)
        {
            string said = null;
            return EdPicture(() => said, () => ed.ViewPicturePng(level, x, y, heading, pitch, eyeHeight, distance, fov, width, height, ceiling, objects, invisible, traps, models, fromEditor, out said));
        }
    }
}
