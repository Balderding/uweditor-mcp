using Avalonia.Interactivity;

namespace UwEditor.App
{
    public partial class MainWindow
    {
        McpWindow mcpWindow;

        void OnMcp(object sender, RoutedEventArgs e) { ShowMcp(); }

        /// <summary>Level ▸ Let Claude work here (MCP): the editor's MCP tools on the game open here, while the window is open.</summary>
        public McpWindow ShowMcp(string pipeName = null)
        {
            if (mcpWindow != null) { mcpWindow.Activate(); return mcpWindow; }
            var w = mcpWindow = new McpWindow(this, pipeName);
            w.Closed += (s, a) => mcpWindow = null;
            w.Show(this);
            return w;
        }
    }
}
