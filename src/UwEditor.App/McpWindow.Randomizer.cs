using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Randomizer;
using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>
    /// The randomizer and Run and Test tools' hooks: the editor's zoning (the Zones window's, so its
    /// overlay follows), the open randomizer windows' settings, the status line after a run, and
    /// the settings of Programs and paths. Nothing here opens a window or starts a program.
    /// </summary>
    public sealed partial class McpWindow
    {
        partial void HooksRandomizer(McpEditor ed, MainViewModel vm)
        {
            ed.HostZoning = () =>
            {
                var z = vm.Session == null ? null : main.Zoning();
                return z == null ? null : new McpZoning(z);
            };
            ed.HostRandomizerSettings = id => RandomizerWindowSettings(id);
            ed.HostRandomizerApplied = status => main.RandomizerApplied(status);
            ed.HostStringsChanged = status => main.MantrasChanged(status);
            ed.HostRun = new RandomizerRunHost
            {
                DosBoxPath = () => main.DosBoxPath, SetDosBoxPath = v => main.DosBoxPath = v,
                DosBoxArgs = () => main.DosBoxArgs, SetDosBoxArgs = v => main.DosBoxArgs = v,
                ExhumedArgs = () => main.ExhumedArgs, SetExhumedArgs = v => main.ExhumedArgs = v,
                ExhumedChosen = main.ExhumedChosen, SetExhumedPort = main.SetExhumedPort,
                ExhumedSettingsText = main.ExhumedSettingsText, SetExhumedSettingsText = main.SetExhumedSettingsText,
                Thoroughness = () => main.ExhumedThoroughness, SetThoroughness = o => main.ExhumedThoroughness = o,
            };
        }

        /// <summary>The settings text of the randomizer window open for a tool (its id), or null.</summary>
        string RandomizerWindowSettings(string id)
        {
            T Open<T>() where T : class { return main.OwnedWindows.OfType<T>().FirstOrDefault(); }
            switch (id)
            {
                case "key-scatter": return main.OpenKeyShuffle?.SettingsText(false);
                case "auto-zones": return main.OpenAutoZones?.SettingsText(false);
                case "item-shuffle": return Open<ItemShuffleWindow>()?.SettingsText(false);
                case "item-swap": return Open<ItemSwapWindow>()?.SettingsText(false);
                case "monster-swap": return Open<MonsterSwapWindow>()?.SettingsText(false);
                case "challenges": return Open<ChallengesWindow>()?.SettingsText(false);
                case "mantras": return Open<MantrasWindow>()?.SettingsText(false);
                case "vowel-play": return Open<VowelPlayWindow>()?.SettingsText(false);
            }
            return null;
        }

        /// <summary>The app's zoning as the MCP tools see it.</summary>
        sealed class McpZoning : IRandomizerZoning
        {
            readonly ZoningState z;
            public McpZoning(ZoningState z) { this.z = z; }
            public GameSession Session { get { return z.Session; } }
            public ZoneSet Set { get { return z.Set; } }
            public string Path { get { return z.Path; } }
            public bool Dirty { get { return z.Dirty; } }
            public bool HasZones { get { return z.HasZones; } }
            public bool AskForName { get { return z.AskForName; } }
            public bool SplitByLevel { get { return z.SplitByLevel; } set { z.SplitByLevel = value; } }
            public int CurrentZone { get { return z.CurrentZone; } set { z.CurrentZone = value; } }
            public int MaxClimb { get { return z.MaxClimb; } }
            public ZoneSet ForSolver(out string note) { return z.ForSolver(out note); }
            public Zone AddZone(string name, int level1) { return z.AddZone(name, level1); }
            public void Rename(int id, string name) { z.Rename(id, name); }
            public void SetColor(int id, string hex) { z.SetColor(id, hex); }
            public void SetNotes(int id, string notes) { z.SetNotes(id, notes); }
            public void RemoveZone(int id) { z.RemoveZone(id); }
            public void EnsureLevel(int level1) { z.EnsureLevel(level1); }
            public bool PaintTile(int level1, int x, int y, bool start) { return z.PaintTile(level1, x, y, start); }
            public int FillArea(int level1, int x, int y, int zone) { return z.FillArea(level1, x, y, zone); }
            public bool CanUndo { get { return z.CanUndo; } }
            public void Undo() { z.Undo(); }
            public ZoneLink Connect(int from, int to, bool twoWay) { return z.Connect(from, to, twoWay); }
            public void LinkChanged() { z.LinkChanged(); }
            public void RemoveLink(ZoneLink k) { z.RemoveLink(k); }
            public void Mark(int level1, int index, bool? important) { z.Mark(level1, index, important); }
            public List<KeyItem> Items { get { return z.Items; } }
            public List<DoorPassage> Doors { get { return z.Doors; } }
            public void Save(string path) { z.Save(path); }
            public void Open(string path) { z.Open(path); }
            public void NewFile() { z.NewFile(); }
            public void Adopt(ZoneSet set, string path) { z.Adopt(set, path); }
            public bool LoadExample() { z.LoadExample(); return true; }
        }
    }
}
