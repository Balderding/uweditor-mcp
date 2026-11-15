using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// The settings row every randomizer window has: Save settings… (a file), Load settings…,
    /// Copy settings (one line, for a chat) and Paste settings. The window gives its settings as
    /// text (RandomizerSettings) and takes text back, setting every control; the row only moves
    /// the text between the window, files and the clipboard, and says in the window's status
    /// line what happened.
    /// </summary>
    public sealed class SettingsBar : StackPanel
    {
        readonly Func<string> fileName;
        readonly Func<bool, string> text;
        readonly Func<string, string> load;
        readonly Action<string> say;

        /// <param name="fileName">The suggested file name (RandomizerSettings.FileName).</param>
        /// <param name="text">The settings on screen now: pretty (false) or on one line (true).</param>
        /// <param name="load">Reads a settings text into the window and its controls; gives the message to show (what was loaded, warnings, or why not).</param>
        /// <param name="say">Shows a message in the window's status line.</param>
        /// <param name="label">A label before the buttons, or null.</param>
        public SettingsBar(Func<string> fileName, Func<bool, string> text, Func<string, string> load, Action<string> say, string label = null)
        {
            this.fileName = fileName;
            this.text = text;
            this.load = load;
            this.say = say;
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            if (label != null) Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 60 });
            var save = new Button { Content = "Save settings…" };
            ToolTip.SetTip(save, "Saves every setting of this window, the seed included, to a file you can share. Load settings… in this window, here or on another computer, sets them all again, so the same game gives the same result.");
            save.Click += async (o, e) => await SaveAsync();
            var open = new Button { Content = "Load settings…" };
            ToolTip.SetTip(open, "Sets every control of this window from a settings file saved with Save settings….");
            open.Click += async (o, e) => await LoadAsync();
            var copy = new Button { Content = "Copy settings" };
            ToolTip.SetTip(copy, "Copies the settings as one line of text, to paste into a chat or a message.");
            copy.Click += async (o, e) => await CopyAsync();
            var paste = new Button { Content = "Paste settings" };
            ToolTip.SetTip(paste, "Sets every control of this window from settings text on the clipboard: the line Copy settings gives, a whole settings file, or the settings line at the top of a spoiler log or report.");
            paste.Click += async (o, e) => await PasteAsync();
            Children.Add(save); Children.Add(open); Children.Add(copy); Children.Add(paste);
        }

        public Button SaveButton { get { return (Button)Children[Children.Count - 4]; } }
        public Button PasteButton { get { return (Button)Children[Children.Count - 1]; } }

        /// <summary>The message after a load: what was loaded, from which editor, and every warning (unknown keys, other levels, missing inputs).</summary>
        public static string Loaded(SettingsRead r, IEnumerable<string> more = null)
        {
            var parts = new List<string> { "Settings loaded" + (r.Seed != null ? " (seed " + r.Seed.Value + ")" : "") + (r.Editor.Length > 0 ? ", written by editor " + r.Editor : "") + "." };
            parts.AddRange(r.Warnings);
            if (more != null) parts.AddRange(more.Where(m => !string.IsNullOrEmpty(m)));
            return string.Join(" ", parts);
        }

        /// <summary>The message when a text is refused.</summary>
        public static string Refused(SettingsRead r) { return "Settings not loaded: " + r.Error; }

        /// <summary>The settings text in what was pasted or opened: the text itself, or the settings line of a log.</summary>
        public static string Pick(string s) { return RandomizerSettings.FromLog(s) is string line && !(s ?? "").TrimStart().StartsWith("{") ? line : s; }

        TopLevel Top { get { return TopLevel.GetTopLevel(this); } }

        async Task SaveAsync()
        {
            var top = Top;
            if (top == null) return;
            var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the settings", SuggestedFileName = fileName(), DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("Randomizer settings") { Patterns = new[] { "*.json" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path == null) return;
            try { File.WriteAllText(path, text(false) + "\n"); say("Settings saved to " + path + "."); }
            catch (Exception ex) { say("Could not save the settings: " + ex.Message); }
        }

        async Task LoadAsync()
        {
            var top = Top;
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load settings", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Randomizer settings") { Patterns = new[] { "*.json", "*.txt" } }, FilePickerFileTypes.All },
            });
            string path = files != null && files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (path == null) return;
            string s;
            try { s = File.ReadAllText(path); }
            catch (Exception ex) { say("Could not read " + path + ": " + ex.Message); return; }
            say(load(Pick(s)));
        }

        async Task CopyAsync()
        {
            var cb = Top?.Clipboard;
            if (cb == null) { say("There is no clipboard here."); return; }
            await cb.SetTextAsync(text(true));
            say("Settings copied as one line of text: paste it into a chat, or into Paste settings in this window.");
        }

        async Task PasteAsync()
        {
            var cb = Top?.Clipboard;
            if (cb == null) { say("There is no clipboard here."); return; }
            string s = await cb.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(s)) { say("The clipboard holds no text. Copy settings text first (Copy settings gives it)."); return; }
            say(load(Pick(s)));
        }
    }
}
