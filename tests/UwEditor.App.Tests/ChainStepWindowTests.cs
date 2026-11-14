using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The properties panel's Previous and Next buttons along a chain, and the rune traps' own group in Triggers and traps.</summary>
    public class ChainStepWindowTests
    {
        [AvaloniaFact]
        public void Previous_and_Next_step_along_a_pull_chains_chain()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(25);
            Ui.Pump();
            var prevButton = Ui.Find<Button>(w, "ChainPreviousButton");
            var nextButton = Ui.Find<Button>(w, "ChainNextButton");
            // nothing selected, or a plain object: no chain
            Assert.False(vm.HasChain);
            // the pull chain: nothing before it, two use triggers after it
            w.GoToObject(25, 1000);
            Ui.Pump();
            Assert.True(vm.HasChain);
            Assert.False(vm.CanChainPrevious);
            Assert.True(vm.CanChainNext);
            Assert.False(prevButton.IsEnabled);
            Assert.True(nextButton.IsEnabled);
            Assert.Equal("Next (2) ▶", vm.ChainNextLabel);
            Assert.Equal("◀ Previous", vm.ChainPreviousLabel);
            Assert.Contains("nothing links to it", vm.ChainText);
            Assert.Null(w.StepChain(false));                                    // the start: no step back
            Assert.Equal(1000, vm.SelectedObject);
            // forward to #998, then on while there is a next
            int at = vm.ChainNext.FindIndex(o => o.Index == 998);
            Assert.True(at >= 0);
            Assert.Equal(998, w.StepChain(true, null, at).Index);
            Ui.Pump();
            Assert.Equal(998, vm.SelectedObject);
            Assert.Contains("(" + (at + 1) + " of 2)", vm.EditStatus);
            var walked = new System.Collections.Generic.List<int> { 1000, 998 };
            while (vm.CanChainNext && walked.Count < 40)
            {
                var to = w.StepChain(true);
                Assert.NotNull(to);
                Ui.Pump();
                Assert.Equal(to.Index, vm.SelectedObject);
                walked.Add(to.Index);
            }
            Assert.Equal(new[] { 1000, 998, 1001, 994, 996 }, walked.Take(5));
            Assert.False(nextButton.IsEnabled);                                 // the end of the chain
            Assert.Null(w.StepChain(true));
            Assert.Contains("ends here", vm.EditStatus);
            // and back again, one step at a time, to the pull chain
            for (int i = walked.Count - 2; i >= 0; i--)
            {
                Assert.True(vm.CanChainPrevious);
                int k = vm.ChainPrevious.FindIndex(o => o.Index == walked[i]);
                Assert.True(k >= 0, "from #" + vm.SelectedObject + " back to #" + walked[i]);
                Assert.Equal(walked[i], w.StepChain(false, null, k).Index);
                Ui.Pump();
            }
            Assert.Equal(1000, vm.SelectedObject);
            Assert.False(prevButton.IsEnabled);
            w.Close();
        }

        [AvaloniaFact]
        public void Rune_traps_have_their_own_group_in_Triggers_and_traps()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var t = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => k.X > 1 && k.Y > 1 && !vm.Level.Tiles[k.X, k.Y].IsSolid);
            int flam = -1, tym = -1;
            Assert.True(vm.Edit(e => { flam = e.AddObject(new ObjectSpec { Item = 0x19E }, t.X, t.Y); tym = e.AddObject(new ObjectSpec { Item = 0x19F }, t.X, t.Y); }));
            vm.FlushRefresh();
            Ui.Pump();
            var tw = w.ShowTriggers();
            Ui.Pump();
            var runes = tw.Model.Nodes.Single(n => n.Header.Contains("rune traps"));
            Assert.StartsWith("2 rune traps", runes.Header);
            Assert.Contains("with no trigger", runes.Header);
            Assert.Equal(new[] { flam, tym }.OrderBy(i => i), runes.Children.Select(c => c.Index).OrderBy(i => i));
            var lone = tw.Model.Nodes.FirstOrDefault(n => n.Header.Contains("traps no trigger reaches"));
            if (lone != null) Assert.DoesNotContain(lone.Children, c => c.Index == flam || c.Index == tym);
            Assert.Contains("2 rune traps", tw.Model.Status);
            tw.Close();
            w.Close();
        }
    }
}
