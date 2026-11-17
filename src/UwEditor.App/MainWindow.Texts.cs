using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Texts that follow their objects (ObjectTextEdits): a book, scroll, gravestone, writing or
    /// text trap that is added, pasted or duplicated gets a string of its own, deleting the last
    /// reader of a text frees it, and Edit its text changes one object's text only. Undo and redo
    /// of the level step put the strings back with it.
    /// </summary>
    public partial class MainWindow
    {
        static readonly List<TextSlot> NoTexts = new List<TextSlot>();

        /// <summary>
        /// Runs a change of the level shown together with the strings it makes (made) and frees
        /// (freed), so that undoing the step empties the made ones and puts the freed ones back,
        /// and redoing it does the reverse.
        /// </summary>
        void WithTexts(Action change, Func<List<TextSlot>> made, Func<List<TextSlot>> freed)
        {
            List<TextSlot> m = NoTexts, f = NoTexts;
            Action<List<TextSlot>, bool> put = (list, text) =>
            {
                var st = Strings();
                if (st == null) return;
                foreach (var t in list) { try { st.Set(t.Block, t.Index, text ? t.Text : ""); } catch (Exception) { } }
            };
            vm.Edits.Together(() =>
            {
                change();
                m = made?.Invoke() ?? NoTexts;
                f = freed?.Invoke() ?? NoTexts;
            },
            undone: () => { put(m, false); put(f, true); },
            redone: () => { put(m, true); put(f, false); });
        }

        /// <summary>After a paste, duplicate or add on the level shown: the copies' texts become their own. Says so in the status line.</summary>
        List<TextSlot> OwnTextsFor(IEnumerable<int> roots)
        {
            var st = vm.Level == null || roots == null ? null : Strings();
            if (st == null) return NoTexts;
            int shared;
            List<TextSlot> made;
            try { made = ObjectTextEdits.GiveCopiesOwnTexts(vm.Edits, vm.Level.Level, roots.ToList(), st, out shared); }
            catch (Exception) { return NoTexts; }
            if (made.Count > 0) vm.EditStatus += string.Format(" {0} text{1} copied to {2} own string{1} ({3}).", made.Count, made.Count == 1 ? "" : "s", made.Count == 1 ? "its" : "their", string.Join(", ", made.Take(4).Select(t => t.ToString())) + (made.Count > 4 ? ", …" : ""));
            if (shared > 0) vm.EditStatus += string.Format(" {0} cop{1} the original's text: no string is free for another.", shared, shared == 1 ? "y shares" : "ies share");
            return made;
        }

        /// <summary>Before a delete on the level shown: the texts the objects (and what they hold) read.</summary>
        List<TextSlot> TextsUnder(IEnumerable<int> roots)
        {
            var st = vm.Level == null ? null : Strings();
            if (st == null) return NoTexts;
            try { return ObjectTextEdits.TextsUnder(vm.Editor.Model, roots, st); } catch (Exception) { return NoTexts; }
        }

        /// <summary>After a delete: the texts among these no object reads any more are freed. Says so in the status line.</summary>
        List<TextSlot> FreeTexts(List<TextSlot> slots)
        {
            var st = slots == null || slots.Count == 0 ? null : Strings();
            if (st == null) return NoTexts;
            var freed = ObjectTextEdits.FreeUnused(vm.Edits, st, slots);
            if (freed.Count > 0) vm.EditStatus += string.Format(" {0} text{1} no object reads any more {2} freed ({3}); Undo brings {4} back.", freed.Count, freed.Count == 1 ? "" : "s", freed.Count == 1 ? "was" : "were", string.Join(", ", freed.Take(4).Select(t => t.ToString())) + (freed.Count > 4 ? ", …" : ""), freed.Count == 1 ? "it" : "them");
            return freed;
        }

        /// <summary>The text an object reads, as edited, or null when it carries none.</summary>
        public TextSlot TextOf(int index)
        {
            var st = vm.Level == null ? null : Strings();
            return st == null || index <= 0 ? null : ObjectTextEdits.TextOf(vm.Session.Kind, vm.Level.Objects[index], st);
        }

        /// <summary>
        /// Sets one object's text: where it alone reads its string, the string is changed; where
        /// others read it too (or it has none), the object gets a string of its own. True when set.
        /// </summary>
        public bool SetObjectText(int index, string text)
        {
            var st = vm.Level == null || vm.Editor == null ? null : Strings();
            if (st == null) return false;
            var o = vm.Level.Objects[index];
            try
            {
                var t = ObjectTextEdits.TextOf(vm.Session.Kind, o, st);
                if (t != null && t.Text != null && ObjectTextEdits.Users(vm.Edits, t.Block, t.Index) == 1)
                {
                    st.Set(t.Block, t.Index, text);
                    vm.EditStatus = string.Format("The text of #{0} {1} is changed ({2}); it is saved with the strings.", index, o.Name, t);
                }
                else
                {
                    TextSlot own = null;
                    WithTexts(() => own = ObjectTextEdits.GiveOwnText(vm.Edits, vm.Level.Level, index, st, text), () => own == null ? NoTexts : new List<TextSlot> { own }, null);
                    vm.EditStatus = string.Format("#{0} {1} has a text of its own now ({2}){3}.", index, o.Name, own, t != null && t.Text != null ? "; the others that read the old one keep it" : "");
                }
            }
            catch (Exception ex) { vm.EditStatus = ex.Message; return false; }
            vm.FlushRefresh();
            vm.Select(index, -1, -1, true);
            return true;
        }

        /// <summary>Asks for an object's text and sets it (the right-click menu's Edit its text).</summary>
        public async Task EditObjectText(int index)
        {
            var level = vm.Level;
            if (level == null || index <= 0) return;
            var o = level.Objects[index];
            var t = TextOf(index);
            int users = t == null || t.Text == null ? 0 : ObjectTextEdits.Users(vm.Edits, t.Block, t.Index);
            string note = users > 1 ? string.Format("{0} objects read this text ({1}): this one gets a copy of its own, the others keep theirs.", users, t)
                : users == 1 ? "Only this object reads this text (" + t + ")." : "It has no text yet: it gets a string of its own.";
            string text = await TextDialog.Ask(this, "Text of #" + index + " " + o.Name, note, t?.Text ?? "");
            if (text != null) SetObjectText(index, text);
        }

        /// <summary>The right-click menu's text commands for an object that carries a text.</summary>
        IEnumerable<Control> TextItems(ObjectInfo o)
        {
            int block, lo, hi; string what;
            if (!ObjectTextEdits.Range(vm.Session.Kind, o.Item, out block, out lo, out hi, out what) || (block == 3 && o.Enchanted)) yield break;
            int idx = o.Index;
            yield return Item("Edit its te_xt…", () => { _ = EditObjectText(idx); }, vm.Editor != null);
            var t = TextOf(idx);
            if (t != null && t.Text != null) yield return Item("Show its text in the strings", () => ShowStrings(t.Block, t.Index));
        }
    }

    /// <summary>A small dialog to type a text of several lines.</summary>
    public sealed class TextDialog : Window
    {
        /// <summary>The text typed, or null when cancelled.</summary>
        public static async Task<string> Ask(Window owner, string title, string note, string text)
        {
            string result = null;
            var box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 360, FontSize = 13 };
            var ok = new Button { Content = "OK", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            var w = new TextDialog { Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            ok.Click += (s, e) => { result = box.Text ?? ""; w.Close(); };
            cancel.Click += (s, e) => w.Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } };
            w.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                    new TextBlock { Text = note + " A new line in the text is a new line in the game.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10) },
                    box,
                    buttons,
                },
            };
            w.Opened += (s, e) => box.Focus();
            await w.ShowDialog(owner);
            return result;
        }
    }
}
