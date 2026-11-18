using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>The saved games, player and globals tools' hooks: they work on what the main window holds.</summary>
    public sealed partial class McpWindow
    {
        partial void HooksSaves(McpEditor ed, MainViewModel vm)
        {
            ed.HostSvPlayer = main.McpPlayerSheet;
            ed.HostSvPlayerEdited = main.McpPlayerEdited;
            ed.HostSvGlobals = main.McpGlobals;
            ed.HostSvConversations = () => vm.Session == null ? null : main.Conversations();
            ed.HostSvOpen = main.McpOpen;
            ed.HostSvUnsaved = main.UnsavedParts;
            ed.HostSvCheckpoints = () => main.CheckpointStore;
            ed.HostSvWriteCheckpoint = label => main.WriteCheckpoint(label, true);
            ed.HostSvRestoreCheckpoint = main.RestoreCheckpoint;
            ed.HostSvCheckpointSettings = (minutes, copies) =>
            {
                if (minutes.HasValue) main.CheckpointMinutes = minutes.Value;
                if (copies.HasValue) main.CheckpointCopies = copies.Value;
                return "The editor keeps a checkpoint " + (main.CheckpointMinutes == 0 ? "only when asked" : "every " + main.CheckpointMinutes + " minute(s) of unsaved work") + ", the newest " + main.CheckpointCopies + ".";
            };
            ed.HostSvRestoreAll = main.McpRestoreAll;
            ed.HostSvRepaired = () => { vm.FlushRefresh(); vm.RaiseEditState(); main.UpdateRunMarker(); };
        }
    }
}
