using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>New conversation and Delete conversation: an empty slot of CNV.ARK gets one from a template, and one is taken out.</summary>
    public partial class ConversationWindow
    {
        async void OnNewConversation(object sender, RoutedEventArgs e)
        {
            if (lib == null) return;
            var empty = lib.EmptySlots();
            if (empty.Count == 0) { Model.Status = "Every slot a creature can name (1-255) has a conversation."; return; }
            var asked = await NewConversationDialog.Ask(this, empty);
            if (asked != null) AddConversation(asked.Item1, asked.Item2);
        }

        async void OnDeleteConversation(object sender, RoutedEventArgs e)
        {
            if (lib == null || current == null) return;
            int slot = current.Slot;
            List<ConversationUser> who;
            int n = users != null && users.TryGetValue(slot, out who) ? who.Count : 0;
            string text = string.Format("Conversation {0}{1} is taken out of CNV.ARK when you save. {2} Its strings stay in their block.",
                slot, lib.Name(slot) != null ? " (" + lib.Name(slot) + ")" : "",
                n == 0 ? "No creature in the levels talks through it." : n + " creature" + (n == 1 ? "" : "s") + " in the levels " + (n == 1 ? "talks" : "talk") + " through it and will have nothing to say.");
            if (await MessageWindow.Ask(this, "Delete conversation " + slot + "?", text, "Delete", "Cancel") == "Delete") DeleteConversation(slot);
        }

        /// <summary>Gives an empty slot a conversation from the template and shows it. True when added.</summary>
        public bool AddConversation(int slot, string name)
        {
            try { lib.AddConversation(slot, name); }
            catch (Exception ex) { Model.Status = "Not added: " + ex.Message; return false; }
            FillList();
            SelectSlot(slot);
            Model.Status = string.Format("Conversation {0} added from the template: change it on the Dialogue tab and Apply. A creature talks through it when its conversation number (whoami) is {0}. Save writes it.", slot);
            return true;
        }

        /// <summary>Takes a slot's conversation out (until Save) and shows the next one. True when deleted.</summary>
        public bool DeleteConversation(int slot)
        {
            try { lib.DeleteConversation(slot); }
            catch (Exception ex) { Model.Status = "Not deleted: " + ex.Message; return false; }
            int next = lib.Archive.Slots().Where(s => s > slot).DefaultIfEmpty(lib.Archive.Slots().DefaultIfEmpty(-1).Last()).First();
            FillList();
            if (next >= 0) SelectSlot(next);
            Model.Status = string.Format("Conversation {0} is deleted; Save writes CNV.ARK without it. Closing without saving keeps it.", slot);
            return true;
        }
    }

    /// <summary>Asks for the slot and the name of a new conversation.</summary>
    public sealed class NewConversationDialog : Window
    {
        /// <summary>The slot and the name (may be empty), or null when cancelled.</summary>
        public static async Task<Tuple<int, string>> Ask(Window owner, List<int> empty)
        {
            Tuple<int, string> result = null;
            var slots = new ComboBox { ItemsSource = empty.Select(s => s.ToString()).ToList(), SelectedIndex = 0, MinWidth = 100 };
            var name = new TextBox { Watermark = "The name the creature talks under (may be left empty)", MaxLength = 30 };
            var ok = new Button { Content = "Add", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            var w = new NewConversationDialog { Title = "New conversation", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            ok.Click += (s, e) => { result = Tuple.Create(empty[Math.Max(0, slots.SelectedIndex)], name.Text ?? ""); w.Close(); };
            cancel.Click += (s, e) => w.Close();
            w.Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "New conversation", FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "An empty slot gets a short conversation (a greeting, one answer, a goodbye) to change on the Dialogue tab. A creature talks through it when its conversation number is the slot's.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Slot", VerticalAlignment = VerticalAlignment.Center }, slots } },
                    name,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel } },
                },
            };
            await w.ShowDialog(owner);
            return result;
        }
    }
}
