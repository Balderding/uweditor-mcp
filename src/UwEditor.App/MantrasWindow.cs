using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Randomize mantras (UW1): a seed, Preview, Apply and Undo. The shrines' mantras
    /// trade words (MantraShuffle) and every string naming one follows, in the shared STRINGS.PAK
    /// edits (the strings and conversation windows show them at once). Nothing is written to the
    /// game until you save.
    /// </summary>
    public sealed class MantrasWindow : Window
    {
        readonly MainWindow main;
        readonly StringsEditor strings;
        readonly TextBox report = ItemToolLayout.TextArea(false);
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo mantras", IsEnabled = false };
        readonly List<MantraResult> applied = new List<MantraResult>();

        public MantrasWindow(MainWindow main, StringsEditor strings)
        {
            this.main = main;
            this.strings = strings;
            Title = "Randomize mantras";
            Width = 820; Height = 680; MinWidth = 560; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Randomize mantras (UW1)", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The shrines' mantras swap words: each skill, the Key of Truth and the three group mantras answer to another mantra's word, and every book, scroll and conversation that names a mantra is changed to match. " +
                       "INSAHN stays (the incense visions show its parts as pictures), and so does NO (it does nothing). The same seed gives the same result.",
            });

            seed.Value = new Random().Next(1, int.MaxValue);
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var seedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            seedRow.Children.Add(new TextBlock { Text = "Seed", Width = 40, VerticalAlignment = VerticalAlignment.Center });
            seedRow.Children.Add(seed);
            seedRow.Children.Add(newSeed);
            head.Children.Add(seedRow);
            SettingsButtons = new SettingsBar(() => RandomizerSettings.FileName(Game, RandomizerSettings.Mantras, Seed), SettingsText, LoadSettings, Show, "Settings");
            head.Children.Add(SettingsButtons);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => Show(MantraShuffle.Preview(strings, Seed).Report(false));
            var apply = new Button { Content = "Apply", FontWeight = FontWeight.SemiBold };
            apply.Click += (o, e) => ApplyMantras();
            undoButton.Click += (o, e) => UndoMantras();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8) };
            bar.Children.Add(preview); bar.Children.Add(apply); bar.Children.Add(undoButton);
            bar.Children.Add(new TextBlock { Text = "The strings change in the editor; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });

            report.Text = "Press Preview to see the new mantras and the texts that would change, or Apply to change them.";
            var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            Grid.SetRow(head, 0); grid.Children.Add(head);
            Grid.SetRow(bar, 1); grid.Children.Add(bar);
            Grid.SetRow(report, 2); grid.Children.Add(report);
            Content = grid;
            ManualWindow.Attach(this, "Randomize mantras (UW1)");
        }

        int Seed { get { return (int)(seed.Value ?? 1); } }

        // ---- the settings file (the seed alone)

        string Game { get { return RandomizerSettings.GameOf(strings.Session); } }

        /// <summary>Save, Load, Copy and Paste settings.</summary>
        public SettingsBar SettingsButtons { get; private set; }

        /// <summary>The settings on screen now (the seed), pretty or on one line.</summary>
        public string SettingsText(bool compact) { return RandomizerSettings.Write(Game, RandomizerSettings.Mantras, new MantraOptions { Seed = Seed }, null, null, compact); }

        /// <summary>Reads settings text into the seed; the report says what was loaded and every warning. Gives that message.</summary>
        public string LoadSettings(string text)
        {
            var r = RandomizerSettings.Read(text, Game, RandomizerSettings.Mantras, new MantraOptions { Seed = Seed });
            if (!r.Ok) { Show(SettingsBar.Refused(r)); return report.Text; }
            seed.Value = r.As<MantraOptions>().Seed;
            Show(SettingsBar.Loaded(r));
            return report.Text;
        }

        /// <summary>The report shown now.</summary>
        public string ReportText { get { return report.Text; } }
        public NumericUpDown SeedBox { get { return seed; } }

        void Show(string text) { report.Text = text; }

        void ApplyMantras()
        {
            MantraResult r;
            try { r = MantraShuffle.Apply(strings, Seed); }
            catch (Exception ex) { Show("The mantras could not be changed: " + ex.Message); return; }
            if (r.Edits.Count == 0) { Show(r.Report(false)); return; }
            applied.Add(r);
            undoButton.IsEnabled = true;
            Show(r.Report(true));
            main.MantrasChanged("Mantras swapped (seed " + r.Seed + "). Undo mantras in the Randomize mantras window puts them back; File ▸ Save writes STRINGS.PAK.");
        }

        void UndoMantras()
        {
            if (applied.Count == 0) return;
            var r = applied[applied.Count - 1];
            applied.RemoveAt(applied.Count - 1);
            int left = MantraShuffle.Undo(strings, r);
            undoButton.IsEnabled = applied.Count > 0;
            Show("Mantras put back (seed " + r.Seed + ")." + (left > 0 ? " " + left + " string" + (left == 1 ? " was" : "s were") + " changed since and stay as they are." : ""));
            main.MantrasChanged("Mantras put back.");
        }
    }
}
