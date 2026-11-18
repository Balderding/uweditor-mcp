using System;
using System.Linq;
using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>
    /// The content tools' hooks (McpEditor.Content*.cs): the conversations, sound and music the
    /// editor's windows hold, so a change made by the assistant shows there and either side's save
    /// writes it; and opening the game again after a mod was applied.
    /// </summary>
    public sealed partial class McpWindow
    {
        partial void HooksContent(McpEditor ed, MainViewModel vm)
        {
            Func<ConversationWindow> talk = () => main.OwnedWindows.OfType<ConversationWindow>().FirstOrDefault();
            Func<SoundWindow> sound = () => main.OwnedWindows.OfType<SoundWindow>().FirstOrDefault();
            Func<MusicPanel> music = () =>
            {
                var w = sound();
                return w == null ? null : w.Tabs.Items.OfType<Avalonia.Controls.TabItem>().Select(t => t.Content).OfType<MusicPanel>().FirstOrDefault();
            };

            // the app's library (it shares STRINGS.PAK with the strings windows); checked first, so the
            // app's own "No conversations" message never opens for the assistant's call
            ed.HostConversations = () =>
            {
                var s = vm.Session;
                if (s == null) return null;
                if (UltimateEditor.Games.GameProfile.Find(s.DataFolder, "CNV.ARK") == null || UltimateEditor.Games.GameProfile.Find(s.DataFolder, "STRINGS.PAK") == null) return null;
                return main.Conversations();
            };
            ed.HostConversationBusy = slot =>
            {
                var w = talk();
                if (w == null || w.Current == null || w.Current.Slot != slot || !w.TextPending) return null;
                return "The editor's conversation window has text typed in conversation " + slot + " that is not applied: the user should apply or revert it there first.";
            };
            ed.HostConversationChanged = slot =>
            {
                var w = talk();
                if (w == null) return;
                var row = w.Model.All.FirstOrDefault(r => r.Slot == slot);
                if (row != null) row.Changed = true;
                if (w.Current != null && w.Current.Slot == slot && !w.TextPending)
                {
                    w.Model.Selected = null;
                    w.SelectSlot(slot);
                }
            };
            ed.HostSound = () => sound()?.Library;
            ed.HostMusic = () => music()?.Library;
            ed.HostSoundChanged = () => music()?.Refresh();
            ed.HostUnsaved = () => main.UnsavedParts().Count > 0;
            ed.HostReopen = () =>
            {
                var s = vm.Session;
                if (s == null) return;
                var again = s.IsSave ? UwEditor.Core.GameSession.Open(s.SaveFolder, s.GameFolder) : UwEditor.Core.GameSession.Open(s.GameFolder);
                main.OpenSession(again);
            };
            ed.HostContentSaved = files =>
            {
                if (vm.Session == null) return;
                vm.Session.ReloadStrings();
                vm.RefreshNames();
                var w = talk();
                if (w != null) foreach (var r in w.Model.All) r.Changed = false;
            };
        }
    }
}
