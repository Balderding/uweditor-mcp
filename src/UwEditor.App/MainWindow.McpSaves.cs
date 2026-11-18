using System;
using System.Collections.Generic;
using UwEditor.Core;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    /// <summary>
    /// What the MCP tools for saved games, the player and the globals need of the main window
    /// (McpWindow.Saves.cs): the shared player sheet, the open Global variables window, opening a
    /// game without asking, and Restore all without its question. No window is opened here.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The open saved game's player sheet (the one the player window edits), read if needed; null when DATA is open or PLAYER.DAT cannot be read.</summary>
        public PlayerSheet McpPlayerSheet()
        {
            var s = vm.Session;
            if (s == null || !s.IsSave) return null;
            if (playerSheet != null) return playerSheet;
            string path = UltimateEditor.Games.GameProfile.Find(s.SaveFolder, "PLAYER.DAT");
            if (path == null) return null;
            try { playerSheet = PlayerSheet.Load(path, s.Kind, s.DataFolder); }
            catch (Exception) { return null; }
            return playerSheet;
        }

        /// <summary>A tool changed or saved the player: the player window, the markers and the unsaved state follow.</summary>
        public void McpPlayerEdited()
        {
            NoteEdit();
            if (playerWindow != null)
            {
                playerWindow.Model.RaiseAll();
                playerWindow.Model.BuildTree();
                playerWindow.Model.Check();
            }
            UpdateMarkers();
            vm.OnStartMoved();
            vm.RaiseEditState();
            UpdateRunMarker();
        }

        /// <summary>The Global variables window's variables, or null when it is closed.</summary>
        public UwEditor.Core.Conversations.GlobalVariables McpGlobals() { return globalsWindow?.Globals; }

        /// <summary>Opens a game folder or saved game without asking; refuses over unsaved work unless discard.</summary>
        public string McpOpen(string folder, bool discard)
        {
            var parts = UnsavedParts();
            if (!discard && vm.Session != null && parts.Count > 0)
                throw new InvalidOperationException("The editor has unsaved work: " + string.Join("; ", parts) + ". Save first, or give discard true to drop it.");
            var s = GameSession.Open(folder);
            OpenSession(s);
            return "Opened " + (s.IsSave ? System.IO.Path.GetFileName(s.SaveFolder) + (string.IsNullOrEmpty(s.SaveDescription) ? "" : " (\"" + s.SaveDescription + "\")") : "DATA") + " of " + s.Kind + " at " + s.GameFolder + " in the editor.";
        }

        /// <summary>File ▸ Restore all without its question: a checkpoint of the unsaved work, then the game read again; the level and camera stay.</summary>
        public string McpRestoreAll()
        {
            var s = vm.Session;
            if (s == null) return "No game is open.";
            if (UnsavedParts().Count == 0) return "Nothing unsaved: the files and the editor agree.";
            var c = WriteCheckpoint("before Restore all", true);
            int level = vm.Level?.Level ?? 0;
            var cam = View.Camera;
            var target = cam.Target; float distance = cam.Distance, yaw = cam.Yaw, pitch = cam.Pitch;
            OpenSession(GameSession.Open(s.SaveFolder ?? s.GameFolder));
            if (level > 0 && vm.Level?.Level != level) vm.LoadLevel(level);
            View.LookAt(target, distance, yaw, pitch);
            vm.EditStatus = "Restored: every unsaved change was dropped and the files read again (a checkpoint kept them).";
            return "Restored: every unsaved change was dropped and the files read again" + (c != null ? " (checkpoint " + c.Summary + " keeps them)." : ".");
        }
    }
}
