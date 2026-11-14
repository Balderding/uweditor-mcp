using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Stepping along a chain (control, trigger, traps) one object at a time, and the rune traps that need no trigger.</summary>
    public class ChainStepTests
    {
        readonly ITestOutputHelper output;
        public ChainStepTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void A_pull_chains_chain_is_walked_forward_to_its_end_and_back_again()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var m = s.LoadLevel(25);
            var start = m.Objects[1000];
            Assert.True(Triggers.IsControl(start.Item), start.ToString());
            Assert.True(Triggers.InChain(m, start));
            // the pull chain holds two use triggers, each the start of a chain of its own
            var held = Triggers.ChainNext(m, start);
            output.WriteLine("#1000 holds " + string.Join(", ", held.Select(o => o.ToString())));
            Assert.Equal(new[] { 998, 999 }, held.Select(o => o.Index).OrderBy(i => i));
            Assert.Contains("Next: 2 objects", Triggers.ChainText(m, start));
            // forward from the pull chain through #998, then always the one next object, to the end
            var walk = new List<ObjectInfo> { start, m.Objects[998] };
            var seen = new HashSet<int> { start.Index, 998 };
            for (var next = Triggers.ChainNext(m, walk[1]); next.Count > 0 && seen.Add(next[0].Index); next = Triggers.ChainNext(m, next[0]))
            {
                Assert.Single(next);
                walk.Add(next[0]);
            }
            output.WriteLine(string.Join(" -> ", walk.Select(o => o.ToString())));
            Assert.Equal(new[] { 1000, 998, 1001, 994, 996 }, walk.Take(5).Select(o => o.Index));
            Assert.True(Triggers.IsTrigger(walk[1].Item));
            Assert.True(Triggers.IsTrap(walk[2].Item));
            Assert.True(Triggers.IsTrigger(walk[3].Item));
            Assert.True(Triggers.IsTrap(walk[4].Item));
            // every step forward can be taken back: the object before is among those one step back
            for (int i = 1; i < walk.Count; i++)
                Assert.Contains(walk[i - 1].Index, Triggers.ChainPrevious(m, walk[i]).Select(o => o.Index));
            // nothing links to the pull chain: the chain starts there
            Assert.Empty(Triggers.ChainPrevious(m, start));
            Assert.Contains("nothing links to it", Triggers.ChainText(m, start));
            Assert.Contains("Next: " + walk[2] + ".", Triggers.ChainText(m, walk[1]));
            Assert.Contains("Previous: " + start + ".", Triggers.ChainText(m, walk[1]));
            // the last object has no next
            var last = walk[walk.Count - 1];
            if (seen.Count == walk.Count && Triggers.ChainNext(m, last).Count == 0 && Triggers.InChain(m, last))
                Assert.Contains("Next: nothing", Triggers.ChainText(m, last));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Steps_agree_with_the_links_on_every_level(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            int steps = 0, several = 0;
            foreach (var slot in s.LevelSlots().Where(x => x.Present).Take(12))
            {
                var m = s.LoadLevel(slot.Level);
                foreach (var o in m.Objects.Where(o => !o.Free && o.TileX >= 0))
                {
                    var next = Triggers.ChainNext(m, o);
                    var prev = Triggers.ChainPrevious(m, o);
                    if (prev.Count > 1) several++;
                    Assert.DoesNotContain(o, next);
                    Assert.DoesNotContain(o, prev);
                    Assert.Equal(next.Count + prev.Count > 0 || o.IsTrapOrTrigger || Triggers.IsControl(o.Item), Triggers.InChain(m, o));
                    foreach (var n in next)
                    {
                        steps++;
                        // a step forward is a link or a held trigger or trap, and leads back
                        Assert.True(o.IsTrapOrTrigger ? o.Link == n.Index : n.Parent == o.Index && n.IsTrapOrTrigger);
                        if (o.IsTrapOrTrigger || n.IsTrapOrTrigger) Assert.Contains(o.Index, Triggers.ChainPrevious(m, n).Select(x => x.Index));
                    }
                    // a plain object inside a container is no chain
                    if (!o.IsTrapOrTrigger && !Triggers.IsControl(o.Item)) Assert.All(prev, x => Assert.True(x.IsTrapOrTrigger));
                }
            }
            output.WriteLine(game + ": " + steps + " steps forward, " + several + " objects with several ways back");
            Assert.True(steps > 50);
        }

        [Fact]
        public void Rune_traps_need_no_trigger_and_are_not_among_the_traps_no_trigger_reaches()
        {
            Assert.True(Triggers.IsRuneTrap(GameKind.UW2, 0x19E));
            Assert.True(Triggers.IsRuneTrap(GameKind.UW2, 0x19F));
            Assert.False(Triggers.IsRuneTrap(GameKind.UW2, 0x19D));
            Assert.False(Triggers.IsRuneTrap(GameKind.UW1, 0x19E));       // UW1 has no rune traps
            string g = GameData.Game("UW2"); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var e = edits.For(1);
                var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => t.x > 1 && t.y > 1 && !e.Model.Tiles[t.x, t.y].IsSolid);
                int before = Triggers.Unreached(e.Model, Triggers.Chains(e.Model)).Count;
                int flam = e.AddObject(new ObjectSpec { Item = 0x19E }, open.x, open.y);
                int tym = e.AddObject(new ObjectSpec { Item = 0x19F }, open.x, open.y);
                int damage = e.AddObject(new ObjectSpec { Item = 0x180 }, open.x, open.y);
                var m = edits.Model(1);
                var lone = Triggers.Unreached(m, Triggers.Chains(m)).Select(o => o.Index).ToList();
                Assert.DoesNotContain(flam, lone);
                Assert.DoesNotContain(tym, lone);
                Assert.Contains(damage, lone);                              // an ordinary trap with no trigger still is
                Assert.Equal(before + 1, lone.Count);
                Assert.Equal(new[] { flam, tym }.OrderBy(i => i), Triggers.RuneTraps(m).Select(o => o.Index));
                Assert.Contains("needs no trigger", Triggers.Explain(s, m.Objects[flam]));
                Assert.Contains("needs no trigger", Triggers.Explain(s, m.Objects[tym]));
            }
        }
    }
}
