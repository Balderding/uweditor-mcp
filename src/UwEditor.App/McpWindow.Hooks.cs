using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>
    /// The window's hooks for the MCP tool groups: each group may implement its partial method in its
    /// own file (McpWindow.Editing.cs, McpWindow.Saves.cs, McpWindow.Content.cs, McpWindow.Randomizer.cs)
    /// to hand McpEditor what the app holds (open windows, the view, the selection).
    /// </summary>
    public sealed partial class McpWindow
    {
        void AttachHooks(McpEditor ed, MainViewModel vm)
        {
            HooksEditing(ed, vm);
            HooksSaves(ed, vm);
            HooksContent(ed, vm);
            HooksRandomizer(ed, vm);
        }

        partial void HooksEditing(McpEditor ed, MainViewModel vm);
        partial void HooksSaves(McpEditor ed, MainViewModel vm);
        partial void HooksContent(McpEditor ed, MainViewModel vm);
        partial void HooksRandomizer(McpEditor ed, MainViewModel vm);
    }
}
