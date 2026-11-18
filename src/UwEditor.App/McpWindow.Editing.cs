using System;
using System.Linq;
using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>
    /// The editing group's hooks: the prefabs folder the app keeps, the selection and the 3D view's
    /// camera, showing a square (without bringing the window forward), and the Schedules window's
    /// schedules while it is open.
    /// </summary>
    public sealed partial class McpWindow
    {
        partial void HooksEditing(McpEditor ed, MainViewModel vm)
        {
            ed.PrefabFolder = MainWindow.PrefabFolder;
            ed.HostSelection = () => new EditorSelection
            {
                Level = vm.Level?.Level ?? -1,
                Object = vm.SelectedObject,
                TileX = vm.SelectedTileX,
                TileY = vm.SelectedTileY,
                Tiles = main.ChosenTiles(),
                Copied = main.CopiedObjects == null || vm.Session == null ? null : main.CopiedObjects.Describe(vm.Session.Names) + " (from level " + main.CopiedObjects.FromLevel + ")",
            };
            ed.HostCamera = () =>
            {
                if (vm.Level == null) return null;
                var c = main.View.Camera;
                return new EditorCamera
                {
                    Level = vm.Level.Level, Target = c.Target, Yaw = c.Yaw, Pitch = c.Pitch, Distance = c.Distance, FieldOfView = c.FieldOfView, Orthographic = c.Orthographic,
                    Options = vm.Options,
                };
            };
            ed.HostShow = (level, x, y, obj) =>
            {
                if (vm.Session == null) return "No game is open in the editor.";
                if (vm.Level == null || vm.Level.Level != level) vm.LoadLevel(level);
                if (vm.Level == null) return "The editor could not show level " + level + ".";
                vm.Select(obj, x, y, true);
                if (x >= 0 && y >= 0)
                {
                    var c = main.View.Camera;
                    float floor = (float)vm.Level.FloorAt(x, y, 0.5, 0.5);
                    main.LookAt(x + 0.5f, floor, y + 0.5f, c.Distance, c.Yaw, c.Pitch);
                }
                return "The editor shows level " + level + (x >= 0 ? " at (" + x + ", " + y + ")" : "") + (obj >= 0 ? " with #" + obj + " selected" : "") + ".";
            };
            Func<ScheduleWindow> schedules = () => main.OwnedWindows.OfType<ScheduleWindow>().FirstOrDefault();
            ed.HostSchedules = () => schedules()?.Schedules;
            ed.HostSchedulesEdited = () => { var w = schedules(); if (w != null) w.SelectBlock(w.Block); };
        }
    }
}
