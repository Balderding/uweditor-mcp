using System;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Linking a trigger, trap or lever to an object that stays where it lies (LevelEditor.LinkObjects), and the links shown for the panel (TriggerLinks.Of).</summary>
    public class LinkObjectsTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_trigger_runs_a_trap_that_stays_on_its_tile_and_a_second_trap_joins_the_chain(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var open = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).Where(t => !e.Model.Tiles[t.X, t.Y].IsSolid).ToList();
            var a = open[open.Count / 8]; var b = open[open.Count / 3]; var c = open[open.Count / 2]; var d = open[open.Count * 2 / 3];
            int trap = e.AddObject(new ObjectSpec { Item = 0x190, IsQuantity = true, Link = 0 }, a.X, a.Y);
            int trig = e.AddObject(new ObjectSpec { Item = TrapRecipes.MoveTrigger, IsQuantity = true, Link = 0 }, b.X, b.Y);
            int steps = e.UndoLabels.Count;

            Assert.Equal(trig, e.LinkObjects(trig, trap));
            Assert.Equal(steps + 1, e.UndoLabels.Count);
            Assert.True(Ok(e));
            var m = e.Model;
            Assert.Equal(trap, m.Objects[trig].Link);
            Assert.True(m.Objects[trig].IsQuantity);
            Assert.Equal((a.X, a.Y), (m.Objects[trig].Quality, m.Objects[trig].Owner));
            // the trap lies where it lay, on its tile's list and in nothing
            Assert.True(m.Objects[trap].OnTile);
            Assert.Equal((a.X, a.Y), (m.Objects[trap].TileX, m.Objects[trap].TileY));
            Assert.Equal(new[] { trap }, Triggers.Chain(m, m.Objects[trig]).Steps.Select(x => x.Index).ToArray());

            // the panel's links: the trap is run by the trigger; the trigger acts on the trap's square
            var ofTrap = TriggerLinks.Of(m, m.Objects[trap]);
            Assert.Contains(ofTrap, l => l.Object == trig && l.Text.StartsWith("Linked from"));
            var ofTrig = TriggerLinks.Of(m, m.Objects[trig]);
            Assert.Contains(ofTrig, l => l.Object < 0 && l.X == a.X && l.Y == a.Y && l.Text.StartsWith("Acts on"));

            // a second trap linked to the trigger comes first, and the old one after it
            int trap2 = e.AddObject(new ObjectSpec { Item = 0x190, IsQuantity = true, Link = 0 }, c.X, c.Y);
            e.LinkObjects(trig, trap2);
            m = e.Model;
            Assert.True(Ok(e));
            Assert.Equal(new[] { trap2, trap }, Triggers.Chain(m, m.Objects[trig]).Steps.Select(x => x.Index).ToArray());
            Assert.Equal((c.X, c.Y), (m.Objects[trig].Quality, m.Objects[trig].Owner));
            Assert.True(m.Objects[trap2].OnTile && m.Objects[trap].OnTile);
            Assert.Contains(TriggerLinks.Of(m, m.Objects[trap]), l => l.Object == trig && l.Text.StartsWith("Run by"));

            // one undo step takes the second link back whole
            e.Undo();
            Assert.Equal(new[] { trap }, Triggers.Chain(e.Model, e.Model.Objects[trig]).Steps.Select(x => x.Index).ToArray());
            Assert.Equal(0, e.Model.Objects[trap2].Link);

            // a lever links through a use trigger made inside it
            int lever = e.AddObject(new ObjectSpec { Item = 0x170, IsQuantity = true, Link = 0 }, d.X, d.Y);
            int use = e.LinkObjects(lever, trap);
            m = e.Model;
            Assert.True(Ok(e));
            Assert.NotEqual(lever, use);
            Assert.Equal(TrapRecipes.UseTrigger, m.Objects[use].Item);
            Assert.Equal(lever, m.Objects[use].Parent);
            Assert.Equal(trap, m.Objects[use].Link);
            Assert.True(m.Objects[trap].OnTile);
            // again: the same use trigger is used, now for the other trap
            Assert.Equal(use, e.LinkObjects(lever, trap2));
            Assert.Single(e.Model.Objects[lever].Children);

            // taking a link off leaves both objects
            e.Unlink(trig);
            Assert.Equal(0, e.Model.Objects[trig].Link);
            Assert.True(e.Model.Objects[trap].OnTile);
            Assert.True(Ok(e));
        }

        [Fact]
        public void What_cannot_link_is_refused_and_nothing_changes()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s).For(1);
            var m = e.Model;
            var plain = m.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && o.Item < 0x40);
            var other = m.Objects.First(o => !o.Free && o.OnTile && o.Index != plain.Index);
            var before = (byte[])e.Data.Clone();
            Assert.Contains("links to nothing", Assert.Throws<InvalidOperationException>(() => e.LinkObjects(plain.Index, other.Index)).Message);
            // a trigger that holds its traps inside it keeps them
            var holder = m.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.IsTrapOrTrigger && !o.IsQuantity && o.Children.Count > 0);
            if (holder != null)
            {
                Assert.Contains("already lies inside", Assert.Throws<InvalidOperationException>(() => e.LinkObjects(holder.Index, holder.Children[0])).Message);
                Assert.Contains("holds", Assert.Throws<InvalidOperationException>(() => e.LinkObjects(holder.Index, other.Index == holder.Index ? plain.Index : other.Index)).Message);
            }
            Assert.Equal(before, e.Data);
            Assert.Equal(0, e.UndoLabels.Count);
        }
    }
}
